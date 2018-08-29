using System;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;

namespace Shared
{
    [Serializable]
    public class Message
    {
        private static readonly BinaryFormatter binaryFormatter = new BinaryFormatter();

        public Message(Operation op)
        {
            this.Operation = op;
            //this.Data = data;

            //if ((this.Data?.Length ?? 0) <= 0)
            //{
            //    byte[] portBytes = new byte[0];

            //    if (delegatePort > 0)
            //    {
            //        portBytes = BitConverter.GetBytes(IPAddress.HostToNetworkOrder(delegatePort));
            //    }

            //    byte[] hostBytes = new byte[0];

            //    if (!string.IsNullOrWhiteSpace(delegateHost))
            //    {
            //        hostBytes = Encoding.UTF8.GetBytes(delegateHost);
            //    }

            //    byte[] s4uBytes = new byte[0];

            //    if (!string.IsNullOrWhiteSpace(s4uToken))
            //    {
            //        s4uBytes = new byte[] { (byte)'`' }.Concat(Encoding.UTF8.GetBytes(s4uToken)).ToArray();
            //    }

            //    this.Data = portBytes.Concat(hostBytes).Concat(s4uBytes).ToArray();
            //}
        }

        public Operation Operation { get; set; }

        public byte[] Token { get; internal set; }

        public int DelegatePort { get; set; }

        public string DelegateHost { get; set; }

        public string S4UToken { get; set; }

        public byte[] Serialize()
        {
            using (var stream = new MemoryStream())
            {
                binaryFormatter.Serialize(stream, this);

                return stream.ToArray();
            }
        }

        public static Message Deserialize(byte[] dataCopy)
        {
            using (var stream = new MemoryStream(dataCopy))
            {
                return (Message)binaryFormatter.Deserialize(stream);
            }
        }
    }
}
