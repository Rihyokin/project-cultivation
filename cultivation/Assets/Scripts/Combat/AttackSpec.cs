using UnityEngine;

/// <summary>
/// 攻击的「伤害属性」轴 —— 决定第二步走哪一套加成判定。
///
///   物理 → 暴击 / 暴击抗性 / 暴击伤害      （做暴击判定，完全不吃会心系）
///   特殊 → 会心 / 会心抗性 / 会心伤害      （做会心判定，完全不吃暴击系）
///
/// 两套判定互斥：一次攻击要么走暴击，要么走会心，不会都判。
///
/// 注意：特殊属性【不等于】远程。远程带子弹那类「通常」归特殊，
/// 但归类由调用方显式指定，不要用「有没有子弹」去推断。
/// </summary>
public enum DamageNature
{
    /// <summary>物理：走暴击判定</summary>
    物理 = 0,

    /// <summary>特殊：走会心判定。不吃暴击属性与暴击伤害，也不经过暴击判定</summary>
    特殊 = 1,
}

/// <summary>
/// 攻击的「来源」轴 —— 决定第三步用哪一套加成 / 减免。
///
///   普通攻击 → 攻击方普攻伤害加成    / 受击方普攻伤害减免
///   主动神通 → 攻击方主动法术伤害加成 / 受击方主动法术伤害减免
///
/// 主动神通【不会】吃到普攻伤害加成，也【不受】对方普攻伤害减免影响。
/// </summary>
public enum AttackKind
{
    /// <summary>普通攻击（含飞剑 basic_sword_01 这类武器普攻）</summary>
    普通攻击 = 0,

    /// <summary>主动神通</summary>
    主动神通 = 1,
}

/// <summary>
/// 一次攻击的结算方式。两个轴相互正交，组合出四种攻击：
///
///   物理 + 普通攻击 = 物理普通攻击   （暴击判定 + 普攻加成/减免）
///   特殊 + 普通攻击 = 特殊普通攻击   （会心判定 + 普攻加成/减免）
///   物理 + 主动神通 = 物理主动神通   （暴击判定 + 主动法术加成/减免）
///   特殊 + 主动神通 = 特殊主动神通   （会心判定 + 主动法术加成/减免）
///
/// 用法：
/// <code>
/// var r = CombatCalculator.Resolve(攻击方, 受击方, AttackSpec.特殊主动神通);
/// </code>
/// </summary>
public struct AttackSpec
{
    /// <summary>伤害属性（物理 / 特殊）—— 决定走暴击还是会心</summary>
    public DamageNature 伤害属性;

    /// <summary>攻击类别（普通攻击 / 主动神通）—— 决定用哪套加成与减免</summary>
    public AttackKind 攻击类别;

    /// <summary>true = 无视闪避，必定命中（部分技能用）</summary>
    public bool 必定命中;

    /// <summary>
    /// 技能倍率：伤害 = 攻击 × (1 + 加成 − 减免) × 加成倍率 × 技能倍率。
    /// 普攻填 1；神通 / 法宝 / 灵阵按自己的强度填。**≤ 0 一律当作 1**
    /// （这样 <c>default(AttackSpec)</c> 仍然是安全的物理普攻）。
    /// </summary>
    public float 技能倍率;

    public AttackSpec(DamageNature 伤害属性, AttackKind 攻击类别, bool 必定命中 = false, float 技能倍率 = 1f)
    {
        this.伤害属性 = 伤害属性;
        this.攻击类别 = 攻击类别;
        this.必定命中 = 必定命中;
        this.技能倍率 = 技能倍率;
    }

    // ---- 四种现成组合，直接当常量用 ----

    /// <summary>物理普通攻击：暴击判定 + 普攻加成/减免（现有飞剑走的就是这套）</summary>
    public static AttackSpec 物理普通攻击 => new AttackSpec(DamageNature.物理, AttackKind.普通攻击);

    /// <summary>特殊普通攻击：会心判定 + 普攻加成/减免</summary>
    public static AttackSpec 特殊普通攻击 => new AttackSpec(DamageNature.特殊, AttackKind.普通攻击);

    /// <summary>物理主动神通：暴击判定 + 主动法术加成/减免</summary>
    public static AttackSpec 物理主动神通 => new AttackSpec(DamageNature.物理, AttackKind.主动神通);

    /// <summary>特殊主动神通：会心判定 + 主动法术加成/减免</summary>
    public static AttackSpec 特殊主动神通 => new AttackSpec(DamageNature.特殊, AttackKind.主动神通);

    // ---- 轴判定 ----

    /// <summary>本次是否走会心判定（= 伤害属性为特殊）</summary>
    public bool 走会心判定 => 伤害属性 == DamageNature.特殊;

    /// <summary>本次是否使用主动法术加成 / 减免（= 攻击类别为主动神通）</summary>
    public bool 用主动法术加成 => 攻击类别 == AttackKind.主动神通;

    // ---- ASCII 别名 ----
    public DamageNature Nature => 伤害属性;
    public AttackKind Kind => 攻击类别;
    public bool GuaranteedHit => 必定命中;
    public bool UsesInsight => 走会心判定;
    public bool UsesActiveSpell => 用主动法术加成;

    public override string ToString()
    {
        return (伤害属性 == DamageNature.特殊 ? "特殊" : "物理")
             + (攻击类别 == AttackKind.主动神通 ? "主动神通" : "普通攻击")
             + (必定命中 ? "（必中）" : "");
    }
}
