using System;
using System.Collections.Generic;
using System.Linq;

namespace Tripo3D.UnityBridge.Editor
{
    /// <summary>
    /// Manages file transfer sessions and chunk assembly
    /// </summary>
    public class FileTransferManager
    {
        private readonly Dictionary<string, FileTransferSession> _sessions = new Dictionary<string, FileTransferSession>();

        /// <summary>
        /// Add a chunk to the session
        /// </summary>
        public void AddChunk(string fileId, string fileName, string fileType, int chunkIndex, int chunkTotal, byte[] chunkData)
        {
            if (!_sessions.ContainsKey(fileId))
            {
                _sessions[fileId] = new FileTransferSession(fileId, fileName, fileType, chunkTotal);
            }

            _sessions[fileId].AddChunk(chunkIndex, chunkData);
        }

        /// <summary>
        /// Check if file transfer is complete
        /// </summary>
        public bool IsComplete(string fileId)
        {
            return _sessions.ContainsKey(fileId) && _sessions[fileId].IsComplete();
        }

        /// <summary>
        /// Assemble all chunks into complete file
        /// </summary>
        public byte[] AssembleFile(string fileId)
        {
            if (!_sessions.ContainsKey(fileId))
            {
                throw new InvalidOperationException($"Session not found: {fileId}");
            }

            return _sessions[fileId].Assemble();
        }

        /// <summary>
        /// Remove session after processing
        /// </summary>
        public void RemoveSession(string fileId)
        {
            _sessions.Remove(fileId);
        }

        /// <summary>
        /// Clear all sessions
        /// </summary>
        public void Clear()
        {
            _sessions.Clear();
        }
    }

    /// <summary>
    /// Represents a single file transfer session
    /// </summary>
    public class FileTransferSession
    {
        public string FileId { get; }
        public string FileName { get; }
        public string FileType { get; }
        public int TotalChunks { get; }
        
        private readonly Dictionary<int, byte[]> _chunks = new Dictionary<int, byte[]>();

        public FileTransferSession(string fileId, string fileName, string fileType, int totalChunks)
        {
            FileId = fileId;
            FileName = fileName;
            FileType = fileType;
            TotalChunks = totalChunks;
        }

        /// <summary>
        /// Add chunk to session
        /// </summary>
        public void AddChunk(int index, byte[] data)
        {
            _chunks[index] = data;
        }

        /// <summary>
        /// Check if all chunks received
        /// </summary>
        public bool IsComplete()
        {
            return _chunks.Count == TotalChunks;
        }

        /// <summary>
        /// Assemble all chunks into complete file data
        /// </summary>
        public byte[] Assemble()
        {
            if (!IsComplete())
            {
                var missing = Enumerable.Range(0, TotalChunks).Where(i => !_chunks.ContainsKey(i)).ToList();
                throw new InvalidOperationException($"Missing chunks: {string.Join(", ", missing)}");
            }

            // Calculate total size
            int totalSize = _chunks.Values.Sum(chunk => chunk.Length);
            byte[] result = new byte[totalSize];
            
            // Copy chunks in order
            int offset = 0;
            for (int i = 0; i < TotalChunks; i++)
            {
                byte[] chunk = _chunks[i];
                Array.Copy(chunk, 0, result, offset, chunk.Length);
                offset += chunk.Length;
            }

            return result;
        }
    }
}
