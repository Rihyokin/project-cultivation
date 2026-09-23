using UnityEngine;

/// <summary>一次攻击结算的完整结果，方便打日志 / 飘字 / 调试。</summary>
public struct AttackResult
{
    /// <summary>是否命中（未命中时其余字段无意义）</summary>
    public bool 命中;

    /// <summary>是否暴击。只有物理攻击才有意义，特殊攻击恒为 false</summary>
    public bool 暴击;

    /// <summary>是否会心。只有特殊攻击才有意义，物理攻击恒为 false</summary>
    public bool 会心;

    /// <summary>本次的闪避生效概率（roll 用）</summary>
    public float 闪避率;

    /// <summary>本次的免暴概率。只有物理攻击才有意义</summary>
    public float 免暴率;

    /// <summary>本次的免会心概率。只有特殊攻击才有意义</summary>
    public float 免会心率;

    /// <summary>本次是否触发了加成判定（物理=暴击，特殊=会心）</summary>
    public bool 加成触发 => 暴击 || 会心;

    /// <summary>本次的加成倍率（物理=暴击伤害，特殊=会心伤害；未触发=1）</summary>
    public float 加成倍率;

    /// <summary>最终扣除的血量</summary>
    public float 伤害;

    /// <summary>本次结算用的伤害属性（物理 / 特殊）</summary>
    public DamageNature 伤害属性;

    /// <summary>本次结算用的攻击类别（普通攻击 / 主动神通）</summary>
    public AttackKind 攻击类别;

    // ---- ASCII 别名：项目内部按习惯用中文命名，对外（UI / 其他脚本 / 自动化）统一用这些 ----
    public bool Hit => 命中;
    public bool Crit => 暴击;
    public bool Insight => 会心;
    public float Damage => 伤害;
    public float DodgeRate => 闪避率;
    public float CritResistRate => 免暴率;
    public float InsightResistRate => 免会心率;
    public float BoostMultiplier => 加成倍率;
    public bool Boosted => 加成触发;

    /// <summary>旧名，等价于 <see cref="加成倍率"/></summary>
    public float 暴击判定结果 => 加成倍率;
    /// <summary>旧名，等价于 <see cref="加成倍率"/></summary>
    public float CritMultiplier => 加成倍率;

    public override string ToString()
    {
        if (!命中) return "未命中（目标闪避，闪避率 " + 闪避率.ToString("P1") + "）";

        bool 特殊 = 伤害属性 == DamageNature.特殊;
        string 判定 = 特殊
            ? (会心 ? "会心" : "命中") + "（免会心率 " + 免会心率.ToString("P1") + "）"
            : (暴击 ? "暴击" : "命中") + "（免暴率 " + 免暴率.ToString("P1") + "）";

        return 判定 + " " + 伤害.ToString("0.##")
             + "（" + (特殊 ? "会心倍率 " : "暴击倍率 ") + 加成倍率.ToString("0.##") + "）";
    }
}

