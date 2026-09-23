using UnityEngine;

/// <summary>
/// 战斗属性枚举。顺序与 lore/玩家基本属性.txt 一致，请勿随意调整顺序
/// （AttributeSet 内部用 enum 值做数组下标，调整顺序会导致已有资产数据错位）。
/// </summary>
public enum AttributeType
{
    /// <summary>气血</summary>
    MaxHealth = 0,
    /// <summary>攻击</summary>
    Attack = 1,
    /// <summary>防御</summary>
    Defense = 2,
    /// <summary>灵力</summary>
    MaxSpirit = 3,

    /// <summary>移动速度</summary>
    MoveSpeed = 4,
    /// <summary>暴击</summary>
    CritChance = 5,
    /// <summary>暴击抗性</summary>
    CritResist = 6,
    /// <summary>暴击伤害</summary>
    CritDamage = 7,
    /// <summary>会心</summary>
    Insight = 8,
    /// <summary>会心抗性</summary>
    InsightResist = 9,
    /// <summary>会心伤害</summary>
    InsightDamage = 10,
    /// <summary>真实</summary>
    TruePower = 11,
    /// <summary>真实抗性</summary>
    TrueResist = 12,
    /// <summary>真实伤害</summary>
    TrueDamage = 13,
    /// <summary>闪避</summary>
    Dodge = 14,
    /// <summary>忽视闪避</summary>
    IgnoreDodge = 15,
    /// <summary>冷却缩减</summary>
    CooldownReduction = 16,
    /// <summary>攻速</summary>
    AttackSpeed = 17,
    /// <summary>气血回复</summary>
    HealthRegen = 18,
    /// <summary>灵力回复</summary>
    SpiritRegen = 19,
    /// <summary>治疗加成</summary>
    HealingBonus = 20,

    /// <summary>普攻伤害加成</summary>
    BasicAttackBonus = 21,
    /// <summary>普攻伤害减免</summary>
    BasicAttackReduction = 22,
    /// <summary>主动法术伤害加成</summary>
    ActiveSpellBonus = 23,
    /// <summary>主动法术伤害减免</summary>
    ActiveSpellReduction = 24,
    /// <summary>被动法术伤害加成</summary>
    PassiveSpellBonus = 25,
    /// <summary>被动法术伤害减免</summary>
    PassiveSpellReduction = 26,
}

/// <summary>属性表的公共常量与中文名查询。</summary>
public static class AttributeUtil
{
    /// <summary>属性总数</summary>
    public const int Count = 27;

    static readonly string[] DisplayNames =
    {
        "气血", "攻击", "防御", "灵力",
        "移动速度", "暴击", "暴击抗性", "暴击伤害",
        "会心", "会心抗性", "会心伤害",
        "真实", "真实抗性", "真实伤害",
        "闪避", "忽视闪避", "冷却缩减", "攻速",
        "气血回复", "灵力回复", "治疗加成",
        "普攻伤害加成", "普攻伤害减免",
        "主动法术伤害加成", "主动法术伤害减免",
        "被动法术伤害加成", "被动法术伤害减免",
    };

    /// <summary>取属性的中文显示名</summary>
    public static string GetDisplayName(AttributeType type)
    {
        int i = (int)type;
        return (i >= 0 && i < DisplayNames.Length) ? DisplayNames[i] : type.ToString();
    }

    /// <summary>该属性是否按百分比显示（用于 UI 格式化）</summary>
    public static bool IsPercent(AttributeType type)
    {
        switch (type)
        {
            case AttributeType.CritChance:
            case AttributeType.CritResist:
            case AttributeType.CritDamage:
            case AttributeType.Insight:
            case AttributeType.InsightResist:
            case AttributeType.InsightDamage:
            case AttributeType.TruePower:
            case AttributeType.TrueResist:
            case AttributeType.TrueDamage:
            case AttributeType.Dodge:
            case AttributeType.IgnoreDodge:
            case AttributeType.CooldownReduction:
            case AttributeType.AttackSpeed:
            case AttributeType.HealingBonus:
            case AttributeType.BasicAttackBonus:
            case AttributeType.BasicAttackReduction:
            case AttributeType.ActiveSpellBonus:
            case AttributeType.ActiveSpellReduction:
            case AttributeType.PassiveSpellBonus:
            case AttributeType.PassiveSpellReduction:
                return true;
            default:
                return false;
        }
    }

    /// <summary>按属性类型格式化数值文本</summary>
    public static string Format(AttributeType type, float value)
    {
        return IsPercent(type) ? (value * 100f).ToString("0.#") + "%" : value.ToString("0.#");
    }
}
