using NSspi;
using NSspi.Contexts;
using NSspi.Credentials;
using Shared;
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace ServerApp
{
    public class Server
    {
        private readonly int port;

        public const int DefaultPort = 5555;

        public Server(int port = DefaultPort, Credential serverCred = null)
        {
            Console.WriteLine($"[Server] Running As {Thread.CurrentPrincipal.Identity.Name}");

            if (port <= 0)
            {
                port = DefaultPort;
            }

            this.port = port;
            this.serverCred = serverCred;
        }

        private Thread receiveThread;

        private Socket serverSocket;

        private bool running;

        public event UnhandledExceptionEventHandler OnError;

        public event Action Stopped;

        private CancellationTokenSource cancel = new CancellationTokenSource();

        public Action<Message> OnReceived { get; set; }

        public void Start()
        {
            if (this.running)
            {
                throw new InvalidOperationException("Already running");
            }

            this.serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

            this.serverSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            this.serverSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.DontLinger, true);
            this.serverSocket.ExclusiveAddressUse = false;

            this.serverSocket.Bind(new IPEndPoint(IPAddress.Any, port));

            this.serverSocket.Listen(1);

            this.running = true;

            this.receiveThread = new Thread(ReceiveThreadEntry)
            {
                Name = "SSPI Server Receive Thread"
            };

            this.receiveThread.Start();

            Console.WriteLine($"[Server] listening on ({port})");
        }

        public void Stop()
        {
            cancel.Cancel();

            if (this.running == false)
            {
                return;
            }

            this.running = false;

            this.serverSocket.Close();

            this.receiveThread.Join();
        }

        private Credential serverCred;

        private void ReceiveThreadEntry()
        {
            try
            {
                while (this.running)
                {
                    if (serverCred == null)
                    {
                        serverCred = new ServerCurrentCredential(PackageNames.Negotiate);

                        Console.WriteLine("[Server] Creating Server Cred");
                    }

                    var request = new ServiceRequest(serverCred, this.serverSocket.Accept(), cancel.Token)
                    {
                        OnReceived = OnReceived
                    };

                    ThreadPool.QueueUserWorkItem(request.Process);
                }
            }
            catch (Exception e)
            {
                OnException(e);
            }
            finally
            {
                this.running = false;

                Console.WriteLine("STOPPED");

                this.Stopped?.Invoke();
            }
        }

        private void OnException(Exception e)
        {
            OnError?.Invoke(this, new UnhandledExceptionEventArgs(e, false));
        }
    }
}
