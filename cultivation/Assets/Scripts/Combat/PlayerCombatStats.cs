using UnityEngine;

/// <summary>
/// 玩家战斗属性的运行时来源。挂在 Player 上。
///
/// 目前汇总：玩家基础属性 + 当前功法每一级提供的增益 × 功法等级。
/// （境界加成、被动神通、法宝等属于「属性结算系统」，等那部分落地后再往这里加一层。）
/// </summary>
public class PlayerCombatStats : MonoBehaviour, ICombatStats
{
    [Header("数据来源")]
    [Tooltip("玩家基本属性定义")]
    public PlayerStatsDefinition 玩家属性;

    [Tooltip("当前修炼的功法，其「每级增益」会按等级累加")]
    public GongFaDefinition 当前功法;

    [Tooltip("当前功法等级。0 表示还没修出增益")]
    [Min(0)]
    public int 功法等级 = 0;

    [Header("调试")]
    [Tooltip("勾选后把最终汇总属性打印到 Console")]
    public bool 打印汇总结果 = false;

    [Tooltip("勾选后最终属性直接取「调试数值」，忽略基础属性与功法汇总。由调试面板自动打开")]
    public bool 使用调试数值 = false;

    [Tooltip("调试用的最终属性值。打开调试面板改这里就能实时生效")]
    public AttributeSet 调试数值 = new AttributeSet();

    [Tooltip("调试用的神识")]
    public float 调试神识 = 12.5f;

    [Tooltip("调试用的吐纳速度")]
    public float 调试吐纳速度 = 2.4f;

    AttributeSet 汇总;

    /// <summary>当前汇总后的属性表（只读）</summary>
    public AttributeSet 当前属性
    {
        get
        {
            if (汇总 == null) Recalculate();
            return 汇总;
        }
    }

    void Awake()
    {
        Recalculate();
    }

    void OnValidate()
    {
        if (Application.isPlaying) Recalculate();
    }

    /// <summary>重新汇总属性。改功法/等级后调用一次即可</summary>
    public void Recalculate()
    {
        var 正常 = new AttributeSet();

        if (玩家属性 != null && 玩家属性.基础属性 != null)
            正常.Add(玩家属性.基础属性);

        if (当前功法 != null && 功法等级 > 0)
            正常.Add(当前功法.GetTotalBonus(功法等级));

        汇总 = 使用调试数值 ? new AttributeSet(调试数值) : 正常;

        if (打印汇总结果)
            Debug.Log("[PlayerCombatStats] 汇总属性：" + 汇总.ToReadableString(), this);
    }

    /// <summary>把「基础属性 + 功法」的当前结果同步进调试数值，供调试面板作为起点</summary>
    public void 同步调试数值()
    {
        var 正常 = new AttributeSet();
        if (玩家属性 != null && 玩家属性.基础属性 != null) 正常.Add(玩家属性.基础属性);
        if (当前功法 != null && 功法等级 > 0) 正常.Add(当前功法.GetTotalBonus(功法等级));
        调试数值.CopyFrom(正常);
        调试神识 = 玩家属性 != null ? 玩家属性.神识 : 0f;
        调试吐纳速度 = 玩家属性 != null ? 玩家属性.吐纳速度 : 0f;
    }

    /// <summary>当前生效的神识（调试开启时取调试值）</summary>
    public float 当前神识 => 使用调试数值 ? 调试神识 : (玩家属性 != null ? 玩家属性.神识 : 0f);

    /// <summary>当前生效的吐纳速度（调试开启时取调试值）</summary>
    public float 当前吐纳速度 => 使用调试数值 ? 调试吐纳速度 : (玩家属性 != null ? 玩家属性.吐纳速度 : 0f);

    // ---- ASCII 别名（内部中文命名，对外统一 ASCII，见项目约定）----
    public float CurrentSense => 当前神识;
    public float CurrentBreathSpeed => 当前吐纳速度;

    /// <summary>关闭调试覆盖，回到「基础 + 功法」的正常结算</summary>
    public void 关闭调试数值()
    {
        使用调试数值 = false;
        Recalculate();
    }

    // ---- ASCII 别名 ----
    public AttributeSet CurrentStats => 当前属性;
    public AttributeSet DebugStats => 调试数值;
    public bool UseDebugStats { get => 使用调试数值; set { 使用调试数值 = value; Recalculate(); } }
    public void SyncDebugFromBase() => 同步调试数值();
    public void DisableDebugStats() => 关闭调试数值();

    // ---- ICombatStats ----
    public float 攻击 => 当前属性[AttributeType.Attack];
    public float 暴击 => 当前属性[AttributeType.CritChance];
    public float 暴击伤害 => 当前属性[AttributeType.CritDamage];
    public float 闪避 => 当前属性[AttributeType.Dodge];
    public float 忽视闪避 => 当前属性[AttributeType.IgnoreDodge];
    public float 暴击抗性 => 当前属性[AttributeType.CritResist];
    public float 会心 => 当前属性[AttributeType.Insight];
    public float 会心伤害 => 当前属性[AttributeType.InsightDamage];
    public float 会心抗性 => 当前属性[AttributeType.InsightResist];
    public float 普攻伤害加成 => 当前属性[AttributeType.BasicAttackBonus];
    public float 普攻伤害减免 => 当前属性[AttributeType.BasicAttackReduction];
    public float 主动法术伤害加成 => 当前属性[AttributeType.ActiveSpellBonus];
    public float 主动法术伤害减免 => 当前属性[AttributeType.ActiveSpellReduction];

    // ---- ASCII 别名：内部按习惯用中文命名，对外（UI / 其他脚本 / 自动化）用这些 ----
    public float Attack => 攻击;
    public float CritChance => 暴击;
    public float CritDamage => 暴击伤害;
    public float Dodge => 闪避;
    public float IgnoreDodge => 忽视闪避;
    public float CritResist => 暴击抗性;
    public float Insight => 会心;
    public float InsightDamage => 会心伤害;
    public float InsightResist => 会心抗性;
    public float BasicAttackBonus => 普攻伤害加成;
    public float BasicAttackReduction => 普攻伤害减免;
    public float ActiveSpellBonus => 主动法术伤害加成;
    public float ActiveSpellReduction => 主动法术伤害减免;
}
