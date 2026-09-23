using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 撤掉所有 NPC 的 AI 组件，只保留木桩那套基础功能（能被选中 / 锁定 / 受击 / 显示血量）。
///
/// 【为什么用类型名字符串匹配，而不是直接引用 NpcBrain】
/// 这样这个工具不依赖 NpcBrain*.cs —— 可以先清干净组件，再安心删掉那些脚本文件，
/// 不会出现"删了脚本、工具编译不过、菜单消失"的死锁。
///
/// 保留的组件（木桩有的）：
///   NpcInstance      气血 / 受伤 / 死亡 / 重生
///   Collider         射线要打得中
///   NpcIndicator     选中光环 + 锁定红环 + 血条
///   NpcAnimator      （留着不碍事，明天写 AI 时直接用）
///   Animator         （同上）
///
/// 移除的组件：任何类名以 NpcBrain 开头的
///
/// 菜单：Cultivation/NPC 批量/撤掉 AI（保留基础功能）
///       Cultivation/NPC 批量/检查（还剩什么组件）
/// </summary>
public static class NpcAiCleanup
{
    const string Npc根 = "Assets/resources/NPC";

    [MenuItem("Cultivation/NPC 批量/检查（还剩什么组件）")]
    public static void Check()
    {
        var 全部 = 收集();
        var sb = new StringBuilder("[NpcAiCleanup] 检查\n");
        sb.Append("  NPC prefab = ").Append(全部.Count).Append('\n');

        var 统计 = new Dictionary<string, int>();
        int 有AI = 0;
        foreach (var p in 全部)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(p);
            if (go == null) continue;
            foreach (var c in go.GetComponents<Component>())
            {
                if (c == null) continue;
                var n = c.GetType().Name;
                if (n.StartsWith("NpcBrain")) 有AI++;
                if (!统计.ContainsKey(n)) 统计[n] = 0;
                统计[n]++;
            }
        }

        sb.Append("  还挂着 AI 的 = ").Append(有AI).Append('\n');
        sb.Append("  组件统计：\n");
        foreach (var kv in 统计) sb.Append("    ").Append(kv.Key.PadRight(22)).Append(kv.Value).Append('\n');
        Debug.Log(sb.ToString());
    }

    [MenuItem("Cultivation/NPC 批量/撤掉 AI（保留基础功能）")]
    public static void Remove()
    {
        var 全部 = 收集();
        int 去掉 = 0, 动过 = 0, 坏 = 0;

        for (int i = 0; i < 全部.Count; i++)
        {
            var p = 全部[i];
            if ((i + 1) % 50 == 0) Debug.Log($"[NpcAiCleanup] 进度 {i + 1}/{全部.Count}");

            var 预览 = AssetDatabase.LoadAssetAtPath<GameObject>(p);
            if (预览 == null) { 坏++; continue; }

            bool 有AI = false;
            foreach (var c in 预览.GetComponents<Component>())
                if (c != null && c.GetType().Name.StartsWith("NpcBrain")) { 有AI = true; break; }
            if (!有AI) continue;

            GameObject 根 = null;
            try { 根 = PrefabUtility.LoadPrefabContents(p); }
            catch { 坏++; continue; }
            if (根 == null) { 坏++; continue; }

            // 收集要删的（不能边遍历边删）
            var 要删 = new List<Component>();
            foreach (var c in 根.GetComponents<Component>())
                if (c != null && c.GetType().Name.StartsWith("NpcBrain")) 要删.Add(c);

            foreach (var c in 要删) { Object.DestroyImmediate(c, true); 去掉++; }

            // 顺手确认基础三件套还在，缺了就补
            确保基础组件(根);

            PrefabUtility.SaveAsPrefabAsset(根, p);
            PrefabUtility.UnloadPrefabContents(根);
            动过++;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[NpcAiCleanup] 完成\n" +
                  "  prefab 总数   = " + 全部.Count + '\n' +
                  "  移除了 AI 组件 = " + 去掉 + '\n' +
                  "  改动过的 prefab = " + 动过 + '\n' +
                  "  读不到        = " + 坏 + '\n' +
                  "  保留：NpcInstance / Collider / NpcIndicator / NpcAnimator / Animator");
    }

    /// <summary>保证木桩那套基础组件在（缺了才补，不动已有的）</summary>
    static void 确保基础组件(GameObject 根)
    {
        if (根.GetComponent<NpcInstance>() == null) 根.AddComponent<NpcInstance>();
        if (根.GetComponent<Collider>() == null) 根.AddComponent<CapsuleCollider>();
        if (根.GetComponent<NpcIndicator>() == null) 根.AddComponent<NpcIndicator>();
    }

    static List<string> 收集()
    {
        var 结果 = new List<string>();
        foreach (var g in AssetDatabase.FindAssets("t:Prefab", new[] { Npc根 }))
        {
            var p = AssetDatabase.GUIDToAssetPath(g);
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(p);
            if (go == null) continue;
            if (go.GetComponentInChildren<SkinnedMeshRenderer>(true) == null) continue;
            结果.Add(p);
        }
        return 结果;
    }
}
