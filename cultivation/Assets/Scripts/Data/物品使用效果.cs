using UnityEngine;

/// <summary>
/// **使用一件物品时的上下文**：谁在用、背包数据在哪、用的是哪一件。
/// 效果脚本只认这个结构，不直接去找玩家/面板，方便以后从别的地方（任务奖励、NPC 给予）复用。
/// </summary>
public struct 物品使用请求
{
    public UIPanelData 面板;
    public GameObject 玩家;
    public ItemDefinition 物品;

    public string 物品名 => 物品 != null ? 物品.DisplayName : "";
}

/// <summary>
/// **物品使用效果**基类。
///
/// 设定（用户 2026-09-26）：物品只分「**能使用的**」和「**不能使用的**（材料、提交物）」。
/// 能使用的物品 = 在背包页多出一个「使用」按钮 + **挂一个专门的效果脚本** —— 就是本类。
///
/// 为什么是 ScriptableObject 而不是 MonoBehaviour：
///   · 效果是**数据**（学会哪门功法、加多少百分比、持续多久），做成资产就能在 Inspector 里配、能复用；
///   · 一件物品一个效果资产，互不干扰，也不用为了每种效果写一个预制体。
///
/// 加新效果 = 新写一个子类（`[CreateAssetMenu]`）+ 建一个资产 + 挂到物品上，**不用改 UI、不用改物品表结构**。
/// </summary>
public abstract class 物品使用效果 : ScriptableObject
{
    [TextArea(2, 4)]
    [Tooltip("效果说明，面板里给玩家看的（留空就用物品自己的介绍）")]
    public string 说明 = "";

    /// <summary>现在能不能用。例：已经学会的功法就不能再用一次</summary>
    public virtual bool 能使用(物品使用请求 请求) => true;

    /// <summary>不能用的原因（飘字/日志用）</summary>
    public virtual string 不能用原因(物品使用请求 请求) => "";

    /// <summary>真正使用。返回 true = **消耗掉一个**；返回 false = 什么都不做（物品留着）</summary>
    public abstract bool 使用(物品使用请求 请求);
}
