using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 按策划给的裁剪表，把 Animation Library 里的 Mixamo 素材做成技能动作 / 技能引导动作片段。
///
/// 裁剪表：
///   技能动作1     Petting Animal              0 ~ 6s     2 倍速
///   技能引导动作1  Petting Animal              6 ~ 12s    2 倍速 循环（3.00s）
///   技能动作2     Standing 2H Magic Attack 01  全程
///   技能动作3     Standing 2H Magic Attack 01  0 ~ 1.17s
///   技能引导动作2  Standing 2H Magic Attack 01  1.08 ~ 1.20s  0.5 倍速 循环
///   技能引导动作3  Standing Idle 03             全程       循环
///   技能动作4     Standing Taunt Battlecry      全程
///
/// 做法：先把素材转成 Humanoid（这样才有肌肉曲线、能重定向到我们的角色），
/// 再按固定采样率在目标时间范围内重新采样，顺便完成「裁剪 + 变速」。
///
/// 菜单：修仙 / 生成技能动作片段   （ASCII：Cultivation / Bake Skill Clips）
/// </summary>
public static class SkillClipBuilder
{
    const string SrcDir = "Assets/resources/Animation Library";
    const string OutDir = "Assets/Animations/技能动作";
    const int Fps = 30;

    class Spec
    {
        public string File;      // 源 fbx 文件名（不含扩展名）
        public string Name;      // 输出片段名
        public float Start;
        public float End;
        public float Speed;
        public bool Loop;
    }

    static readonly Spec[] Specs =
    {
        new Spec { File = "Petting Animal",              Name = "技能动作1",      Start = 0f,    End = 6f,    Speed = 2f,   Loop = false },
        new Spec { File = "Petting Animal",              Name = "技能引导动作1",  Start = 6f,    End = 12f,   Speed = 2f,   Loop = true  },
        new Spec { File = "Standing 2H Magic Attack 01", Name = "技能动作2",      Start = 0f,    End = -1f,   Speed = 1f,   Loop = false },
        new Spec { File = "Standing 2H Magic Attack 01", Name = "技能动作3",      Start = 0f,    End = 1.17f, Speed = 1f,   Loop = false },
        new Spec { File = "Standing 2H Magic Attack 01", Name = "技能引导动作2",  Start = 1.08f, End = 1.20f, Speed = 0.5f, Loop = true  },
        new Spec { File = "Standing Idle 03",            Name = "技能引导动作3",  Start = 0f,    End = -1f,   Speed = 1f,   Loop = true  },
        new Spec { File = "Standing Taunt Battlecry",    Name = "技能动作4",      Start = 0f,    End = -1f,   Speed = 1f,   Loop = false },
    };

    [MenuItem("修仙/生成技能动作片段")]
    [MenuItem("Cultivation/Bake Skill Clips")]
    public static void Bake()
    {
        // 1) 素材先转 Humanoid
        foreach (var f in new[] { "Petting Animal", "Standing 2H Magic Attack 01", "Standing Idle 03", "Standing Taunt Battlecry" })
        {
            string p = SrcDir + "/" + f + ".fbx";
            var mi = AssetImporter.GetAtPath(p) as ModelImporter;
            if (mi == null) { Debug.LogWarning("[SkillClipBuilder] 找不到 " + p); continue; }
            if (mi.animationType != ModelImporterAnimationType.Human)
            {
                mi.animationType = ModelImporterAnimationType.Human;
                mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                mi.SaveAndReimport();
            }
        }
        AssetDatabase.Refresh();

        if (!AssetDatabase.IsValidFolder("Assets/Animations")) AssetDatabase.CreateFolder("Assets", "Animations");
        if (!AssetDatabase.IsValidFolder(OutDir)) AssetDatabase.CreateFolder("Assets/Animations", "技能动作");

        // 2) 逐个裁剪
        var report = new System.Text.StringBuilder();
        foreach (var s in Specs)
        {
            var src = LoadClip(SrcDir + "/" + s.File + ".fbx");
            if (src == null) { report.Append("跳过（读不到源片段）: ").Append(s.File).Append("\n"); continue; }

            float end = s.End < 0f ? src.length : Mathf.Min(s.End, src.length);
            float start = Mathf.Clamp(s.Start, 0f, Mathf.Max(0f, end - 0.01f));
            Make(s, src, start, end);
            report.Append(s.Name).Append("  ").Append(s.File)
                  .Append("  ").Append(start.ToString("F2")).Append("~").Append(end.ToString("F2"))
                  .Append("s  x").Append(s.Speed).Append(s.Loop ? "  循环" : "")
                  .Append("  -> ").Append(((end - start) / s.Speed).ToString("F2")).Append("s\n");
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[SkillClipBuilder] 完成：\n" + report);
    }

    static void Make(Spec s, AnimationClip src, float start, float end)
    {
        var clip = new AnimationClip { name = s.Name, frameRate = Fps };
        float outLen = Mathf.Max(1f / Fps, (end - start) / s.Speed);
        int frames = Mathf.Max(1, Mathf.RoundToInt(outLen * Fps));

        var curves = new Dictionary<string, AnimationCurve>();
        foreach (var b in AnimationUtility.GetCurveBindings(src))
        {
            var srcCurve = AnimationUtility.GetEditorCurve(src, b);
            if (srcCurve == null) continue;
            var c = new AnimationCurve();
            for (int f = 0; f <= frames; f++)
            {
                float outT = f / (float)Fps;
                // 变速：输出时间走 1 秒，源时间走 Speed 秒
                float srcT = Mathf.Clamp(start + outT * s.Speed, 0f, src.length);
                c.AddKey(new Keyframe(outT, srcCurve.Evaluate(srcT)));
            }
            curves[b.propertyName] = c;
        }

        foreach (var kv in curves)
        {
            var binding = EditorCurveBinding.FloatCurve("", typeof(Animator), kv.Key);
            AnimationUtility.SetEditorCurve(clip, binding, kv.Value);
        }

        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = s.Loop;
        AnimationUtility.SetAnimationClipSettings(clip, settings);

        string path = OutDir + "/" + s.Name + ".anim";
        if (AssetDatabase.LoadAssetAtPath<AnimationClip>(path) != null) AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(clip, path);
    }

    static AnimationClip LoadClip(string fbx)
    {
        foreach (var o in AssetDatabase.LoadAllAssetsAtPath(fbx))
            if (o is AnimationClip c && !c.name.StartsWith("__preview")) return c;
        return null;
    }
}
