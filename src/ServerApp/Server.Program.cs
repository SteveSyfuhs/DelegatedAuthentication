using ClientApp;
using NSspi;
using NSspi.Credentials;
using Shared;
using System;
using System.Linq;
using System.Security.Principal;
using System.Text;
using System.Threading;

namespace ServerApp
{
    class Program
    {
        private static Credential serverCredential;
        private static Server server;

        static void Main(string[] args)
        {
            AppDomain.CurrentDomain.SetPrincipalPolicy(PrincipalPolicy.WindowsPrincipal);

            Console.Title = "Server";

            serverCredential = TryCreateCredential(args);

            StartServer(
                TryGet(args, 0, Server.DefaultPort),
                serverCredential
            );

            Console.ReadKey();
        }

        private static Credential TryCreateCredential(string[] args)
        {
            var username = TryGet(args, 1, "");
            var password = TryGet(args, 2, "");
            var domain = TryGet(args, 3, "");

            if (!string.IsNullOrWhiteSpace(username) &&
               !string.IsNullOrWhiteSpace(password) &&
               !string.IsNullOrWhiteSpace(domain))
            {
                Console.WriteLine($"Starting as {username}@{domain}: {password}");

                return new PasswordCredential(domain, username, password, PackageNames.Negotiate, CredentialUse.Both);
            }

            return null;
        }

        private static T TryGet<T>(string[] args, int index, T defaultValue = default(T))
        {
            if ((args?.Length ?? 0) <= index)
            {
                return defaultValue;
            }

            var arg = args[index];

            try
            {
                return (T)Convert.ChangeType(arg, typeof(T));
            }
            catch
            {
                return defaultValue;
            }
        }

        private static void StopServer()
        {
            if (server != null)
            {
                server.Stop();
            }
        }

        private static void StartServer(int port, Credential serverCred)
        {
            server = new Server(port, serverCred)
            {
                OnReceived = (m) =>
                {
                    try
                    {
                        CallDelegatedServer(m);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("ReceiveError: " + ex.ToString());
                    }
                }
            };

            server.OnError += (s, e) =>
            {
                Console.WriteLine("Error: " + e.ExceptionObject.ToString());
            };

            server.Start();
        }

        private static void CallDelegatedServer(Message m)
        {
            switch (m.Operation)
            {
                case Operation.WhoAmI:
                    if ((m.Data?.Length ?? 0) > 0)
                    {
                        RelayDelegated(m);
                    }
                    break;
            }
        }

        private static void RelayDelegated(Message m)
        {
            var id = Thread.CurrentPrincipal.Identity as WindowsIdentity;

            Console.WriteLine($"[Server] Impersonated {id.Name} | {id.ImpersonationLevel}");

            var portBytes = m.Data.Take(4).ToArray();
            var hostBytes = m.Data.Skip(4).ToArray();

            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(portBytes);
            }

            var delegatePort = BitConverter.ToInt32(portBytes, 0);
            var delegatedHost = Encoding.UTF8.GetString(hostBytes);

            Console.WriteLine($"Relaying to {delegatedHost}:{delegatePort}");

            var delegateClient = new Client(delegatedHost, delegatePort);
            delegateClient.Start();

            delegateClient.Send(new Message(m.Operation, m.Data));

            Thread.Sleep(1500);

            delegateClient.Stop();

            Console.WriteLine();
            Console.WriteLine();
        }
    }
}
