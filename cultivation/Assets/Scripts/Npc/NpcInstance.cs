using System;
using UnityEngine;

/// <summary>
/// 场景中所有非玩家生物（怪物 / 木桩 / NPC）的运行时载体。
/// 数据全部来自 <see cref="NpcDefinition"/>（对应 Assets/Data/Tables/NPC表.csv），
/// 本组件只负责「把表里的数值变成可被打、会死、能重生的实体」。
///
/// 挂上这个组件再指定 NpcDefinition，就相当于「继承」了 NPC 父类。
/// 行为（AI）由 <c>NpcAiBase</c> 那一套负责，会在 Awake 时按 <see cref="NpcDefinition.类型"/> 自动装配。
/// </summary>
public class NpcInstance : MonoBehaviour, ICombatStats
{
    [Header("数据来源")]
    [Tooltip("该 NPC 的数据定义，由 NPC表.csv 生成")]
    public NpcDefinition 定义;

    [Header("好感度")]
    [Tooltip("每挨一次伤害掉多少好感。策划要求「极大程度降低」，默认 20")]
    public float 每次受击降好感 = 20f;

    [Tooltip("好感度上下限")]
    public Vector2 好感度范围 = new Vector2(-100f, 100f);

    [Header("无敌")]
    [Tooltip("勾上 = **打不死**：气血永远是满的、也不会死。\n" +
             "战阵真灵用这个（它是主人的灵体，被自己的 AoE 蹭一下就没法看了）。\n" +
             "**注意**：它同时也免疫「击杀掉修炼次数」，不会刷经验")]
    public bool 无敌 = false;

    [Header("运行时状态（只读，方便调试查看）")]
    [SerializeField] float 当前气血;
    [SerializeField] bool 已死亡;

    /// <summary>
    /// 运行时好感度。
    ///
    /// **为什么要单独存一份**：好感度原本直接读写 <see cref="NpcDefinition.对主角好感度"/>，
    /// 那是个 ScriptableObject 资产 —— 运行时改它会**写回磁盘**，
    /// 玩一次就把表里的初始值改脏了（编辑器里尤其明显）。
    /// 所以资产上的值只当「初始值」，运行时改的是这里的副本。
    /// </summary>
    [SerializeField] float 运行好感度;
    [SerializeField] bool 好感度已就绪;

    /// <summary>当前气血</summary>
    public float CurrentHealth => 当前气血;

    /// <summary>气血上限。取 NPC 表里的「气血」</summary>
    public float MaxHealth => 定义 != null ? 定义.属性[AttributeType.MaxHealth] : 0f;

    /// <summary>防御。取 NPC 表里的「防御」</summary>
    public float Defense => 定义 != null ? 定义.属性[AttributeType.Defense] : 0f;

    /// <summary>是否已死亡</summary>
    public bool IsDead => 已死亡;

    /// <summary>显示名</summary>
    public string DisplayName => 定义 != null ? 定义.DisplayName : name;

    /// <summary>气血比例 0~1，给血条用</summary>
    public float HealthPercent => MaxHealth > 0f ? Mathf.Clamp01(当前气血 / MaxHealth) : 0f;

    /// <summary>受伤事件（参数：本实例、实际扣血量）</summary>
    public event Action<NpcInstance, float> Damaged;

    /// <summary>
    /// 走完一次统一结算后触发（**无论命中与否**），参数是完整的 <see cref="AttackResult"/>。
    /// 比 <see cref="Damaged"/> 多带了「命中 / 暴击 / 会心」，所以飘伤害数字用它。
    /// </summary>
    public event Action<NpcInstance, AttackResult> 结算完成;

    /// <summary>死亡事件</summary>
    public event Action<NpcInstance> Died;

    /// <summary>重生事件</summary>
    public event Action<NpcInstance> Revived;

    void Awake()
    {
        初始化好感度();
        if (定义 != null) ResetHealth();
    }