/// <summary>
/// 战斗结算。命中判定与伤害结算都是**独立于具体技能**的公共规则，
/// 任何普攻/神通都调用这里，不要各自实现一份。
///
/// 一次攻击由两个正交的轴描述（见 <see cref="AttackSpec"/>）：
///   · 伤害属性：物理 / 特殊     —— 决定走【暴击】还是【会心】
///   · 攻击类别：普通攻击 / 主动神通 —— 决定用【普攻加成】还是【主动法术加成】
///
/// 组合出四种攻击，三步公式如下：
///
/// 【第一步 · 命中判定】四种攻击完全一致
///     闪避率 = 受击方闪避 ÷ (受击方闪避 + 攻击方忽视闪避 + 闪避基准)
///     · 受击方闪避为 0 → 必中
///     · 否则按该概率判定「被闪避」，结果夹在 [0, 闪避上限]
///   （必定命中的技能跳过这一步。闪避基准 / 上限见 CombatCalculator.闪避基准）
///
/// 【第二步 · 加成判定】按「伤害属性」二选一，两者互斥
///
///   物理 → 暴击判定
///       免暴率 = (受击方暴击抗性 − 攻击方暴击) / 受击方暴击抗性
///         · 结果 ≤ 0 → 必定暴击
///         · 结果 &gt; 0 → 按该概率判定「未暴击」
///       （受击方暴击抗性为 0 时视为必定暴击）
///       加成倍率 = 攻击方暴击伤害（暴击时），否则 = 1
///
///   特殊 → 会心判定（形式与暴击判定一模一样，只是换成会心系属性）
///       免会心率 = (受击方会心抗性 − 攻击方会心) / 受击方会心抗性
///         · 结果 ≤ 0 → 必定会心
///         · 结果 &gt; 0 → 按该概率判定「未会心」
///       （受击方会心抗性为 0 时视为必定会心）
///       加成倍率 = 攻击方会心伤害（会心时），否则 = 1
///
///   特殊攻击**不会经过暴击判定**，也完全不吃暴击属性与暴击伤害；反之物理攻击不吃会心系。
///
/// 【第三步 · 伤害结算】按「攻击类别」二选一
///
///   普通攻击 → 用普攻伤害加成 / 减免
///       扣除血量 = 攻击方攻击 × (1 + 攻击方普攻伤害加成 − 受击方普攻伤害减免) × 加成倍率
///
///   主动神通 → 用主动法术伤害加成 / 减免
///       扣除血量 = 攻击方攻击 × (1 + 攻击方主动法术伤害加成 − 受击方主动法术伤害减免) × 加成倍率
///
///   主动神通【不会】吃到普攻伤害加成，也【不受】对方普攻伤害减免影响。
///   减免堆到 100% 以上时按 0 倍处理，不倒扣血。
///
///   最后再乘一个 <see cref="AttackSpec.技能倍率"/>（普攻 = 1），
///   供神通 / 法宝 / 灵阵表达自己的强度。
/// </summary>
public static class CombatCalculator
{
    // ==================== 第一步：命中 ====================

    /// <summary>
    /// 闪避公式里的【基准值】—— 决定「攻击方忽视闪避为 0 时，闪避还剩下多少效果」。
    ///
    /// <see cref="GetDodgeRate"/> 用的公式是：
    /// <code>闪避率 = 闪避 ÷ (闪避 + 忽视闪避 + 闪避基准)</code>
    ///
    /// **为什么要加这个基准**：老公式是 `(闪避 − 忽视闪避) ÷ 闪避`，
    /// 拿受击方属性做分母 —— 只要攻击方忽视闪避是 0、受击方闪避 &gt; 0，
    /// 闪避率就恒等于 **1**（100% 闪避，永远打不中）。
    /// 加了基准之后，分母永远不会是 0，忽视闪避为 0 也不再等于完全免疫。
    ///
    /// **这个值和表里的数值同量级**：
    ///   · 表里填小数（现在的数据，如 0.08 = 8%）→ 保持 1
    ///   · 表里填点数（如 闪避 100 / 忽视闪避 350）→ 改成 100
    /// </summary>
    public static float 闪避基准 = 1f;

    /// <summary>闪避率上限，防止堆到必闪</summary>
    public static float 闪避上限 = 0.75f;

    /// <summary>
    /// 受击方的闪避生效概率（0~1）。
    ///
    /// `闪避率 = 闪避 ÷ (闪避 + 忽视闪避 + 闪避基准)`，再夹到 [0, 闪避上限]。
    ///
    /// 举例（基准 = 100 的点数写法）：
    /// <code>
    ///   闪避 100 / 忽视   0  → 100 ÷ 200 = 50%   （被上限压到 75% 以内）
    ///   闪避 100 / 忽视 100  → 100 ÷ 300 = 33%
    ///   闪避 100 / 忽视 350  → 100 ÷ 550 = 18%
    ///   闪避   0 / 忽视 任意 → 0%                （没有闪避就是必中）
    /// </code>
    /// 攻击方的「忽视闪避」越高，这个概率单调下降、永远到不了 0 也到不了 1（除非闪避为 0）。
    /// </summary>
    public static float GetDodgeRate(ICombatStats attacker, ICombatStats defender)
    {
        if (defender == null || attacker == null) return 0f;
        float def = defender.闪避;
        if (def <= 0f) return 0f;                       // 没有闪避 → 必中

        float 忽视 = Mathf.Max(0f, attacker.忽视闪避);
        float 分母 = def + 忽视 + Mathf.Max(0f, 闪避基准);
        if (分母 <= 0f) return 0f;

        return Mathf.Clamp(def / 分母, 0f, Mathf.Clamp01(闪避上限));
    }

