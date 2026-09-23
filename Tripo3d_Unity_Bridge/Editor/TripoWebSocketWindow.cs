using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Tripo3D.UnityBridge.Editor
{
    /// <summary>
    /// EditorWindow for WebSocket server control and monitoring
    /// </summary>
    public class TripoWebSocketWindow : EditorWindow
    {
        private WebSocketSharpServer _server;
        private Vector2 _scrollPosition;
        private List<string> _logMessages = new List<string>();
        private const int MAX_LOG_MESSAGES = 100;

        // UI State
        private bool _isConnected = false;
        private float _currentProgress = 0f;
        private string _currentFileName = "";
        private int _currentChunkIndex = 0;
        private int _totalChunks = 0;
        private float _lastProgressUpdate = 0f;
        private const float PROGRESS_UPDATE_THROTTLE = 0.5f; // 500ms throttle
        
        // Logo texture
        private Texture2D _logoTexture;

        [MenuItem("Tools/Tripo Bridge")]
        public static void ShowWindow()
        {
            var window = GetWindow<TripoWebSocketWindow>(Localization.Get(LocalizationKey.WindowTitle));
            window.minSize = new Vector2(400, 500);
            window.Show();
        }

        private void OnEnable()
        {
            // Load logo texture
            _logoTexture = Resources.Load<Texture2D>("TripoLogo");
            
            if (_server == null)
            {
                InitializeServer();
            }
            
            // Auto detect render pipeline on startup
            ModelImporter.DetectAndSetRenderPipeline();
            
            // Auto-start server when window opens
            if (!_server.IsRunning)
            {
                _server.Start();
            }
        }

        private void OnDisable()
        {
            // Stop server when window closes
            if (_server != null && _server.IsRunning)
            {
                _server.Stop();
            }
            
            // Unsubscribe from events
            LogHelper.OnLog -= AddLog;
        }

        private void OnDestroy()
        {
            if (_server != null && _server.IsRunning)
            {
                _server.Stop();
            }
            
            // Unsubscribe from events
            LogHelper.OnLog -= AddLog;
        }

        private void InitializeServer()
        {
            _server = new WebSocketSharpServer();
            LogHelper.OnLog -= AddLog;
            LogHelper.OnLog += AddLog;
            _server.OnConnectionStatusChanged += OnConnectionChanged;
            _server.OnProgressUpdate += OnProgressChanged;
            _server.OnFileTransferStarted += OnFileTransferStarted;
        }

        private void OnGUI()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            GUILayout.Space(15);
            // Logo
            DrawLogo();
            GUILayout.Space(10);
            
            // Server Control
            DrawServerControl();
            GUILayout.Space(10);

            // Status Display
            DrawStatusSection();
            GUILayout.Space(10);

            // Render Pipeline
            DrawRenderPipelineSection();
            GUILayout.Space(10);

            // Progress Bar
            DrawProgressSection();
            GUILayout.Space(10);

            // Log Messages
            DrawLogSection();

            EditorGUILayout.EndVertical();
        }
        
        private void DrawLogo()
        {
            if (_logoTexture != null)
            {
                GUILayout.Space(10);
                GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                GUILayout.Label(_logoTexture, GUILayout.Width(200), GUILayout.Height(60));
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
                GUILayout.Space(10);
            }
        }

        private void DrawServerControl()
        {
            EditorGUILayout.BeginHorizontal();
            
            bool isRunning = _server != null && _server.IsRunning;
            
            // Button color: gray/white when stopped, white/gray when running
            GUI.backgroundColor = isRunning ? Color.gray : new Color(0.5f, 1f, 0.5f);
            
            if (GUILayout.Button(isRunning ? Localization.Get(LocalizationKey.StopServer) : Localization.Get(LocalizationKey.StartServer), GUILayout.Height(30)))
            {
                if (isRunning)
                {
                    _server.Stop();
                }
                else
                {
                    if (_server == null)
                    {
                        InitializeServer();
                    }
                    _server.Start();
                }
            }
            
            GUI.backgroundColor = Color.white;
            
            EditorGUILayout.EndHorizontal();
        }

        private void DrawStatusSection()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            
            EditorGUILayout.LabelField(Localization.Get(LocalizationKey.Status), EditorStyles.boldLabel);
            
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(Localization.Get(LocalizationKey.Port), GUILayout.Width(100));
            EditorGUILayout.LabelField($"{ProtocolConstants.SERVER_PORT}");
            EditorGUILayout.EndHorizontal();
            
            // Connection with bold text and color
            EditorGUILayout.BeginHorizontal();
            GUIStyle boldStyle = new GUIStyle(EditorStyles.boldLabel);
            EditorGUILayout.LabelField(Localization.Get(LocalizationKey.Connection), GUILayout.Width(100));
            boldStyle.normal.textColor = _isConnected ? Color.green : Color.red;
            EditorGUILayout.LabelField(_isConnected ? Localization.Get(LocalizationKey.Connected) : Localization.Get(LocalizationKey.Disconnected), boldStyle);
            EditorGUILayout.EndHorizontal();
            
            if (!string.IsNullOrEmpty(_currentFileName))
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(Localization.Get(LocalizationKey.File), GUILayout.Width(100));
                EditorGUILayout.LabelField(System.IO.Path.GetFileNameWithoutExtension(_currentFileName));
                EditorGUILayout.EndHorizontal();
            }
            
            EditorGUILayout.EndVertical();
        }

        private void DrawRenderPipelineSection()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            
            EditorGUILayout.LabelField(Localization.Get(LocalizationKey.RenderPipeline), EditorStyles.boldLabel);
            ModelImporter.CurrentPipelineType = (ModelImporter.RenderPipelineType)EditorGUILayout.EnumPopup(
                Localization.Get(LocalizationKey.PipelineType), ModelImporter.CurrentPipelineType);
            
            EditorGUILayout.EndVertical();
        }

        private void DrawProgressSection()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            
            EditorGUILayout.LabelField(Localization.Get(LocalizationKey.Progress), EditorStyles.boldLabel);
            
            Rect progressRect = GUILayoutUtility.GetRect(18, 18, GUILayout.ExpandWidth(true));
            EditorGUI.ProgressBar(progressRect, _currentProgress, $"{(_currentProgress * 100):F1}%");
            
            EditorGUILayout.EndVertical();
        }

        private void DrawLogSection()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(Localization.Get(LocalizationKey.MessageLog), EditorStyles.boldLabel);
            
            if (GUILayout.Button(Localization.Get(LocalizationKey.Clear), GUILayout.Width(60)))
            {
                _logMessages.Clear();
            }
            
            EditorGUILayout.EndHorizontal();
            
            // Use TextArea for selectable text (like client)
            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition, GUILayout.Height(200));
            
            string logText = string.Join("\n", _logMessages);
            EditorGUILayout.TextArea(logText, EditorStyles.wordWrappedLabel, GUILayout.ExpandHeight(true));
            
            EditorGUILayout.EndScrollView();
            
            EditorGUILayout.EndVertical();
        }

        private void AddLog(string message)
        {
            // Ensure UI updates happen on main thread
            if (System.Threading.Thread.CurrentThread.ManagedThreadId != 1)
            {
                EditorApplication.delayCall += () => AddLog(message);
                return;
            }
            
            _logMessages.Add(message);
            
            // Limit log size
            if (_logMessages.Count > MAX_LOG_MESSAGES)
            {
                _logMessages.RemoveAt(0);
            }
            
            // Auto-scroll to bottom
            _scrollPosition.y = float.MaxValue;
            
            Repaint();
        }

        private void OnConnectionChanged(bool isConnected)
        {
            // Ensure UI updates happen on main thread
            if (System.Threading.Thread.CurrentThread.ManagedThreadId != 1)
            {
                EditorApplication.delayCall += () => OnConnectionChanged(isConnected);
                return;
            }
            
            _isConnected = isConnected;
            
            if (!isConnected)
            {
                _currentProgress = 0f;
                _currentFileName = "";
                _currentChunkIndex = 0;
                _totalChunks = 0;
            }
            
            Repaint();
        }

        private void OnProgressChanged(float progress)
        {
            // Ensure UI updates happen on main thread
            if (System.Threading.Thread.CurrentThread.ManagedThreadId != 1)
            {
                EditorApplication.delayCall += () => OnProgressChanged(progress);
                return;
            }
            
            // Throttle UI updates to 500ms
            if (Time.realtimeSinceStartup - _lastProgressUpdate < PROGRESS_UPDATE_THROTTLE)
            {
                return;
            }
            
            _lastProgressUpdate = Time.realtimeSinceStartup;
            _currentProgress = progress;
            
            Repaint();
        }

        private void OnFileTransferStarted(string fileId, string fileName, int chunkIndex, int chunkTotal)
        {
            // Ensure UI updates happen on main thread
            if (System.Threading.Thread.CurrentThread.ManagedThreadId != 1)
            {
                EditorApplication.delayCall += () => OnFileTransferStarted(fileId, fileName, chunkIndex, chunkTotal);
                return;
            }
            
            _currentFileName = fileName;
            _currentChunkIndex = chunkIndex;
            _totalChunks = chunkTotal;
            Repaint();
        }
    }
}
