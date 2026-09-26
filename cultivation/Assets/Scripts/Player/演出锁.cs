using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// **演出锁：过场/对话期间锁住玩家操作**（主线需求第 ⑥ 项）。
///
/// 挂 Player 上即可。它盯的是 <see cref="黑幕字幕.演出中"/>（黑幕过场/起名/强制演出）
/// 和 <see cref="DialogueUI.正在显示"/>（对话框开着）；只要其中一个成立，就把**走 / 打 / 技能 / 飞**
/// 四类组件关掉（名单和 <c>PlayerDeathSequence.收集操作组件</c> 保持一致 —— 死亡流程也是这么干的）。
///
/// 【关键】只还原**自己关掉的那些**：`BasicSword01` 这类能力组件平时就可能是关的
/// （没学功法时本来就该关着，见 `PlayerAbilityLoader`）。所以：
///   1. 关之前记下每个组件当时的 enabled；
///   2. 解锁时按记录还原；
///   3. 最后再让 `PlayerAbilityLoader.Refresh()` 按"当前功法 + 已获得被动"重对一遍
///      —— 免得把不该开的开起来（重生后冒出剑那个 bug 就是这么来的）。
/// </summary>
[DisallowMultipleComponent]
public class 演出锁 : MonoBehaviour
{
    [Tooltip("演出/对话期间要关掉的操作组件类名（和 PlayerDeathSequence 的死亡禁用名单一致）")]
    public string[] 要锁的组件 = { "PlayerController", "BasicSword01", "ActiveSkillCaster", "YufengFlight" };

    [Tooltip("对话框开着的时候也锁（不只是黑幕过场）")]
    public bool 对话也锁 = true;

    readonly List<MonoBehaviour> 关着的 = new List<MonoBehaviour>();
    readonly List<bool> 原本状态 = new List<bool>();
    PlayerAbilityLoader 能力装载;

    public bool 正在锁 => 关着的.Count > 0;

    void Awake() { 能力装载 = GetComponent<PlayerAbilityLoader>(); }

    void Update()
    {
        bool 该锁 = 黑幕字幕.演出中 || (对话也锁 && DialogueUI.正在显示);
        if (该锁 && !正在锁) 锁();
        else if (!该锁 && 正在锁) 解锁();
    }

    void 锁()
    {
        foreach (var c in GetComponents<MonoBehaviour>())
        {
            if (c == null || c == this) continue;
            bool 在名单里 = false;
            if (要锁的组件 != null)
                for (int i = 0; i < 要锁的组件.Length; i++)
                    if (要锁的组件[i] == c.GetType().Name) { 在名单里 = true; break; }
            if (!在名单里) continue;
            关着的.Add(c);
            原本状态.Add(c.enabled);
            c.enabled = false;
        }
        if (关着的.Count > 0) Debug.Log("[演出锁] 锁住玩家操作：" + 关着的.Count + " 个组件");
    }

    void 解锁()
    {
        for (int i = 0; i < 关着的.Count; i++)
            if (关着的[i] != null) 关着的[i].enabled = 原本状态[i];   // 只还原"本来是什么样"
        关着的.Clear();
        原本状态.Clear();

        // 能力类组件再按玩法规则对一遍（当前功法 / 已获得被动），
        // 否则会把"本来就该关着"的普攻方法开起来 ✗
        if (能力装载 == null) 能力装载 = GetComponent<PlayerAbilityLoader>();
        if (能力装载 != null) 能力装载.Refresh();

        Debug.Log("[演出锁] 解锁玩家操作");
    }

    void OnDisable()
    {
        if (正在锁) 解锁();
    }

    // ---- ASCII 别名 ----
    public bool IsLocking => 正在锁;
}
