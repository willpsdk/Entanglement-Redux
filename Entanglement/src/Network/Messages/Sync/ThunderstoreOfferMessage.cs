using System;
using System.Text;

using Entanglement.Sync;

namespace Entanglement.Network
{
    public class ThunderstoreOfferMessageHandler : NetworkMessageHandler<ThunderstoreOfferData>
    {
        public override byte? MessageIndex => BuiltInMessageType.ThunderstoreOffer;

        public override NetworkMessage CreateMessage(ThunderstoreOfferData data) {
            byte[] author = Encoding.UTF8.GetBytes(data.author ?? "");
            byte[] name = Encoding.UTF8.GetBytes(data.name ?? "");
            byte[] version = Encoding.UTF8.GetBytes(data.version ?? "");
            byte[] fileName = Encoding.UTF8.GetBytes(data.fileName ?? "");
            byte[] note = Encoding.UTF8.GetBytes(data.note ?? "");

            NetworkMessage message = new NetworkMessage();
            message.messageData = new byte[6 + author.Length + name.Length + version.Length + fileName.Length + note.Length];

            int index = 0;
            message.messageData[index++] = (byte)data.category;
            index = Write(message.messageData, index, author);
            index = Write(message.messageData, index, name);
            index = Write(message.messageData, index, version);
            index = Write(message.messageData, index, fileName);
            Write(message.messageData, index, note);
            return message;
        }

        public override void HandleMessage(NetworkMessage message, long sender) {
            if (message.messageData == null || message.messageData.Length < 6)
                return;

            int index = 0;
            var data = new ThunderstoreOfferData();
            data.category = (FileTransferCategory)message.messageData[index++];
            data.author = Read(message.messageData, ref index);
            data.name = Read(message.messageData, ref index);
            data.version = Read(message.messageData, ref index);
            data.fileName = Read(message.messageData, ref index);
            data.note = Read(message.messageData, ref index);
            Thunderstore.AcceptOffer(sender, data);
        }

        static int Write(byte[] dest, int index, byte[] value) {
            int count = value == null ? 0 : value.Length;
            if (count > 255)
                count = 255;
            dest[index++] = (byte)count;
            if (count > 0)
                Array.Copy(value, 0, dest, index, count);
            return index + count;
        }

        static string Read(byte[] data, ref int index) {
            if (index >= data.Length)
                return "";
            int count = data[index++];
            if (count <= 0 || index + count > data.Length) {
                index = data.Length;
                return "";
            }
            string text = Encoding.UTF8.GetString(data, index, count);
            index += count;
            return text;
        }
    }

    public class ThunderstoreOfferData : NetworkMessageData {
        public FileTransferCategory category;
        public string author;
        public string name;
        public string version;
        public string fileName;
        public string note;
    }
}
