using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 批量把「能被选中、能被打、能显示血条」这件事补齐到所有 NPC prefab 上。
///
/// ============================================================
/// 背景：一次检查发现 257 个 NPC prefab 里
///         有 NpcInstance 的只有 1 个（之前只给母鸡配过）
///         有 NpcIndicator 的只有 1 个
///       也就是说除了木桩和母鸡，其它 NPC 根本选不中、不会死、不显示血条。
/// ============================================================
///
/// 一个 NPC 要能正常被左键选中 / 右键锁定 / 显示血量，需要四样：
///
///   1. Collider            —— Physics.Raycast 要打得中（CharacterController 不可靠）
///   2. NpcInstance         —— RaycastNpc() 用 GetComponentInParent<NpcInstance>() 认人
///   3. NpcInstance.定义     —— 没接 NpcDefinition 的话血量是 0，一碰就死
///   4. NpcIndicator        —— 画选中/锁定光环 + 血条。没有它，选中了也看不出变化
///
/// ============================================================
/// 定义资产怎么自动配对
/// ============================================================
///   从路径反推 id：
///     Assets/resources/NPC/<分类>/<目录>/xxx.prefab
///       Animal → beast_ , Demon → demon_ , Human → human_ ,
///       NPC → npc_ , Mount → mount_ , 其它 → 不加前缀
///     id = 前缀 + <目录> 转小写
///   例：resources/NPC/Animal/MuJi/MuJi_01.prefab  →  beast_muji
///
/// 菜单：Cultivation/NPC 批量/检查
///       Cultivation/NPC 批量/补齐（指示器 + 碰撞体 + NpcInstance + 定义）
/// </summary>
public static class NpcIndicatorSetup
{
    const string Npc根 = "Assets/resources/NPC";
    const string 定义目录 = "Assets/Data/Generated/NpcDefinition";

    // ================================================================
    // 检查
    // ================================================================

    [MenuItem("Cultivation/NPC 批量/检查")]
    public static void Check()
    {
        var 全部 = 收集();
        int 有实例 = 0, 有定义 = 0, 有指示器 = 0, 有碰撞 = 0;
        var 缺实例 = new List<string>();
        var 缺定义 = new List<string>();

        foreach (var p in 全部)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(p);
            if (go == null) continue;

            var 实例 = go.GetComponent<NpcInstance>();
            if (实例 != null)
            {
                有实例++;
                if (实例.定义 != null) 有定义++;
                else if (缺定义.Count < 12) 缺定义.Add(System.IO.Path.GetFileName(p));
            }
            else if (缺实例.Count < 12) 缺实例.Add(System.IO.Path.GetFileName(p));

            if (go.GetComponent<NpcIndicator>() != null) 有指示器++;
            if (go.GetComponent<Collider>() != null || go.GetComponent<CharacterController>() != null) 有碰撞++;
        }