    /// <summary>命中判定。返回 true 表示命中。</summary>
    public static bool RollHit(ICombatStats attacker, ICombatStats defender, out float dodgeRate)
    {
        dodgeRate = GetDodgeRate(attacker, defender);
        if (dodgeRate <= 0f) return true;               // 必中
        return Random.value >= dodgeRate;               // 掷骰：落在闪避区间内则被闪避
    }

    // ==================== 第二步：加成判定 ====================

    /// <summary>受击方的免暴概率（物理攻击用）。</summary>
    public static float GetCritResistRate(ICombatStats attacker, ICombatStats defender)
    {
        if (defender == null || attacker == null) return 0f;
        float res = defender.暴击抗性;
        if (res <= 0f) return 0f;                       // 没有暴击抗性 → 必定暴击
        float rate = (res - attacker.暴击) / res;
        return rate <= 0f ? 0f : Mathf.Clamp01(rate);
    }

    /// <summary>受击方的免会心概率（特殊攻击用）。形式与免暴率一致，只是换成会心系属性。</summary>
    public static float GetInsightResistRate(ICombatStats attacker, ICombatStats defender)
    {
        if (defender == null || attacker == null) return 0f;
        float res = defender.会心抗性;
        if (res <= 0f) return 0f;                       // 没有会心抗性 → 必定会心
        float rate = (res - attacker.会心) / res;
        return rate <= 0f ? 0f : Mathf.Clamp01(rate);
    }

    /// <summary>暴击判定。返回 true 表示暴击。</summary>
    public static bool RollCrit(ICombatStats attacker, ICombatStats defender, out float critResistRate)
    {
        critResistRate = GetCritResistRate(attacker, defender);
        if (critResistRate <= 0f) return true;          // 必定暴击
        return Random.value >= critResistRate;          // 掷骰：落在免暴区间内则不暴击
    }

    /// <summary>会心判定。返回 true 表示会心。规则与暴击判定完全对称。</summary>
    public static bool RollInsight(ICombatStats attacker, ICombatStats defender, out float insightResistRate)
    {
        insightResistRate = GetInsightResistRate(attacker, defender);
        if (insightResistRate <= 0f) return true;       // 必定会心
        return Random.value >= insightResistRate;       // 掷骰：落在免会心区间内则不会心
    }

    /// <summary>按「伤害属性」轴做加成判定：物理走暴击，特殊走会心。</summary>
    /// <param name="免加成率">回传本次的免暴率（物理）或免会心率（特殊）</param>
    public static bool RollBoost(ICombatStats attacker, ICombatStats defender, DamageNature 属性, out float 免加成率)
    {
        return 属性 == DamageNature.特殊
            ? RollInsight(attacker, defender, out 免加成率)
            : RollCrit(attacker, defender, out 免加成率);
    }

    /// <summary>加成倍率：物理取暴击伤害，特殊取会心伤害。没配（≤0）时退化成 1 倍。</summary>
    public static float GetBoostMultiplier(ICombatStats attacker, DamageNature 属性)
    {
        if (attacker == null) return 1f;
        float m = 属性 == DamageNature.特殊 ? attacker.会心伤害 : attacker.暴击伤害;
        return m <= 0f ? 1f : m;
    }

    // ==================== 第三步：伤害结算 ====================

