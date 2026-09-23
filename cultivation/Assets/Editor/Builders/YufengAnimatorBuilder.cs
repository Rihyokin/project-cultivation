using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// 为被动神通【凭虚御风】在 PlayerLocomotion 控制器里补上四个状态：
///   御风_升空 / 御风_Idle / 御风_前进 / 御风_落地
///
/// 动画来源是项目里现有的 Animation Library（没有专门做御风动画，先用最接近的片段）：
///   升空       ← Armature|Jump_Start      （起跳，用作腾空）
///   御风idle   ← Armature|Jump_Loop       （滞空循环）
///   御风前进   ← Armature|Swim_Fwd_Loop   （前游，最接近「御风前飞」）
///   落地       ← Armature|Jump_Land       （落地缓冲）
///
/// 可重复执行：会先删掉旧的御风状态再重建，不会重复堆积。
/// 菜单：修仙 / 构建凭虚御风动画状态   （ASCII：Cultivation / Build Yufeng Animator）
/// </summary>
public static class YufengAnimatorBuilder
{
    const string ControllerPath = "Assets/Animations/PlayerLocomotion.controller";
    const string Lib1 = "Assets/resources/Animation Library/1/UAL1_Standard.fbx";
    const string Lib2 = "Assets/resources/Animation Library/2/UAL2_Standard.fbx";

    // 参数名（要和 PlayerAnimationController 里的字段保持一致）
    const string PFlying = "Flying";
    const string PFlyMoving = "FlyMoving";
    const string PTakeOff = "TakeOff";
    const string PLand = "Land";

    const string STakeOff = "Yufeng_TakeOff";
    const string SIdle = "Yufeng_Idle";
    const string SFwd = "Yufeng_Forward";
    const string SLand = "Yufeng_Land";

    [MenuItem("修仙/构建凭虚御风动画状态")]
    [MenuItem("Cultivation/Build Yufeng Animator")]
    public static void Build()
    {
        var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (ctrl == null) { Debug.LogError("[YufengAnimatorBuilder] 找不到 " + ControllerPath); return; }

        // 优先用专门烘焙的御风片段；没有再退回动画库里的近似片段
        var takeOffClip = LoadBaked("御风_升空") ?? LoadClip(Lib1, "Armature|Jump_Start");
        var idleClip    = LoadBaked("御风_Idle") ?? LoadClip(Lib1, "Armature|Jump_Loop");
        var fwdClip     = LoadBaked("御风_前进") ?? LoadClip(Lib1, "Armature|Swim_Fwd_Loop");
        var landClip    = LoadBaked("御风_落地") ?? LoadClip(Lib1, "Armature|Jump_Land");

        if (takeOffClip == null || idleClip == null || fwdClip == null || landClip == null)
        {
            Debug.LogError("[YufengAnimatorBuilder] 缺少源动画片段");
            return;
        }
        Debug.Log("[YufengAnimatorBuilder] 使用片段：" + takeOffClip.name + " / " + idleClip.name
                  + " / " + fwdClip.name + " / " + landClip.name);

        EnsureParameter(ctrl, PFlying, AnimatorControllerParameterType.Bool);
        EnsureParameter(ctrl, PFlyMoving, AnimatorControllerParameterType.Bool);
        EnsureParameter(ctrl, PTakeOff, AnimatorControllerParameterType.Trigger);
        EnsureParameter(ctrl, PLand, AnimatorControllerParameterType.Trigger);

        var sm = ctrl.layers[0].stateMachine;
        var locomotion = sm.defaultState;

        // 清掉旧的御风状态，保证可重复执行
        var toRemove = new List<AnimatorState>();
        foreach (var c in sm.states)
            if (c.state != null && c.state.name != null &&
                (c.state.name.StartsWith("御风") || c.state.name.StartsWith("Yufeng_")))
                toRemove.Add(c.state);
        foreach (var s in toRemove) sm.RemoveState(s);

        var sTakeOff = sm.AddState(STakeOff); sTakeOff.motion = takeOffClip; sTakeOff.speed = 1f;
        var sIdle = sm.AddState(SIdle); sIdle.motion = idleClip; sIdle.speed = 1f;
        var sFwd = sm.AddState(SFwd); sFwd.motion = fwdClip; sFwd.speed = 1f;
        var sLand = sm.AddState(SLand); sLand.motion = landClip; sLand.speed = 1f;

        // AnyState → 升空（按 Trigger）
        var a1 = sm.AddAnyStateTransition(sTakeOff);
        a1.hasExitTime = false; a1.duration = 0.18f; a1.canTransitionToSelf = false;
        a1.AddCondition(AnimatorConditionMode.If, 0f, PTakeOff);

        // 升空 → 御风idle（播完）
        var t1 = sTakeOff.AddTransition(sIdle);
        t1.hasExitTime = true; t1.exitTime = 0.85f; t1.duration = 0.35f;

        // 御风idle ↔ 御风前进
        var t2 = sIdle.AddTransition(sFwd);
        t2.hasExitTime = false; t2.duration = 0.30f;
        t2.AddCondition(AnimatorConditionMode.If, 0f, PFlyMoving);

        var t3 = sFwd.AddTransition(sIdle);
        t3.hasExitTime = false; t3.duration = 0.30f;
        t3.AddCondition(AnimatorConditionMode.IfNot, 0f, PFlyMoving);

        // AnyState → 落地（按 Trigger）
        var a2 = sm.AddAnyStateTransition(sLand);
        a2.hasExitTime = false; a2.duration = 0.15f; a2.canTransitionToSelf = false;
        a2.AddCondition(AnimatorConditionMode.If, 0f, PLand);

        // 落地 → 回到常规移动
        if (locomotion != null)
        {
            var t4 = sLand.AddTransition(locomotion);
            t4.hasExitTime = true; t4.exitTime = 0.85f; t4.duration = 0.30f;
        }

        EditorUtility.SetDirty(ctrl);
        AssetDatabase.SaveAssets();

        Debug.Log("[YufengAnimatorBuilder] 已构建御风状态："
                  + STakeOff + " / " + SIdle + " / " + SFwd + " / " + SLand);
    }

    /// <summary>读烘焙出来的御风片段</summary>
    static AnimationClip LoadBaked(string clipName)
    {
        return AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Animations/凭虚御风/" + clipName + ".anim");
    }

    static AnimationClip LoadClip(string fbxPath, string clipName)
    {
        foreach (var o in AssetDatabase.LoadAllAssetsAtPath(fbxPath))
            if (o is AnimationClip c && c.name == clipName) return c;
        return null;
    }

    static void EnsureParameter(AnimatorController ctrl, string name, AnimatorControllerParameterType type)
    {
        foreach (var p in ctrl.parameters)
            if (p.name == name) return;
        ctrl.AddParameter(name, type);
    }
}
