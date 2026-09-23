using UnityEngine;

/// <summary>
/// 一次攻击结算所需要的双方属性。
/// 玩家的最终属性由属性结算系统汇总后提供；NPC 直接取 NPC 表里的数值。
/// </summary>
public interface ICombatStats
{
    /// <summary>攻击</summary>
    float 攻击 { get; }

    /// <summary>暴击（比率，如 0.15 表示 15%）</summary>
    float 暴击 { get; }

    /// <summary>暴击伤害（倍率，如 1.5 表示 150%）</summary>
    float 暴击伤害 { get; }

    /// <summary>闪避（比率）</summary>
    float 闪避 { get; }

    /// <summary>忽视闪避（比率）</summary>
    float 忽视闪避 { get; }

    /// <summary>暴击抗性（比率）</summary>
    float 暴击抗性 { get; }

    /// <summary>普攻伤害加成（比率）</summary>
    float 普攻伤害加成 { get; }

    /// <summary>普攻伤害减免（比率）</summary>
    float 普攻伤害减免 { get; }
}

/// <summary>把 AttributeSet 适配成 ICombatStats，方便任何持有属性表的对象直接参与结算。</summary>
public class AttributeCombatStats : ICombatStats
{
    readonly AttributeSet set;

    public AttributeCombatStats(AttributeSet set) { this.set = set; }

    public float 攻击 => Get(AttributeType.Attack);
    public float 暴击 => Get(AttributeType.CritChance);
    public float 暴击伤害 => Get(AttributeType.CritDamage);
    public float 闪避 => Get(AttributeType.Dodge);
    public float 忽视闪避 => Get(AttributeType.IgnoreDodge);
    public float 暴击抗性 => Get(AttributeType.CritResist);
    public float 普攻伤害加成 => Get(AttributeType.BasicAttackBonus);
    public float 普攻伤害减免 => Get(AttributeType.BasicAttackReduction);

    float Get(AttributeType t) => set != null ? set[t] : 0f;
}
