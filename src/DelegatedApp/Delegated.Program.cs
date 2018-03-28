using NSspi;
using NSspi.Credentials;
using ServerApp;
using Shared;
using System;
using System.Security.Principal;
using System.Threading;

namespace DelegatedApp
{
    class Program
    {
        private static Server server;
        private static ManualResetEvent restart;

        static void Main(string[] args)
        {
            Console.Title = "Delegate";
            
            while (true)
            {
                try
                {
                    StartServer(
                        TryGet(args, 0, Server.DefaultPort + 100),
                        TryGet(args, 1, ""),
                        TryGet(args, 2, ""),
                        TryGet(args, 3, "")
                    );
                }
                catch (Exception ex)
                {
                    Console.WriteLine("[DelegateReceive] Error: " + ex.ToString());
                }
            };
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

        private static void StartServer(int port, string username, string password, string domain)
        {
            Credential serverCred = null;

            if (!string.IsNullOrWhiteSpace(username) &&
                !string.IsNullOrWhiteSpace(password) &&
                !string.IsNullOrWhiteSpace(domain))
            {
                Console.WriteLine($"Starting as {username}@{domain}: {password}");

                serverCred = new PasswordCredential(
                    domain,
                    username,
                    password,
                    PackageNames.Negotiate,
                    CredentialUse.Both
                );
            }
            restart = new ManualResetEvent(false);

            if (server != null)
            {
                server.Stop();
            }

            server = null;

            server = new Server(port)
            {
                OnReceived = (m) =>
                {
                    CallDelegatedMethod(m);
                }
            };

            server.OnError += (s, e) =>
            {
                Console.WriteLine("[Delegated] Error: " + e.ExceptionObject);
            };

            server.Stopped += () =>
            {
                restart.Set();
            };

            server.Start();

            restart.WaitOne();
        }

        private static void CallDelegatedMethod(Message m)
        {
            switch (m.Operation)
            {
                case Operation.WhoAmI:
                    var identity = Thread.CurrentPrincipal.Identity as WindowsIdentity;

                    var name = identity?.Name;
                    var imp = identity?.ImpersonationLevel;

                    Console.WriteLine($"[Delegated] WhoAmI: {name} | {imp}");

                    break;
            }
        }
    }
}
