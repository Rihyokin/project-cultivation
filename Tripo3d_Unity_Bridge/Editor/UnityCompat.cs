using UnityEngine;

namespace Tripo3D.UnityBridge.Editor
{
    /// <summary>
    /// Central compatibility layer for Unity version-specific API differences,
    /// so call sites stay free of #if. Always keyed on Unity's built-in
    /// compile-time version defines (e.g. UNITY_6000_1_OR_NEWER), never on a
    /// custom define written at runtime by editor code (those are unset on the
    /// first compile and would select the wrong, already-obsolete branch).
    /// </summary>
    internal static class UnityCompat
    {
        /// <summary>
        /// Session-only tracking id for deduplication (not persisted).
        /// GetInstanceID() was replaced by GetEntityId() in Unity 6.1 (6000.1)
        /// and is a hard-error obsolete (CS0619) as of 6.5.
        /// </summary>
        public static string GetTrackingId(Object obj)
        {
#if UNITY_6000_1_OR_NEWER
            return obj.GetEntityId().ToString();
#else
            return obj.GetInstanceID().ToString();
#endif
        }
    }
}
