using UnityEngine;

/// <summary>
/// 一次攻击结算所需要的双方属性。
/// 玩家的最终属性由属性结算系统汇总后提供；NPC 直接取 NPC 表里的数值。
///
/// 属性分两组，对应 <see cref="AttackSpec"/> 的两个轴：
///   · 暴击系 / 会心系 —— 由「伤害属性」选一组（物理走暴击，特殊走会心）
///   · 普攻系 / 主动法术系 —— 由「攻击类别」选一组（普通攻击走普攻，主动神通走主动法术）
/// </summary>
public interface ICombatStats
{
    /// <summary>攻击</summary>
    float 攻击 { get; }

    /// <summary>闪避（比率）</summary>
    float 闪避 { get; }

    /// <summary>忽视闪避（比率）</summary>
    float 忽视闪避 { get; }

    // ---- 暴击系：物理攻击用 ----

    /// <summary>暴击（比率，如 0.15 表示 15%）</summary>
    float 暴击 { get; }

    /// <summary>暴击伤害（倍率，如 1.5 表示 150%）</summary>
    float 暴击伤害 { get; }

    /// <summary>暴击抗性（比率）</summary>
    float 暴击抗性 { get; }

    // ---- 会心系：特殊攻击用 ----

    /// <summary>会心（比率）。特殊攻击用，替代暴击</summary>
    float 会心 { get; }

    /// <summary>会心伤害（倍率）。特殊攻击用，替代暴击伤害</summary>
    float 会心伤害 { get; }

    /// <summary>会心抗性（比率）。特殊攻击用，替代暴击抗性</summary>
    float 会心抗性 { get; }

    // ---- 普攻系：普通攻击用 ----

    /// <summary>普攻伤害加成（比率）</summary>
    float 普攻伤害加成 { get; }

    /// <summary>普攻伤害减免（比率）</summary>
    float 普攻伤害减免 { get; }

    // ---- 主动法术系：主动神通用 ----

    /// <summary>主动法术伤害加成（比率）。主动神通用，替代普攻伤害加成</summary>
    float 主动法术伤害加成 { get; }

    /// <summary>主动法术伤害减免（比率）。主动神通用，替代普攻伤害减免</summary>
    float 主动法术伤害减免 { get; }
}

/// <summary>把 AttributeSet 适配成 ICombatStats，方便任何持有属性表的对象直接参与结算。</summary>
public class AttributeCombatStats : ICombatStats
{
    readonly AttributeSet set;

    public AttributeCombatStats(AttributeSet set) { this.set = set; }

    public float 攻击 => Get(AttributeType.Attack);
    public float 闪避 => Get(AttributeType.Dodge);
    public float 忽视闪避 => Get(AttributeType.IgnoreDodge);

    public float 暴击 => Get(AttributeType.CritChance);
    public float 暴击伤害 => Get(AttributeType.CritDamage);
    public float 暴击抗性 => Get(AttributeType.CritResist);

    public float 会心 => Get(AttributeType.Insight);
    public float 会心伤害 => Get(AttributeType.InsightDamage);
    public float 会心抗性 => Get(AttributeType.InsightResist);

    public float 普攻伤害加成 => Get(AttributeType.BasicAttackBonus);
    public float 普攻伤害减免 => Get(AttributeType.BasicAttackReduction);

    public float 主动法术伤害加成 => Get(AttributeType.ActiveSpellBonus);
    public float 主动法术伤害减免 => Get(AttributeType.ActiveSpellReduction);

    // ---- ASCII 别名 ----
    public float Attack => 攻击;
    public float Dodge => 闪避;
    public float IgnoreDodge => 忽视闪避;
    public float CritChance => 暴击;
    public float CritDamage => 暴击伤害;
    public float CritResist => 暴击抗性;
    public float Insight => 会心;
    public float InsightDamage => 会心伤害;
    public float InsightResist => 会心抗性;
    public float BasicAttackBonus => 普攻伤害加成;
    public float BasicAttackReduction => 普攻伤害减免;
    public float ActiveSpellBonus => 主动法术伤害加成;
    public float ActiveSpellReduction => 主动法术伤害减免;

    float Get(AttributeType t) => set != null ? set[t] : 0f;
}
