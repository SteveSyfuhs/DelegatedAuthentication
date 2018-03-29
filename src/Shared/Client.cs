using NSspi;
using NSspi.Contexts;
using NSspi.Credentials;
using Shared;
using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security;
using System.Threading;

namespace ClientApp
{
    public class Client
    {
        static Client()
        {
            ServicePointManager.DnsRefreshTimeout = 0;
        }

        public const int DefaultPort = 5555;

        private ClientContext context;
        private ClientCurrentCredential cred;

        private byte[] lastServerToken;

        private int port;
        private readonly string host;

        public Client(string host, int port = DefaultPort)
        {
            if (port <= 0)
            {
                port = DefaultPort;
            }

            this.host = host.Trim();
            this.port = port;

            var spn = $"host/{host}";

            Console.WriteLine($"[Client] SPN {spn}");

            this.cred = new ClientCurrentCredential(PackageNames.Negotiate);

            this.context = new ClientContext(
                cred,
                spn,
                ContextAttrib.InitIntegrity |
                ContextAttrib.ReplayDetect |
                ContextAttrib.SequenceDetect |
                ContextAttrib.MutualAuth |
                ContextAttrib.Delegate |
                ContextAttrib.Confidentiality
            );
        }

        private Thread receiveThread;
        private Socket socket;

        private bool running;

        private ManualResetEvent waitForCompletion = new ManualResetEvent(false);

        public delegate void ReceivedAction(Message message);

        public event ReceivedAction Received;

        public event Action Disconnected;
        
        public void Start()
        {
            if (this.running)
            {
                throw new InvalidOperationException("Already running");
            }

            this.socket = new Socket(
                AddressFamily.InterNetwork,
                SocketType.Stream,
                ProtocolType.Tcp
            );

            Console.WriteLine($"[Client] Resolving host {host}");

            IPAddress ipv4Addr;

            try
            {
                ipv4Addr = Dns.GetHostAddresses(host)
                                  .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
            }
            catch (Exception ex)
            {
                Console.WriteLine("WTF: " + ex);

                ipv4Addr = IPAddress.Loopback;
            }

            Console.WriteLine($"[Client] Connecting to {ipv4Addr.ToString()}:{port}");

            this.socket.Connect(ipv4Addr, port);

            this.running = true;

            this.receiveThread = new Thread(ReceiveThreadEntry)
            {
                Name = "SSPI Client Receive Thread"
            };

            this.receiveThread.Start();

            DoInit();
        }

        public void Stop()
        {
            if (this.running == false)
            {
                return;
            }

            if (!waitForCompletion.WaitOne(TimeSpan.FromSeconds(10)))
            {
                Console.WriteLine("[Client] Wait time out");
            }

            this.socket.Close();
            this.receiveThread.Join();
        }

        public void Send(Message message)
        {
            if (!waitForAuthentication.WaitOne(TimeSpan.FromSeconds(10)))
            {
                throw new SecurityException();
            }

            SendInternal(message);
        }

        private void SendInternal(Message message)
        {
            if (this.running == false)
            {
                throw new InvalidOperationException("Not connected");
            }

            byte[] outBuffer = new byte[message.Data.Length + 8];
            int position = 0;

            ByteWriter.WriteInt32_BE((int)message.Operation, outBuffer, position);
            position += 4;

            ByteWriter.WriteInt32_BE(message.Data.Length, outBuffer, position);
            position += 4;

            Array.Copy(message.Data, 0, outBuffer, position, message.Data.Length);

            this.socket.Send(outBuffer, 0, outBuffer.Length, SocketFlags.None, out SocketError error);

            waitForCompletion.Set();
        }

        private ManualResetEvent waitForAuthentication = new ManualResetEvent(false);

        private int initCounter = 0;

        private void DoInit()
        {
            var id = Thread.CurrentPrincipal;

            Console.WriteLine($"[Client] Calling as {id.Identity.Name}");

            var serverToken = "";

            if (this.lastServerToken != null && this.lastServerToken.Length > 0)
            {
                serverToken = Convert.ToBase64String(this.lastServerToken);
            }

            Console.WriteLine($"[Client] Server Token ({initCounter}) {serverToken}");

            var status = this.context.Init(this.lastServerToken, out byte[] outToken);

            Console.WriteLine($"[Client] Init status ({initCounter}) {status} outToken {outToken?.Length ?? 0}");

            initCounter++;

            if (status == SecurityStatus.ContinueNeeded || (outToken?.Length ?? 0) > 0)
            {
                Console.WriteLine("[Client] token " + Convert.ToBase64String(outToken));

                Message message = new Message(Operation.ClientToken, outToken);

                this.SendInternal(message);
            }

            if (status == SecurityStatus.OK)
            {
                Console.WriteLine("[Client] DoInit Status = ok");

                this.lastServerToken = null;
                this.initCounter = 0;

                waitForAuthentication.Set();
            }
        }

        private void ReceiveThreadEntry()
        {
            try
            {
                ReadResponseLoop();
            }
            finally
            {
                this.running = false;

                this.Disconnected?.Invoke();
            }
        }

        private void ReadResponseLoop()
        {
            byte[] readBuffer = new byte[65536];

            Operation operation;
            int messageLength;
            int remaining;
            int chunkLength;
            int position;

            while (this.running)
            {
                if (!this.socket.Connected)
                {
                    Console.WriteLine("[Client] socket disconnected");
                    break;
                }

                try
                {
                    //                          |--4 bytes--|--4 bytes--|---N--|
                    // Every command is a TLV - | Operation |  Length   | Data |

                    // Read the operation.
                    this.socket.Receive(readBuffer, 4, SocketFlags.None);

                    // Check if we popped out of a receive call after we were shut down.
                    if (this.running == false) { break; }

                    operation = (Operation)ByteWriter.ReadInt32_BE(readBuffer, 0);

                    // Read the length
                    this.socket.Receive(readBuffer, 4, SocketFlags.None);
                    messageLength = ByteWriter.ReadInt32_BE(readBuffer, 0);

                    if (readBuffer.Length < messageLength)
                    {
                        readBuffer = new byte[messageLength];
                    }

                    // Read the data
                    // Keep in mind that Socket.Receive may return less data than asked for.
                    remaining = messageLength;
                    chunkLength = 0;
                    position = 0;
                    while (remaining > 0)
                    {
                        chunkLength = this.socket.Receive(readBuffer, position, remaining, SocketFlags.None);
                        remaining -= chunkLength;
                        position += chunkLength;
                    }
                }
                catch (SocketException e)
                {
                    if (e.SocketErrorCode == SocketError.ConnectionAborted ||
                        e.SocketErrorCode == SocketError.Interrupted ||
                        e.SocketErrorCode == SocketError.OperationAborted ||
                        e.SocketErrorCode == SocketError.Shutdown ||
                        e.SocketErrorCode == SocketError.ConnectionReset)
                    {
                        // Shutting down.
                        break;
                    }
                    else
                    {
                        throw;
                    }
                }

                byte[] dataCopy = new byte[messageLength];
                Array.Copy(readBuffer, 0, dataCopy, 0, messageLength);
                Message message = new Message(operation, dataCopy);

                Console.WriteLine($"[Client] Operation {message.Operation}");

                if (message.Operation == Operation.ServerToken)
                {
                    this.lastServerToken = message.Data;

                    DoInit();
                }
                else
                {
                    this.Received?.Invoke(message);

                    waitForCompletion.Set();
                }
            }
        }
    }
}
