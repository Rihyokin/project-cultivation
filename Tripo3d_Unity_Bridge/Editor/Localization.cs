using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Tripo3D.UnityBridge.Editor
{
    /// <summary>
    /// Localization keys for UI text
    /// </summary>
    public enum LocalizationKey
    {
        WindowTitle,
        StartServer,
        StopServer,
        Status,
        Port,
        Connection,
        Connected,
        Disconnected,
        File,
        Progress,
        MessageLog,
        Clear,
        RenderPipeline,
        PipelineType
    }

    /// <summary>
    /// Localization manager for UI text
    /// </summary>
    public static class Localization
    {
        private static SystemLanguage _currentLanguage;
        private static Dictionary<LocalizationKey, string> _texts;

        static Localization()
        {
            _currentLanguage = GetPreferredLanguage();
            LoadLanguage();
        }

        /// <summary>
        /// Get localized text by key
        /// </summary>
        public static string Get(LocalizationKey key)
        {
            if (_texts == null)
            {
                LoadLanguage();
            }
            return _texts.TryGetValue(key, out string value) ? value : key.ToString();
        }

        private static void LoadLanguage()
        {
            switch (_currentLanguage)
            {
                case SystemLanguage.Chinese:
                case SystemLanguage.ChineseSimplified:
                case SystemLanguage.ChineseTraditional:
                    _texts = GetChineseTexts();
                    break;
                case SystemLanguage.Japanese:
                    _texts = GetJapaneseTexts();
                    break;
                case SystemLanguage.Korean:
                    _texts = GetKoreanTexts();
                    break;
                case SystemLanguage.Russian:
                    _texts = GetRussianTexts();
                    break;
                case SystemLanguage.French:
                    _texts = GetFrenchTexts();
                    break;
                case SystemLanguage.German:
                    _texts = GetGermanTexts();
                    break;
                case SystemLanguage.Spanish:
                    _texts = GetSpanishTexts();
                    break;
                case SystemLanguage.Portuguese:
                    _texts = GetPortugueseTexts();
                    break;
                default:
                    _texts = GetEnglishTexts();
                    break;
            }
        }

        private static SystemLanguage GetPreferredLanguage()
        {
            try
            {
                var localizationDatabaseType = typeof(EditorWindow).Assembly.GetType("UnityEditor.LocalizationDatabase");
                var currentEditorLanguageProperty = localizationDatabaseType?.GetProperty(
                    "currentEditorLanguage",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

                if (currentEditorLanguageProperty?.GetValue(null) is SystemLanguage editorLanguage)
                    return editorLanguage;
            }
            catch
            {
                // Fall back to the system language below if Unity changes this internal API.
            }

            return Application.systemLanguage;
        }

        private static Dictionary<LocalizationKey, string> GetEnglishTexts()
        {
            return new Dictionary<LocalizationKey, string>
            {
                { LocalizationKey.WindowTitle, "Tripo Bridge" },
                { LocalizationKey.StartServer, "Start Server" },
                { LocalizationKey.StopServer, "Stop Server" },
                { LocalizationKey.Status, "Status" },
                { LocalizationKey.Port, "Port:" },
                { LocalizationKey.Connection, "Tripo Studio" },
                { LocalizationKey.Connected, "Connected" },
                { LocalizationKey.Disconnected, "Disconnected" },
                { LocalizationKey.File, "Receiving File:" },
                { LocalizationKey.Progress, "Progress" },
                { LocalizationKey.MessageLog, "Message Log" },
                { LocalizationKey.Clear, "Clear" },
                { LocalizationKey.RenderPipeline, "Render Pipeline" },
                { LocalizationKey.PipelineType, "Pipeline Type" }
            };
        }

        private static Dictionary<LocalizationKey, string> GetChineseTexts()
        {
            return new Dictionary<LocalizationKey, string>
            {
                { LocalizationKey.WindowTitle, "Tripo Bridge" },
                { LocalizationKey.StartServer, "启动服务器" },
                { LocalizationKey.StopServer, "停止服务器" },
                { LocalizationKey.Status, "状态" },
                { LocalizationKey.Port, "端口：" },
                { LocalizationKey.Connection, "Tripo Studio" },
                { LocalizationKey.Connected, "已连接" },
                { LocalizationKey.Disconnected, "未连接" },
                { LocalizationKey.File, "接收文件：" },
                { LocalizationKey.Progress, "进度" },
                { LocalizationKey.MessageLog, "消息日志" },
                { LocalizationKey.Clear, "清空" },
                { LocalizationKey.RenderPipeline, "渲染管线" },
                { LocalizationKey.PipelineType, "管线类型" }
            };
        }

        private static Dictionary<LocalizationKey, string> GetJapaneseTexts()
        {
            return new Dictionary<LocalizationKey, string>
            {
                { LocalizationKey.WindowTitle, "Tripo Bridge" },
                { LocalizationKey.StartServer, "サーバーを起動" },
                { LocalizationKey.StopServer, "サーバーを停止" },
                { LocalizationKey.Status, "ステータス" },
                { LocalizationKey.Port, "ポート：" },
                { LocalizationKey.Connection, "Tripo Studio" },
                { LocalizationKey.Connected, "接続済み" },
                { LocalizationKey.Disconnected, "未接続" },
                { LocalizationKey.File, "ファイルを受信中：" },
                { LocalizationKey.Progress, "進行状況" },
                { LocalizationKey.MessageLog, "メッセージログ" },
                { LocalizationKey.Clear, "クリア" },
                { LocalizationKey.RenderPipeline, "レンダリングパイプライン" },
                { LocalizationKey.PipelineType, "パイプラインタイプ" }
            };
        }

        private static Dictionary<LocalizationKey, string> GetKoreanTexts()
        {
            return new Dictionary<LocalizationKey, string>
            {
                { LocalizationKey.WindowTitle, "Tripo Bridge" },
                { LocalizationKey.StartServer, "서버 시작" },
                { LocalizationKey.StopServer, "서버 중지" },
                { LocalizationKey.Status, "상태" },
                { LocalizationKey.Port, "포트：" },
                { LocalizationKey.Connection, "Tripo Studio" },
                { LocalizationKey.Connected, "연결됨" },
                { LocalizationKey.Disconnected, "연결 안 됨" },
                { LocalizationKey.File, "파일 수신 중：" },
                { LocalizationKey.Progress, "진행률" },
                { LocalizationKey.MessageLog, "메시지 로그" },
                { LocalizationKey.Clear, "지우기" },
                { LocalizationKey.RenderPipeline, "렌더 파이프라인" },
                { LocalizationKey.PipelineType, "파이프라인 유형" }
            };
        }

        private static Dictionary<LocalizationKey, string> GetRussianTexts()
        {
            return new Dictionary<LocalizationKey, string>
            {
                { LocalizationKey.WindowTitle, "Tripo Bridge" },
                { LocalizationKey.StartServer, "Запустить сервер" },
                { LocalizationKey.StopServer, "Остановить сервер" },
                { LocalizationKey.Status, "Статус" },
                { LocalizationKey.Port, "Порт：" },
                { LocalizationKey.Connection, "Tripo Studio" },
                { LocalizationKey.Connected, "Подключено" },
                { LocalizationKey.Disconnected, "Отключено" },
                { LocalizationKey.File, "Получение файла：" },
                { LocalizationKey.Progress, "Прогресс" },
                { LocalizationKey.MessageLog, "Журнал сообщений" },
                { LocalizationKey.Clear, "Очистить" },
                { LocalizationKey.RenderPipeline, "Конвейер рендеринга" },
                { LocalizationKey.PipelineType, "Тип конвейера" }
            };
        }

        private static Dictionary<LocalizationKey, string> GetFrenchTexts()
        {
            return new Dictionary<LocalizationKey, string>
            {
                { LocalizationKey.WindowTitle, "Tripo Bridge" },
                { LocalizationKey.StartServer, "Démarrer le serveur" },
                { LocalizationKey.StopServer, "Arrêter le serveur" },
                { LocalizationKey.Status, "Statut" },
                { LocalizationKey.Port, "Port :" },
                { LocalizationKey.Connection, "Tripo Studio" },
                { LocalizationKey.Connected, "Connecté" },
                { LocalizationKey.Disconnected, "Déconnecté" },
                { LocalizationKey.File, "Réception du fichier :" },
                { LocalizationKey.Progress, "Progression" },
                { LocalizationKey.MessageLog, "Journal des messages" },
                { LocalizationKey.Clear, "Effacer" },
                { LocalizationKey.RenderPipeline, "Pipeline de Rendu" },
                { LocalizationKey.PipelineType, "Type de Pipeline" }
            };
        }

        private static Dictionary<LocalizationKey, string> GetGermanTexts()
        {
            return new Dictionary<LocalizationKey, string>
            {
                { LocalizationKey.WindowTitle, "Tripo Bridge" },
                { LocalizationKey.StartServer, "Server starten" },
                { LocalizationKey.StopServer, "Server stoppen" },
                { LocalizationKey.Status, "Status" },
                { LocalizationKey.Port, "Port:" },
                { LocalizationKey.Connection, "Tripo Studio" },
                { LocalizationKey.Connected, "Verbunden" },
                { LocalizationKey.Disconnected, "Getrennt" },
                { LocalizationKey.File, "Datei empfangen:" },
                { LocalizationKey.Progress, "Fortschritt" },
                { LocalizationKey.MessageLog, "Nachrichtenprotokoll" },
                { LocalizationKey.Clear, "Löschen" },
                { LocalizationKey.RenderPipeline, "Render-Pipeline" },
                { LocalizationKey.PipelineType, "Pipeline-Typ" }
            };
        }

        private static Dictionary<LocalizationKey, string> GetSpanishTexts()
        {
            return new Dictionary<LocalizationKey, string>
            {
                { LocalizationKey.WindowTitle, "Tripo Bridge" },
                { LocalizationKey.StartServer, "Iniciar servidor" },
                { LocalizationKey.StopServer, "Detener servidor" },
                { LocalizationKey.Status, "Estado" },
                { LocalizationKey.Port, "Puerto:" },
                { LocalizationKey.Connection, "Tripo Studio" },
                { LocalizationKey.Connected, "Conectado" },
                { LocalizationKey.Disconnected, "Desconectado" },
                { LocalizationKey.File, "Recibiendo archivo:" },
                { LocalizationKey.Progress, "Progreso" },
                { LocalizationKey.MessageLog, "Registro de mensajes" },
                { LocalizationKey.Clear, "Limpiar" },
                { LocalizationKey.RenderPipeline, "Pipeline de Render" },
                { LocalizationKey.PipelineType, "Tipo de Pipeline" }
            };
        }

        private static Dictionary<LocalizationKey, string> GetPortugueseTexts()
        {
            return new Dictionary<LocalizationKey, string>
            {
                { LocalizationKey.WindowTitle, "Tripo Bridge" },
                { LocalizationKey.StartServer, "Iniciar servidor" },
                { LocalizationKey.StopServer, "Parar servidor" },
                { LocalizationKey.Status, "Status" },
                { LocalizationKey.Port, "Porta:" },
                { LocalizationKey.Connection, "Tripo Studio" },
                { LocalizationKey.Connected, "Conectado" },
                { LocalizationKey.Disconnected, "Desconectado" },
                { LocalizationKey.File, "Recebendo arquivo:" },
                { LocalizationKey.Progress, "Progresso" },
                { LocalizationKey.MessageLog, "Registro de mensagens" },
                { LocalizationKey.Clear, "Limpar" },
                { LocalizationKey.RenderPipeline, "Pipeline de Render" },
                { LocalizationKey.PipelineType, "Tipo de Pipeline" }
            };
        }
    }
}
