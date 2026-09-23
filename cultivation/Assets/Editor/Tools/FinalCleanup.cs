using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 项目收尾清理。
///
/// 分三块：
///   1. 框架垃圾材质 —— 名字是 Standard_xx 这种 Unity 默认名，没有任何 NPC 在用
///   2. Character/ —— 当初为它接过动画，现在 resources/NPC 那套已经完全可用
///      （连带删掉为它建的 6 个控制器，不然会变成空引用）
///   3. 用过就没用的开发工具脚本
///
/// 【保留】
///   DshBridge.cs        —— 控制通道，项目还在早期，留着
///   NpcAnimator.cs      —— 挂在 261 个 prefab 上
///   NpcPrefabBuilder / NpcCreatureSetup / FixNpcMaterials —— 可复用
///   各种 Builder（村庄/角色面板/主菜单/技能片段等）—— 生成资产的，将来还要用
///
/// 菜单：Cultivation / 项目收尾清理
/// </summary>
public static class FinalCleanup
{
    /// <summary>Character 目录（连带它的控制器一起删）</summary>
    const string 角色目录 = "Assets/Character";

    /// <summary>为 Character 建的那 6 个控制器，删 Character 时要一起删</summary>
    static readonly string[] 角色控制器 =
    {
        "Assets/Animations/NPC/Archer_Female.controller",
        "Assets/Animations/NPC/Archer_Male.controller",
        "Assets/Animations/NPC/Mage_Female.controller",
        "Assets/Animations/NPC/Mage_Male.controller",
        "Assets/Animations/NPC/Warrior_Female.controller",
        "Assets/Animations/NPC/Warrior_Male.controller",
    };

    /// <summary>用过就没用的开发工具</summary>
    static readonly string[] 废弃工具 =
    {
        "Assets/Editor/Tools/MenuRaycastProbe.cs",   // 主菜单点击诊断
        "Assets/Editor/Tools/SwordTipProbe.cs",      // 飞剑朝向标定
        "Assets/Editor/Tools/StartSceneRename.cs",   // 场景改名（已改完）
        "Assets/Editor/Tools/MenuArtSetup.cs",       // 菜单图导入设置（已设好）
        "Assets/Editor/Tools/MainMenuTest.cs",       // 主菜单测试
        "Assets/Editor/Tools/SaveSystemTest.cs",     // 存档测试
        "Assets/Editor/Tools/MmoPackCleaner.cs",     // 删 MMO 包（已删完）
        "Assets/Editor/Tools/ProjectOrganizer.cs",   // 项目规整（已规整）
        "Assets/Editor/Tools/CharacterAnimSetup.cs", // 为 Character 做的，连带删
        "Assets/Editor/Tools/UIPageCapture.cs",      // UI 截图工具（一次性）
        "Assets/Editor/Tools/CleanupTestStuff.cs",   // 上一个清理工具（已跑完）
    };

    [MenuItem("Cultivation/项目收尾清理/先检查（不删）")]
    public static void Check()
    {
        var sb = new StringBuilder("[FinalCleanup] 检查\n\n");

        // 垃圾材质
        var 垃圾 = 找垃圾材质();
        sb.Append("1) 框架垃圾材质 = ").Append(垃圾.Count).Append(" 个\n");
        sb.Append("   被 prefab 引用的 = ").Append(数引用(垃圾)).Append("（>0 就不能删）\n\n");

        // Character
        bool 有角色 = AssetDatabase.IsValidFolder(角色目录);
        sb.Append("2) ").Append(角色目录).Append(" 存在 = ").Append(有角色).Append('\n');
        if (有角色)
        {
            int n = System.IO.Directory.GetFiles(角色目录, "*", System.IO.SearchOption.AllDirectories).Length;
            sb.Append("   文件数 = ").Append(n).Append('\n');
        }
        sb.Append("   指向 Character 的控制器 = ").Append(角色控制器.Length).Append(" 个\n\n");

        // 工具
        int 有 = 0;
        foreach (var p in 废弃工具) if (AssetDatabase.LoadAssetAtPath<Object>(p) != null) 有++;
        sb.Append("3) 可删的开发工具 = ").Append(有).Append(" / ").Append(废弃工具.Length).Append(" 个\n");

        Debug.Log(sb.ToString());
    }

