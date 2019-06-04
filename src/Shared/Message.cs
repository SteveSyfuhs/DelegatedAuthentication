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
