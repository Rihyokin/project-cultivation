using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// 接非人形生物（四足/怪物/坐骑）的动画。
///
/// 背景：上一轮把模型都当 Humanoid 处理，结果 82 个动画配不上 —— 因为
/// 白鹿精、双头蛇、火狼、坐骑这些是四足动物，骨架里根本没有 Hips/Spine 那套人形骨骼，
/// Unity 生不出 Humanoid Avatar。
///
/// 解法来自 Unity 自己的报错：
///   "Legacy AnimationClips are not allowed in Animator Controllers.
///    To use this animation in this Animator Controller, you must reimport
///    in as a Generic or Humanoid animation clip"
/// —— 人形走 Humanoid，**非人形就走 Generic**。
///
/// Generic 的动画曲线是按【骨骼路径】存的，只要模型和动画出自同一套骨架就能对上。
/// 这批正好是同一个包里的，路径一致，所以能直接接。
///
/// 菜单：Cultivation / 非人形生物 (1) 模型转 Generic
///       Cultivation / 非人形生物 (2) 动画转 Generic
///       Cultivation / 非人形生物 (3) 建控制器 + 挂到 prefab
///       Cultivation / 非人形生物 (4) 核查
/// </summary>
public static class NpcCreatureSetup
{
    const string Npc根 = "Assets/resources/NPC";
    const string 控制器目录 = "Assets/Animations/NPC_Creature";

    static string 角色名(string 路径或名)
    {
        var n = System.IO.Path.GetFileNameWithoutExtension(路径或名);
        int at = n.IndexOf('@');
        if (at >= 0) return n.Substring(0, at);
        return 去序号(n);
    }

    static string 去序号(string n)
    {
        int 下划 = n.LastIndexOf('_');
        if (下划 <= 0 || 下划 >= n.Length - 1) return n;
        var 尾 = n.Substring(下划 + 1);
        if (尾.Length <= 2 && int.TryParse(尾, out _)) return n.Substring(0, 下划);
        return n;
    }

    static void 扫描(out List<string> 模型, out List<string> 动画, out List<string> prefab)
    {
        模型 = new List<string>(); 动画 = new List<string>(); prefab = new List<string>();
        foreach (var f in System.IO.Directory.GetFiles(Npc根, "*", System.IO.SearchOption.AllDirectories))
        {
            var p = f.Replace('\\', '/');
            var 名 = System.IO.Path.GetFileName(p);
            if (名.EndsWith(".meta")) continue;
            if (名.EndsWith(".prefab")) { prefab.Add(p); continue; }
            if (名.EndsWith(".FBX") || 名.EndsWith(".fbx"))
            {
                if (System.IO.Path.GetFileNameWithoutExtension(p).Contains("@")) 动画.Add(p);
                else 模型.Add(p);
            }
        }
    }

    /// <summary>这个模型有没有成功的 Humanoid Avatar</summary>
    static bool 是Humanoid(string 模型路径)
    {
        foreach (var o in AssetDatabase.LoadAllAssetsAtPath(模型路径))
            if (o is Avatar a && a.isHuman) return true;
        return false;
    }

    /// <summary>把非人形模型的名字收集起来（这些就是"生物"）</summary>
    static HashSet<string> 生物名()
    {
        扫描(out var 模型, out _, out _);
        var 生物 = new HashSet<string>();
        foreach (var p in 模型)
            if (!是Humanoid(p)) 生物.Add(角色名(p));
        return 生物;
    }

    // ---------------------------------------------------------------- 第 1 步

    [MenuItem("Cultivation/非人形生物 (1) 模型转 Generic")]
    public static void Step1()
    {
        扫描(out var 模型, out _, out _);
        int 改了 = 0, 已是 = 0;
        var 名单 = new List<string>();

        foreach (var p in 模型)
        {
            if (是Humanoid(p)) continue;          // 人形的跳过
            var mi = AssetImporter.GetAtPath(p) as ModelImporter;
            if (mi == null) continue;

            if (mi.animationType == ModelImporterAnimationType.Generic) { 已是++; continue; }

            mi.animationType = ModelImporterAnimationType.Generic;
            mi.avatarSetup = ModelImporterAvatarSetup.NoAvatar;
            mi.importAnimation = false;
            mi.SaveAndReimport();
            改了++;
            名单.Add(角色名(p));
        }
        AssetDatabase.Refresh();
        Debug.Log("[NpcCreatureSetup] 第1步：非人形模型转 Generic，改 " + 改了 + "，已是 " + 已是
                  + "\n  角色：" + string.Join(", ", 名单.ToArray()));
    }

    // ---------------------------------------------------------------- 第 2 步

    [MenuItem("Cultivation/非人形生物 (2) 动画转 Generic")]
    public static void Step2()
    {
        扫描(out _, out var 动画, out _);
        var 生物 = 生物名();
        int 改了 = 0, 已是 = 0, 跳过人形 = 0;

        foreach (var p in 动画)
        {
            var 角 = 角色名(p);
            if (!生物.Contains(角)) { 跳过人形++; continue; }   // 人形的上一轮已经处理好了

            var mi = AssetImporter.GetAtPath(p) as ModelImporter;
            if (mi == null) continue;
            if (mi.animationType == ModelImporterAnimationType.Generic) { 已是++; continue; }

            mi.animationType = ModelImporterAnimationType.Generic;
            mi.avatarSetup = ModelImporterAvatarSetup.NoAvatar;
            mi.importAnimation = true;
            mi.SaveAndReimport();
            改了++;
        }
        AssetDatabase.Refresh();
        Debug.Log("[NpcCreatureSetup] 第2步：生物动画转 Generic，改 " + 改了 + "，已是 " + 已是
                  + "，跳过（人形，上轮已处理）" + 跳过人形 + "，生物角色数 " + 生物.Count);
    }

