using UnityEngine;

/// <summary>
/// **战阵真灵的行为。**
///
/// 它是 <see cref="NpcAiCombatant"/> 的子类 —— 也就是**和敌对妖魔 / 人类用的是同一套战斗 AI**。
/// 动画、出手（进度 / 冷却 / 飞弹 / 命中特效）、贴地、状态机、伤害链路**全部**复用，
/// 真灵只覆盖两件事：
///
///   · **敌人是谁**：把 <see cref="敌人"/> 覆盖成主人的**锁定目标**
///     （<see cref="NpcTargeting.LockedNpc"/>），而不是主人。
///     基类所有战斗判定（够不够得着 / 往哪走 / 朝哪转 / 打的是谁）都读这一个属性 ——
///     所以真灵和怪的行为**不可能再各自跑偏**。
///   · **没敌人时站哪**：回自己的**阵位**（跟着主人转，朝向也和主人一致）。
///
/// 【为什么这么改】最初真灵是自己把「决策 / 接近 / 可出手 / 朝向」重写了一遍，
/// 结果和怪的行为跑偏了：**侧翼格的真灵离目标 0.8 米也永远不出手**
/// （它那句"待机朝主人"和"转向目标"互相打架，朝向判定永远过不去）✗
/// 现在收敛成「只有敌人不同」，这类问题从结构上消失。
///
/// 【坑】基类的 <c>立即命中()</c> 是**写死打玩家**的，所以这里仍必须自己实现
/// <see cref="结算伤害"/>，否则真灵的每一拳都会打在主人身上。
/// </summary>
public class FormationSpirit : NpcAiCombatant
{
    [Header("真灵")]
    [Tooltip("所属战阵管理器。生成时由管理器写入")]
    public SpiritFormationManager 管理器;

    [Tooltip("阵位（格子序号 0~8）。生成时由管理器写入")]
    public int 阵位 = -1;

    [Header("走位")]
    [Tooltip("站桩时离阵位超过这个距离就往回走（米）。调小 = 站得更准，但会多走两步")]
    public float 回位容差 = 0.25f;

    [Tooltip("正常往阵位走的速度倍率")]
    public float 回位速度倍率 = 1.0f;

    [Tooltip("追赶时的速度倍率上限（乘在 自己.移动速度 上）。\n" +
             "**必须给**：主人奔跑 6 m/s，而白鹿精本身只有 1.5 m/s —— 不加速就永远追不上，越落越远")]
    public float 冲刺倍率 = 4f;

    [Tooltip("离阵位多远开始加速（米）")]
    public float 加速起点 = 1.5f;

    [Tooltip("离阵位多远加到最快（米）")]
    public float 加速终点 = 8f;

    [Tooltip("离阵位超过这个距离就当「跟丢」——直接瞬移回阵位（米）。\n" +
             "覆盖主人御风飞走 / 传送 / 重生这些情况，免得真灵被扔在天边")]
    public float 跟丢距离 = 25f;

    [Header("出手")]
    [Tooltip("子弹从多高打出去（米）。真灵的碰撞体是关掉的（无敌、不挡路），\n" +
             "所以不依赖 collider.bounds，直接按身高取胸口")]
    public float 出膛高度 = 0.9f;

    [Tooltip("打印真灵决策日志")]
    public bool 打印真灵日志 = false;

    /// <summary>当前瞄的那只怪</summary>
    NpcInstance 目标;

    /// <summary>当前瞄的怪</summary>
    public NpcInstance 当前目标 => 目标;

    Vector3 主人位置 => 管理器 != null && 管理器.主人 != null ? 管理器.主人.position : transform.position;

    /// <summary>到当前目标的水平距离</summary>
    public float 到目标距离
        => 目标 == null ? float.PositiveInfinity : 水平距离(transform.position, 目标.transform.position);

    static float 水平距离(Vector3 a, Vector3 b)
    {
        a.y = 0f; b.y = 0f;
        return Vector3.Distance(a, b);
    }

    /// <summary>自己现在站的位置离阵位多远</summary>
    public float 离阵位距离
        => 管理器 == null ? 0f : 水平距离(transform.position, 管理器.取阵位世界位置(阵位));

    // ============================================================ 敌人 = 主人锁定的那只

    /// <summary>**真灵的敌人 = 主人锁定的那只怪。** 基类所有战斗判定都走它</summary>
    protected override Transform 敌人 => 目标 != null ? 目标.transform : null;

