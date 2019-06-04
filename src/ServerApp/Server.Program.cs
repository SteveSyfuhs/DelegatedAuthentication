using ClientApp;
using Shared;
using System;
using System.Security.Principal;
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
                ContextDebugger.WriteLine($"Starting as {username}@{domain}: {password}");

                return new PasswordCredential(domain, username, password);
            }

            return null;
        }

        private static T TryGet<T>(string[] args, int index, T defaultValue = default)
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
                        ContextDebugger.WriteLine("ReceiveError: " + ex.ToString());
                    }
                }
            };

            server.OnError += (s, e) =>
            {
                ContextDebugger.WriteLine("Error: " + e.ExceptionObject.ToString());
            };

            server.Start();
        }

        private static void CallDelegatedServer(Message m)
        {
            switch (m.Operation)
            {
                case Operation.WhoAmI:
                    RelayDelegated(m);
                    break;
            }
        }

        private static void RelayDelegated(Message m)
        {
            var id = Thread.CurrentPrincipal.Identity as WindowsIdentity;

            ContextDebugger.WriteLine($"[Server] Impersonated {id.Name} | {id.ImpersonationLevel}");

            WindowsImpersonationContext impersonation = null;

            if (!string.IsNullOrWhiteSpace(m.S4UToken))
            {
                ContextDebugger.WriteLine($"[Server] S4U token received {m.S4UToken}");

                id = new WindowsIdentity(m.S4UToken);

                impersonation = id.Impersonate();

                Thread.CurrentPrincipal = new WindowsPrincipal(id);

                ContextDebugger.WriteLine($"[Server] S4U Impersonated {id.Name} | {id.ImpersonationLevel}");
            }

            ContextDebugger.WriteLine($"[Server] Relaying to {m.DelegateHost}:{m.DelegatePort}");

            var delegateClient = new Client(m.DelegateHost, m.DelegatePort);
            delegateClient.Start();

            delegateClient.Send(Message.Deserialize(m.Serialize()));

            Thread.Sleep(1500);

            delegateClient.Stop();

            impersonation?.Dispose();

            ContextDebugger.WriteLine();
            ContextDebugger.WriteLine();
        }
    }
}