    [MenuItem("Cultivation/项目收尾清理/执行")]
    public static void Run()
    {
        var 报告 = new StringBuilder("[FinalCleanup] 执行结果\n\n");
        int 删材质 = 0, 删文件 = 0;

        // ---------- 1. 垃圾材质（要确认没被引用） ----------
        var 垃圾 = 找垃圾材质();
        var 被引用 = 找引用者(垃圾);
        foreach (var m in 垃圾)
        {
            if (被引用.Contains(m)) continue;         // 有人用就留着
            var p = AssetDatabase.GetAssetPath(m);
            if (AssetDatabase.DeleteAsset(p)) 删材质++;
        }
        报告.Append("1) 删除框架垃圾材质 ").Append(删材质).Append(" 个");
        if (被引用.Count > 0) 报告.Append("（").Append(被引用.Count).Append(" 个仍被引用，保留）");
        报告.Append("\n\n");

        // ---------- 2. Character + 它的控制器 ----------
        int 删控制器 = 0;
        foreach (var p in 角色控制器)
            if (AssetDatabase.LoadAssetAtPath<Object>(p) != null && AssetDatabase.DeleteAsset(p)) 删控制器++;

        if (AssetDatabase.IsValidFolder(角色目录))
        {
            // 先确认没有场景/prefab 引用它
            if (AssetDatabase.DeleteAsset(角色目录)) 报告.Append("2) 删除 ").Append(角色目录).Append('\n');
            else 报告.Append("2) 【删不掉】").Append(角色目录).Append('\n');
        }
        else 报告.Append("2) ").Append(角色目录).Append(" 已不存在\n");
        报告.Append("   删除控制器 ").Append(删控制器).Append(" 个\n\n");

        // ---------- 3. 废弃工具 ----------
        int 删工具 = 0;
        foreach (var p in 废弃工具)
        {
            if (AssetDatabase.LoadAssetAtPath<Object>(p) == null) continue;
            if (AssetDatabase.DeleteAsset(p)) { 删工具++; 报告.Append("   删 ").Append(System.IO.Path.GetFileName(p)).Append('\n'); }
        }
        报告.Append("3) 删除开发工具 ").Append(删工具).Append(" 个\n\n");

        // ---------- 4. 保留项核查 ----------
        报告.Append("=== 保留项核查 ===\n");
        foreach (var p in new[] {
            "Assets/Editor/Tools/DshBridge.cs",
            "Assets/Scripts/Npc/NpcAnimator.cs",
            "Assets/Editor/Tools/NpcPrefabBuilder.cs",
            "Assets/Editor/Tools/NpcCreatureSetup.cs",
            "Assets/Editor/Tools/FixNpcMaterials.cs",
            "Assets/Editor/Builders/VillageBuilder3.cs",
            "Assets/Editor/Builders/MainMenuBuilder.cs",
            "Assets/Editor/Builders/CharacterPanelBuilder.cs" })
            报告.Append(AssetDatabase.LoadAssetAtPath<Object>(p) != null ? "  [有] " : "  [丢了！] ").Append(p).Append('\n');

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log(报告.ToString());
    }

    static List<Material> 找垃圾材质()
    {
        var 结果 = new List<Material>();
        foreach (var g in AssetDatabase.FindAssets("t:Material", new[] { "Assets/resources/NPC" }))
        {
            var p = AssetDatabase.GUIDToAssetPath(g);
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (m == null) continue;
            var 名 = System.IO.Path.GetFileNameWithoutExtension(p);
            // Unity 默认名 + 没有贴图的，就是框架留下的空壳
            bool 默认名 = 名.StartsWith("Standard") || 名.Contains("Default") || (名.Length > 0 && char.IsDigit(名[0])) || 名.StartsWith("Material #");
            if (!默认名) continue;
            var t = m.HasProperty("_MainTex") ? m.GetTexture("_MainTex") : null;
            if (t == null) 结果.Add(m);
        }
        return 结果;
    }

    static int 数引用(List<Material> 材质)
    {
        return 找引用者(材质).Count;
    }

    /// <summary>扫所有 prefab，看哪些材质真的被用了</summary>
    static HashSet<Material> 找引用者(List<Material> 材质)
    {
        var 被用 = new HashSet<Material>();
        if (材质.Count == 0) return 被用;
        var 集合 = new HashSet<Material>(材质);

        foreach (var g in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/resources/NPC" }))
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(g));
            if (go == null) continue;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                foreach (var m in r.sharedMaterials)
                    if (m != null && 集合.Contains(m)) 被用.Add(m);
        }
        return 被用;
    }
}