    protected override bool 敌人还活着 => 目标 != null && !目标.IsDead;

    protected override ICombatTarget 敌人靶 => 目标 != null ? new NpcTarget(目标) : null;

    /// <summary>**真灵不看好感度** —— 主人锁了谁就打谁</summary>
    protected override bool 可以开打 => 目标 != null;

    /// <summary>目标的「索敌范围」= 主人的追击半径（离主人不能太远）</summary>
    protected override bool 敌人在索敌范围() => 在追击范围内(目标);

    /// <summary>
    /// 真灵**不按「离敌人多远」脱战**，恒为 false。
    ///
    /// 它的脱战判据只有一条：目标跑出**主人的追击半径**（见 <see cref="决策"/>，
    /// 那时 `目标` 直接算 null、走回位分支）。
    ///
    /// 【坑】不能让它走基类那套「到敌人距离 > 脱战范围」——
    /// 主人和怪贴在一起、真灵还在 20 米外往回赶的时候，
    /// 到敌人距离可能到 40 米，会被判定成"脱战" → `已交战 = false` → 永远站在那儿发呆 ✗
    /// </summary>
    protected override bool 敌人已脱战() => false;

    /// <summary>真灵永远不把主人当敌人</summary>
    public override bool 视玩家为敌 => false;

    // ============================================================ 决策

    /// <summary>
    /// 真灵只做**基类不做的那部分**：把「目标」选出来 + 开打前的跟丢兜底，
    /// 剩下的战斗决策（够不够得着 / 追到哪停 / 首次发现 / 脱战）**全部交给
    /// <see cref="NpcAiCombatant.决策"/>** —— 和敌对 NPC 一模一样的代码。
    /// </summary>
    protected override void 决策()
    {
        if (管理器 == null) { 进入状态(NpcAiState.待机); return; }

        // 主人跑太远（御风 / 传送 / 重生）→ 直接回阵位，别在天边追
        if (管理器.主人 != null && 水平距离(transform.position, 主人位置) > 跟丢距离)
        {
            瞬移回阵位();
            目标 = null;
            进入状态(NpcAiState.待机);
            return;
        }

        // 目标 = 主人锁定的那只（没死、并且离主人没超出追击半径）
        var 候选 = 管理器.玩家锁定目标;
        目标 = 候选 != null && !候选.IsDead && 在追击范围内(候选) ? 候选 : null;

        // 没目标 → 回阵位站好（这一段是基类不会做的，真灵独有）
        if (目标 == null) { 回位决策(); return; }

        // 有目标 → 交给基类那套（接近 / 出手 / 转向）
        base.决策();
    }

    /// <summary>
    /// 这个目标还值不值得追 = 它离**主人**没超出「追击半径」。
    ///
    /// 【bug·已修】以前这里比的是**神识范围**（默认只有 14 米）——
    /// 于是用户锁了一只 20 米外的怪，三只真灵**连目标都不认**（`目标 = null`）、
    /// 原位站桩一动不动 ✗ 现在比的是 <see cref="SpiritFormationManager.有效追击半径"/>
    /// （默认 20 米，和敌对 NPC 的脱战范围一个量级）。
    /// </summary>
    bool 在追击范围内(NpcInstance 谁)
    {
        if (谁 == null) return false;
        if (管理器.主人 == null) return true;
        return 水平距离(谁.transform.position, 主人位置) <= 管理器.有效追击半径;
    }

    void 回位决策()
    {
        if (管理器.主人 == null) { 进入状态(NpcAiState.待机); return; }

        float 差 = 离阵位距离;
        if (差 > 回位容差) { 进入状态(NpcAiState.接近); return; }
        进入状态(NpcAiState.待机);
    }

    // ============================================================ 移动

    protected override void 执行接近()
    {
        if (敌人 != null) { 追目标(); return; }
        回阵位();
    }

    void 追目标()
    {
        Vector3 目标点 = 目标.transform.position;

        // 【追击半径】落脚点夹在「主人为中心、半径 = 追击半径」的圆内 ——
        // 目标跑出圈时真灵就停在圈的边上，不会一路追出天边
        if (管理器.主人 != null)
        {
            Vector3 从主人 = 目标点 - 主人位置;
            从主人.y = 0f;
            float 半径 = Mathf.Max(0.5f, 管理器.有效追击半径);
            if (从主人.magnitude > 半径) 目标点 = 主人位置 + 从主人.normalized * 半径;
        }

        Vector3 差 = 目标点 - transform.position;
        差.y = 0f;
        if (差.magnitude <= Mathf.Max(0.3f, 攻击距离 * 0.8f))
        {
            停止移动动画();
            转向(目标.transform.position - transform.position);
            return;
        }

        朝点走(目标点, 取追赶倍率(差.magnitude, 奔跑倍率));
    }

