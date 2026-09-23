using UnityEngine;

/// <summary>
/// 神通父类。主动神通与被动神通共用的字段放在这里。
/// 对应 lore/主动神通父类.txt 与 lore/被动神通父类.txt。
///
/// 注意：Unity 要求 ScriptableObject 的类名与文件名一致，所以
/// ActiveDivineAbility / PassiveDivineAbility 各自单独成文件。
/// </summary>
public abstract class DivineAbilityDefinition : ScriptableObject, IPanelEntry
{
    [Header("lore 字段 · 基本信息")]
    [Tooltip("神通名称")]
    public string 神通名称 = "新神通";

    [Tooltip("神通id，全局唯一")]
    public string 神通id = "";

    [Tooltip("神通品阶")]
    public QualityTier 神通品阶 = QualityTier.凡品;

    [Tooltip("神通修炼门槛：可修炼的最低境界序号")]
    public int 修炼门槛 = 0;

    [Header("lore 字段 · 效果与消耗")]
    [Tooltip("神通提供的特效方法id")]
    public string 特效方法id = "";

    [Tooltip("释放/施展消耗的灵力")]
    public float 消耗灵力 = 0f;

    [Tooltip("维持期间每秒消耗的灵力")]
    public float 维持消耗灵力 = 0f;

    [Header("UI 用")]
    public Sprite 图标;
    [TextArea(2, 8)]
    public string 介绍 = "";

    /// <summary>是否为主动神通（主动=需手动释放，被动=常驻生效）</summary>
    public abstract bool IsActive { get; }

    // ---- IPanelEntry ----
    public string DisplayName => string.IsNullOrEmpty(神通名称) ? name : 神通名称;
    public string DisplayDescription => 介绍;
    public QualityTier DisplayTier => 神通品阶;
    public Sprite DisplayIcon => 图标;

    protected void ValidateCommon()
    {
        if (string.IsNullOrEmpty(神通id)) 神通id = name;
    }
}
