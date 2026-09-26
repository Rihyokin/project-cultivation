using UnityEngine;

/// <summary>
/// **修「NPC 僵在原地、完全没有 idle 动作」**。
///
/// 实测到的病根（2026-09-26）：
///   · 场景文件里**存下来的** NPC 实例，Animator 的 playable 是断的：
///     状态时钟照走（`normalizedTime` 从 1.676 涨到 1.758）、`isInitialized=true`、
///     `hasBoundPlayables=true`、`cullingMode=AlwaysAnimate`、`weight=1.0`、`avatar=null`（generic，正常），
///     **但 87 根骨头 0.8 秒内 0 根动，姿势一个字节都不变** → 看起来就是"僵硬地站在那"。
///   · 同一个实例，手动 `Rebind() + Update(0f)` 之后 **立刻恢复**（17/87 根动）。
///   · 从预制体**新 Instantiate** 出来的实例本来就是好的（23/87 根动、最大 1.54°）——
///     所以预制体没问题，是场景里那份序列化状态坏了。
///
/// 修法就是启动时补一次 `Rebind + Update(0f)`（标准做法：换 controller / 加载存档后都该来一次）。
/// 健康的实例上再调一次也无害。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Animator))]
public class Npc动画自检 : MonoBehaviour
{
    [Tooltip("启动时补一次 Rebind，修老场景里僵住的实例")]
    public bool 启动时修复 = true;

    void Start()
    {
        if (启动时修复) 修();
    }

    /// <summary>对本物体上的 Animator 修一次</summary>
    public void 修()
    {
        var a = GetComponent<Animator>();
        修(a);
    }

    /// <summary>
    /// 对一个 Animator 做一次 Rebind + 立即求值。返回是否真的动手了。
    /// 动画器没启用 / 没 controller 时什么都不做（避免 Unity 报错）。
    /// </summary>
    public static bool 修(Animator a)
    {
        if (a == null || !a.isActiveAndEnabled) return false;
        if (a.runtimeAnimatorController == null) return false;
        a.Rebind();
        a.Update(0f);
        return true;
    }
}
