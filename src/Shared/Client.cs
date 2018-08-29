using NSspi;
using NSspi.Contexts;
using NSspi.Credentials;
using Shared;
using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security;
using System.Security.Principal;
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

        private readonly ClientContext context;
        private readonly Credential cred;

        private byte[] lastServerToken;

        private int port;
        private readonly string host;

        public Client(string host, int port = DefaultPort, Credential clientCred = null)
        {
            if (port <= 0)
            {
                port = DefaultPort;
            }

            this.host = host.Trim();
            this.port = port;

            var spn = $"host/{host}";

            Console.WriteLine($"[Client] SPN {spn}");

            this.cred = clientCred ?? new ClientCurrentCredential(PackageNames.Negotiate);

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

        public void Start(bool authenticate = true)
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

            if (authenticate)
            {
                DoInit();
            }
            else
            {
                waitForAuthentication.Set();
            }
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

            var outBuffer = message.Serialize();

            var lengthBuffer = new byte[4];

            ByteWriter.WriteInt32_BE(outBuffer.Length, lengthBuffer, 0);

            this.socket.Send(lengthBuffer, 0, lengthBuffer.Length, SocketFlags.None, out SocketError error);

            this.socket.Send(outBuffer, 0, outBuffer.Length, SocketFlags.None, out error);

            waitForCompletion.Set();
        }

        private ManualResetEvent waitForAuthentication = new ManualResetEvent(false);

        private int initCounter = 0;

        private void DoInit()
        {
            var id = Thread.CurrentPrincipal.Identity as WindowsIdentity;

            Console.WriteLine($"[Client] Calling as {id?.Name} | {id?.ImpersonationLevel}");

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

                Message message = new Message(Operation.ClientToken) { Token = outToken };

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
            byte[] readBuffer = new byte[4];

            while (this.running)
            {
                if (!this.socket.Connected)
                {
                    Console.WriteLine("[Client] socket disconnected");
                    break;
                }

                try
                {
                    if (this.running == false) { break; }

                    this.socket.Receive(readBuffer, 4, SocketFlags.None);

                    var messageLength = ByteWriter.ReadInt32_BE(readBuffer, 0);

                    if (readBuffer.Length < messageLength)
                    {
                        readBuffer = new byte[messageLength];
                    }

                    var remaining = messageLength;
                    var chunkLength = 0;
                    var position = 0;

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

                Message message = Message.Deserialize(readBuffer);

                Console.WriteLine($"[Client] Operation {message.Operation}");

                if (message.Operation == Operation.ServerToken)
                {
                    this.lastServerToken = message.Token;

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
