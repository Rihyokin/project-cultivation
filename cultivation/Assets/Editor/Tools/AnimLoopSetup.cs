using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 批量把循环类动画设成 Loop。
///
/// 【为什么 NPC 走着走着动画就停了】
/// FBX 导入的 AnimationClip 默认 loopTime = false —— 播一次就停住，
/// 停在最后一帧不动。表现就是"人在移动，但动画定住了"。
/// （之前探查时日志里就有证据：片段 'Attack1' 1.17s 循环=False）
///
/// 所以要把【本来该循环的】动作打开 Loop：
///   Idle / Specialidle / Walk / Run / RideIdle / RideRun / FloatIdle / FloatRun ...
/// 而一次性的动作要【保持不循环】：
///   Attack* / Death / Wound / Jump / Dodge / Pick / Drop ...
///
/// 菜单：Cultivation/动画/设循环（全部 NPC 动画）
///       Cultivation/动画/检查循环设置
/// </summary>
public static class AnimLoopSetup
{
    const string 动画根 = "Assets/resources/NPC";

    /// <summary>该循环的动作名（精确匹配，大小写不敏感）</summary>
    static readonly HashSet<string> 该循环 = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase)
    {
        "Idle", "Specialidle", "SpecialIdle", "Walk", "Run",
        "RideIdle", "RideRun", "FloatIdle", "FloatRun",
        "DropStand", "Fight", "Swim", "SwimIdle", "Fly", "FlyIdle",
        "WalkBack", "RunBack", "Stand",
    };

    /// <summary>明显一次性的（用于诊断提示，不改）</summary>
    static readonly HashSet<string> 一次性 = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase)
    {
        "Death", "Wound", "Jump", "Dodge", "Pick", "Drop", "DropRun",
        "Attack1","Attack2","Attack3","Attack4","Attack5",
        "Dig", "Fish", "Hit", "Skill", "Cast",
    };

    [MenuItem("Cultivation/动画/检查循环设置")]
    public static void Check()
    {
        var sb = new StringBuilder("[AnimLoopSetup] 循环设置检查\n");
        int 该循环却没循环 = 0;

        foreach (var p in 收集动画FBX())
        {
            var 名 = System.IO.Path.GetFileNameWithoutExtension(p);
            int at = 名.IndexOf('@');
            var 动作 = at >= 0 ? 名.Substring(at + 1) : 名;

            foreach (var obj in AssetDatabase.LoadAllAssetsAtPath(p))
            {
                if (!(obj is AnimationClip c) || c.name.StartsWith("__preview")) continue;
                var 设置 = AnimationUtility.GetAnimationClipSettings(c);
                bool 应循环 = 该循环.Contains(动作) || 该循环.Contains(c.name);

                if (应循环 && !设置.loopTime)
                {
                    该循环却没循环++;
                    sb.Append("  【没设循环】").Append(名).Append("  片段=").Append(c.name)
                      .Append("  时长=").Append(c.length.ToString("F2")).Append("s\n");
                }
            }
        }
        sb.Append("  该循环却没设的 = ").Append(该循环却没循环).Append('\n');
        Debug.Log(sb.ToString());
    }

    [MenuItem("Cultivation/动画/设循环（全部 NPC 动画）")]
    public static void Fix()
    {
        var 改过 = new List<string>();
        int 设了 = 0, 检查过 = 0;

        foreach (var p in 收集动画FBX())
        {
            var 名 = System.IO.Path.GetFileNameWithoutExtension(p);
            int at = 名.IndexOf('@');
            var 动作 = at >= 0 ? 名.Substring(at + 1) : 名;

            bool 应循环 = 该循环.Contains(动作);
            if (!应循环) continue;              // 只动循环类，一次性动作不碰

            var importer = AssetImporter.GetAtPath(p) as ModelImporter;
            if (importer == null) continue;

            var 片段们 = importer.clipAnimations;
            if (片段们 == null || 片段们.Length == 0)
            {
                // 没显式列片段时，改默认设置
                片段们 = importer.defaultClipAnimations;
            }
            if (片段们 == null || 片段们.Length == 0) continue;

            bool 动了 = false;
            foreach (var c in 片段们)
            {
                检查过++;
                if (!c.loopTime) { c.loopTime = true; 动了 = true; 设了++; }
            }

            if (动了)
            {
                importer.clipAnimations = 片段们;
                importer.SaveAndReimport();
                改过.Add(名);
            }
        }

        AssetDatabase.Refresh();
        var sb = new StringBuilder("[AnimLoopSetup] 完成\n");
        sb.Append("  检查片段 = ").Append(检查过).Append('\n');
        sb.Append("  设成循环 = ").Append(设了).Append('\n');
        sb.Append("  改动文件 = ").Append(改过.Count).Append(" 个\n");
        foreach (var n in 改过) sb.Append("    ").Append(n).Append('\n');
        Debug.Log(sb.ToString());
    }

    static List<string> 收集动画FBX()
    {
        var 结果 = new List<string>();
        foreach (var g in AssetDatabase.FindAssets("t:Model", new[] { 动画根 }))
        {
            var p = AssetDatabase.GUIDToAssetPath(g);
            if (!p.EndsWith(".FBX") && !p.EndsWith(".fbx")) continue;
            // 只要动画文件（名字带 @）
            if (!System.IO.Path.GetFileNameWithoutExtension(p).Contains("@")) continue;
            结果.Add(p);
        }
        return 结果;
    }
}
