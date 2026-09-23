using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// 把 resources/NPC 里的模型、贴图、动画组合成【可直接使用的 prefab】。
///
/// 现状（探查结果）：
///   · 每个 NPC 一个目录：模型 FBX + 贴图 + 材质 + 【已经有 prefab】+ .mat
///   · 动画放在同级的 xxx_Animation 目录里，命名 角色@动作.FBX
///   · 但是全 NPC 下 0 个 AnimatorController —— 所以 prefab 是死的，动不起来
///
/// 这个工具做四件事：
///   1. 模型 FBX → Humanoid（生成 Avatar）
///   2. 动画 FBX → Humanoid + 共享同角色的 Avatar（变成可重定向的曲线）
///   3. 每个 NPC 建一个 AnimatorController，把它的动作全接进去
///   4. 给该 NPC 的 prefab 挂 Animator + NpcAnimator，指向控制器
///
/// 【必须分步跑】FBX 重新导入是异步的，一口气做完会拿到旧的 Avatar。
///
/// 菜单：Cultivation / NPC 组合 (1) 模型转 Humanoid
///       Cultivation / NPC 组合 (2) 动画共享 Avatar
///       Cultivation / NPC 组合 (3) 建控制器 + 挂到 prefab
/// </summary>
public static class NpcPrefabBuilder
{
    const string Npc根 = "Assets/resources/NPC";
    const string 控制器目录 = "Assets/Animations/NPC";

    /// <summary>从 "BaiGuNiangNiang@Attack1" 取出 "BaiGuNiangNiang"</summary>
    static string 角色名(string 路径或名)
    {
        var n = System.IO.Path.GetFileNameWithoutExtension(路径或名);
        int at = n.IndexOf('@');
        if (at >= 0) return n.Substring(0, at);
        // 没有 @ 的多半是 prefab，名字带 _01 _02 这种序号，要去掉才能和控制器对上。
        // 之前没去，导致 245 个 prefab 配不上控制器。
        return 去序号(n);
    }

    /// <summary>去掉名字末尾的 _01 / _1 这类序号</summary>
    static string 去序号(string n)
    {
        int 下划 = n.LastIndexOf('_');
        if (下划 <= 0 || 下划 >= n.Length - 1) return n;
        var 尾 = n.Substring(下划 + 1);
        if (尾.Length <= 2 && int.TryParse(尾, out _)) return n.Substring(0, 下划);
        return n;
    }

    /// <summary>给 prefab 找控制器时用：先按原名、再去序号、再截 @ 前</summary>
    static AnimatorController 找控制器(Dictionary<string, AnimatorController> 表, string prefab路径)
    {
        var n = System.IO.Path.GetFileNameWithoutExtension(prefab路径);
        if (表.TryGetValue(n, out var c)) return c;
        var 去 = 去序号(n);
        if (表.TryGetValue(去, out c)) return c;
        int at = n.IndexOf('@');
        if (at >= 0 && 表.TryGetValue(n.Substring(0, at), out c)) return c;
        return null;
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

    // ---------------------------------------------------------------- 第 1 步

    [MenuItem("Cultivation/NPC 组合 (1) 模型转 Humanoid")]
    public static void Step1()
    {
        扫描(out var 模型, out _, out _);
        int 改了 = 0, 已是 = 0;

        foreach (var p in 模型)
        {
            var mi = AssetImporter.GetAtPath(p) as ModelImporter;
            if (mi == null) continue;
            if (mi.animationType == ModelImporterAnimationType.Human
                && mi.avatarSetup == ModelImporterAvatarSetup.CreateFromThisModel) { 已是++; continue; }

            mi.animationType = ModelImporterAnimationType.Human;
            mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            mi.importAnimation = false;
            mi.SaveAndReimport();
            改了++;
        }
        AssetDatabase.Refresh();
        Debug.Log("[NpcPrefabBuilder] 第1步：模型 " + 模型.Count + " 个，改 " + 改了 + "，已是 " + 已是);
    }

    // ---------------------------------------------------------------- 第 2 步

    [MenuItem("Cultivation/NPC 组合 (2) 动画共享 Avatar")]
    public static void Step2()
    {
        扫描(out var 模型, out var 动画, out _);

        var 角色Avatar = new Dictionary<string, Avatar>();
        foreach (var p in 模型)
        {
            var 角 = 角色名(p);
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(p))
                if (o is Avatar a && a.isHuman && !角色Avatar.ContainsKey(角)) { 角色Avatar[角] = a; break; }
        }

        int 改了 = 0, 跳过 = 0, 没配 = 0;
        var 缺Avatar = new List<string>();
        foreach (var p in 动画)
        {
            var 角 = 角色名(p);
            if (!角色Avatar.TryGetValue(角, out var av))
            {
                没配++;
                if (!缺Avatar.Contains(角) && 缺Avatar.Count < 15) 缺Avatar.Add(角);
                continue;
            }
            var mi = AssetImporter.GetAtPath(p) as ModelImporter;
            if (mi == null) continue;
            if (mi.animationType == ModelImporterAnimationType.Human && mi.sourceAvatar == av) { 跳过++; continue; }

            mi.animationType = ModelImporterAnimationType.Human;
            mi.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
            mi.sourceAvatar = av;
            mi.importAnimation = true;
            mi.SaveAndReimport();
            改了++;
        }
        AssetDatabase.Refresh();

        var sb = new StringBuilder();
        sb.Append("[NpcPrefabBuilder] 第2步：动画 ").Append(动画.Count).Append(" 个，改 ").Append(改了)
          .Append("，已是 ").Append(跳过).Append("，没配对 ").Append(没配).Append('\n');
        sb.Append("  可用角色 Avatar = ").Append(角色Avatar.Count).Append('\n');
        if (缺Avatar.Count > 0) sb.Append("  缺 Avatar 的角色（前 15）：").Append(string.Join(", ", 缺Avatar.ToArray())).Append('\n');
        Debug.Log(sb.ToString());
    }

