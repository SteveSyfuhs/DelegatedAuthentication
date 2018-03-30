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
                setThreadIdentity: true
            );
        }

        public Action<Message> OnReceived { get; set; }

        internal void Process(object state)
        {
            ReadRequestLoop();
        }

        private void ReadRequestLoop()
        {
            byte[] readBuffer = new byte[65536];
            Operation operation;
            int messageLength;
            int position;
            int remaining;

            while (!cancel.IsCancellationRequested)
            {
                try
                {
                    //                          |--4 bytes--|--4 bytes--|---N--|
                    // Every command is a TLV - | Operation |  Length   | Data |

                    int chunkLength;

                    // Read the operation.
                    this.readSocket.Receive(readBuffer, 4, SocketFlags.None);

                    // Check if we popped out of a receive call after we were shut down.
                    if (cancel.IsCancellationRequested)
                    {
                        break;
                    }

                    operation = (Operation)ByteWriter.ReadInt32_BE(readBuffer, 0);

                    // Read the length
                    this.readSocket.Receive(readBuffer, 4, SocketFlags.None);

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

                Receive(readBuffer, operation, messageLength);
            }
        }

        private void Receive(byte[] readBuffer, Operation operation, int messageLength)
        {
            byte[] dataCopy = new byte[messageLength];

            Array.Copy(readBuffer, 0, dataCopy, 0, messageLength);

            Message message = new Message(operation, dataCopy);

            Console.WriteLine($"[ServiceRequest] received {operation}");

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

            byte[] outBuffer = new byte[message.Data.Length + 8];

            ByteWriter.WriteInt32_BE((int)message.Operation, outBuffer, 0);
            ByteWriter.WriteInt32_BE(message.Data.Length, outBuffer, 4);

            Array.Copy(message.Data, 0, outBuffer, 8, message.Data.Length);

            readSocket.Send(outBuffer, 0, outBuffer.Length, SocketFlags.None);
        }

        private void HandleInit(Message message)
        {
            SecurityStatus status;

            if (initializing)
            {
                status = this.serverContext.AcceptToken(message.Data, out byte[] nextToken);

                Console.WriteLine($"[ServiceRequest] AcceptToken {status} | next {nextToken?.Length ?? 0}");

                if (status == SecurityStatus.OK || status == SecurityStatus.ContinueNeeded)
                {
                    if (nextToken != null && nextToken.Length > 0)
                    {
                        this.SendResponse(new Message(Operation.ServerToken, nextToken));
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