    // ---------------------------------------------------------------- 第 3 步

    [MenuItem("Cultivation/非人形生物 (3) 建控制器 + 挂到 prefab")]
    public static void Step3()
    {
        扫描(out _, out var 动画, out var prefabs);
        var 生物 = 生物名();
        if (!AssetDatabase.IsValidFolder("Assets/Animations")) AssetDatabase.CreateFolder("Assets", "Animations");
        if (!AssetDatabase.IsValidFolder(控制器目录)) AssetDatabase.CreateFolder("Assets/Animations", "NPC_Creature");

        // 只收 生物 的动画
        var 角色动作 = new Dictionary<string, SortedDictionary<string, AnimationClip>>();
        foreach (var p in 动画)
        {
            var 角 = 角色名(p);
            if (!生物.Contains(角)) continue;
            var 全名 = System.IO.Path.GetFileNameWithoutExtension(p);
            int at = 全名.IndexOf('@');
            if (at < 0) continue;
            var 动作 = 全名.Substring(at + 1);

            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(p))
            {
                if (!(o is AnimationClip c) || c.name.StartsWith("__preview")) continue;
                if (!角色动作.ContainsKey(角)) 角色动作[角] = new SortedDictionary<string, AnimationClip>();
                角色动作[角][动作] = c;
            }
        }

        // 建控制器
        var 角色控制器 = new Dictionary<string, AnimatorController>();
        foreach (var kv in 角色动作)
        {
            var 路径 = 控制器目录 + "/" + kv.Key + ".controller";
            var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(路径);
            if (ctrl != null) AssetDatabase.DeleteAsset(路径);
            ctrl = AnimatorController.CreateAnimatorControllerAtPath(路径);
            if (ctrl == null) continue;

            ctrl.AddParameter("Action", AnimatorControllerParameterType.Int);
            ctrl.AddParameter("Speed", AnimatorControllerParameterType.Float);

            var sm = ctrl.layers[0].stateMachine;
            foreach (var a in kv.Value)
            {
                var st = sm.AddState(a.Key);
                st.motion = a.Value;
            }
            int i = 0;
            foreach (var a in kv.Value)
            {
                var t = sm.AddAnyStateTransition(sm.states[i].state);
                t.hasExitTime = false;
                t.duration = 0.15f;
                t.canTransitionToSelf = false;
                t.AddCondition(AnimatorConditionMode.Equals, i, "Action");
                i++;
            }
            if (sm.states.Length > 0) sm.defaultState = sm.states[0].state;
            角色控制器[kv.Key] = ctrl;
        }
        AssetDatabase.SaveAssets();

        // 挂到 prefab
        int 挂了 = 0, 坏 = 0, 没控 = 0;
        foreach (var p in prefabs)
        {
            if (!角色控制器.TryGetValue(角色名(p), out var ctrl)) { 没控++; continue; }

            GameObject 根 = null;
            try { 根 = PrefabUtility.LoadPrefabContents(p); } catch { 坏++; continue; }
            if (根 == null) { 坏++; continue; }

            bool 动 = false;
            var anim = 根.GetComponent<Animator>();
            if (anim == null) { anim = 根.AddComponent<Animator>(); 动 = true; }
            if (anim.runtimeAnimatorController != ctrl) { anim.runtimeAnimatorController = ctrl; 动 = true; }
            if (根.GetComponent<NpcAnimator>() == null) { 根.AddComponent<NpcAnimator>(); 动 = true; }

            if (动) { PrefabUtility.SaveAsPrefabAsset(根, p); 挂了++; }
            PrefabUtility.UnloadPrefabContents(根);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[NpcCreatureSetup] 第3步：生物角色 " + 生物.Count + "，建控制器 " + 角色控制器.Count
                  + "，挂 prefab " + 挂了 + "，坏 prefab " + 坏 + "，没控制器 " + 没控);
    }

    // ---------------------------------------------------------------- 核查

    [MenuItem("Cultivation/非人形生物 (4) 核查")]
    public static void Verify()
    {
        扫描(out var 模型, out var 动画, out var prefab);
        var 生物 = 生物名();

        int 已转Generic模型 = 0, 已转Generic动画 = 0, 还是Legacy = 0;
        foreach (var p in 模型)
        {
            var mi = AssetImporter.GetAtPath(p) as ModelImporter;
            if (mi != null && mi.animationType == ModelImporterAnimationType.Generic) 已转Generic模型++;
        }
        foreach (var p in 动画)
        {
            if (!生物.Contains(角色名(p))) continue;
            var mi = AssetImporter.GetAtPath(p) as ModelImporter;
            if (mi == null) continue;
            if (mi.animationType == ModelImporterAnimationType.Generic) 已转Generic动画++;
            else if (mi.animationType == ModelImporterAnimationType.Legacy) 还是Legacy++;
        }
        int 控制器 = 0;
        if (AssetDatabase.IsValidFolder(控制器目录))
            foreach (var g in AssetDatabase.FindAssets("t:AnimatorController", new[] { 控制器目录 })) 控制器++;

        var sb = new StringBuilder();
        sb.Append("[NpcCreatureSetup] 核查\n");
        sb.Append("  非人形角色数      = ").Append(生物.Count).Append('\n');
        sb.Append("  已转 Generic 模型 = ").Append(已转Generic模型).Append('\n');
        sb.Append("  已转 Generic 动画 = ").Append(已转Generic动画).Append("   还是 Legacy = ").Append(还是Legacy).Append('\n');
        sb.Append("  生物控制器        = ").Append(控制器).Append('\n');
        sb.Append("  角色名单：").Append(string.Join(", ", new List<string>(生物).ToArray())).Append('\n');
        Debug.Log(sb.ToString());
    }
}
