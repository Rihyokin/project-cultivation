using UnityEngine;

/// <summary>
/// 玩家的初始默认数值。放在这里统一维护，
/// 被 DemoDataSetup（生成玩家属性资产）和调试面板（一键重置）共用。
/// </summary>
public static class PlayerDefaultStats
{
    // ---- lore 里的独立字段 ----
    public const float 默认神识 = 12.5f;
    public const float 默认吐纳速度 = 2.4f;
    public static SpiritualRoot 默认灵根 => SpiritualRoot.水 | SpiritualRoot.火;

    /// <summary>26 项战斗属性的默认值</summary>
    public static AttributeSet Create()
    {
        var s = new AttributeSet();

        // 基础三维
        s[AttributeType.MaxHealth] = 320f;      // 气血
        s[AttributeType.Attack] = 42f;          // 攻击
        s[AttributeType.Defense] = 18f;         // 防御
        s[AttributeType.MaxSpirit] = 150f;      // 灵力

        // 机动
        s[AttributeType.MoveSpeed] = 1.0f;      // 移动速度

        // 暴击系
        s[AttributeType.CritChance] = 0.15f;    // 暴击
        s[AttributeType.CritResist] = 0.05f;    // 暴击抗性
        s[AttributeType.CritDamage] = 1.5f;     // 暴击伤害（倍率）

        // 会心系
        s[AttributeType.Insight] = 0.10f;
        s[AttributeType.InsightResist] = 0.05f;
        s[AttributeType.InsightDamage] = 1.30f;

        // 真实系
        s[AttributeType.TruePower] = 0f;
        s[AttributeType.TrueResist] = 0f;
        s[AttributeType.TrueDamage] = 0f;

        // 闪避系
        s[AttributeType.Dodge] = 0.08f;
        s[AttributeType.IgnoreDodge] = 0.05f;

        // 节奏
        s[AttributeType.CooldownReduction] = 0.10f;   // 冷却缩减
        s[AttributeType.AttackSpeed] = 1.0f;          // 攻速

        // 回复与增益
        s[AttributeType.HealthRegen] = 4f;            // 气血回复
        s[AttributeType.SpiritRegen] = 4f;            // 灵力回复
        s[AttributeType.HealingBonus] = 0.05f;        // 治疗加成

        // 伤害加成 / 减免
        s[AttributeType.BasicAttackBonus] = 0.05f;
        s[AttributeType.BasicAttackReduction] = 0.02f;
        s[AttributeType.ActiveSpellBonus] = 0.05f;
        s[AttributeType.ActiveSpellReduction] = 0.02f;
        s[AttributeType.PassiveSpellBonus] = 0.05f;
        s[AttributeType.PassiveSpellReduction] = 0.02f;

        return s;
    }
}
