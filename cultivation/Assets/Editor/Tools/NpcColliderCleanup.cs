using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 去掉 NPC prefab 上多余的 CharacterController。
///
/// ============================================================
/// 为什么要去掉
/// ============================================================
/// CharacterController 会【自己维护一套内部位置】，不能用 transform.position 推。
/// 我们的 NPC AI 是直接改 transform.position 的，于是：
///   1. AI 把 transform 挪过去
///   2. 下一物理帧 CharacterController 把它拽回它认为的位置
///   3. 表现为"走了一下就卡住不动"
///
/// 而且 prefab 上往往还同时存在一个 CapsuleCollider，两个实心体重叠，
/// CharacterController 还会自己顶自己。
///
/// CharacterController 是给【玩家移动】用的（要爬坡、上台阶、贴墙滑动那套）。
/// NPC 用不上：能被射线选中、能挡住玩家，一个 CapsuleCollider 就够了。
///
/// 菜单：Cultivation/NPC 批量/检查 CharacterController
///       Cultivation/NPC 批量/去掉 CharacterController
/// </summary>
public static class NpcColliderCleanup
{
    const string Npc根 = "Assets/resources/NPC";
    const string 玩家prefab关键词 = "Player";

    [MenuItem("Cultivation/NPC 批量/检查 CharacterController")]
    public static void Check()
    {
        var 全部 = 收集();
        int 有CC = 0, 有CC同时有别的碰撞体 = 0, 只有CC = 0;
        var 名单 = new List<string>();

        foreach (var p in 全部)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(p);
            if (go == null) continue;
            var cc = go.GetComponent<CharacterController>();
            if (cc == null) continue;

            有CC++;
            bool 有别的 = go.GetComponent<CapsuleCollider>() != null
                       || go.GetComponent<BoxCollider>() != null
                       || go.GetComponent<SphereCollider>() != null;
            if (有别的) 有CC同时有别的碰撞体++;
            else 只有CC++;

            if (名单.Count < 10) 名单.Add(System.IO.Path.GetFileName(p));
        }

        var sb = new StringBuilder("[NpcColliderCleanup] 检查\n");
        sb.Append("  NPC prefab 总数        = ").Append(全部.Count).Append('\n');
        sb.Append("  带 CharacterController = ").Append(有CC).Append('\n');
        sb.Append("    其中还有别的碰撞体（会自己顶自己）= ").Append(有CC同时有别的碰撞体).Append('\n');
        sb.Append("    只有它一个（去掉后要补 CapsuleCollider）= ").Append(只有CC).Append('\n');
        if (名单.Count > 0)
        {
            sb.Append("  前 10 个：\n");
            foreach (var n in 名单) sb.Append("    ").Append(n).Append('\n');
        }
        Debug.Log(sb.ToString());
    }

    [MenuItem("Cultivation/NPC 批量/去掉 CharacterController")]
    public static void Remove()
    {
        var 全部 = 收集();
        int 去掉 = 0, 补了胶囊 = 0, 跳过 = 0, 坏 = 0;

        for (int i = 0; i < 全部.Count; i++)
        {
            var p = 全部[i];
            if ((i + 1) % 50 == 0) Debug.Log($"[NpcColliderCleanup] 进度 {i + 1}/{全部.Count}");

            var 预览 = AssetDatabase.LoadAssetAtPath<GameObject>(p);
            if (预览 == null) { 坏++; continue; }
            if (预览.GetComponent<CharacterController>() == null) { 跳过++; continue; }

            GameObject 根 = null;
            try { 根 = PrefabUtility.LoadPrefabContents(p); }
            catch { 坏++; continue; }
            if (根 == null) { 坏++; continue; }

            var cc = 根.GetComponent<CharacterController>();
            if (cc != null) { Object.DestroyImmediate(cc); 去掉++; }

            // 去掉之后如果没有别的碰撞体了，会被射线打不到 → 补一个胶囊
            bool 还有 = 根.GetComponent<CapsuleCollider>() != null
                     || 根.GetComponent<BoxCollider>() != null
                     || 根.GetComponent<SphereCollider>() != null;
            if (!还有)
            {
                var 胶囊 = 根.AddComponent<CapsuleCollider>();
                按包围盒定尺寸(根, 胶囊);
                补了胶囊++;
            }

            PrefabUtility.SaveAsPrefabAsset(根, p);
            PrefabUtility.UnloadPrefabContents(根);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        var sb = new StringBuilder("[NpcColliderCleanup] 完成\n");
        sb.Append("  prefab 总数   = ").Append(全部.Count).Append('\n');
        sb.Append("  去掉 CC       = ").Append(去掉).Append('\n');
        sb.Append("  补了胶囊      = ").Append(补了胶囊).Append('\n');
        sb.Append("  本来就没有、跳过 = ").Append(跳过).Append('\n');
        sb.Append("  读不到        = ").Append(坏).Append('\n');
        sb.Append("  【注意】玩家 prefab 不在这里的范围内，玩家该有的 CharacterController 不受影响\n");
        Debug.Log(sb.ToString());
    }

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

    /// <summary>只收 NPC prefab —— 玩家 prefab 在别的目录，不会误伤</summary>
    static List<string> 收集()
    {
        var 结果 = new List<string>();
        foreach (var g in AssetDatabase.FindAssets("t:Prefab", new[] { Npc根 }))
        {
            var p = AssetDatabase.GUIDToAssetPath(g);
            if (p.Contains(玩家prefab关键词)) continue;
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(p);
            if (go == null) continue;
            var an = go.GetComponent<Animator>();
            if (an == null || an.runtimeAnimatorController == null) continue;
            结果.Add(p);
        }
        return 结果;
    }
}
