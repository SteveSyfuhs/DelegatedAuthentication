using Shared;
using System;
using System.Collections.Generic;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;

namespace ClientApp
{
    class Program
    {
        private static Client client;

        static void Main(string[] args)
        {
            AppDomain.CurrentDomain.SetPrincipalPolicy(PrincipalPolicy.WindowsPrincipal);

            Console.Title = "Client";

            string host = null;
            string delegatedHost = null;

            int port = Client.DefaultPort;
            int delegatePort = port + 100;

            if (args.Length > 0)
            {
                host = args[0];

                if (args.Length <= 1 || !int.TryParse(args[1], out port))
                {
                    port = Client.DefaultPort;
                }

                if (args.Length > 2)
                {
                    delegatedHost = args[2];
                }

                if (args.Length <= 3 || !int.TryParse(args[3], out delegatePort))
                {
                    delegatePort = port + 100;
                }
            }

            host = host ?? "localhost";

            delegatedHost = delegatedHost ?? host ?? "localhost";

            Console.WriteLine($"[Client] Connecting to {host}:{port}");

            new ManualResetEvent(false).WaitOne(TimeSpan.FromSeconds(10));

            bool disconnected = true;

            var tasks = new List<Task>();

            while (true)
            {
                for (var i = 0; i < 100; i++)
                {
                    if (disconnected)
                    {
                        StartClient(host, port);

                        client.Disconnected += () => { disconnected = true; Thread.Sleep(50); };

                        disconnected = false;
                    }

                    if (!disconnected)
                    {
                        Send(delegatedHost, delegatePort);

                        //var task = Task.Run(() =>
                        //{
                        //    Send(delegatedHost, delegatePort);
                        //});

                        //tasks.Add(task);
                    }
                }

                //Task.WhenAll(tasks).Wait();
            }
        }

        private static void Send(string delegatedHost, int delegatePort)
        {
            client.Send(
                                            new Message(
                                                Operation.WhoAmI,
                                                new byte[0],
                                                delegatePort: delegatePort,
                                                delegateHost: delegatedHost
                                            )
                                        );

            Console.WriteLine($"[Client {Thread.CurrentThread.ManagedThreadId}] send WhoAmI");
        }

        private static void StartClient(string host, int port)
        {
            client = new Client(host, port);

            client.Start();
        }
    }
}
