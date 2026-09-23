namespace Tripo3D.UnityBridge.Editor
{
    /// <summary>
    /// WebSocket protocol constants and message types
    /// </summary>
    public static class ProtocolConstants
    {
        // Server Configuration
        public const string SERVER_HOST = "127.0.0.1";
        public const int SERVER_PORT = 60610;
        public const string PROTOCOL_VERSION = "1.0.0";
        public const string CLIENT_NAME = "Unity";
        
        // Message Types
        public const string MSG_HANDSHAKE = "handshake";
        public const string MSG_HANDSHAKE_ACK = "handshake_ack";
        public const string MSG_PING = "ping";
        public const string MSG_PONG = "pong";
        public const string MSG_FILE_TRANSFER = "file_transfer";
        public const string MSG_FILE_TRANSFER_ACK = "file_transfer_ack";
        public const string MSG_FILE_TRANSFER_COMPLETE = "file_transfer_complete";
        public const string MSG_IMPORT_COMPLETE = "import_complete";
        
        // Transfer Settings
        public const int CHUNK_SIZE = 5 * 1024 * 1024; // 5MB
        public const int HEARTBEAT_INTERVAL_MS = 1000; // 1 second
        public const int HEARTBEAT_TIMEOUT_MS = 30000; // 30 seconds
        
        // Error Codes
        public const int ERROR_INVALID_JSON = 1001;
        public const int ERROR_PROCESSING = 1002;
        
        // File Formats
        public static readonly string[] SUPPORTED_FORMATS = { ".fbx", ".obj", ".zip" };
    }
}