    void 回阵位()
    {
        Vector3 位 = 管理器.取阵位世界位置(阵位);
        Vector3 差 = 位 - transform.position;
        差.y = 0f;
        if (差.magnitude <= 0.06f)
        {
            停止移动动画();
            转向(管理器.主人 != null ? 管理器.主人.forward : transform.forward);
            return;
        }
        朝点走(位, 取追赶倍率(差.magnitude, 回位速度倍率));
    }

    /// <summary>落后越多走得越快：近距离正常速度，远距离拉到 <see cref="冲刺倍率"/></summary>
    float 取追赶倍率(float 距离, float 基础倍率)
    {
        float 上限 = Mathf.Max(基础倍率, 冲刺倍率);
        return Mathf.Lerp(基础倍率, 上限, Mathf.InverseLerp(加速起点, Mathf.Max(加速起点 + 0.01f, 加速终点), 距离));
    }

    void 瞬移回阵位()
    {
        Vector3 位 = 管理器.取阵位世界位置(阵位);
        transform.position = new Vector3(位.x, transform.position.y, 位.z);
        贴地();
    }

    /// <summary>给管理器用：分离推挤之后重新贴地（<c>贴地()</c> 是基类的 protected）</summary>
    public void 贴合地面() => 贴地();

    /// <summary>
    /// 站桩：播待机；**没目标时和主人同朝向**（列队站好的观感）。
    ///
    /// 【bug·已修·现在结构上不可能再犯】有目标时这里**什么都不做** ——
    /// 朝向交给 <see cref="NpcAiCombatant.Update"/> 里那句「每帧朝敌」，
    /// 和敌对 NPC 走的是同一行代码。以前真灵自己在这里"朝主人"，
    /// 和决策里的"转向目标"互相打架，导致**侧翼格的离目标 0.8 米也永远不出手** ✗
    /// </summary>
    protected override void 执行待机()
    {
        播待机动画();

        if (敌人 != null) return;       // 有敌人 → 交给基类的每帧朝敌

        var 主人 = 管理器 != null ? 管理器.主人 : null;
        if (主人 != null) 转向(主人.forward);
    }

    // ============================================================ 出手
    //
    // 「够不够得着 / 朝向正不正 / 冷却好没好」**一条都不用自己写** ——
    // NpcAiBase.可出手() 现在判的就是 `敌人`（= 主人锁定的那只）和 `到敌人距离`。
    // 「出手前朝哪边转」同理，基类默认就是朝敌人。

    /// <summary>
    /// 出膛点：**先用基类的实现**（它会按招式配置里的「挂点」去找出生点骨骼，
    /// 比如冰魄怪的冰弹就该从 `Bip01 L Hand` 出来）。
    /// 真灵的碰撞体是关掉的，万一基类退化成拿不到点（返回原点），就退回胸口高度。
    /// </summary>
    protected override Vector3 取出膛点(NpcAttackConfig 配置 = null)
    {
        var 点 = base.取出膛点(配置);
        return 点 == Vector3.zero ? transform.position + Vector3.up * 出膛高度 : 点;
    }

    /// <summary>
    /// 【必须自己实现】基类的 <c>立即命中()</c> 写死打玩家，真灵用它就会打主人。
    /// 这里一律结算到 <see cref="锁定单位"/> 身上。
    /// </summary>
    protected override void 结算伤害(NpcAttackConfig 配置)
    {
        var 规则 = 配置.取规则();
        var 靶 = 锁定单位;
        if (靶 == null || 靶.已倒下) return;

        // 施法**并且配了弹道**：交给基类的飞弹链路（它的瞄准 / 命中结算 / 命中特效
        // 全都走 锁定单位，已经是对的）
        if (配置.是施法 && !string.IsNullOrEmpty(配置.子弹特效路径))
        {
            生成子弹(配置, 规则);
            return;
        }

        // 其余（包括"勾了施法但没配弹道"）立即结算。
        // 注意不能调基类的 生成子弹 → 它没弹道时会退化成 立即命中() → 打到主人身上 ✗
        var 结果 = 靶.受到攻击(自己, 规则, 自己);
        广播出手(配置, 结果);

        if (打印真灵日志)
            Debug.Log("[真灵] " + name + " 打「" + 靶.名字 + "」" + 配置.动作名
                      + " 命中=" + 结果.命中 + " 伤害=" + 结果.伤害.ToString("0.##"), this);
    }

