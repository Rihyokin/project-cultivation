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

    /// <summary>这几个根总是用源场景的覆盖。
    /// · Player：场景里若残留一个**空的** Player 根物体（手删角色预制体只删了子物体），
    ///   "缺失才补"会跳过它留下空壳 ✗
    /// · Main Camera：必须带跟随脚本
    /// · UI（HudCanvas / CharacterUI / EventSystem / 暂停菜单）：**必须整体覆盖** ——
    ///   各场景里的旧版 UI 只要还在，就会挡住新 UI 同步（实测 Sect 一直是老 UI ✗）</summary>
    static readonly string[] 总是覆盖 = { "Player", "Main Camera", "HudCanvas", "CharacterUI", "EventSystem", "暂停菜单" };

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

                    // ★ 主相机是"总是覆盖"的，但**各场景会自己调相机**（例如镇妖塔把滚轮抬高上限 ×1.5）。
                    //   覆盖前先记下这些调参，覆盖后原样还回去 —— 否则每次同步都会把别人的调参冲掉 ✗
                    float sMin = 0f, sMax = 0f, hMin = 0f, hMax = 0f, 格 = 0f;
                    bool 有调参 = false;
                    if (k == "Main Camera" && 有)
                    {
                        var 旧 = 目标根[k].GetComponent<TopDownCameraZoom>();
                        if (旧 != null)
                        {
                            有调参 = true;
                            sMin = 旧.softMin; sMax = 旧.softMax;
                            hMin = 旧.hardMin; hMax = 旧.hardMax;
                            格 = 旧.zoomPerNotch;
                        }
                        // ★ 遮挡透视**不再在这里按场景保留/删除了**（用户 2026-09-23：可以删）。
                        //   建筑透明 / 玩家描边从那时起由 Assets/resources/场景特效开关.asset
                        //   一张表统一决定：组件在每个游玩场景都挂着，运行时自己按表开关
                        //   （见 场景特效开关.自己该生效）。所以它就是个普通组件，
                        //   跟着源场景一起覆盖就行 —— 这里再"按本场景有没有"搬一次反而是多余的规则 ✗
                    }

                    if (有) Object.DestroyImmediate(目标根[k]);

                    var 搬 = 源根[k];
                    EditorSceneManager.MoveGameObjectToScene(搬, 目标场景);
                    // ★ 位置一律保留目标场景原有的（没有同名物体才用源的位置）——
                    //   例如 Sect 的玩家在 (-83.6, 18.1, 89.9)、塔里在 (0, 4.99, -9)，
                    //   强行归零会把玩家丢到塔外/地底下 ✗
                    if (有 && k != "Main Camera") { 搬.transform.position = 原位置; 搬.transform.rotation = 原旋转; }

                    if (有调参)
                    {
                        var 新 = 搬.GetComponent<TopDownCameraZoom>();
                        if (新 != null)
                        {
                            新.softMin = sMin; 新.softMax = sMax;
                            新.hardMin = hMin; 新.hardMax = hMax;
                            新.zoomPerNotch = 格;
                            Debug.Log("[装配同步]   已保留本场景的滚轮调参：软 " + sMin + "~" + sMax + " 硬 " + hMin + "~" + hMax);
                        }
                    }
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