        var sb = new StringBuilder("[NpcIndicatorSetup] 检查\n");
        sb.Append("  NPC prefab 总数   = ").Append(全部.Count).Append('\n');
        sb.Append("    有 NpcInstance  = ").Append(有实例).Append('\n');
        sb.Append("    定义已接        = ").Append(有定义).Append('\n');
        sb.Append("    有 NpcIndicator = ").Append(有指示器).Append('\n');
        sb.Append("    有碰撞体        = ").Append(有碰撞).Append('\n');
        if (缺实例.Count > 0)
        {
            sb.Append("  【缺 NpcInstance】（前 12）：\n");
            foreach (var n in 缺实例) sb.Append("    ").Append(n).Append('\n');
        }
        if (缺定义.Count > 0)
        {
            sb.Append("  【有实例但定义没接】（前 12）：\n");
            foreach (var n in 缺定义) sb.Append("    ").Append(n).Append('\n');
        }
        Debug.Log(sb.ToString());
    }

    // ================================================================
    // 补齐
    // ================================================================

    [MenuItem("Cultivation/NPC 批量/补齐（指示器 + 碰撞体 + NpcInstance + 定义）")]
    public static void Fix()
    {
        var 定义表 = 建定义索引();
        var 全部 = 收集();

        int 加实例 = 0, 接定义 = 0, 加指示器 = 0, 加碰撞 = 0, 跳过 = 0, 坏 = 0;
        var 没定义 = new List<string>();

        for (int i = 0; i < 全部.Count; i++)
        {
            var p = 全部[i];
            if ((i + 1) % 50 == 0) Debug.Log($"[NpcIndicatorSetup] 进度 {i + 1}/{全部.Count}");

            var 预览 = AssetDatabase.LoadAssetAtPath<GameObject>(p);
            if (预览 == null) { 坏++; continue; }

            bool 需实例 = 预览.GetComponent<NpcInstance>() == null;
            bool 需指示器 = 预览.GetComponent<NpcIndicator>() == null;
            bool 需碰撞 = 预览.GetComponent<Collider>() == null && 预览.GetComponent<CharacterController>() == null;
            bool 需定义 = !需实例 && 预览.GetComponent<NpcInstance>().定义 == null;

            if (!需实例 && !需指示器 && !需碰撞 && !需定义) { 跳过++; continue; }

            GameObject 根 = null;
            try { 根 = PrefabUtility.LoadPrefabContents(p); }
            catch { 坏++; continue; }
            if (根 == null) { 坏++; continue; }

            bool 动了 = false;

            // 1) NpcInstance
            var 实例 = 根.GetComponent<NpcInstance>();
            if (实例 == null) { 实例 = 根.AddComponent<NpcInstance>(); 加实例++; 动了 = true; }

            // 2) 定义
            if (实例.定义 == null)
            {
                var id = 猜id(p);
                if (定义表.TryGetValue(id, out var 定义))
                {
                    实例.定义 = 定义;
                    接定义++;
                    动了 = true;
                }
                else if (没定义.Count < 12) 没定义.Add(System.IO.Path.GetFileName(p) + " → 想要 " + id);
            }

            // 3) 碰撞体
            if (需碰撞)
            {
                var 胶囊 = 根.GetComponent<CapsuleCollider>();
                if (胶囊 == null) 胶囊 = 根.AddComponent<CapsuleCollider>();
                按包围盒定尺寸(根, 胶囊);
                加碰撞++;
                动了 = true;
            }

            // 4) 指示器（有 [RequireComponent(NpcInstance)]，所以要在实例之后加）
            if (根.GetComponent<NpcIndicator>() == null)
            {
                根.AddComponent<NpcIndicator>();
                加指示器++;
                动了 = true;
            }

            if (动了) PrefabUtility.SaveAsPrefabAsset(根, p);
            PrefabUtility.UnloadPrefabContents(根);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        var sb = new StringBuilder("[NpcIndicatorSetup] 补齐完成\n");
        sb.Append("  prefab 总数   = ").Append(全部.Count).Append('\n');
        sb.Append("  加 NpcInstance = ").Append(加实例).Append('\n');
        sb.Append("  接上定义      = ").Append(接定义).Append('\n');
        sb.Append("  加 NpcIndicator = ").Append(加指示器).Append('\n');
        sb.Append("  加碰撞体      = ").Append(加碰撞).Append('\n');
        sb.Append("  本来就齐、跳过 = ").Append(跳过).Append('\n');
        sb.Append("  读不到/空 prefab = ").Append(坏).Append('\n');
        if (没定义.Count > 0)
        {
            sb.Append("  【配不到定义的】（前 12）：\n");
            foreach (var n in 没定义) sb.Append("    ").Append(n).Append('\n');
        }
        Debug.Log(sb.ToString());
    }

    // ================================================================
    // 工具
    // ================================================================

    /// <summary>所有 NpcDefinition，按 id 建索引</summary>
    static Dictionary<string, NpcDefinition> 建定义索引()
    {
        var 表 = new Dictionary<string, NpcDefinition>();
        foreach (var g in AssetDatabase.FindAssets("t:NpcDefinition", new[] { 定义目录 }))
        {
            var p = AssetDatabase.GUIDToAssetPath(g);
            var d = AssetDatabase.LoadAssetAtPath<NpcDefinition>(p);
            if (d == null) continue;
            if (!string.IsNullOrEmpty(d.id) && !表.ContainsKey(d.id)) 表[d.id] = d;
            var 文件名 = System.IO.Path.GetFileNameWithoutExtension(p);
            if (!string.IsNullOrEmpty(文件名) && !表.ContainsKey(文件名)) 表[文件名] = d;
        }
        return 表;
    }

    /// <summary>从 prefab 路径反推 NPC 表里的 id</summary>
    static string 猜id(string prefab路径)
    {
        var 段 = prefab路径.Split('/');
        // Assets / resources / NPC / <分类> / <目录> / <文件>.prefab
        if (段.Length < 6) return "";
        var 分类 = 段[3];
        var 目录 = 段[4];

        string 前缀;
        switch (分类)
        {
            case "Animal": 前缀 = "beast_"; break;
            case "Demon":  前缀 = "demon_"; break;
            case "Human":  前缀 = "human_"; break;
            case "NPC":    前缀 = "npc_"; break;
            case "Mount":  前缀 = "mount_"; break;
            default:       前缀 = ""; break;
        }
        return 前缀 + 目录.ToLower();
    }

    /// <summary>
    /// 按渲染体的世界包围盒定胶囊尺寸。
    /// 【注意】Collider 尺寸是本地空间，要除以根缩放 ——
    /// 母鸡根缩放是 12，除完只有 0.06，那是对的，别看着小就"修正"它。
    /// </summary>
    static void 按包围盒定尺寸(GameObject 根, CapsuleCollider 胶囊)
    {
        var 渲染器 = 根.GetComponentsInChildren<Renderer>(true);
        if (渲染器.Length == 0) return;

        var b = 渲染器[0].bounds;
        for (int i = 1; i < 渲染器.Length; i++) b.Encapsulate(渲染器[i].bounds);

        var s = 根.transform.lossyScale;
        float 缩Y = Mathf.Max(0.0001f, Mathf.Abs(s.y));
        float 缩X = Mathf.Max(0.0001f, Mathf.Abs(s.x));
        float 缩Z = Mathf.Max(0.0001f, Mathf.Abs(s.z));

        胶囊.center = 根.transform.InverseTransformPoint(b.center);
        float 高 = Mathf.Max(0.01f, b.size.y / 缩Y);
        float 粗 = Mathf.Max(0.01f, Mathf.Max(b.size.x / 缩X, b.size.z / 缩Z));
        胶囊.height = Mathf.Max(高, 粗 + 0.001f);
        胶囊.radius = Mathf.Max(0.005f, 粗 * 0.5f);
        胶囊.direction = 1;
        胶囊.isTrigger = false;
    }

    /// <summary>收集所有「组装好的」NPC prefab（有 Animator 且接了控制器）</summary>
    static List<string> 收集()
    {
        var 结果 = new List<string>();
        foreach (var g in AssetDatabase.FindAssets("t:Prefab", new[] { Npc根 }))
        {
            var p = AssetDatabase.GUIDToAssetPath(g);
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(p);
            if (go == null) continue;
            var an = go.GetComponent<Animator>();
            if (an == null || an.runtimeAnimatorController == null) continue;   // 原始 FBX 之类
            if (go.GetComponentInChildren<SkinnedMeshRenderer>(true) == null) continue;
            结果.Add(p);
        }
        return 结果;
    }
}
