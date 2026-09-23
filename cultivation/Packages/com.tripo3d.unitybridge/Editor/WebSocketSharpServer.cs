using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using UnityEngine;
using WebSocketSharp;
using WebSocketSharp.Server;

namespace Tripo3D.UnityBridge.Editor
{
    /// <summary>
    /// WebSocket server using websocket-sharp library
    /// </summary>
    public class WebSocketSharpServer
    {
        private WebSocketServer _server;
        private bool _isRunning;
        private readonly FileTransferManager _transferManager = new FileTransferManager();
        
        public event Action<bool> OnConnectionStatusChanged;
        public event Action<float> OnProgressUpdate;
        public event Action<string, string, int, int> OnFileTransferStarted;

        public bool IsRunning => _isRunning;

        public void Start()
        {
            if (_isRunning)
            {
                LogHelper.Log("Server already running");
                return;
            }

            try
            {
                // Setup static context for behavior class
                TripoWebSocketBehavior.FileTransferManager = _transferManager;
                TripoWebSocketBehavior.OnConnectionChanged = (connected) =>
                {
                    UnityEditor.EditorApplication.delayCall += () => OnConnectionStatusChanged?.Invoke(connected);
                };
                TripoWebSocketBehavior.OnProgressChanged = (progress) =>
                {
                    UnityEditor.EditorApplication.delayCall += () => OnProgressUpdate?.Invoke(progress);
                };
                TripoWebSocketBehavior.OnFileTransferStart = (fileId, fileName, chunkIndex, chunkTotal) =>
                {
                    UnityEditor.EditorApplication.delayCall += () => OnFileTransferStarted?.Invoke(fileId, fileName, chunkIndex, chunkTotal);
                };
                
                string url = $"ws://{ProtocolConstants.SERVER_HOST}:{ProtocolConstants.SERVER_PORT}";
                _server = new WebSocketServer(url);
                _server.AddWebSocketService<TripoWebSocketBehavior>("/");
                
                _server.Start();
                _isRunning = true;
                
                LogHelper.Log($"WebSocket server started on {url}");
                LogHelper.Log("Waiting for client connections...");
            }
            catch (Exception ex)
            {
                LogHelper.Error($"Failed to start server: {ex.Message}");
                Stop();
            }
        }