    void Start()
    {
        // 定义是在编辑器里接的，Awake 时可能还没加载好，这里兜一次
        if (当前气血 <= 0f && !已死亡) ResetHealth();

        // 按「类型」自动装配 AI。已经有 AI 就不动它（方便在 prefab 上单独调参）
        NpcAiBase.确保(this);
    }

    /// <summary>把气血恢复满并清除死亡标记</summary>
    public void ResetHealth()
    {
        当前气血 = MaxHealth;
        已死亡 = false;
        Revived?.Invoke(this);
    }

    /// <summary>
    /// 受到伤害。
    /// 这里只做「防御减伤」这一层最朴素的结算，伤害公式的正式实现由普攻/神通方法自己决定，
    /// 调用方也可以传 alreadyMitigated=true 表示自己已经算完了减伤。
    /// </summary>
    /// <param name="amount">伤害值（原始值）</param>
    /// <param name="alreadyMitigated">true 表示传入的已经是最终伤害，本组件不再减防御</param>
    /// <returns>实际扣掉的气血</returns>
    public float TakeDamage(float amount, bool alreadyMitigated = false)
    {
        if (无敌) return 0f;                  // 打不死（战阵真灵）
        if (已死亡 || amount <= 0f) return 0f;

        float final = alreadyMitigated ? amount : Mathf.Max(0f, amount - Defense);
        float applied = Mathf.Min(final, 当前气血);
        当前气血 -= applied;

        Damaged?.Invoke(this, applied);

        if (当前气血 <= 0f)
        {
            当前气血 = 0f;
            已死亡 = true;
            Died?.Invoke(this);

            // 【杀怪 → 修炼次数】按设计文档：单只掉落 = 基础 1 次 × 等级差系数
            //（等级差 = 怪物等级 − 玩家等级）。挂在这里而不是各 AI 里，
            // 这样**所有** NPC（含以后新加的）都自动覆盖到。
            // 死亡不频繁，FindObjectOfType 的开销可以接受。
            if (定义 != null)
            {
                var 修炼 = FindObjectOfType<PlayerCultivation>();
                if (修炼 != null) 修炼.记录击杀(定义.境界);
            }

            // 沙包类 NPC（练功木桩）：气血归零后自动满血，方便一直试招
            if (定义 != null && 定义.死亡后立即重生)
            {
                if (定义.重生延迟 <= 0f) ResetHealth();
                else Invoke(nameof(ResetHealth), 定义.重生延迟);
            }
        }
        return applied;
    }

    /// <summary>回血</summary>
    public float Heal(float amount)
    {
        if (amount <= 0f) return 0f;
        float before = 当前气血;
        当前气血 = Mathf.Min(MaxHealth, 当前气血 + amount);
        return 当前气血 - before;
    }

    // ---- lore 字段直通 ----

    /// <summary>对主角好感度。&lt;0 表示敌对，会被普攻自动锁定</summary>
    public float 对主角好感度
    {
        get
        {
            if (!好感度已就绪) 初始化好感度();
            return 运行好感度;
        }
    }

    /// <summary>表里配的初始好感度（不会被运行时改动污染）</summary>
    public float 初始好感度 => 定义 != null ? 定义.对主角好感度 : 0f;

    /// <summary>NPC 类型</summary>
    public NpcKind 类型 => 定义 != null ? 定义.类型 : NpcKind.未指定;

    /// <summary>好感度变化（谁、旧值、新值）</summary>
    public event Action<NpcInstance, float, float> 好感度变化;

    /// <summary>把运行好感度重置成表里的初始值</summary>
    public void 重置好感度()
    {
        运行好感度 = 初始好感度;
        好感度已就绪 = true;
        好感度变化?.Invoke(this, 运行好感度, 运行好感度);
    }

    void 初始化好感度()
    {
        运行好感度 = 初始好感度;
        好感度已就绪 = true;
    }

