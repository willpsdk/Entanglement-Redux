using System;

using Entanglement.Sync;

namespace Entanglement.Network
{
    [Net.SkipHandleOnLoading]
    public class FileTransferChunkMessageHandler : NetworkMessageHandler<FileTransferChunkData>
    {
        public override byte? MessageIndex => BuiltInMessageType.FileTransferChunk;

        public override NetworkMessage CreateMessage(FileTransferChunkData data)
        {
            NetworkMessage message = new NetworkMessage();

            message.messageData = new byte[sizeof(ushort) + data.chunk.Length];
            message.messageData[0] = (byte)(data.transferId & 0xFF);
            message.messageData[1] = (byte)(data.transferId >> 8);
            Array.Copy(data.chunk, 0, message.messageData, 2, data.chunk.Length);

            return message;
        }

        public override void HandleMessage(NetworkMessage message, long sender)
        {
            if (message.messageData.Length < 2)
                return;

            ushort transferId = (ushort)(message.messageData[0] | (message.messageData[1] << 8));

            byte[] chunk = new byte[message.messageData.Length - 2];
            Array.Copy(message.messageData, 2, chunk, 0, chunk.Length);

            FileTransferManager.OnChunkReceived(sender, new FileTransferChunkData { transferId = transferId, chunk = chunk });
        }
    }

    public class FileTransferChunkData : NetworkMessageData {
        public ushort transferId;
        public byte[] chunk;
    }
}