        public void Stop()
        {
            if (!_isRunning)
                return;

            try
            {
                _server?.Stop();
                _transferManager.Clear();
                _isRunning = false;
                
                OnConnectionStatusChanged?.Invoke(false);
                LogHelper.Log("WebSocket server stopped");
            }
            catch (Exception ex)
            {
                LogHelper.Error($"Stop error: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// WebSocket behavior for handling client connections
    /// </summary>
    public class TripoWebSocketBehavior : WebSocketBehavior
    {
        // Static shared state (set by WebSocketSharpServer)
        public static FileTransferManager FileTransferManager { get; set; }
        public static Action<bool> OnConnectionChanged { get; set; }
        public static Action<float> OnProgressChanged { get; set; }
        public static Action<string, string, int, int> OnFileTransferStart { get; set; }

        protected override void OnOpen()
        {
            LogHelper.Log($"Client connected: {ID}");
            OnConnectionChanged?.Invoke(true);
            
            // Send handshake acknowledgment
            var ackMessage = new HandshakeAckMessage
            {
                payload = new HandshakeAckPayload
                {
                    success = true,
                    clientName = ProtocolConstants.CLIENT_NAME,
                    dccVersion = UnityEngine.Application.unityVersion,
                    pluginVersion = ProtocolConstants.PROTOCOL_VERSION,
                    protocolVersion = ProtocolConstants.PROTOCOL_VERSION
                }
            };
            
            Send(JsonUtility.ToJson(ackMessage));
        }

        protected override void OnClose(CloseEventArgs e)
        {
            LogHelper.Log($"Client disconnected: {ID} (Code: {e.Code}, Reason: {e.Reason})");
            OnConnectionChanged?.Invoke(false);
        }

        protected override void OnError(ErrorEventArgs e)
        {
            LogHelper.Error(e.Message);
        }

        protected override void OnMessage(MessageEventArgs e)
        {
            try
            {
                if (e.IsText)
                {
                    ProcessTextMessage(e.Data);
                }
                else if (e.IsBinary)
                {
                    ProcessBinaryMessage(e.RawData);
                }
            }
            catch (Exception ex)
            {
                LogHelper.Error($"Message processing error: {ex.Message}");
            }
        }

        private void ProcessTextMessage(string message)
        {
            try
            {
                var baseMessage = JsonUtility.FromJson<WebSocketMessage>(message);
                
                if (baseMessage.type == ProtocolConstants.MSG_HANDSHAKE)
                {
                    var handshake = JsonUtility.FromJson<HandshakeMessage>(message);
                    LogHelper.Log($"Handshake from {handshake.payload.clientName}");
                }
                else if (baseMessage.type == ProtocolConstants.MSG_PING)
                {
                    // LogHelper.Log("Ping received, sending pong...");
                    var pongMessage = new PongMessage();
                    Send(JsonUtility.ToJson(pongMessage));
                    // LogHelper.Log("Pong sent");
                }
            }
            catch (Exception ex)
            {
                LogHelper.Error($"Text message error: {ex.Message}");
            }
        }

        private void ProcessBinaryMessage(byte[] data)
        {
            try
            {
                // Find JSON end
                int jsonEnd = FindJsonEnd(data);
                if (jsonEnd <= 0)
                {
                    LogHelper.Log("Invalid binary message format");
                    SendError("", 0, ProtocolConstants.ERROR_INVALID_JSON);
                    return;
                }
                
                string jsonString = Encoding.UTF8.GetString(data, 0, jsonEnd);
                
                // Check message type first
                var baseMessage = JsonUtility.FromJson<WebSocketMessage>(jsonString);
                
                // Handle ping message in binary format
                if (baseMessage.type == ProtocolConstants.MSG_PING)
                {
                    // LogHelper.Log("Ping received (binary format), sending pong...");
                    var pongMessage = new PongMessage();
                    Send(JsonUtility.ToJson(pongMessage));
                    // LogHelper.Log("Pong sent");
                    return;
                }

                // Handle handshake message in binary format
                if (baseMessage.type == ProtocolConstants.MSG_HANDSHAKE)
                {
                    var handshake = JsonUtility.FromJson<HandshakeMessage>(jsonString);
                    // LogHelper.Log($"Handshake from {handshake.payload?.clientName} (binary format)");
                    return;
                }

                // Only handle file transfer messages
                if (baseMessage.type != ProtocolConstants.MSG_FILE_TRANSFER)
                {
                    LogHelper.Log($"Ignoring unknown binary message type: {baseMessage.type}");
                    return;
                }

                // Handle file transfer
                var transferMessage = JsonUtility.FromJson<FileTransferMessage>(jsonString);
                var payload = transferMessage?.payload;

                if (payload == null)
                {
                    LogHelper.Error("Binary message error: payload is null");
                    SendError("", 0, ProtocolConstants.ERROR_INVALID_JSON);
                    return;
                }

                if (string.IsNullOrEmpty(payload.fileId))
                {
                    LogHelper.Error($"Binary message error: fileId is null or empty. JSON: {jsonString}");
                    SendError("", 0, ProtocolConstants.ERROR_INVALID_JSON);
                    return;
                }

                // Extract binary data
                byte[] chunkData = new byte[data.Length - jsonEnd];
                Array.Copy(data, jsonEnd, chunkData, 0, chunkData.Length);
                
                // Report progress
                if (payload.chunkIndex == 0)
                {
                    OnFileTransferStart?.Invoke(payload.fileId, payload.fileName, payload.chunkIndex, payload.chunkTotal);
                }
                
                float progress = (float)payload.chunkIndex / payload.chunkTotal;
                OnProgressChanged?.Invoke(progress);
                
                // Store chunk
                FileTransferManager.AddChunk(payload.fileId, payload.fileName, payload.fileType,
                    payload.chunkIndex, payload.chunkTotal, chunkData);
                
                // Send ACK
                var ackMessage = new FileTransferAckMessage
                {
                    payload = new FileTransferAckPayload
                    {
                        success = true,
                        fileId = payload.fileId,
                        fileIndex = payload.chunkIndex
                    }
                };
                Send(JsonUtility.ToJson(ackMessage));
                
                // Check if complete
                if (FileTransferManager.IsComplete(payload.fileId))
                {
                    LogHelper.Log($"File transfer complete: {payload.fileName}");
                    
                    // Send file transfer complete message
                    var completeMessage = new FileTransferCompleteMessage
                    {
                        payload = new FileTransferCompletePayload
                        {
                            fileId = payload.fileId,
                            status = "importing",
                            message = "File transfer complete, importing model..."
                        }
                    };
                    Send(JsonUtility.ToJson(completeMessage));
                    
                    // Assemble and immediately remove session to prevent duplicate imports
                    // if the same fileId is re-transferred before delayCall fires
                    byte[] fileData = FileTransferManager.AssembleFile(payload.fileId);
                    FileTransferManager.RemoveSession(payload.fileId);
                    
                    // Process file on main thread
                    UnityEditor.EditorApplication.delayCall += () =>
                    {
                        ProcessReceivedFile(payload.fileId, payload.fileName, payload.fileType, fileData);
                    };
                }
            }
            catch (Exception ex)
            {
                LogHelper.Error($"Binary message error: {ex.Message}");
                SendError("", 0, ProtocolConstants.ERROR_PROCESSING);
            }
        }

        private void ProcessReceivedFile(string fileId, string fileName, string fileType, byte[] fileData)
        {
            try
            {
                var importer = new ModelImporter();
                importer.OnProgressUpdate += (progress) => OnProgressChanged?.Invoke(progress);
                
                bool success = importer.ImportModel(fileId, fileName, fileType, fileData);
                
                var importMessage = new ImportCompleteMessage
                {
                    payload = new ImportCompletePayload
                    {
                        fileId = fileId,
                        success = success,
                        message = success ? "Model imported successfully" : "Import failed"
                    }
                };
                
                Send(JsonUtility.ToJson(importMessage));
                
                if (success)
                {
                    LogHelper.Log($"Import complete: {fileName}");
                }
            }
            catch (Exception ex)
            {
                LogHelper.Error($"Import error: {ex.Message}");
                
                var errorMessage = new ImportCompleteMessage
                {
                    payload = new ImportCompletePayload
                    {
                        fileId = fileId,
                        success = false,
                        message = ex.Message
                    }
                };
                
                Send(JsonUtility.ToJson(errorMessage));
            }
        }

        private void SendError(string fileId, int fileIndex, int code)
        {
            var ackMessage = new FileTransferAckMessage
            {
                payload = new FileTransferAckPayload
                {
                    success = false,
                    fileId = fileId,
                    fileIndex = fileIndex,
                    code = code
                }
            };
            
            Send(JsonUtility.ToJson(ackMessage));
        }

        private int FindJsonEnd(byte[] data)
        {
            int braceCount = 0;
            bool inString = false;
            bool escape = false;
            
            for (int i = 0; i < data.Length; i++)
            {
                if (data[i] >= 128) continue;
                
                char c = (char)data[i];
                
                if (escape)
                {
                    escape = false;
                    continue;
                }
                
                if (c == '\\')
                {
                    escape = true;
                    continue;
                }
                
                if (c == '"')
                {
                    inString = !inString;
                    continue;
                }
                
                if (!inString)
                {
                    if (c == '{')
                    {
                        braceCount++;
                    }
                    else if (c == '}')
                    {
                        braceCount--;
                        if (braceCount == 0)
                        {
                            return i + 1;
                        }
                    }
                }
            }
            
            return -1;
        }
    }
}
