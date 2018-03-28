using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace Shared
{
    public class Message
    {
        public Message(Operation op, byte[] data, int delegatePort = 0, string delegateHost = null)
        {
            this.Operation = op;
            this.Data = data;

            if ((this.Data?.Length ?? 0) <= 0)
            {
                byte[] portBytes = new byte[0];

                if (delegatePort > 0)
                {
                    portBytes = BitConverter.GetBytes(IPAddress.HostToNetworkOrder(delegatePort));
                }

                byte[] hostBytes = new byte[0];

                if (!string.IsNullOrWhiteSpace(delegateHost))
                {
                    hostBytes = Encoding.UTF8.GetBytes(delegateHost);
                }

                this.Data = portBytes.Concat(hostBytes).ToArray();
            }
        }

        public Operation Operation { get; private set; }

        public byte[] Data { get; private set; }
    }
}
