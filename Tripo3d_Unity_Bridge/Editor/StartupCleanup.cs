using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Tripo3D.UnityBridge.Editor
{
    /// <summary>
    /// Startup cleanup for residual temporary files
    /// </summary>
    [InitializeOnLoad]
    public static class StartupCleanup
    {
        private const string IMPORT_FOLDER = "Assets/ImportedModels";
        private const string CLEANUP_PREF_KEY = "Tripo3D.LastCleanup";
        private const int CLEANUP_INTERVAL_HOURS = 24;

        static StartupCleanup()
        {
            EditorApplication.delayCall += PerformCleanup;
        }

        private static void PerformCleanup()
        {
            // Check if cleanup is needed (once per day)
            string lastCleanupStr = EditorPrefs.GetString(CLEANUP_PREF_KEY, "");
            
            if (!string.IsNullOrEmpty(lastCleanupStr))
            {
                if (DateTime.TryParse(lastCleanupStr, out DateTime lastCleanup))
                {
                    if ((DateTime.Now - lastCleanup).TotalHours < CLEANUP_INTERVAL_HOURS)
                    {
                        return; // Skip cleanup
                    }
                }
            }

            // Perform cleanup
            CleanupImportedModels();
            CleanupTempCache();

            // Update cleanup timestamp
            EditorPrefs.SetString(CLEANUP_PREF_KEY, DateTime.Now.ToString());
        }

        /// <summary>
        /// Clean up old imported model folders
        /// </summary>
        private static void CleanupImportedModels()
        {
            if (!Directory.Exists(IMPORT_FOLDER))
                return;

            try
            {
                var directories = Directory.GetDirectories(IMPORT_FOLDER);
                int cleanedCount = 0;

                foreach (var dir in directories)
                {
                    var dirInfo = new DirectoryInfo(dir);
                    
                    // Delete folders older than 7 days
                    if ((DateTime.Now - dirInfo.CreationTime).TotalDays > 7)
                    {
                        try
                        {
                            Directory.Delete(dir, true);
                            cleanedCount++;
                        }
                        catch
                        {
                            // Skip if locked
                        }
                    }
                }

                if (cleanedCount > 0)
                {
                    AssetDatabase.Refresh();
                    Debug.Log($"[Tripo3D] Cleaned up {cleanedCount} old imported model folder(s)");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Tripo3D] Cleanup warning: {ex.Message}");
            }
        }

        /// <summary>
        /// Clean up temporary cache files
        /// </summary>
        private static void CleanupTempCache()
        {
            try
            {
                string tempPath = UnityEngine.Application.temporaryCachePath;
                
                if (!Directory.Exists(tempPath))
                    return;

                var directories = Directory.GetDirectories(tempPath);
                int cleanedCount = 0;

                foreach (var dir in directories)
                {
                    // Clean up Tripo3D related temp folders
                    if (Path.GetFileName(dir).Length == 36) // GUID format
                    {
                        try
                        {
                            Directory.Delete(dir, true);
                            cleanedCount++;
                        }
                        catch
                        {
                            // Skip if locked
                        }
                    }
                }

                if (cleanedCount > 0)
                {
                    Debug.Log($"[Tripo3D] Cleaned up {cleanedCount} temporary folder(s)");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Tripo3D] Temp cleanup warning: {ex.Message}");
            }
        }


    }
}
