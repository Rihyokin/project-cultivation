using System;
using UnityEngine;

namespace Tripo3D.UnityBridge.Editor
{
    /// <summary>
    /// Centralized logging utility for Tripo3D Unity Bridge
    /// </summary>
    public static class LogHelper
    {
        public static event Action<string> OnLog;

        /// <summary>
        /// Log a message with timestamp
        /// </summary>
        public static void Log(string message)
        {
            string formattedMessage = $"[{DateTime.Now:HH:mm:ss}] {message}";
            OnLog?.Invoke(formattedMessage);
        }

        /// <summary>
        /// Write diagnostic details to the Unity Console without adding them to the UI log.
        /// </summary>
        public static void Verbose(string message)
        {
            Debug.Log($"[Tripo3D] {message}");
        }

        /// <summary>
        /// Log an error message
        /// </summary>
        public static void Error(string message)
        {
            Log($"ERROR: {message}");
        }

        /// <summary>
        /// Log a warning message
        /// </summary>
        public static void Warning(string message)
        {
            Log($"WARNING: {message}");
        }
    }
}
