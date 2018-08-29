using NSspi;
using NSspi.Contexts;
using NSspi.Credentials;
using System;
using System.ComponentModel;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Runtime.Remoting.Messaging;
using System.Security.Principal;
using System.Threading;

namespace Shared
{
    internal class ServiceRequest
    {
        private readonly Socket readSocket;
        private readonly CancellationToken cancel;

        private bool initializing = true;

        private readonly ServerContext serverContext;

        public ServiceRequest(Credential serverCred, Socket readSocket, CancellationToken cancel)
        {
            var identity = Thread.CurrentPrincipal.Identity;

            this.readSocket = readSocket;
            this.cancel = cancel;

            this.serverContext = new ServerContext(
                serverCred,
                ContextAttrib.AcceptIntegrity |
                ContextAttrib.ReplayDetect |
                ContextAttrib.SequenceDetect |
                ContextAttrib.MutualAuth |
                ContextAttrib.Delegate |
                ContextAttrib.Confidentiality,
                impersonationSetsThreadPrinciple: true // gaaaaaaaahhh
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

                    messageLength = ByteWriter.ReadInt32_BE(readBuffer, 0);

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

            Console.WriteLine($"[ServiceRequest] received {message.Operation}; s4u: {message.S4UToken}");

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
            Console.WriteLine($"[ServiceRequest] Send Response {message.Operation}");

            if (cancel.IsCancellationRequested)
            {
                throw new InvalidOperationException("Not connected");
            }

            var outBuffer = message.Serialize();

            var lengthBuffer = new byte[4];

            ByteWriter.WriteInt32_BE(outBuffer.Length, lengthBuffer, 0);

            readSocket.Send(lengthBuffer, 0, lengthBuffer.Length, SocketFlags.None, out SocketError error);

            readSocket.Send(outBuffer, 0, outBuffer.Length, SocketFlags.None);
        }

        private void HandleInit(Message message)
        {
            SecurityStatus status;

            if (initializing)
            {
                status = this.serverContext.AcceptToken(message.Token, out byte[] nextToken);

                Console.WriteLine($"[ServiceRequest] AcceptToken {status} | next {nextToken?.Length ?? 0}");

                if (status == SecurityStatus.OK || status == SecurityStatus.ContinueNeeded)
                {
                    if (nextToken != null && nextToken.Length > 0)
                    {
                        this.SendResponse(new Message(Operation.ServerToken) { Token = nextToken });
                    }

                    if (status == SecurityStatus.OK)
                    {
                        Console.WriteLine($"[ServiceRequest] context user {this.serverContext.ContextUserName}");

                        var imp = this.serverContext.ImpersonateClient();

                        var identity = Thread.CurrentPrincipal.Identity as WindowsIdentity;

                        Console.WriteLine($"[ServiceRequest] impersonated {identity.Name} | {identity.ImpersonationLevel}");
                    }
                }
            }
        }
    }
}