    /// <summary>加减好感度（正数=讨好，负数=得罪）</summary>
    public void 改变好感度(float 增量)
    {
        if (增量 == 0f) return;
        if (!好感度已就绪) 初始化好感度();

        float 旧 = 运行好感度;
        运行好感度 = Mathf.Clamp(运行好感度 + 增量, 好感度范围.x, 好感度范围.y);
        if (!Mathf.Approximately(旧, 运行好感度))
            好感度变化?.Invoke(this, 旧, 运行好感度);
    }

    /// <summary>是否敌对（好感度 &lt; 0）</summary>
    public bool 是敌对目标 => 对主角好感度 < 0f;

    // ---- ICombatStats：数值全部来自 NPC 表 ----

    public float 攻击 => Get(AttributeType.Attack);
    /// <summary>移动速度（AI 走路用）。表里的值是 m/s 量级</summary>
    public float 移动速度 => Get(AttributeType.MoveSpeed);
    /// <summary>攻速（AI 出手冷却用）。1 = 标准</summary>
    public float 攻速 => Get(AttributeType.AttackSpeed);
    public float 暴击 => Get(AttributeType.CritChance);
    public float 暴击伤害 => Get(AttributeType.CritDamage);
    public float 闪避 => Get(AttributeType.Dodge);
    public float 忽视闪避 => Get(AttributeType.IgnoreDodge);
    public float 暴击抗性 => Get(AttributeType.CritResist);
    public float 会心 => Get(AttributeType.Insight);
    public float 会心伤害 => Get(AttributeType.InsightDamage);
    public float 会心抗性 => Get(AttributeType.InsightResist);
    public float 普攻伤害加成 => Get(AttributeType.BasicAttackBonus);
    public float 普攻伤害减免 => Get(AttributeType.BasicAttackReduction);
    public float 主动法术伤害加成 => Get(AttributeType.ActiveSpellBonus);
    public float 主动法术伤害减免 => Get(AttributeType.ActiveSpellReduction);

    float Get(AttributeType t) => 定义 != null ? 定义.属性[t] : 0f;

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

    /// <summary>是否敌对（好感度 &lt; 0），会被普攻自动锁定</summary>
    public bool IsHostile => 是敌对目标;

    /// <summary>对主角好感度</summary>
    public float FavorToPlayer => 对主角好感度;

    /// <summary>用统一结算规则打这个 NPC（物理普通攻击），返回本次结果</summary>
    public AttackResult ReceiveBasicAttack(ICombatStats attacker)
    {
        return ReceiveAttack(attacker, AttackSpec.物理普通攻击);
    }

    /// <summary>
    /// 用统一结算规则打这个 NPC，攻击方式由 <paramref name="spec"/> 指定。
    /// 物理/特殊 × 普通攻击/主动神通 四种组合都走这里。
    /// </summary>
    public AttackResult ReceiveAttack(ICombatStats attacker, AttackSpec spec)
    {
        var result = CombatCalculator.Resolve(attacker, this, spec);
        if (result.命中 && result.伤害 > 0f)
        {
            TakeDamage(result.伤害, alreadyMitigated: true);   // 伤害已按公式算完，别再减一次防御

            // 挨打就掉好感（策划要求「极大程度降低」，默认每次 -20）。
            // 注意这是【每次伤害结算】都掉：焚天炎术那种多段 AoE 会掉很多次。
            if (每次受击降好感 != 0f) 改变好感度(-每次受击降好感);
        }

        // 广播完整结果（含暴击 / 会心），飘字之类的表现层用它
        结算完成?.Invoke(this, result);
        return result;
    }

    void OnValidate()
    {
        if (Application.isPlaying) return;
        // 编辑态下改定义时，把预览血量同步一下，方便在 Inspector 里看
        if (定义 != null && 当前气血 <= 0f) 当前气血 = MaxHealth;
    }
}
