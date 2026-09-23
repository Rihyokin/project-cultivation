using UnityEngine;

/// <summary>
/// 玩家的「当前值」容器：气血与灵气。
/// 上限取自 PlayerCombatStats 汇总出来的「气血 / 灵力」，回复取「气血回复 / 灵力回复」。
///
/// 说明：lore 里战斗属性叫「灵力」，而策划口述飞行消耗时说「灵气」，
/// 这里统一按同一个资源处理，运行时对外叫【灵气】，上限就是「灵力」那条属性。
/// </summary>
public class PlayerVitals : MonoBehaviour
{
    [Header("引用")]
    [Tooltip("属性来源。留空则自动在本体找 PlayerCombatStats")]
    public PlayerCombatStats 属性;

    [Header("当前值")]
    public float 当前气血 = 1f;
    public float 当前灵气 = 1f;

    [Header("回复")]
    [Tooltip("是否按属性里的回复速度自动回复")]
    public bool 自动回复 = true;

    [Tooltip("回复速度的整体倍率，方便测试时调快")]
    public float 回复倍率 = 1f;

    /// <summary>气血上限</summary>
    public float 气血上限 => 属性 != null ? Mathf.Max(1f, 属性.当前属性[AttributeType.MaxHealth]) : 1f;

    /// <summary>灵气上限（= 战斗属性里的「灵力」）</summary>
    public float 灵气上限 => 属性 != null ? Mathf.Max(1f, 属性.当前属性[AttributeType.MaxSpirit]) : 1f;

    /// <summary>灵气比例 0~1</summary>
    public float 灵气比例 => Mathf.Clamp01(当前灵气 / 灵气上限);

    /// <summary>是否还有灵气</summary>
    public bool 有灵气 => 当前灵气 > 0f;

    void Awake()
    {
        if (属性 == null) 属性 = GetComponent<PlayerCombatStats>();
        当前气血 = 气血上限;
        当前灵气 = 灵气上限;
    }

    void Update()
    {
        if (!自动回复) return;
        float dt = Time.deltaTime;

        float 气血回复 = 属性 != null ? 属性.当前属性[AttributeType.HealthRegen] : 0f;
        float 灵力回复 = 属性 != null ? 属性.当前属性[AttributeType.SpiritRegen] : 0f;

        if (气血回复 > 0f) 当前气血 = Mathf.Min(气血上限, 当前气血 + 气血回复 * 回复倍率 * dt);
        if (灵力回复 > 0f) 当前灵气 = Mathf.Min(灵气上限, 当前灵气 + 灵力回复 * 回复倍率 * dt);
    }

    /// <summary>灵气是否够扣</summary>
    public bool 够灵气(float amount) => amount <= 0f || 当前灵气 >= amount;

    /// <summary>扣灵气。返回是否成功（不够就不扣）</summary>
    public bool 扣灵气(float amount)
    {
        if (amount <= 0f) return true;
        if (当前灵气 < amount) return false;
        当前灵气 -= amount;
        return true;
    }

    /// <summary>扣灵气，扣到 0 为止（用于「持续性消耗」，不要求一次扣满）</summary>
    public float 扣灵气直到零(float amount)
    {
        if (amount <= 0f) return 0f;
        float actual = Mathf.Min(amount, 当前灵气);
        当前灵气 -= actual;
        return actual;
    }

    /// <summary>
    /// 回满气血和灵气。
    ///
    /// 【重要】**同时清掉死亡标记。**
    ///
    /// 以前这里不清 `已死亡`，而全项目没有任何地方会自动清它（只有手动 <see cref="复活"/>）——
    /// 于是玩家一旦被打到 0 血，`已死亡` 就**永久为真**。
    /// 而所有敌对 NPC 的决策第一行就是「玩家已死 → 待机」，
    /// 结果就是：**打到一半全场 NPC 突然集体不动，连新召唤出来的也不动**。
    /// （排查了很久，一开始还以为是 NPC 状态机挂了 —— 其实是玩家这边。）
    ///
    /// "回满"的语义本来就是"恢复到满状态"，那就应该是活的，所以在这里清标记。
    /// </summary>
    public void 回满()
    {
        已死亡 = false;
        当前气血 = 气血上限;
        当前灵气 = 灵气上限;
    }

    // ------------------------------------------------------------ 受伤

    /// <summary>气血变化（参数：本组件、实际扣掉的气血、是否是致命一击）</summary>
    public event System.Action<PlayerVitals, float, bool> 气血变化;

    /// <summary>死亡</summary>
    public event System.Action<PlayerVitals> 死亡;

    /// <summary>是否已经倒下</summary>
    public bool 已死亡 { get; private set; }

    /// <summary>
    /// 无敌（重生保护）。<see cref="PlayerDeathSequence"/> 重生后开一小段时间，
    /// 免得刚站起来又被同一波怪秒掉。
    /// </summary>
    public bool 无敌 { get; set; }

    /// <summary>
    /// 受到伤害（NPC 的 AI 打玩家走这里）。
    /// 返回实际扣掉的气血 —— 伤害公式由调用方用 <see cref="CombatCalculator"/> 算完再传进来，
    /// 这里只管扣当前值。
    /// </summary>
    public float 受到伤害(float 伤害)
    {
        if (伤害 <= 0f || 已死亡 || 无敌) return 0f;   // 已倒下 / 无敌期 都不受伤。要恢复请调 复活() 或 回满()

        float 实际 = Mathf.Min(伤害, 当前气血);
        当前气血 -= 实际;

        bool 致命 = 当前气血 <= 0f;
        if (致命)
        {
            当前气血 = 0f;
            已死亡 = true;
        }

        气血变化?.Invoke(this, 实际, 致命);
        if (致命) 死亡?.Invoke(this);
        return 实际;
    }

    /// <summary>满血复活并清除死亡标记</summary>
    public void 复活()
    {
        已死亡 = false;
        当前气血 = 气血上限;
        当前灵气 = 灵气上限;
    }

    // ---- ASCII 别名 ----
    public float CurrentHealth { get => 当前气血; set => 当前气血 = value; }
    public float CurrentSpirit { get => 当前灵气; set => 当前灵气 = value; }
    public float MaxSpirit => 灵气上限;
    public float SpiritPercent => 灵气比例;
    public bool HasSpirit => 有灵气;
    public bool SpendSpirit(float amount) => 扣灵气(amount);
    public void Refill() => 回满();
    public float TakeDamage(float amount) => 受到伤害(amount);
    public bool IsDead => 已死亡;
}
