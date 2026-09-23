using System;

namespace Tripo3D.UnityBridge.Editor
{
    // Base message structure
    [Serializable]
    public class WebSocketMessage
    {
        public string type;
    }

    // Handshake Messages
    [Serializable]
    public class HandshakeMessage
    {
        public string type = ProtocolConstants.MSG_HANDSHAKE;
        public HandshakePayload payload;
    }

    [Serializable]
    public class HandshakePayload
    {
        public string clientName;
        public string protocolVersion;
    }

    [Serializable]
    public class HandshakeAckMessage
    {
        public string type = ProtocolConstants.MSG_HANDSHAKE_ACK;
        public HandshakeAckPayload payload;
    }

    [Serializable]
    public class HandshakeAckPayload
    {
        public bool success;
        public string clientName;
        public string dccVersion;
        public string pluginVersion;
        public string protocolVersion;
    }

    // File Transfer Messages
    [Serializable]
    public class FileTransferMessage
    {
        public string type = ProtocolConstants.MSG_FILE_TRANSFER;
        public FileTransferPayload payload;
    }

    [Serializable]
    public class FileTransferPayload
    {
        public string fileId;
        public string fileName;
        public string fileType;
        public int chunkIndex;
        public int chunkTotal;
        public int chunkSize;
    }

    [Serializable]
    public class FileTransferAckMessage
    {
        public string type = ProtocolConstants.MSG_FILE_TRANSFER_ACK;
        public FileTransferAckPayload payload;
    }

    [Serializable]
    public class FileTransferAckPayload
    {
        public bool success;
        public string fileId;
        public int fileIndex;
        public int code;
    }

    [Serializable]
    public class FileTransferCompleteMessage
    {
        public string type = ProtocolConstants.MSG_FILE_TRANSFER_COMPLETE;
        public FileTransferCompletePayload payload;
    }

    [Serializable]
    public class FileTransferCompletePayload
    {
        public string fileId;
        public string status;
        public string message;
    }

    [Serializable]
    public class ImportCompleteMessage
    {
        public string type = ProtocolConstants.MSG_IMPORT_COMPLETE;
        public ImportCompletePayload payload;
    }

    [Serializable]
    public class ImportCompletePayload
    {
        public string fileId;
        public bool success;
        public string message;
    }

    // Heartbeat Messages
    [Serializable]
    public class PingMessage
    {
        public string type = ProtocolConstants.MSG_PING;
    }

    [Serializable]
    public class PongMessage
    {
        public string type = ProtocolConstants.MSG_PONG;
    }
}