    // ============================================================ 生命周期

    protected override void Awake()
    {
        base.Awake();

        借用物种招式();

        // 真灵**不做前方探障**。
        //
        // 基类的 朝点走() 会往前扔一个球体探障，撞到就停下不动。
        // 而"跟随主人"这条路上最大的障碍物就是**主人自己** ——
        // 阵位在主人正前方，真灵要走到阵位就得从主人身上穿过去；
        // 探障一挡（实测卡在离主人 1.68 米处），
        // 状态一直是「接近」、每帧位移 0，永远动不了 ✗
        //
        // 真灵本来就可以互相穿过（碰撞体是关掉的），所以干脆不探障。
        // 代价：极端情况下可能蹭进石头里 —— 但"卡死在主人背后"严重得多。
        前方探障距离 = 0f;

        // 【真灵专属】主人**一锁定就立刻开打**，不要那 1.5 秒的"重新索敌冷却"
        //（那是给野怪在索敌边界反复横跳用的；真灵的目标是玩家点名的，没有"发现"这个过程）
        重新索敌冷却 = 0f;
    }

    /// <summary>
    /// **把「这个物种自己的 AI」的招式表借过来。**
    ///
    /// 【bug·已修】真灵身上挂的是 <see cref="FormationSpirit"/>，而
    /// `NpcAiBase.确保()` 一看到"已经有 AI"就**不会**调 `应用类型默认值()`；
    /// 于是 `Awake` 里那句 `确保攻击方式()` 给真灵填的是**三条通用默认招式** ——
    ///
    /// ```
    /// Attack1 普通攻击 物理 倍率1.0 冷却0
    /// Attack2 主动神通 主动 倍率1.4 冷却0
    /// Attack3 主动神通 特殊 倍率1.8 冷却0   ← 而且 是施法
    /// ```
    ///
    /// **冷却全是 0** ✗ 表现就是用户报的「**冰魄怪真灵一直在放神通**」；
    /// 另外白鹿精真灵不放飞弹（改近战了）、白熊精真灵爪击也没有冷却。
    ///
    /// 办法：临时挂一个物种 AI、让它把自己的默认参数算出来、把要用的字段抄过来、再拆掉。
    /// `NpcAiBase.选脚本()` 认不出来（普通村民之类）就保持通用默认值，不影响。
    /// </summary>
    void 借用物种招式()
    {
        var 脚本 = NpcAiBase.选脚本(自己);
        if (脚本 == null || 脚本 == typeof(FormationSpirit)) return;
        if (!typeof(NpcAiBase).IsAssignableFrom(脚本)) return;

        var 临时 = gameObject.AddComponent(脚本) as NpcAiBase;
        if (临时 == null) return;

        临时.应用类型默认值();          // ← 物种自己的 招式/冷却/攻击距离 都在这儿

        攻击方式 = 临时.攻击方式;        // 数组是普通 C# 对象，组件拆掉也还在
        攻击距离 = 临时.攻击距离;
        攻击间隔倍率 = 临时.攻击间隔倍率;
        奔跑倍率 = 临时.奔跑倍率;
        行走倍率 = 临时.行走倍率;
        转向速度 = 临时.转向速度;
        模型朝向补偿 = 临时.模型朝向补偿;
        需要视线 = 临时.需要视线;
        视线遮挡层 = 临时.视线遮挡层;
        // 索敌范围 / 脱战范围 **不抄** —— 真灵的追击范围是「主人的神识范围」，
        // 由 SpiritFormationManager 算（见 决策() / 在追击范围内()）

        DestroyImmediate(临时);

        if (打印真灵日志)
            Debug.Log("[真灵] " + name + " 借用「" + 脚本.Name + "」的招式："
                      + (攻击方式 != null && 攻击方式.Length > 0 ? 攻击方式[0].动作名 + " 冷却 " + 攻击方式[0].冷却 : "?")
                      + "｜攻击距离 " + 攻击距离, this);
    }
}
