using System;
using System.Net.Sockets;
using System.Security.Principal;
using System.Threading;

namespace Shared
{
    internal class ServiceRequest
    {
        private readonly Socket readSocket;
        private readonly CancellationToken cancel;

        private readonly SecurityContext serverContext;

        public ServiceRequest(Credential serverCred, Socket readSocket, CancellationToken cancel)
        {
            this.readSocket = readSocket;
            this.cancel = cancel;

            this.serverContext = new SecurityContext(
                serverCred,
                "Negotiate"                
            );
        }

        public Action<Message> OnReceived { get; set; }

        internal void Process(object state)
        {
            ReadRequestLoop();
        }

        private void ReadRequestLoop()
        {
            byte[] readBuffer = new byte[4];

            int messageLength;
            int remaining;

            while (!cancel.IsCancellationRequested)
            {
                try
                {
                    if (cancel.IsCancellationRequested)
                    {
                        break;
                    }

                    // Read the length

                    this.readSocket.Receive(readBuffer, 4, SocketFlags.None);

                    messageLength = Endian.ConvertFromBigEndian(readBuffer);

                    if (readBuffer.Length < messageLength)
                    {
                        readBuffer = new byte[messageLength];
                    }

                    remaining = messageLength;

                    var chunkLength = 0;
                    var position = 0;

                    while (remaining > 0)
                    {
                        chunkLength = this.readSocket.Receive(readBuffer, position, remaining, SocketFlags.None);
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

                Receive(readBuffer);
            }
        }

        private void Receive(byte[] readBuffer)
        {
            var message = Message.Deserialize(readBuffer);

            ContextDebugger.WriteLine($"[ServiceRequest] received {message.Operation}; s4u: {message.S4UToken}");

            if (message.Operation == Operation.ClientToken)
            {
                HandleInit(message);
            }
            else
            {
                this.OnReceived?.Invoke(message);
            }
        }

        private void SendResponse(Message message)
        {
            ContextDebugger.WriteLine($"[ServiceRequest] Send Response {message.Operation}");

            if (cancel.IsCancellationRequested)
            {
                throw new InvalidOperationException("Not connected");
            }

            var outBuffer = message.Serialize();

            var lengthBuffer = new byte[4];

            Endian.ConvertToBigEndian(outBuffer.Length, lengthBuffer);

            readSocket.Send(lengthBuffer, 0, lengthBuffer.Length, SocketFlags.None, out SocketError error);

            readSocket.Send(outBuffer, 0, outBuffer.Length, SocketFlags.None);
        }

        private int tripCount = 0;

        private void HandleInit(Message message)
        {
            var status = this.serverContext.AcceptSecurityContext(message.Token, out byte[] nextToken);

            ContextDebugger.WriteLine($"[ServiceRequest] AcceptToken {status} | trip {tripCount} | next {nextToken?.Length ?? 0}");

            tripCount++;

            if (status == ContextStatus.Accepted || status == ContextStatus.RequiresContinuation)
            {
                if (nextToken != null && nextToken.Length > 0)
                {
                    this.SendResponse(new Message(Operation.ServerToken) { Token = nextToken });
                }

                if (status == ContextStatus.Accepted)
                {
                    ContextDebugger.WriteLine($"[ServiceRequest] context user {this.serverContext.UserName}");

                    var imp = this.serverContext.ImpersonateClient();

                    var identity = Thread.CurrentPrincipal.Identity as WindowsIdentity;

                    ContextDebugger.WriteLine($"[ServiceRequest] impersonated {identity.Name} | {identity.ImpersonationLevel}");
                }
            }
        }
    }
}
