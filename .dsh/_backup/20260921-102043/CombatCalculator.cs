using UnityEngine;

/// <summary>一次普攻结算的完整结果，方便打日志 / 飘字 / 调试。</summary>
public struct AttackResult
{
    /// <summary>是否命中（未命中时其余字段无意义）</summary>
    public bool 命中;

    /// <summary>是否暴击</summary>
    public bool 暴击;

    /// <summary>本次的闪避生效概率（roll 用）</summary>
    public float 闪避率;

    /// <summary>本次的免暴概率（roll 用）</summary>
    public float 免暴率;

    /// <summary>最终扣除的血量</summary>
    public float 伤害;

    /// <summary>本次的暴击判定结果（暴击=攻击方暴击伤害，未暴击=1）</summary>
    public float 暴击判定结果;

    // ---- ASCII 别名：项目内部按习惯用中文命名，对外（UI / 其他脚本 / 自动化）统一用这些 ----
    public bool Hit => 命中;
    public bool Crit => 暴击;
    public float Damage => 伤害;
    public float DodgeRate => 闪避率;
    public float CritResistRate => 免暴率;
    public float CritMultiplier => 暴击判定结果;

    public override string ToString()
    {
        if (!命中) return "未命中（目标闪避，闪避率 " + 闪避率.ToString("P1") + "）";
        return (暴击 ? "暴击 " : "命中 ") + 伤害.ToString("0.##")
             + "（免暴率 " + 免暴率.ToString("P1") + "，暴击倍率 " + 暴击判定结果.ToString("0.##") + "）";
    }
}

/// <summary>
/// 战斗结算。命中判定与伤害结算都是**独立于具体技能**的公共规则，
/// 任何普攻/神通都调用这里，不要各自实现一份。
///
/// 三条规则（来自策划说明）：
///
/// 1) 命中判定
///      闪避率 = (受击方闪避 − 攻击方忽视闪避) / 受击方闪避
///      · 结果 ≤ 0  → 必中
///      · 结果 &gt; 0  → 按该概率判定「被闪避」
///    （受击方闪避为 0 时视为必中）
///
/// 2) 暴击判定
///      免暴率 = (受击方暴击抗性 − 攻击方暴击) / 受击方暴击抗性
///      · 结果 ≤ 0  → 必定暴击
///      · 结果 &gt; 0  → 按该概率判定「未暴击」
///    （受击方暴击抗性为 0 时视为必定暴击）
///      暴击判定结果 = 攻击方暴击伤害（暴击时），否则 = 1
///
/// 3) 普攻伤害结算
///      扣除血量 = 攻击方攻击 × (1 + 攻击方普攻伤害加成 − 受击方普攻伤害减免) × 暴击判定结果
/// </summary>
public static class CombatCalculator
{
    /// <summary>受击方的闪避生效概率。</summary>
    public static float GetDodgeRate(ICombatStats attacker, ICombatStats defender)
    {
        if (defender == null || attacker == null) return 0f;
        float def = defender.闪避;
        if (def <= 0f) return 0f;                       // 没有闪避 → 必中
        float rate = (def - attacker.忽视闪避) / def;
        return rate <= 0f ? 0f : Mathf.Clamp01(rate);
    }

    /// <summary>受击方的免暴概率。</summary>
    public static float GetCritResistRate(ICombatStats attacker, ICombatStats defender)
    {
        if (defender == null || attacker == null) return 0f;
        float res = defender.暴击抗性;
        if (res <= 0f) return 0f;                       // 没有暴击抗性 → 必定暴击
        float rate = (res - attacker.暴击) / res;
        return rate <= 0f ? 0f : Mathf.Clamp01(rate);
    }

    /// <summary>命中判定。返回 true 表示命中。</summary>
    public static bool RollHit(ICombatStats attacker, ICombatStats defender, out float dodgeRate)
    {
        dodgeRate = GetDodgeRate(attacker, defender);
        if (dodgeRate <= 0f) return true;               // 必中
        return Random.value >= dodgeRate;               // 掷骰：落在闪避区间内则被闪避
    }

    /// <summary>暴击判定。返回 true 表示暴击。</summary>
    public static bool RollCrit(ICombatStats attacker, ICombatStats defender, out float critResistRate)
    {
        critResistRate = GetCritResistRate(attacker, defender);
        if (critResistRate <= 0f) return true;          // 必定暴击
        return Random.value >= critResistRate;          // 掷骰：落在免暴区间内则不暴击
    }

    /// <summary>普攻伤害结算（只算数值，不做判定）。</summary>
    public static float CalcBasicAttackDamage(ICombatStats attacker, ICombatStats defender, bool isCrit)
    {
        if (attacker == null) return 0f;
        float factor = 1f + attacker.普攻伤害加成 - (defender != null ? defender.普攻伤害减免 : 0f);
        factor = Mathf.Max(0f, factor);                 // 减免堆到 100% 以上时不再倒扣血
        float critMul = isCrit ? attacker.暴击伤害 : 1f;
        if (critMul <= 0f) critMul = 1f;                // 暴击伤害没配时退化成 1 倍
        return attacker.攻击 * factor * critMul;
    }

    /// <summary>完整结算一次普攻：命中 → 暴击 → 伤害。未命中时伤害为 0。</summary>
    public static AttackResult ResolveBasicAttack(ICombatStats attacker, ICombatStats defender)
    {
        var r = new AttackResult();
        r.命中 = RollHit(attacker, defender, out r.闪避率);
        if (!r.命中) { r.伤害 = 0f; r.暴击判定结果 = 0f; return r; }

        r.暴击 = RollCrit(attacker, defender, out r.免暴率);
        r.暴击判定结果 = r.暴击 ? attacker.暴击伤害 : 1f;
        r.伤害 = CalcBasicAttackDamage(attacker, defender, r.暴击);
        return r;
    }

    /// <summary>必定命中的版本（部分技能会无视闪避）。</summary>
    public static AttackResult ResolveGuaranteedHit(ICombatStats attacker, ICombatStats defender)
    {
        var r = new AttackResult();
        r.命中 = true;
        r.暴击 = RollCrit(attacker, defender, out r.免暴率);
        r.暴击判定结果 = r.暴击 ? attacker.暴击伤害 : 1f;
        r.伤害 = CalcBasicAttackDamage(attacker, defender, r.暴击);
        return r;
    }
}