    // ---------------------------------------------------------------- 第 3 步

    [MenuItem("Cultivation/NPC 组合 (3) 建控制器 + 挂到 prefab")]
    public static void Step3()
    {
        扫描(out _, out var 动画, out var prefabs);
        if (!AssetDatabase.IsValidFolder("Assets/Animations")) AssetDatabase.CreateFolder("Assets", "Animations");
        if (!AssetDatabase.IsValidFolder(控制器目录)) AssetDatabase.CreateFolder("Assets/Animations", "NPC");

        // 角色 → 动作表
        var 角色动作 = new Dictionary<string, SortedDictionary<string, AnimationClip>>();
        foreach (var p in 动画)
        {
            var 角 = 角色名(p);
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

        var 报告 = new StringBuilder();
        int 建控 = 0, 挂上 = 0, 没动画 = 0, 坏Prefab = 0;

        // 先把控制器都建好
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
            int i = 0;
            foreach (var a in kv.Value)
            {
                var st = sm.AddState(a.Key);
                st.motion = a.Value;
                i++;
            }
            // AnyState → 每个状态，条件 Action == 索引
            i = 0;
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
            建控++;
        }
        AssetDatabase.SaveAssets();

        // 再把 Animator 挂到 prefab 上
        foreach (var p in prefabs)
        {
            var ctrl = 找控制器(角色控制器, p);
            if (ctrl == null) { 没动画++; continue; }

            // 有些 prefab 是空的（0 个根节点），LoadPrefabContents 会抛异常，
            // 不接住的话整个循环会中断，后面的 prefab 全挂不上。
            GameObject 根 = null;
            try { 根 = PrefabUtility.LoadPrefabContents(p); }
            catch { 坏Prefab++; continue; }
            if (根 == null) { 坏Prefab++; continue; }

            bool 动了 = false;
            var anim = 根.GetComponent<Animator>();
            if (anim == null) { anim = 根.AddComponent<Animator>(); 动了 = true; }

            if (anim.runtimeAnimatorController != ctrl) { anim.runtimeAnimatorController = ctrl; 动了 = true; }

            // 模型自己的 Avatar
            if (anim.avatar == null)
            {
                foreach (var o in AssetDatabase.LoadAllAssetsAtPath(p))
                    if (o is Avatar a && a.isHuman) { anim.avatar = a; 动了 = true; break; }
            }
            if (anim.avatar == null && ctrl != null && ctrl.animationClips.Length > 0)
            {
                // 用控制器的（会走 SharedAvatar）
            }

            if (根.GetComponent<NpcAnimator>() == null) { 根.AddComponent<NpcAnimator>(); 动了 = true; }

            if (动了) { PrefabUtility.SaveAsPrefabAsset(根, p); 挂上++; }
            PrefabUtility.UnloadPrefabContents(根);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        报告.Append("[NpcPrefabBuilder] 第3步：建控制器 ").Append(建控)
            .Append("，挂了 prefab ").Append(挂上)
            .Append("，没动画可挂 ").Append(没动画)
            .Append("，坏 prefab ").Append(坏Prefab).Append('\n');
        Debug.Log(报告.ToString());
    }

    /// <summary>核查：有多少 NPC 已经能用了</summary>
    [MenuItem("Cultivation/NPC 组合 (4) 核查")]
    public static void Verify()
    {
        扫描(out var 模型, out var 动画, out var prefabs);
        int 有控制器 = 0, 有Animator = 0;
        foreach (var p in prefabs)
        {
            var 根 = AssetDatabase.LoadAssetAtPath<GameObject>(p);
            if (根 == null) continue;
            if (根.GetComponent<Animator>() != null) 有Animator++;
        }
        foreach (var g in AssetDatabase.FindAssets("t:AnimatorController", new[] { 控制器目录 })) 有控制器++;

        var sb = new StringBuilder();
        sb.Append("[NpcPrefabBuilder] 核查\n");
        sb.Append("  模型 FBX      = ").Append(模型.Count).Append('\n');
        sb.Append("  动画 FBX      = ").Append(动画.Count).Append('\n');
        sb.Append("  prefab        = ").Append(prefabs.Count).Append("（其中挂了 Animator 的 ").Append(有Animator).Append("）\n");
        sb.Append("  控制器        = ").Append(有控制器).Append('\n');
        Debug.Log(sb.ToString());
    }
}
