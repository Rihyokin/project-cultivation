using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.Collections.Generic;
using System.IO;

/// <summary>
/// 【约定】改 UI / 角色这类"通用装配"时，必须同步到**所有实际游玩场景**。
///
/// 目前实际游玩场景 = 3C_Testbed / Sect / Demon-Suppressing Tower（见 <see cref="游玩场景"/>）。
///
/// 实现要诀：**不能用 Instantiate 复制**（那样副本里的引用还指向源场景的物体 ✗）。
/// 正确做法是「复制一份源场景 → 附加打开 → `MoveGameObjectToScene` 把根物体搬过去」——
/// 搬移会被 Unity 自动重映射引用 ✓，而且源场景**只读**（改的是副本，副本最后丢弃）。
/// </summary>
public static class SceneRigSyncer
{
    const string 源场景 = "Assets/Scenes/3C_Testbed.scene";
    const string 临时场景 = "Assets/Scenes/__RigSyncTemp.scene";

    static readonly string[] 游玩场景 = {
        "Assets/Scenes/3C_Testbed.scene",
        "Assets/Scenes/Sect.scene",
        "Assets/Scenes/Demon-Suppressing Tower.scene",
    };

    /// <summary>通用装配的根物体。平行光不在内 —— 各场景的氛围各自保留。</summary>
    static readonly string[] 通用根 = {
        "Player", "Main Camera", "EventSystem", "HudCanvas", "CharacterUI", "暂停菜单", "SpawnPoint",
    };

    /// <summary>这几个根总是用源场景的覆盖（相机必须带跟随脚本，否则"能玩"不成立）</summary>
    static readonly string[] 总是覆盖 = { "Main Camera" };

    [MenuItem("修仙/同步通用角色与UI到所有游玩场景")]
    public static void SyncMenu() => Sync();

    public static void Sync()
    {
        if (!File.Exists(源场景)) { Debug.LogError("[装配同步] 源场景不存在：" + 源场景); return; }

        // 有未保存改动时让用户决定（不要默默丢掉别人正在编的东西）
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) { Debug.Log("[装配同步] 用户取消"); return; }

        string 原活动场景 = EditorSceneManager.GetActiveScene().path;
        AssetDatabase.DeleteAsset(临时场景);
        if (!AssetDatabase.CopyAsset(源场景, 临时场景)) { Debug.LogError("[装配同步] 复制源场景失败"); return; }

        try
        {
            foreach (var 目标 in 游玩场景)
            {
                if (目标 == 源场景) continue;
                if (!File.Exists(目标)) { Debug.LogWarning("[装配同步] 跳过（不存在）：" + 目标); continue; }

                var 目标场景 = EditorSceneManager.OpenScene(目标, OpenSceneMode.Single);
                var 目标根 = new Dictionary<string, GameObject>();
                foreach (var g in 目标场景.GetRootGameObjects()) 目标根[g.name] = g;

                var 动作 = new List<string>();
                foreach (var k in 通用根)
                {
                    bool 有 = 目标根.ContainsKey(k);
                    bool 覆盖 = System.Array.IndexOf(总是覆盖, k) >= 0;
                    if (有 && !覆盖) continue;
                    动作.Add(有 ? k + "(覆盖)" : k + "(补)");
                }
                if (动作.Count == 0)
                {
                    Debug.Log("[装配同步] 【" + Path.GetFileNameWithoutExtension(目标) + "】通用根齐全，跳过");
                    continue;
                }

                var 临时 = EditorSceneManager.OpenScene(临时场景, OpenSceneMode.Additive);
                var 源根 = new Dictionary<string, GameObject>();
                foreach (var g in 临时.GetRootGameObjects()) 源根[g.name] = g;

                foreach (var k in 通用根)
                {
                    bool 有 = 目标根.ContainsKey(k);
                    bool 覆盖 = System.Array.IndexOf(总是覆盖, k) >= 0;
                    if (有 && !覆盖) continue;
                    if (!源根.ContainsKey(k)) { Debug.LogWarning("[装配同步]   源场景里没有「" + k + "」，跳过"); continue; }

                    Vector3 原位置 = 有 ? 目标根[k].transform.position : Vector3.zero;
                    Quaternion 原旋转 = 有 ? 目标根[k].transform.rotation : Quaternion.identity;
                    if (有) Object.DestroyImmediate(目标根[k]);

                    var 搬 = 源根[k];
                    EditorSceneManager.MoveGameObjectToScene(搬, 目标场景);
                    if (k == "Player" || k == "SpawnPoint") { 搬.transform.position = new Vector3(0f, 0f, 0f); }
                    if (有 && k != "Main Camera") { 搬.transform.position = 原位置; 搬.transform.rotation = 原旋转; }
                }

                EditorSceneManager.CloseScene(临时, true);
                EditorSceneManager.SaveScene(目标场景);
                Debug.Log("[装配同步] 【" + Path.GetFileNameWithoutExtension(目标) + "】完成：" + string.Join("、", 动作));
            }
        }
        finally
        {
            if (File.Exists(原活动场景) && 原活动场景 != "") EditorSceneManager.OpenScene(原活动场景, OpenSceneMode.Single);
            AssetDatabase.DeleteAsset(临时场景);
        }
        Debug.Log("[装配同步] 全部结束。★ 别忘了把 Player / SpawnPoint 摆到该在的位置");
    }
}