    /// <summary>
    /// 伤害数值（只算数值，不做判定）。加成 / 减免按「攻击类别」轴取：
    /// 普通攻击用普攻系，主动神通用主动法术系。
    /// </summary>
    /// <param name="加成触发">本次第二步是否触发了暴击 / 会心</param>
    public static float CalcDamage(ICombatStats attacker, ICombatStats defender, AttackSpec spec, bool 加成触发)
    {
        if (attacker == null) return 0f;

        float 加成, 减免 = 0f;
        if (spec.用主动法术加成)
        {
            加成 = attacker.主动法术伤害加成;
            if (defender != null) 减免 = defender.主动法术伤害减免;
        }
        else
        {
            加成 = attacker.普攻伤害加成;
            if (defender != null) 减免 = defender.普攻伤害减免;
        }

        float factor = 1f + 加成 - 减免;
        factor = Mathf.Max(0f, factor);                 // 减免堆到 100% 以上时不再倒扣血
        float mul = 加成触发 ? GetBoostMultiplier(attacker, spec.伤害属性) : 1f;
        float 倍率 = spec.技能倍率 > 0f ? spec.技能倍率 : 1f;   // default(AttackSpec) 里是 0，当 1 处理
        return attacker.攻击 * factor * mul * 倍率;
    }

    /// <summary>普攻伤害结算（保留旧签名：等价于物理普通攻击的第三步）。</summary>
    public static float CalcBasicAttackDamage(ICombatStats attacker, ICombatStats defender, bool isCrit)
    {
        return CalcDamage(attacker, defender, AttackSpec.物理普通攻击, isCrit);
    }

    // ==================== 完整结算 ====================

    /// <summary>完整结算一次攻击：命中 → 加成判定（暴击/会心）→ 伤害。未命中时伤害为 0。</summary>
    public static AttackResult Resolve(ICombatStats attacker, ICombatStats defender, AttackSpec spec)
    {
        var r = new AttackResult();
        r.伤害属性 = spec.伤害属性;
        r.攻击类别 = spec.攻击类别;

        // 第一步：命中
        if (spec.必定命中)
        {
            r.命中 = true;
            r.闪避率 = 0f;
        }
        else
        {
            r.命中 = RollHit(attacker, defender, out r.闪避率);
        }

        if (!r.命中) { r.伤害 = 0f; r.加成倍率 = 0f; return r; }

        // 第二步：加成判定（物理=暴击，特殊=会心，互斥）
        if (spec.走会心判定)
        {
            r.会心 = RollInsight(attacker, defender, out r.免会心率);
            r.加成倍率 = r.会心 ? GetBoostMultiplier(attacker, spec.伤害属性) : 1f;
        }
        else
        {
            r.暴击 = RollCrit(attacker, defender, out r.免暴率);
            r.加成倍率 = r.暴击 ? GetBoostMultiplier(attacker, spec.伤害属性) : 1f;
        }

        // 第三步：伤害
        r.伤害 = CalcDamage(attacker, defender, spec, r.加成触发);
        return r;
    }

    /// <summary>完整结算一次物理普通攻击（现有飞剑走的就是这条）。</summary>
    public static AttackResult ResolveBasicAttack(ICombatStats attacker, ICombatStats defender)
    {
        return Resolve(attacker, defender, AttackSpec.物理普通攻击);
    }

    /// <summary>必定命中的物理普通攻击（部分技能会无视闪避）。</summary>
    public static AttackResult ResolveGuaranteedHit(ICombatStats attacker, ICombatStats defender)
    {
        return Resolve(attacker, defender, new AttackSpec(DamageNature.物理, AttackKind.普通攻击, true));
    }

    /// <summary>特殊普通攻击：会心判定 + 普攻加成/减免</summary>
    public static AttackResult ResolveSpecialBasicAttack(ICombatStats attacker, ICombatStats defender)
    {
        return Resolve(attacker, defender, AttackSpec.特殊普通攻击);
    }

    /// <summary>物理主动神通：暴击判定 + 主动法术加成/减免</summary>
    public static AttackResult ResolvePhysicalActiveAbility(ICombatStats attacker, ICombatStats defender)
    {
        return Resolve(attacker, defender, AttackSpec.物理主动神通);
    }

    /// <summary>特殊主动神通：会心判定 + 主动法术加成/减免</summary>
    public static AttackResult ResolveSpecialActiveAbility(ICombatStats attacker, ICombatStats defender)
    {
        return Resolve(attacker, defender, AttackSpec.特殊主动神通);
    }
}
