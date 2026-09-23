using UnityEngine;

/// <summary>
/// 主动神通的「结算方式」—— 决定按下快捷键之后干什么。
///
/// 没实现的档位刻意放在 0，这样表里没填的行默认就是「未实现」，
/// 按下去只给一句提示，不会静默失败、也不会白扣灵力。
/// </summary>
public enum ActiveSkillKind
{
    /// <summary>还没做。按快捷键只提示，不扣灵力（法宝 / 灵阵目前都在这一档）</summary>
    未实现 = 0,

    /// <summary>
    /// 范围伤害：以【锁定的敌人】（不需要锁定时以自己）为圆心，
    /// 对范围内所有敌人走 <see cref="CombatCalculator"/> 的主动神通结算。
    /// </summary>
    范围伤害 = 1,
}

/// <summary>
/// 主动神通。需要手动释放，占用主动技能装备位。
/// 对应 lore/主动神通父类.txt。
///
/// 拓展字段（结算方式 / 伤害属性 / 伤害倍率 / 范围 / 冷却时间 / …）
/// 全部来自 <c>Assets/Data/Tables/主动神通表.csv</c> 的同名列，
/// 由 DataTableImporter 按列名自动填进来 —— 加新神通只要加一行。
/// </summary>
[CreateAssetMenu(fileName = "ActiveAbility_", menuName = "修仙/主动神通父类", order = 2)]
public class ActiveDivineAbility : DivineAbilityDefinition
{
    public override bool IsActive => true;

    [Header("结算（对照 主动神通表 的同名列）")]

    [Tooltip("结算方式。未实现时按下快捷键只给提示，不扣灵力")]
    public ActiveSkillKind 结算方式 = ActiveSkillKind.未实现;

    [Tooltip("伤害属性：物理走暴击判定，特殊走会心判定")]
    public DamageNature 伤害属性 = DamageNature.物理;

    [Tooltip("技能倍率：伤害 = 攻击 × (1 + 加成 − 减免) × 加成倍率 × 技能倍率")]
    public float 伤害倍率 = 1f;

    [Tooltip("范围（米）：以锁定目标（或自己）为圆心的作用范围。特效会按这个值同步缩放")]
    public float 范围 = 0f;

    [Tooltip("冷却时间（秒）")]
    public float 冷却时间 = 0f;

    [Tooltip("是否必须先锁定一个目标才能施放")]
    public bool 需要锁定目标 = true;

    [Tooltip("持续时长（秒）。填 0 = 一次性结算；" +
             "有特效时建议对齐特效的动画时长（duration + 最长粒子生命）")]
    public float 持续时长 = 0f;

    [Tooltip("【首次造成伤害时间】（秒）：施放后隔多久才结算第一次伤害。\n" +
             "专门用来把「伤害生效时刻」对齐到特效动画上\n" +
             "（比如陨石落地、火焰升起的那一刻）。\n" +
             "填 0 = 施放瞬间立刻结算。")]
    public float 首次造成伤害时间 = 0f;

    [Tooltip("首次结算之后，每隔这么久再结算一次（秒）")]
    public float 伤害间隔 = 1f;

    [Tooltip("特效 prefab 的 Resources 相对路径（Assets/resources 之下，不含扩展名）")]
    public string 特效资源路径 = "";

    [Tooltip("【施法动作】施放时玩家播的角色动作，填 Assets/resources/技能动作/ 里的文件名（不带扩展名）。留空 = 不播动作。例：技能动作2")]
    public string 施法动作 = "";

    void OnValidate()
    {
        ValidateCommon();

        // 防呆：这几个值填 0 / 负数会让伤害或节奏变得莫名其妙
        if (伤害倍率 <= 0f) 伤害倍率 = 1f;
        if (伤害间隔 <= 0f) 伤害间隔 = 1f;
        if (首次造成伤害时间 < 0f) 首次造成伤害时间 = 0f;
        if (范围 < 0f) 范围 = 0f;
        if (冷却时间 < 0f) 冷却时间 = 0f;
        if (持续时长 < 0f) 持续时长 = 0f;
    }
}
