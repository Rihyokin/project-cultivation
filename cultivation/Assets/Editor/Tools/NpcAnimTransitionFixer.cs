using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// **把 NPC 动画控制器里 `AnyState → X` 的过渡时长压短。**
///
/// 菜单：修仙 / NPC 资产 / 压短动画过渡（起手别再"边跑边挥"）
///
/// ## 背景（两个实测出来的毛病，都是这 0.3 秒造成的）
///
/// 本工程的控制器**没有任何状态之间的过渡**，全靠 9 条 `AnyState → X`
/// （条件 `Action Equals N`），而**每条过渡时长都是 0.3 秒**。
///
/// 1. **起手混着跑步姿势**：真灵/怪在跑动中进入「攻击」时，
///    `GetCurrentAnimatorStateInfo(0)` 先报 `Idle`/`Run` 约 0.35 秒，
///    等 Attack1 变成主状态时 `normalizedTime` 已经是 **0.29** ——
///    攻击动画的前 30% 是和跑步混合着播的。
///
/// 2. **★ 把 Root Motion 的转体搅乱**：本工程动画是 Humanoid，
///    攻击动作的「转体」烘在根旋转 `RootQ` 里（见 `NpcAiBase.OnAnimatorMove`）。
///    0.3 秒的交叉淡入会把**上一个状态的根旋转**（待机 ≈ 0°）和
///    攻击动作的根旋转**混在一起**，于是累计转出来的角度根本不对 ——
///    实测单次攻击里累计偏航能甩到 **±90°+**，看着就是"身体乱扭/刺歪"。
///    压到 0.1 秒之后混合区间短到看不出来，累计角度才对得上动画本身。
///
/// 只改 `AnyState` 那几条（那也是唯一的一类），状态自身没有过渡。
/// </summary>
public static class NpcAnimTransitionFixer
{
    /// <summary>改完之后的过渡时长（秒）</summary>
    public const float 目标时长 = 0.1f;

    /// <summary>NPC 的动画控制器都在这儿（**不在 `Assets/resources` 下**，是独立目录）。
    /// `NPC_Creature` 是第二批（熔岩怪 / 双头蛇那些没 Avatar 的 Generic 骨架），**别漏**。</summary>
    public static readonly string[] 控制器目录 = { "Assets/Animations/NPC", "Assets/Animations/NPC_Creature" };

    [MenuItem("修仙/NPC 资产/压短动画过渡（起手别再边跑边挥）")]
    [MenuItem("Cultivation/NPC Assets/Shorten Animator Transitions")]
    public static void 压短全部()
    {
        var 控制器 = new List<AnimatorController>();
        foreach (var 目录 in 控制器目录)
        {
            if (!AssetDatabase.IsValidFolder(目录)) continue;
            foreach (var g in AssetDatabase.FindAssets("t:AnimatorController", new[] { 目录 }))
            {
                var p = AssetDatabase.GUIDToAssetPath(g);
                var c = AssetDatabase.LoadAssetAtPath<AnimatorController>(p);
                if (c != null && !控制器.Contains(c)) 控制器.Add(c);
            }
        }
        if (控制器.Count == 0) { Debug.LogWarning("[动画过渡] 目录里没找到 AnimatorController：" + string.Join(", ", 控制器目录)); return; }

        int 改 = 0, 跳过 = 0;
        foreach (var c in 控制器)
        {
            int n = 压一个(c);
            if (n > 0) 改 += n; else 跳过++;
        }
        AssetDatabase.SaveAssets();
        Debug.Log("[动画过渡] 控制器 " + 控制器.Count + " 个：改了 " + 改 + " 条过渡（时长 → " + 目标时长
                  + "s），" + 跳过 + " 个本来就已经够短");
    }

    /// <summary>压一个控制器，返回改了几条。已经够短就返回 0。</summary>
    public static int 压一个(AnimatorController c)
    {
        int 改 = 0;
        foreach (var layer in c.layers)
        {
            var sm = layer.stateMachine;
            if (sm == null) continue;

            foreach (var t in sm.anyStateTransitions)
                if (t.duration > 目标时长) { t.duration = 目标时长; 改++; }

            foreach (var cs in sm.states)
                foreach (var t in cs.state.transitions)
                    if (t.duration > 目标时长) { t.duration = 目标时长; 改++; }
        }
        if (改 > 0) EditorUtility.SetDirty(c);
        return 改;
    }

    /// <summary>只压「冰魄怪」那一个（做验证用）</summary>
    public static int 压冰魄怪()
    {
        var c = AssetDatabase.LoadAssetAtPath<AnimatorController>(控制器目录[0] + "/BingPoGuai.controller");
        if (c == null) return -1;
        int n = 压一个(c);
        AssetDatabase.SaveAssets();
        return n;
    }
}
