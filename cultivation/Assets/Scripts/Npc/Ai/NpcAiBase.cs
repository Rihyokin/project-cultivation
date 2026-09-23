using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>NPC AI 的状态。子类只负责「决策出下一个状态」，执行由基类统一做。</summary>
public enum NpcAiState
{
    待机 = 0,
    接近 = 1,
    远离 = 2,
    攻击 = 3,
    受击 = 4,
    死亡 = 5,
}

/// <summary>
/// 一种出手方式。一个 NPC 可以配多条（Attack1 / Attack2 / Attack3…），
/// 由子类决定这次用哪条。
///
/// 策划约定：**Attack1 走普通攻击，Attack2 / Attack3 走主动神通**
/// （物理还是特殊在具体 AI 里定）。
/// </summary>
[Serializable]
public class NpcAttackConfig
{
    [Tooltip("动画动作名，要和 Animator 里的状态同名（如 Attack1）")]
    public string 动作名 = "Attack1";

    [Tooltip("伤害属性：决定走暴击还是会心")]
    public DamageNature 伤害属性 = DamageNature.物理;

    [Tooltip("攻击类别：决定吃普攻加成还是主动法术加成")]
    public AttackKind 攻击类别 = AttackKind.普通攻击;

    [Tooltip("技能倍率")]
    public float 技能倍率 = 1f;

    [Tooltip("动画播到多少比例时结算伤害（0~1）。调这个把手感对齐动画")]
    [Range(0.05f, 0.95f)]
    public float 出手进度 = 0.4f;

    [Tooltip("是不是「施法」动作 —— 需要飞行道具（子弹）而不是立即命中")]
    public bool 是施法 = false;

    [Tooltip("勾上 = 无视闪避必定命中。\n" +
             "**临时开关**：当前闪避公式是「(受击方闪避 − 攻击方忽视闪避) ÷ 受击方闪避」，\n" +
             "只要攻击方「忽视闪避」是 0 而对方闪避 > 0，命中率就是 0\n" +
             "（NPC 打玩家正好是这种情况）。公式定下来之前先用它验证 AI 链路。")]
    public bool 必定命中 = false;

    [Tooltip("子弹特效的 Resources 路径（不含扩展名）。留空 = 即使勾了施法也只做立即结算")]
    public string 子弹特效路径 = "";

    [Tooltip("子弹飞行速度（米/秒）")]
    public float 子弹速度 = 18f;

    [Tooltip("枪口闪光的 Resources 路径（可选）。发射瞬间在出膛点生成，纯装饰不结算伤害")]
    public string 闪光特效路径 = "";

    [Tooltip("命中特效的 Resources 路径（不含扩展名）（可选）。\n" +
             "**留空 = 物理攻击自动用「基础命中特效」**（见 NpcAiBase.物理基础命中特效）；\n" +
             "特殊攻击留空就是「没有命中特效」。\n" +
             "用 QFX 那种**自带飞行脚本**的实体弹丸时会塞进它的 ImpactFX，到点自动播；\n" +
             "自己的 NpcProjectile 则不管它")]
    public string 命中特效路径 = "";

    [Tooltip("这一招要不要命中特效。**默认勾着** —— 策划要求「所有物理属性的攻击最起码要有一个基础命中特效」。\n" +
             "只有那种一秒钟打十几下的多段攻击才需要关掉（否则满屏都是特效）")]
    public bool 用命中特效 = true;

    [Tooltip("闪光特效存活多久（秒）")]
    public float 闪光特效存活 = 0.8f;

    [Tooltip("命中特效存活多久（秒）")]
    public float 命中特效存活 = 2f;

    [Tooltip("命中特效的缩放。想让爆炸更有分量就调大")]
    public float 命中特效缩放 = 1f;

    [Header("飞弹 · 5 要素")]
    [Tooltip("【出生点】挂点骨骼名（在子物件里按名字找，例如 `Bip01 L Finger0Nub`）。\n" +
             "留空 = 用胸口（身高的 70%）")]
    public string 挂点 = "";

    [Tooltip("挂点的本地偏移，在挂点自己的坐标系里")]
    public Vector3 挂点偏移 = Vector3.zero;

    [Tooltip("【消灭点】飞出这么远就停止并淡化消散（米）。0 = 不限")]
    public float 最大飞行距离 = 22f;

    [Tooltip("【能否被阻挡】能被哪些层拦下来（技能神通 / 法宝 / 灵阵）。0 = 谁都拦不住。\n" +
             "飞弹**不和场景碰撞**，所以墙永远挡不住它")]
    public LayerMask 拦截层 = 0;

    [Tooltip("弹体缩放。想让飞弹更大就调这个（粒子包的效果会整体放大）")]
    public float 弹体缩放 = 1f;

    [Tooltip("这一招自己的冷却（秒）。0 = 用默认的出手间隔")]
    public float 冷却 = 0f;

    [Tooltip("【命中判定】目标离弹道多近算命中（米）。弹道是直线不追踪的，\n" +
             "所以玩家躲开就打不中，这一发会继续飞直到超过「最大飞行距离」再消散")]
    public float 命中半径 = 1.0f;

    [Tooltip("收招后摇（秒）：动作播完还要等这么久才可能再次出手")]
    public float 收招后摇 = 0.25f;

    /// <summary>这一次出手用的结算规则</summary>
    public AttackSpec 取规则() => new AttackSpec(伤害属性, 攻击类别, 必定命中, 技能倍率);
}

/// <summary>
/// NPC 行为基类。
///
/// 基类负责所有类型共用的东西：
///   · 感知（找玩家、算距离）
///   · 状态机骨架（待机 / 接近 / 远离 / 攻击 / 受击 / 死亡）
///   · 移动（`transform.position` + 地面射线。**NPC 身上只有 CapsuleCollider，
///     没有 CharacterController / Rigidbody，见开发注意事项 §8.2**）
///   · 动画（按动作名切，**缺什么自动降级**：Run → Walk → Idle）
///   · 出手（按 `出手进度` 在动画中途结算伤害，倍率 / 属性 / 类别来自 <see cref="NpcAttackConfig"/>）
///   · 死亡（播 Death、可选粒子消散、延迟销毁）
///
/// 子类只需要实现 <see cref="决策"/>：
///   · <see cref="NpcAiAnimal"/> 野兽
///   · <see cref="NpcAiDemon"/>  妖魔
///   · <see cref="NpcAiHuman"/>  人类
///
/// 挂载方式：<see cref="NpcInstance.Start"/> 会按 <see cref="NpcDefinition.类型"/>
/// 自动调用 <see cref="确保"/> 装配。**prefab 上已经手动放了 AI 就不动它**，
/// 方便单个 NPC 单独调参。
/// </summary>
[RequireComponent(typeof(NpcInstance))]
public abstract class NpcAiBase : MonoBehaviour
{
    // ============================================================ 配置

    [Header("感知")]
    [Tooltip("索敌范围（米）。表里「索敌范围」填了就以表为准")]
    public float 索敌范围 = 12f;

    [Tooltip("超过这个距离就脱战（一般比索敌范围大一些，避免边界反复横跳）")]
    public float 脱战范围 = 20f;

    [Tooltip("勾上则要求「看得见」玩家（中间不能被墙挡住）")]
    public bool 需要视线 = false;

    [Tooltip("挡视线的层")]
    public LayerMask 视线遮挡层 = ~0;

    [Header("移动")]
    [Tooltip("走路速度 = NPC 属性里的「移动速度」× 这个值")]
    public float 行走倍率 = 0.5f;

    [Tooltip("奔跑/追击速度 = NPC 属性里的「移动速度」× 这个值")]
    public float 奔跑倍率 = 1f;

    [Tooltip("转向速度（度/秒）。0 = 瞬间转向")]
    public float 转向速度 = 540f;

    [Tooltip("模型视觉正面相对 transform.forward 的偏差（度）")]
    public float 模型朝向补偿 = 0f;

    [Tooltip("自动贴地（沿地面射线摆正高度）")]
    public bool 自动贴地 = true;

    [Tooltip("贴地时额外抬高的量（米）")]
    public float 贴地微调 = 0f;

    [Tooltip("地面层")]
    public LayerMask 地面层 = ~0;

    [Tooltip("前进方向上探测障碍的距离（米）。0 = 不探（会直接穿墙）")]
    public float 前方探障距离 = 1.2f;

    [Tooltip("障碍层")]
    public LayerMask 障碍层 = ~0;

    [Header("战斗")]
    [Tooltip("进到这个距离内就可能出手（米）。\n" +
             "**注意这是「两个 pivot 的中心距」**，要比双方碰撞体半径之和大一些；\n" +
             "否则会出现「贴到跟前却永远够不着、一直停在接近」的死循环")]
    public float 攻击距离 = 3f;

    [Tooltip("出手间隔 = 这个值 ÷ NPC 属性里的「攻速」。攻速 1 时就是这么多秒")]
    public float 攻击间隔倍率 = 1.2f;

    [Tooltip("出手方式。留空会自动填 Attack1/2/3 的默认值；\n" +
             "**实际只会用 Animator 里真有的动作**，没有的自动跳过")]
    public NpcAttackConfig[] 攻击方式;

    [Tooltip("受击硬直时长（秒）。攻击动作过程中不会被硬直打断")]
    public float 受击硬直 = 0.3f;

    [Tooltip("两次受击动画之间最少隔多久（秒），避免多段伤害把动画刷爆")]
    public float 受击动画冷却 = 0.45f;

    [Header("死亡")]
    [Tooltip("死亡消散特效的 Resources 路径。\n" +
             "**留空 = 用代码生成的通用消散**（粒子从身躯炸开、各自随机渐隐）—— 推荐留空。\n" +
             "只有想给某个 NPC 一个专属死亡特效时才填资产路径。")]
    public string 死亡消散特效路径 = "";

    [Tooltip("通用消散的颜色")]
    public Color 死亡消散颜色 = new Color(0.80f, 0.87f, 1f, 0.95f);

    [Tooltip("通用消散的粒子数量。0 = 按体型自动算")]
    public int 死亡消散粒子数 = 0;

    [Tooltip("消散规模的整体缩放（**基准值 3**，用户定的观感）。\n" +
             "用资产 prefab 时是它的 localScale；用通用消散时是「身躯半径的倍率」，\n" +
             "会同时放大粒子大小、初速和散布范围。")]
    public float 死亡消散特效缩放 = 3f;

    [Tooltip("消散特效存活多久后销毁（秒）")]
    public float 死亡消散特效存活 = 2f;

    [Tooltip("**死亡粒子被玩家吸收**（用户要求，默认勾着）：\n" +
             "粒子炸开 → **落到地上停住** → 等 1 秒 → **飞向玩家、在玩家身上消失**。\n\n" +
             "去掉勾 = 老行为（粒子各自随机淡出）。\n" +
             "只对「代码生成的通用消散」生效；如果这个 NPC 单独配了 `死亡消散特效路径`，" +
             "那就按资产那份演，不走吸收流程。")]
    public bool 死亡粒子被玩家吸收 = true;

    [Tooltip("死亡后多久销毁自己。0 = 不销毁（留尸体）")]
    public float 死亡后销毁延迟 = 3f;

    [Tooltip("死亡时是否关掉碰撞体（关掉后鼠标点不到尸体）")]
    public bool 死亡时关闭碰撞 = true;

    [Tooltip("**没有死亡动画**的 NPC（野兽那类）：死亡瞬间就把模型藏起来，只演粒子消散。\n" +
             "不藏的话模型会保持站姿一直杵到消亡延迟结束，和粒子对不上，很出戏。")]
    public bool 没有死亡动画就立即隐藏模型 = true;

    /// <summary>模型是否被 隐藏模型() 藏起来了</summary>
    protected bool 模型已隐藏;

    [Header("动画")]
    [Tooltip("**由 AI 接管 Root Motion：位移丢掉，只吃旋转。**\n\n" +
             "【为什么必须这样】本工程的动画是 **Humanoid**（Animator 带 Avatar），\n" +
             "攻击动作的「转体」烘在 **RootQ（根旋转）** 里 —— 实测 Attack1 的根路径曲线有\n" +
             "`RootT.x/y/z` + `RootQ.x/y/z/w`。\n\n" +
             "以前这里是直接把 `applyRootMotion` 设成 false，那等于把 **RootT 和 RootQ 一起丢掉**，\n" +
             "于是**身体扭不过来**、手刺歪（用户原话「好像身体转不过来一样」）✗\n" +
             "（当年设 false 是为了治「Wound / Attack 的位移累积、挨一下直接飞天」，治过头了。）\n\n" +
             "现在：`applyRootMotion = true` + 自己实现 `OnAnimatorMove()` ——\n" +
             "  · **旋转**：攻击期间乘到 `transform.rotation` 上 → 转体正常\n" +
             "  · **位移**：一律丢掉 → 不会再「挨一下飞天」\n\n" +
             "去掉勾 = 回到老行为（applyRootMotion = false，转体也一起没有）")]
    public bool 强制关闭RootMotion = true;

    [Header("命中特效")]
    [Tooltip("**物理攻击的基础命中特效**（Resources 路径，不带扩展名）。\n" +
             "策划要求：所有物理属性的攻击最起码要有一个基础命中特效 —— 所以\n" +
             "**只要是物理攻击、又没自己配 命中特效路径，就自动放这个**。\n" +
             "预制体由菜单「修仙 / 构建命中特效」生成（自绘贴图，不依赖第三方特效包）")]
    public string 物理基础命中特效 = "FX/Hit_Physical";

    [Header("调试")]
    [Tooltip("切换状态时打一条日志")]
    public bool 打印状态日志 = false;

    // ============================================================ 运行时

    /// <summary>自己</summary>
    public NpcInstance 自己 { get; private set; }

    /// <summary>当前状态</summary>
    public NpcAiState 状态 { get; private set; } = NpcAiState.待机;

    /// <summary>玩家（可能为 null，比如纯展示场景）</summary>
    public PlayerVitals 玩家生命 { get; private set; }

    /// <summary>玩家的战斗属性（ICombatStats）</summary>
    public ICombatStats 玩家战斗属性 { get; private set; }

    /// <summary>到玩家的水平距离（米）。没玩家时是 +∞</summary>
    public float 到玩家距离 { get; private set; } = float.PositiveInfinity;

    /// <summary>
    /// 这个 NPC 现在把玩家当敌人吗。
    /// 各类型阈值不同（妖魔 / 人类），所以由子类定；**野兽永远是 false**（只会躲，不会打）。
    /// </summary>
    public virtual bool 视玩家为敌 => false;

    // ============================================================ 敌人（战斗的唯一靶子）

    /// <summary>
    /// **当前要打的敌人。默认 = 玩家。**
    ///
    /// 战阵真灵（<see cref="FormationSpirit"/>）把它覆盖成「主人锁定的那只怪」——
    /// 于是**同一套战斗逻辑**（够不够得着 / 往哪走 / 朝哪转 / 打的是谁）
    /// 同时服务敌对 NPC 和真灵。
    ///
    /// 【为什么要有这一层】以前真灵是自己另写了一套「决策 / 接近 / 可出手 / 朝向」，
    /// 结果就是它和敌对 NPC 的行为**会各自跑偏**（实测踩过：
    /// 侧翼格的真灵离目标 0.8 米却永远不出手 —— 因为它那句"待机朝主人"和
    /// "转向目标"互相打架）。收敛成一个 `敌人` 之后，这种偏差不可能再出现。
    /// </summary>
    protected virtual Transform 敌人 => 玩家生命 != null ? 玩家生命.transform : null;

    /// <summary>敌人还活着吗（默认 = 玩家没死）。真灵覆盖成「目标没死」</summary>
    protected virtual bool 敌人还活着 => 玩家生命 != null && !玩家生命.已死亡;

    /// <summary>敌人的受击靶子（飞弹瞄准 / 伤害结算用）。默认 = 玩家</summary>
    protected virtual ICombatTarget 敌人靶 => 玩家生命 != null ? new PlayerTarget(玩家生命) : null;

    /// <summary>敌人到自己的**水平**距离（米）。没敌人 = +∞</summary>
    public float 到敌人距离 { get; private set; } = float.PositiveInfinity;

    /// <summary>状态变化事件（旧状态、新状态）</summary>
    public event Action<NpcAiBase, NpcAiState, NpcAiState> 状态变化;

    /// <summary>出手结算了一次（参数：自己、用的哪条出手方式、打了个什么结果）</summary>
    public event Action<NpcAiBase, NpcAttackConfig, AttackResult> 出手;

    protected NpcAnimator 动画器;
    protected Animator 动画;

    // 移动 / 贴地
    float 贴地基准偏移;
    bool 贴地已标定;

    // 攻击节奏
    NpcAttackConfig 当前出手;
    float 下次可出手时间;
    float 动作开始时间;
    float 动作结束时间;
    bool 待校正动作时长;
    bool 本次出手已结算;

    // 受击
    float 受击结束时间;
    float 上次受击动画时间 = -999f;
    bool 待校正受击时长;

    // 死亡
    float 死亡时间;
    bool 死亡已处理;

    // 动画缓存
    string 当前动作 = "";
    static PlayerVitals 缓存的玩家;

    // ============================================================ 装配

    /// <summary>
    /// 按定义里的「类型」装配 AI。已经有 AI 就原样返回（不覆盖 prefab 上的手调参数）。
    /// <paramref name="npc"/> 为 null 或类型是「未指定 / 中立」时返回 null。
    /// </summary>
    public static NpcAiBase 确保(NpcInstance npc)
    {
        if (npc == null) return null;

        var 已有 = npc.GetComponent<NpcAiBase>();
        if (已有 != null) return 已有;

        var 脚本 = 选脚本(npc);
        if (脚本 == null) return null;

        var ai = npc.gameObject.AddComponent(脚本) as NpcAiBase;
        // 只有「代码自动装的」才灌默认值 —— prefab 上手动摆的不能覆盖人家调好的数
        if (ai != null) ai.应用类型默认值();
        return ai;
    }

    /// <summary>
    /// 挑这次该装哪个 AI 脚本。优先级：
    ///   **1) 物种专属**（<see cref="找物种脚本"/>，按 prefab / 模型名匹配 `NpcAiXXX`）
    ///   2) 类型默认（<see cref="类型对应脚本"/>）
    ///
    /// 「物种专属」是靠**类名约定 + 反射**自动发现的 —— 想给某个 NPC 写专属 AI，
    /// 只要新建 `NpcAi&lt;模型名&gt;.cs`（例如母鸡就是 `NpcAiMuJi`），**不用改任何注册表**。
    /// </summary>
    public static Type 选脚本(NpcInstance npc)
    {
        if (npc == null) return null;

        var 物种 = 找物种脚本(npc.gameObject.name)
                ?? 找物种脚本(npc.定义 != null ? npc.定义.名字 : null)
                ?? 找物种脚本(npc.定义 != null ? npc.定义.id : null);
        if (物种 != null) return 物种;

        return 类型对应脚本(npc.类型);
    }

    /// <summary>把「该类型的默认参数」灌上去。只在代码自动装配时调用</summary>
    public void 应用类型默认值()
    {
        取默认参数();
        确保攻击方式();
    }

    /// <summary>类型 → AI 脚本。没实现 / 不需要 AI 的类型返回 null</summary>
    public static Type 类型对应脚本(NpcKind 类型)
    {
        switch (类型)
        {
            case NpcKind.兽类: return typeof(NpcAiAnimal);
            case NpcKind.妖魔: return typeof(NpcAiDemon);
            case NpcKind.人类: return typeof(NpcAiHuman);
            default: return null;      // 未指定 / 中立：不装 AI
        }
    }

    // ============================================================ 物种专属脚本表

    static Dictionary<string, Type> 物种脚本表;

    /// <summary>这些是实现「类型默认行为」的基类 / 中间类，不参与物种匹配</summary>
    static readonly string[] 非物种名 = { "Animal", "Demon", "Human", "Combatant" };

    /// <summary>
    /// 扫描程序集里所有 `NpcAiXXX`，把 `XXX` 当作「模型关键字」登记。
    /// 例如 `NpcAiMuJi` → 关键字 `MuJi`，prefab 叫 `MuJi_01` 的 NPC 就会用它。
    /// </summary>
    public static Dictionary<string, Type> 物种脚本()
    {
        if (物种脚本表 != null) return 物种脚本表;

        物种脚本表 = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
        Type[] 全部;
        try { 全部 = typeof(NpcAiBase).Assembly.GetTypes(); }
        catch (System.Exception e) { Debug.LogWarning("[AI] 扫描 AI 脚本失败：" + e.Message); return 物种脚本表; }

        foreach (var t in 全部)
        {
            if (t == null || t.IsAbstract || !typeof(NpcAiBase).IsAssignableFrom(t)) continue;

            const string 前缀 = "NpcAi";
            string 名 = t.Name;
            if (!名.StartsWith(前缀, StringComparison.Ordinal)) continue;

            string 关键字 = 名.Substring(前缀.Length);
            if (关键字.Length == 0) continue;
            if (System.Array.IndexOf(非物种名, 关键字) >= 0) continue;

            物种脚本表[关键字] = t;
        }
        return 物种脚本表;
    }

    /// <summary>按名字找物种专属脚本。`MuJi_01` / `MuJi_01(Clone)` / `母鸡` 都能匹配到 `NpcAiMuJi`</summary>
    public static Type 找物种脚本(string 名字)
    {
        if (string.IsNullOrEmpty(名字)) return null;
        string 净 = 名字.Trim().ToLowerInvariant();

        // 先精确、再到前缀匹配 —— 避免短关键字乱撞
        foreach (var kv in 物种脚本())
            if (净 == kv.Key.ToLowerInvariant()) return kv.Value;

        foreach (var kv in 物种脚本())
        {
            string k = kv.Key.ToLowerInvariant();
            if (净.StartsWith(k + "_") || 净.StartsWith(k + "(") || 净.StartsWith(k + " "))
                return kv.Value;
        }

        // 最后兜一层「下划线 + 关键字」：定义 id 是 `beast_muji` 这种写法，
        // 光靠前缀匹配不到。放最后是为了不让短关键字乱撞。
        foreach (var kv in 物种脚本())
        {
            string k = kv.Key.ToLowerInvariant();
            if (净.Contains("_" + k)) return kv.Value;
        }
        return null;
    }

    // ============================================================ 生命周期

    protected virtual void Awake()
    {
        自己 = GetComponent<NpcInstance>();
        动画器 = GetComponent<NpcAnimator>();
        动画 = GetComponent<Animator>();
        接管RootMotion();
        确保攻击方式();

        if (自己 != null)
        {
            自己.结算完成 += 处理结算;
            自己.Died += 处理死亡事件;
        }
    }

    /// <summary>
    /// **必须关掉 Animator 的 Root Motion。**
    ///
    /// AI 是用 <c>transform.position</c> 自己驱动移动的；Root Motion 开着的话，
    /// 播带位移曲线的片段时**整条 GameObject 会被片段带走**：
    ///   · `Idle` / `Walk` 这种循环片段，位移每轮抵消，看不太出来
    ///   · `Wound` / `Attack` 这种**一次性**片段，位移会**累积**
    ///     → 表现就是「挨了一下，直接飞天」
    ///
    /// 实测：`MuJi_01` / `Gou_01` 的 prefab 是 `applyRootMotion = True`
    /// （母鸡挨打飞天就是它），而 `BaiLuJing_01` 是 False（所以妖魔一直正常）。
    ///
    /// 这里运行时再兜一层，保证不管 prefab 怎么配都不会飞；
    /// 资产层面也修：菜单 **修仙 / NPC 资产 / 修复 Root Motion**。
    /// </summary>
    /// <summary>
    /// **由 AI 接管 Root Motion**（位移丢掉、旋转留着）。
    ///
    /// 【关键】这里必须把 `applyRootMotion` 设成 **true** —— 不是为了让它自动应用，
    /// 而是因为 **只有 true，Unity 才会调 <see cref="OnAnimatorMove"/>**；
    /// 而只要有组件实现了 `OnAnimatorMove`，Unity 就**不再自动应用** root motion，
    /// 改成由我们决定怎么消费 `deltaPosition` / `deltaRotation`。
    ///
    /// 老代码设的是 false，看着"安全"，实际把动画的**根旋转**也一起丢了（见字段说明）。
    /// </summary>
    void 接管RootMotion()
    {
        if (!强制关闭RootMotion || 动画 == null) return;
        if (!动画.applyRootMotion) 动画.applyRootMotion = true;
    }

    /// <summary>
    /// **自己消费 Root Motion：只吃旋转、不吃位移。**
    ///
    /// `applyRootMotion = true` 且本组件实现了这个方法时，Unity 不再自动应用 root motion，
    /// 由这里说了算：
    ///   · `deltaRotation` → **只取「攻击」状态的**（攻击动作的转体就靠它）
    ///   · `deltaPosition` → **一律丢掉**（AI 自己用 transform 驱动移动；
    ///     当年就是位移累积才「挨一下飞天」的）
    /// </summary>
    protected virtual void OnAnimatorMove()
    {
        if (动画 == null || !强制关闭RootMotion) return;
        if (状态 != NpcAiState.攻击 || !攻击期间锁定朝向) return;
        if (自己 != null && 自己.IsDead) return;

        transform.rotation *= 动画.deltaRotation;
    }

    protected virtual void Start()
    {
        绑定玩家();
        标定贴地();
        if (自己 != null) 自己.Revived += 处理重生;
        进入状态(NpcAiState.待机);
    }

    protected virtual void OnDestroy()
    {
        if (自己 == null) return;
        自己.Revived -= 处理重生;
        自己.结算完成 -= 处理结算;
        自己.Died -= 处理死亡事件;
    }

    void 处理结算(NpcInstance 谁, AttackResult 结果) => 收到伤害通知(结果);
    void 处理死亡事件(NpcInstance 谁) => 进入死亡();

    protected void 确保攻击方式()
    {
        if (攻击方式 != null && 攻击方式.Length > 0) return;

        // 策划约定：Attack1 = 普通攻击；Attack2 / Attack3 = 主动神通
        攻击方式 = new[]
        {
            new NpcAttackConfig
            {
                动作名 = "Attack1", 伤害属性 = DamageNature.物理, 攻击类别 = AttackKind.普通攻击,
                技能倍率 = 1f, 出手进度 = 0.4f,
            },
            new NpcAttackConfig
            {
                动作名 = "Attack2", 伤害属性 = DamageNature.物理, 攻击类别 = AttackKind.主动神通,
                技能倍率 = 1.4f, 出手进度 = 0.45f,
            },
            new NpcAttackConfig
            {
                动作名 = "Attack3", 伤害属性 = DamageNature.特殊, 攻击类别 = AttackKind.主动神通,
                技能倍率 = 1.8f, 出手进度 = 0.5f, 是施法 = true,
            },
        };
    }

    /// <summary>
    /// 子类在这里把「该类型自己的默认值」填上（表里没配时用）。
    ///
    /// 【坑】子类开头**必须先调 <see cref="确保攻击方式"/>()** ——
    /// 正常情况下 <c>Awake</c> 会先把它填好，但**编辑态直接调 `应用类型默认值()` 时 Awake 不跑**，
    /// 于是 <c>攻击方式</c> 还是空的，子类里那句 `if (攻击方式 == null) return;` 会**静默早退**、
    /// 所有招式配置都不生效（排查起来很费劲）。
    /// </summary>
    protected virtual void 取默认参数() { }

    void 绑定玩家()
    {
        if (玩家生命 == null)
        {
            if (缓存的玩家 == null) 缓存的玩家 = FindObjectOfType<PlayerVitals>();
            玩家生命 = 缓存的玩家;
        }
        if (玩家生命 == null) return;

        玩家战斗属性 = 玩家生命.GetComponent<PlayerCombatStats>();
    }

    /// <summary>
    /// 标定「脚底相对 pivot 的高度」：
    /// 用 CapsuleCollider 的世界包围盒底部算，这样不同 pivot 的模型都能正确贴地。
    /// </summary>
    void 标定贴地()
    {
        if (贴地已标定) return;
        贴地已标定 = true;

        // 用【身躯渲染体的最低点】而不是碰撞体的最低点。
        //
        // 为什么：不少 prefab 的胶囊底在 pivot 之上（甚至是个巨大的球），
        // 按碰撞体贴地会把整个模型按进地板里 —— 表现就是「只剩一个头露在外面」。
        // NpcBodyBounds 还会把武器排除掉，否则垂到地上的武器会把模型抬起来/压下去。
        贴地基准偏移 = transform.position.y - NpcBodyBounds.取最低点(gameObject);
        if (float.IsNaN(贴地基准偏移) || float.IsInfinity(贴地基准偏移)) 贴地基准偏移 = 0f;
    }

    /// <summary>表里的「索敌范围」优先</summary>
    protected float 有效索敌范围
        => 自己 != null && 自己.定义 != null && 自己.定义.索敌范围 > 0f
           ? 自己.定义.索敌范围
           : 索敌范围;

    // ============================================================ 主循环

    protected virtual void Update()
    {
        if (自己 == null) return;

        刷新距离();

        switch (状态)
        {
            case NpcAiState.死亡: 死亡中(); return;
            case NpcAiState.攻击: 攻击中(); return;
            case NpcAiState.受击:
                // 第一次进来时用 Wound 片段的真实长度，保证"播完才允许动"
                if (待校正受击时长)
                {
                    待校正受击时长 = false;
                    // 同样不能问 Animator「现在在播什么」（那可能还是上一招）——
                    // 直接查 Wound 这个动作自己的长度
                    float 长 = 动画器 != null ? 动画器.取动作长度("Wound") : 0f;
                    if (长 <= 0.05f) 长 = 当前片段长度();
                    if (长 > 0.05f) 受击结束时间 = Mathf.Max(受击结束时间, 上次受击动画时间 + 长);
                }
                if (Time.time >= 受击结束时间) 进入状态(NpcAiState.待机);
                else return;
                break;
        }

        if (自己.IsDead) { 进入死亡(); return; }

        决策();

        switch (状态)
        {
            case NpcAiState.接近: 执行接近(); break;
            case NpcAiState.远离: 执行远离(); break;
            case NpcAiState.待机: 执行待机(); break;
        }
    }

    // ============================================================ 感知

    protected void 刷新距离()
    {
        if (玩家生命 == null) { 绑定玩家(); }
        if (玩家生命 == null) 到玩家距离 = float.PositiveInfinity;
        else
        {
            Vector3 差 = 玩家生命.transform.position - transform.position;
            差.y = 0f;
            到玩家距离 = 差.magnitude;
        }

        刷新敌人距离();
    }

    /// <summary>单独抽出来：没有玩家（纯展示场景）或敌人不是玩家时也要能算</summary>
    void 刷新敌人距离()
    {
        var 目 = 敌人;
        if (目 == null) { 到敌人距离 = float.PositiveInfinity; return; }

        Vector3 差 = 目.position - transform.position;
        差.y = 0f;
        到敌人距离 = 差.magnitude;
    }

    /// <summary>玩家是否在索敌范围内（可选要求视线通畅）</summary>
    protected bool 玩家在索敌范围()
    {
        if (玩家生命 == null || 玩家生命.已死亡) return false;
        if (到玩家距离 > 有效索敌范围) return false;
        return !需要视线 || 视线通畅(玩家生命.transform);
    }

    /// <summary>玩家是否已经跑出脱战范围</summary>
    protected bool 玩家已脱战()
        => 玩家生命 == null || 玩家生命.已死亡 || 到玩家距离 > Mathf.Max(脱战范围, 有效索敌范围);

    protected bool 视线通畅(Transform 目标)
    {
        if (目标 == null) return false;
        Vector3 起点 = transform.position + Vector3.up * 1.2f;
        Vector3 终点 = 目标.position + Vector3.up * 1.2f;
        Vector3 方向 = 终点 - 起点;
        float 距离 = 方向.magnitude;
        if (距离 < 0.01f) return true;
        // 同样要排掉自己：起点在自己身上时，射线会先命中自己的碰撞体，
        // 结果就是"永远看不见玩家"
        return !取首个外部命中(起点, 方向 / 距离, 距离, 视线遮挡层, out _);
    }

    // ============================================================ 决策（子类实现）

    /// <summary>
    /// 子类在这里决定接下来干什么 —— 只能通过 <see cref="进入状态"/>（或 <see cref="想出手"/>）表达，
    /// 具体怎么走 / 怎么播动画由基类统一处理。
    /// </summary>
    protected abstract void 决策();

    /// <summary>子类可以覆盖：现在能不能出手（默认：有敌人、敌人没死、距离够、朝向正、冷却好了）</summary>
    protected virtual bool 可出手()
        => 敌人 != null
        && 敌人还活着
        && 到敌人距离 <= 攻击距离
        && 朝向敌人够正()
        && 可用出手方式() != null;   // 各招的冷却已经在 可用出手方式() 里过滤过了

    /// <summary>
    /// 朝向敌人够不够正（度）。**没转过来之前不出手** ——
    /// 否则刚生成的敌对 NPC 会背对着你就开火/挥砍，观感很怪。
    /// </summary>
    protected bool 朝向敌人够正(float 容差 = 40f)
    {
        var 目 = 敌人;
        if (目 == null) return true;
        Vector3 向 = 目.position - transform.position;
        向.y = 0f;
        if (向.sqrMagnitude < 0.0001f) return true;
        return Vector3.Angle(transform.forward, 向.normalized) <= 容差;
    }

    /// <summary>旧名，等价于 <see cref="朝向敌人够正"/></summary>
    protected bool 朝向玩家够正(float 容差 = 40f) => 朝向敌人够正(容差);

    /// <summary>子类可以覆盖：从配置里挑这次用哪条（默认随机）</summary>
    protected virtual NpcAttackConfig 选出手方式()
    {
        var 可选 = 可用出手方式();
        if (可选 == null || 可选.Count == 0) return null;
        return 可选[UnityEngine.Random.Range(0, 可选.Count)];
    }

    readonly System.Collections.Generic.List<NpcAttackConfig> 出手候选 = new System.Collections.Generic.List<NpcAttackConfig>();

    /// <summary>配置里**动画真存在**的那几条</summary>
    protected System.Collections.Generic.List<NpcAttackConfig> 可用出手方式()
    {
        出手候选.Clear();
        if (攻击方式 == null) return 出手候选;
        foreach (var c in 攻击方式)
            if (c != null && 有动作(c.动作名) && 该招就绪(c)) 出手候选.Add(c);
        return 出手候选;
    }

    // ============================================================ 状态机

    protected void 进入状态(NpcAiState 新状态)
    {
        if (状态 == 新状态) return;
        if (状态 == NpcAiState.死亡) return;       // 死了就不再切

        var 旧 = 状态;
        状态 = 新状态;
        if (打印状态日志) Debug.Log("[AI] " + name + " " + 旧 + " → " + 新状态);

        // 【关键】只有真的"进待机"才切回待机动画。
        // 以前是"只要不是攻击就切"，结果进入「受击」时会把刚播的 Wound 覆盖成 Idle/Fight
        // —— 表现就是"受击动画不立刻播"。
        if (新状态 == NpcAiState.待机) 停止移动动画();
        状态变化?.Invoke(this, 旧, 新状态);
    }

    /// <summary>够得着就出手，够不着就继续接近</summary>
    protected void 想出手()
    {
        if (可出手()) { 进入攻击(); return; }
        进入状态(NpcAiState.接近);
    }

    // ============================================================ 待机 / 接近 / 远离

    protected virtual void 执行待机()
    {
        播待机动画();
    }

    protected virtual void 执行接近()
    {
        var 目 = 敌人;
        if (目 == null) { 进入状态(NpcAiState.待机); return; }

        Vector3 目标点 = 目.position;
        float 保留 = Mathf.Max(0.1f, 攻击距离 * 0.8f);
        Vector3 差 = 目标点 - transform.position;
        差.y = 0f;
        if (差.magnitude <= 保留) { 停止移动动画(); 转向(差); return; }

        朝点走(目标点, 奔跑倍率);
    }

    protected virtual void 执行远离()
    {
        if (玩家生命 == null) { 进入状态(NpcAiState.待机); return; }

        Vector3 差 = transform.position - 玩家生命.transform.position;
        差.y = 0f;
        if (差.sqrMagnitude < 0.0001f) 差 = transform.forward;

        朝点走(transform.position + 差.normalized * 3f, 奔跑倍率);
    }

    // ============================================================ 移动

    /// <summary>朝一个世界坐标点走（会贴地、会探前方障碍）</summary>
    protected void 朝点走(Vector3 目标点, float 速度倍率)
    {
        Vector3 差 = 目标点 - transform.position;
        差.y = 0f;
        if (差.sqrMagnitude < 0.0001f) { 停止移动动画(); return; }

        Vector3 方向 = 差.normalized;
        float 速度 = Mathf.Max(0.05f, 自己.移动速度) * 速度倍率;

        // 前方探障：只探一个「胸口高度的小球」，不拿整条胶囊扫（开发注意事项 §8.3）
        if (前方探障距离 > 0f && 前方被挡(方向))
        {
            停止移动动画();
            return;
        }

        transform.position += 方向 * 速度 * Time.deltaTime;
        贴地();
        转向(方向);
        播移动动画(速度倍率);
    }

    bool 前方被挡(Vector3 方向)
    {
        Vector3 起点 = transform.position + Vector3.up * 0.9f;
        var 命中 = Physics.SphereCastAll(起点, 0.25f, 方向, 前方探障距离,
                                         障碍层, QueryTriggerInteraction.Ignore);
        foreach (var h in 命中)
        {
            if (h.collider == null) continue;
            // 排除自己身上的碰撞体 —— SphereCast 从自己体内出发会打到自己，
            // 不排掉的话 NPC 会「一步都不动」，而且从日志完全看不出原因
            if (h.collider.transform.IsChildOf(transform)) continue;
            return true;
        }
        return false;
    }

    /// <summary>把高度摆到地面上</summary>
    protected void 贴地()
    {
        if (!自动贴地) return;
        if (!贴地已标定) 标定贴地();

        Vector3 起点 = transform.position + Vector3.up * 3f;
        if (!取首个外部命中(起点, Vector3.down, 12f, 地面层, out var hit)) return;

        float 目标Y = hit.point.y + 贴地基准偏移 + 贴地微调;
        transform.position = new Vector3(transform.position.x, 目标Y, transform.position.z);
    }

    /// <summary>
    /// 沿方向取第一个**不属于自己**的命中。
    ///
    /// 【为什么必须排除自己】射线/球体从自己身上出发时，会先命中自己的碰撞体：
    ///   · `贴地()` 命中自己的胶囊顶 → 被摆到顶上 → 下一帧起点更高又命中自己
    ///     → **每帧往上棘轮一大截，表现就是「挨一下直接飞天」**
    ///     （实测母鸡 Y 从 0.02 一路涨到 372 米，约 41 m/s 匀速爬升）
    ///   · `视线通畅()` 命中自己 → 永远判成"看不见玩家"
    ///
    /// 这个坑我在 `前方被挡()` 修过一次，但**漏了地面射线和视线射线** —— 三处都得排自己。
    /// </summary>
    protected bool 取首个外部命中(Vector3 起点, Vector3 方向, float 距离, LayerMask 层, out RaycastHit 结果)
    {
        结果 = default;
        var 命中 = Physics.RaycastAll(起点, 方向, 距离, 层, QueryTriggerInteraction.Ignore);
        if (命中 == null || 命中.Length == 0) return false;

        // RaycastAll 的顺序没有保证，自己按距离排一下
        System.Array.Sort(命中, (a, b) => a.distance.CompareTo(b.distance));
        foreach (var h in 命中)
        {
            if (h.collider == null) continue;
            if (h.collider.transform.IsChildOf(transform)) continue;   // 自己的碰撞体，跳过
            结果 = h;
            return true;
        }
        return false;
    }

    /// <summary>
    /// **攻击动画播放期间锁死朝向**（默认勾着，用户要求）。
    ///
    /// 含义：起手那一下对准目标，然后**整个攻击状态期间 AI 不再转向** ——
    /// 让动画自己的「转体」（Root Motion 的 `deltaRotation`，见 `OnAnimatorMove`）
    /// 把身体转过去，手才会正好刺向目标。
    /// 去掉勾 = 旧行为（每帧都朝目标纠，会把转体抵消掉）。
    /// </summary>
    [Tooltip("攻击动画播放期间锁死朝向（攻击动作自带转体，AI 边播边转会把手刺歪）")]
    public bool 攻击期间锁定朝向 = true;

    /// <summary>转向某个方向（水平面内）</summary>
    protected void 转向(Vector3 方向)
    {
        // ★【坑·已修】**攻击动画播放期间一律不转向。**
        //
        // 攻击动作里自带「**转体**」—— 挥臂的同时身子跟着转，手臂才正好扫/刺向正前方。
        // 而 AI 的 `Update` 每帧都在把根节点转回目标：动画转一点、AI 就纠回去一点，
        // 等于把这套转体**整个抵消掉**了。
        // 实测表现：右手看着不是往前伸，而是「往侧下方挥 / 刺歪了」✗（用户报的）
        //
        // 所以起手那一刻对准目标之后就**锁死朝向**，直到收招回待机才恢复转向。
        // 这也是"攻击动画 + AI 转向"打架时的标准解法。
        if (攻击期间锁定朝向 && 状态 == NpcAiState.攻击) return;

        方向.y = 0f;
        if (方向.sqrMagnitude < 0.0001f) return;

        Quaternion 目标 = Quaternion.LookRotation(方向.normalized, Vector3.up)
                        * Quaternion.Euler(0f, 模型朝向补偿, 0f);
        transform.rotation = 转向速度 <= 0f
            ? 目标
            : Quaternion.RotateTowards(transform.rotation, 目标, 转向速度 * Time.deltaTime);
    }

    /// <summary>转向敌人（默认的敌人就是玩家；真灵的敌人是主人锁的那只怪）</summary>
    protected void 朝向敌人()
    {
        var 目 = 敌人;
        if (目 == null) return;
        转向(目.position - transform.position);
    }

    /// <summary>转向玩家（等价于 <see cref="朝向敌人"/> —— 除非子类把敌人换掉了）</summary>
    protected void 朝向玩家()
    {
        if (玩家生命 == null) return;
        转向(玩家生命.transform.position - transform.position);
    }

    // ============================================================ 动画

    /// <summary>动作表里有没有这个动作</summary>
    protected bool 有动作(string 动作名)
    {
        if (动画器 == null || string.IsNullOrEmpty(动作名)) return false;
        var 表 = 动画器.AvailableActions;
        return 表 != null && Array.IndexOf(表, 动作名) >= 0;
    }

    /// <summary>播一个动作。同一个循环动作重复调用不会重设参数</summary>
    protected bool 播(string 动作名, bool 循环 = false)
    {
        if (!有动作(动作名)) return false;
        if (循环 && 当前动作 == 动作名) return true;

        当前动作 = 动作名;
        动画器.PlayAction(动作名, 循环);
        return true;
    }

    /// <summary>按候选顺序播第一个存在的（缺动画时的降级链）</summary>
    protected bool 播首个可用(params string[] 候选)
    {
        foreach (var n in 候选)
            if (播(n, true)) return true;
        return false;
    }

    /// <summary>移动动画：优先 Run，没有就 Walk，再没有就 Idle</summary>
    protected void 播移动动画(float 速度倍率)
    {
        // 倍率小的时候走，大的时候跑
        if (速度倍率 >= 0.85f) 播首个可用("Run", "Walk", "Idle");
        else 播首个可用("Walk", "Run", "Idle");
    }

    protected void 停止移动动画()
    {
        播首个可用(取待机动作名(), "Idle", "Specialidle", "Fight", "Run");
    }

    protected void 播待机动画()
    {
        // 战斗中用 Fight（战斗待机），平时用 Idle —— 子类可以覆盖 取待机动作名()
        播首个可用(取待机动作名(), "Idle", "Specialidle", "Fight", "Run");
    }

    /// <summary>
    /// 待机播哪个动作。默认 `Idle`；
    /// **会打人的 NPC 在交战状态应该覆盖成 `Fight`**（见 NpcAiCombatant）。
    /// </summary>
    protected virtual string 取待机动作名() => "Idle";

    /// <summary>当前动画状态还剩多久（读不到返回 0）</summary>
    protected float 当前片段长度()
    {
        if (动画 == null) return 0f;
        var st = 动画.GetCurrentAnimatorStateInfo(0);
        return st.length;
    }

    // ============================================================ 出手

    protected void 进入攻击()
    {
        var 配置 = 选出手方式();
        if (配置 == null) { 进入状态(NpcAiState.待机); return; }

        当前出手 = 配置;
        本次出手已结算 = false;
        动作开始时间 = Time.time;
        动作结束时间 = Time.time + 0.6f;    // 先给兜底，下一帧用真实片段长度覆盖
        待校正动作时长 = true;

        // ★ 顺序很重要：**先对准目标，再进「攻击」状态**。
        // 进了攻击状态之后 `转向()` 就被 `攻击期间锁定朝向` 挡住了（见 转向()）——
        // 目的是把朝向**锁在起手这一刻**，让动画自己的转体（Root Motion 的 deltaRotation）把剩下的角度转完。
        // 调换这两行的话，`朝向出手目标()` 会被自己挡住、起手朝向就完全没人管了 ✗
        朝向出手目标();
        进入状态(NpcAiState.攻击);
        播(配置.动作名, false);
    }

    /// <summary>
    /// **起手前「瞬时」把朝向对准出手目标**（不按 <see cref="转向速度"/> 平滑）。
    ///
    /// 【为什么必须瞬时】进了「攻击」状态之后 <see cref="转向"/> 就被
    /// <see cref="攻击期间锁定朝向"/> 挡死了，所以**起手这一刻就必须已经对准** ——
    /// 否则会带着偏差开打，而动画自带的转体只能补正它自己那部分，补不了这个偏差 ✗
    ///
    /// 实际角度通常只有几度（接近 / 站桩等待冷却时每帧都在 <see cref="朝向敌人"/>），
    /// 所以瞬转看不出来。
    ///
    /// 默认朝敌人 —— 敌对 NPC 的敌人是玩家，真灵的敌人是主人锁定的那只怪。
    /// </summary>
    protected virtual void 朝向出手目标() => 瞬时对准(敌人);

    /// <summary>一次到位地把朝向设成该方向（水平面内）</summary>
    void 瞬时对准(Transform 目)
    {
        if (目 == null) return;
        Vector3 方向 = 目.position - transform.position;
        方向.y = 0f;
        if (方向.sqrMagnitude < 0.0001f) return;

        transform.rotation = Quaternion.LookRotation(方向.normalized, Vector3.up)
                           * Quaternion.Euler(0f, 模型朝向补偿, 0f);
    }

    /// <summary>
    /// 广播一次出手。子类需要自己实现 <see cref="结算伤害"/> 时用它 ——
    /// C# 的事件只能在声明它的类里 Invoke，子类直接写 <c>出手?.Invoke</c> 编不过。
    /// </summary>
    protected void 广播出手(NpcAttackConfig 配置, AttackResult 结果)
        => 出手?.Invoke(this, 配置, 结果);

    protected virtual void 攻击中()
    {
        if (当前出手 == null) { 收招(); return; }

        // 【注意】**这里不能锁 transform.rotation！**
        // 攻击动作的「转体」是靠 Root Motion 的 deltaRotation 加在 transform 上的
        //（见 OnAnimatorMove），锁死朝向等于把刚找回来的转体又摁掉了。
        // 攻击期间不转向靠的是两道：`转向()` 里的闸门 + OnAnimatorMove 里只吃旋转不吃位移。

        if (待校正动作时长)
        {
            // 【坑·已修·老代码就有的】以前这里读的是 `当前片段长度()`（= Animator 当前状态的长度）。
            // 但这一帧动作刚 SetInteger 切过去，**状态还没切完**，读到的是**上一个状态**的长度
            // （战斗待机 Fight = 1.3 秒）。于是：
            //   · 白熊精 Attack2（1.9 秒）在 **1.3 秒**就被 收招() 砍断，动作播不完 ✗（用户报的）
            //   · Attack1（1.4 秒）和 Fight 只差 0.1 秒，所以一直没人看出来
            // 正确做法：**按这一招自己的动作名**去控制器里查片段长度。
            float 长 = 动画器 != null ? 动画器.取动作长度(当前出手.动作名) : 0f;
            if (长 <= 0.05f) 长 = 当前片段长度();       // 退路：查不到才问 Animator
            if (长 > 0.05f) 动作结束时间 = 动作开始时间 + 长;
            待校正动作时长 = false;
            if (当前出手.动作名 != 当前动作) 播(当前出手.动作名, false);
        }

        float 总长 = Mathf.Max(0.05f, 动作结束时间 - 动作开始时间);
        float 进度 = Mathf.Clamp01((Time.time - 动作开始时间) / 总长);

        // 【出伤 + 命中特效】都在**同一时刻**：动画放到 出手进度 那一下
        if (!本次出手已结算 && 进度 >= 当前出手.出手进度)
        {
            本次出手已结算 = true;
            结算伤害(当前出手);
        }

        if (Time.time >= 动作结束时间) 收招();
    }

    void 收招()
    {
        // 每招记自己的冷却（配置里填了冷却就用它，否则用默认出手间隔）
        if (当前出手 != null)
        {
            float 冷 = 当前出手.冷却 > 0f ? 当前出手.冷却 : 攻击间隔;
            各招冷却[当前出手] = Time.time + 冷 + 当前出手.收招后摇;
        }
        下次可出手时间 = Time.time + 攻击间隔;
        当前出手 = null;
        进入状态(NpcAiState.待机);
    }

    /// <summary>这一招现在能不能出（自己的冷却好了没）</summary>
    bool 该招就绪(NpcAttackConfig 配置)
        => !各招冷却.TryGetValue(配置, out var 就绪时间) || Time.time >= 就绪时间;

    readonly System.Collections.Generic.Dictionary<NpcAttackConfig, float> 各招冷却
        = new System.Collections.Generic.Dictionary<NpcAttackConfig, float>();

    /// <summary>出手间隔（秒）= 攻击间隔倍率 ÷ 攻速</summary>
    protected float 攻击间隔
    {
        get
        {
            float 攻速 = 自己 != null ? 自己.攻速 : 1f;
            return Mathf.Max(0.2f, 攻击间隔倍率 / Mathf.Max(0.05f, 攻速));
        }
    }

    /// <summary>
    /// 真正把伤害打出去。施法类动作会走 <see cref="生成子弹"/>（飞行道具），
    /// 其余是立即命中。
    /// </summary>
    protected virtual void 结算伤害(NpcAttackConfig 配置)
    {
        var 规则 = 配置.取规则();

        if (配置.是施法)
        {
            生成子弹(配置, 规则);
            return;
        }

        立即命中(规则, 配置);
    }

    /// <summary>立即结算一次，打到玩家身上</summary>
    protected void 立即命中(AttackSpec 规则, NpcAttackConfig 配置)
    {
        if (玩家生命 == null || 玩家战斗属性 == null) return;

        var 结果 = CombatCalculator.Resolve(自己, 玩家战斗属性, 规则);
        if (结果.命中 && 结果.伤害 > 0f)
        {
            玩家生命.受到伤害(结果.伤害);

            // 【命中特效】以前这条路径**一个特效都不放**，打上去光秃秃的。
            // 现在统一走 生成命中特效 → 物理攻击自动兜上基础特效
            var 判定点 = 玩家生命.transform.position + Vector3.up * PlayerTarget.判定高度;
            生成命中特效(配置, 判定点, transform.position - 判定点);
        }

        出手?.Invoke(this, 配置, 结果);
    }

    /// <summary>
    /// 施法：生成一个飞行道具（子弹）。
    ///
    /// 子弹特效资产还没导入时，这里退化成「延迟一小会儿后立即命中」，
    /// 所以现在就能跑通逻辑；等特效包进来只要把 <see cref="NpcAttackConfig.子弹特效路径"/> 填上。
    /// </summary>
    protected virtual void 生成子弹(NpcAttackConfig 配置, AttackSpec 规则)
    {
        Vector3 出膛点 = 取出膛点(配置);
        var 目标 = 锁定单位;
        Vector3 目标点 = 目标 != null ? 目标.判定点 : 出膛点 + transform.forward * 5f;

        Vector3 方向 = 目标点 - 出膛点;
        float 距离 = 方向.magnitude;
        if (距离 < 0.01f) 方向 = transform.forward; else 方向 /= 距离;
        float 飞行时间 = 距离 / Mathf.Max(1f, 配置.子弹速度);

        // 1) 枪口闪光（纯装饰，不参与结算）
        生成装饰特效(配置.闪光特效路径, 出膛点, 方向, 配置.闪光特效存活);

        // 2) 弹道
        if (string.IsNullOrEmpty(配置.子弹特效路径))
        {
            // 没配弹道：按飞行时间延迟结算，手感上等价
            StartCoroutine(延迟命中(飞行时间, 规则, 配置));
            return;
        }

        var prefab = Resources.Load<GameObject>(配置.子弹特效路径);
        if (prefab == null)
        {
            Debug.LogWarning("[AI] " + name + " 找不到子弹特效：" + 配置.子弹特效路径 + "，退化成延迟命中", this);
            StartCoroutine(延迟命中(飞行时间, 规则, 配置));
            return;
        }

        // 【关键】弹道要生成在**自己碰撞体之外**，否则它一出生就撞到自己、当帧销毁。
        // QFX 的 PFX_ProjectileObject 是「飞行中一碰就爆」的写法，实测生在自己身上时
        // 0.5 秒内就没了（等于子弹根本没飞出去）。
        float 前移 = 出膛余量();
        Vector3 弹道起点 = 出膛点 + 方向 * 前移;

        var 弹道 = Instantiate(prefab, 弹道起点, Quaternion.LookRotation(方向, Vector3.up));
        if (!Mathf.Approximately(配置.弹体缩放, 1f)) 弹道.transform.localScale *= 配置.弹体缩放;
        弹道.name = "NpcProjectile_" + name;

        // QFX 那类【实体弹丸】自带飞行脚本（PFX_ProjectileObject）：
        // 把速度和命中特效塞给它，剩下的飞行 / 命中爆炸它自己包了，我们只安排结算。
        // 用反射取字段是为了**不依赖特效包的脚本** —— 哪天把包删了也不会编译不过。
        var 自带 = 弹道.GetComponent("PFX_ProjectileObject");
        if (自带 != null)
        {
            设字段(自带, "Speed", 配置.子弹速度);
            var 命中 = 载入特效(配置.命中特效路径);
            if (命中 != null) 设字段(自带, "ImpactFX", 命中);
            Destroy(弹道, 飞行时间 + 3f);
            StartCoroutine(延迟命中(飞行时间, 规则, 配置));
            return;
        }

        // 否则用我们自己的飞行器带着它飞
        var 飞 = 弹道.GetComponent<NpcProjectile>();
        if (飞 == null) 飞 = 弹道.AddComponent<NpcProjectile>();
        飞.最大飞行距离 = 配置.最大飞行距离;
        飞.拦截层 = 配置.拦截层;
        飞.命中半径 = 配置.命中半径;
        飞.设置直线飞行(方向, 目标 != null ? 目标.根 : null, 配置.子弹速度);
        飞.到达时 += new NpcProjectile.命中回调(() => 在目标结算(规则, 配置, 飞));
    }

    /// <summary>打中了目标点：按公式结算一次并广播</summary>
    void 在目标结算(AttackSpec 规则, NpcAttackConfig 配置, NpcProjectile 弹 = null)
    {
        // **统一走「锁定单位」** —— 不再写死玩家。伤害公式由适配层内部走
        // （玩家侧 PlayerTarget 会调 CombatCalculator 再扣血；NPC 侧 NpcTarget 转发给 ReceiveAttack）。
        var 目标 = 锁定单位;
        if (目标 == null || 目标.已倒下) return;

        var 结果 = 目标.受到攻击(自己, 规则, 自己);

        if (结果.命中 && 结果.伤害 > 0f)
        {
            // ---- 命中特效 ----
            // 位置：**弹道的真实命中点**（不是目标身上 —— 目标会动，放身上会飘）
            // 朝向：**迎着飞来的方向**（爆炸朝施法者那侧张开，看着才"打上去了"）
            // 弹道是在"进到命中半径内"就算命中的，所以它的位置离身上还差几十厘米。
            // 把特效点**往目标身上拉 85%** —— 爆炸才像"打在人身上"而不是悬在身前。
            Vector3 目标点 = 目标.判定点;
            Vector3 命中点 = 弹 != null ? Vector3.Lerp(弹.命中点, 目标点, 0.85f) : 目标点;
            Vector3 朝向 = 弹 != null ? -弹.飞行朝向 : (transform.position - 命中点);
            生成命中特效(配置, 命中点, 朝向);       // ← 走统一入口：物理攻击会自动兜上基础特效
        }
        出手?.Invoke(this, 配置, 结果);
    }

    /// <summary>
    /// 弹道要往前让开多少，才不会一出生就撞到施法者自己。
    /// 取「自己碰撞体的水平半径 + 一点余量」。
    /// </summary>
    protected float 出膛余量()
    {
        var col = GetComponent<Collider>();
        if (col == null) return 0.5f;
        float 半径 = Mathf.Max(col.bounds.extents.x, col.bounds.extents.z);
        return Mathf.Max(0.3f, 半径 + 0.35f);
    }

    /// <summary>生成一个纯装饰特效（闪光 / 命中），到时自动销毁</summary>
    protected GameObject 生成装饰特效(string 路径, Vector3 位置, Vector3 朝向, float 存活)
    {
        var prefab = 载入特效(路径);
        if (prefab == null) return null;
        // 【坑】Quaternion.LookRotation(up, up) 是退化调用（前向和上方向平行）——
        // Unity 会报错并返回 identity，特效朝向就变随机的。竖直方向要换个 up 轴。
        Vector3 前 = 朝向.sqrMagnitude > 0.0001f ? 朝向.normalized : Vector3.forward;
        Quaternion 旋转 = Mathf.Abs(Vector3.Dot(前, Vector3.up)) > 0.999f
            ? Quaternion.LookRotation(前, Vector3.forward)
            : Quaternion.LookRotation(前, Vector3.up);
        var fx = Instantiate(prefab, 位置, 旋转);
        fx.name = "NpcFx_" + name;
        Destroy(fx, Mathf.Max(0.2f, 存活));
        return fx;
    }

    // ============================================================ 命中特效

    /// <summary>
    /// **这一招该放哪个命中特效**（返回空 = 不放）。
    ///
    /// 优先级：
    ///   1. 这一招自己配了 <see cref="NpcAttackConfig.命中特效路径"/> → 用它（**高级攻击走这条**）
    ///   2. 没配、但这一招是**物理**伤害 → 用 <see cref="物理基础命中特效"/>
    ///      （策划要求：所有物理攻击最起码要有一个基础命中特效）
    ///   3. 特殊伤害又没配 → 不放（法术各自有各自的表现，不强制）
    /// </summary>
    protected virtual string 取命中特效路径(NpcAttackConfig 配置)
    {
        if (配置 == null || !配置.用命中特效) return null;
        if (!string.IsNullOrEmpty(配置.命中特效路径)) return 配置.命中特效路径;
        if (配置.伤害属性 == DamageNature.物理) return 物理基础命中特效;
        return null;
    }

    /// <summary>
    /// 在**命中点**放一个命中特效。
    /// 朝向 = 从命中点指向攻击者（特效的 +Z 朝攻击者，火星朝攻击者那侧溅）。
    /// </summary>
    protected GameObject 生成命中特效(NpcAttackConfig 配置, Vector3 命中点, Vector3 朝向)
    {
        var 路径 = 取命中特效路径(配置);
        if (string.IsNullOrEmpty(路径)) return null;

        var fx = 生成装饰特效(路径, 命中点, 朝向, Mathf.Max(1.2f, 配置.命中特效存活));
        if (fx != null && 配置.命中特效缩放 > 0f && !Mathf.Approximately(配置.命中特效缩放, 1f))
            fx.transform.localScale *= 配置.命中特效缩放;
        return fx;
    }

    /// <summary>「打在这个单位身上」的命中特效（朝向自动从命中点指向自己）</summary>
    protected GameObject 生成命中特效(NpcAttackConfig 配置, ICombatTarget 目标)
    {
        if (配置 == null || 目标 == null || 目标.根 == null) return null;
        Vector3 命中点 = 目标.判定点;
        return 生成命中特效(配置, 命中点, transform.position - 命中点);
    }

    static GameObject 载入特效(string 路径)
        => string.IsNullOrEmpty(路径) ? null : Resources.Load<GameObject>(路径);

    /// <summary>按名字给组件的字段赋值（找不到就静默跳过，不会崩）</summary>
    static void 设字段(object 组件, string 字段名, object 值)
    {
        if (组件 == null) return;
        var fi = 组件.GetType().GetField(字段名);
        if (fi != null && 值 != null) fi.SetValue(组件, 值);
    }

    System.Collections.IEnumerator 延迟命中(float 秒, AttackSpec 规则, NpcAttackConfig 配置)
    {
        yield return new WaitForSeconds(Mathf.Max(0.05f, 秒));
        if (自己 == null || 自己.IsDead) yield break;
        立即命中(规则, 配置);
    }

    /// <summary>
    /// **飞弹的要素 1：出生点。**
    /// 配了 <see cref="NpcAttackConfig.挂点"/> 就用那个骨骼（例如 `Bip01 L Finger0Nub` = 左手末端），
    /// 否则退回胸口（身高的 70%）。
    /// </summary>
    /// <summary>
    /// **当前锁定的单位** —— 飞弹和伤害的靶子。
    ///
    /// 默认 = <see cref="敌人靶"/>（敌对 NPC 就是玩家，战阵真灵就是主人锁的那只怪）——
    /// 伤害链路（飞弹瞄准 / 追踪目标 / 命中结算 / 命中特效）全都走它。
    /// </summary>
    protected virtual ICombatTarget 锁定单位 => 敌人靶;

    /// <summary>锁定单位身上的判定点（胸口高度）。没锁定单位时退化成"正前方 5 米"</summary>
    protected Vector3 锁定判定点
    {
        get
        {
            var t = 锁定单位;
            return t != null ? t.判定点 : transform.position + transform.forward * 5f;
        }
    }

    protected virtual Vector3 取出膛点(NpcAttackConfig 配置 = null)
    {
        if (配置 != null && !string.IsNullOrEmpty(配置.挂点))
        {
            var 挂 = 找挂点(配置.挂点);
            if (挂 != null) return 挂.TransformPoint(配置.挂点偏移);
            if (打印状态日志)
                Debug.LogWarning("[AI] " + name + " 找不到挂点「" + 配置.挂点 + "」，退回胸口出膛", this);
        }

        var col = GetComponent<Collider>();
        if (col != null)
            return new Vector3(col.bounds.center.x, col.bounds.min.y + col.bounds.size.y * 0.7f, col.bounds.center.z);
        return transform.position + Vector3.up * 1.2f;
    }

    /// <summary>按名字在子物件里找骨骼挂点</summary>
    protected Transform 找挂点(string 名字)
    {
        if (string.IsNullOrEmpty(名字)) return null;
        foreach (var t in GetComponentsInChildren<Transform>(true))
            if (t.name == 名字) return t;
        return null;
    }

    // ============================================================ 受击

    /// <summary>
    /// 挨打时的统一反应。
    ///
    /// 规则（策划要求）：
    ///   · 在 **待机 / 战斗待机** 时受击 → **立刻**播 Wound
    ///   · **正在播 Wound 时又挨打 → 重播**（不设冷却，每次挨打都重来）
    ///   · **播 Wound 期间不能开始别的行动**（普攻 / 追击 / 神通）—— 靠"受击"状态挡住决策
    ///   · 出手（攻击）过程中**不打断** —— 否则被多段 AoE 一打就永远出不了手
    /// </summary>
    public virtual void 收到伤害通知(AttackResult 结果)
    {
        if (自己 == null || 自己.IsDead) return;
        if (!结果.命中 || 结果.伤害 <= 0f) return;

        bool 在出手 = 状态 == NpcAiState.攻击;
        if (在出手) return;                       // 出手不打断

        进入状态(NpcAiState.受击);                  // 先切状态（免得后面的表现被状态切换覆盖）
        受击表现();                                // ★ 无条件立刻（重）播 Wound
        受击结束时间 = Time.time + 受击硬直;
        待校正受击时长 = true;                      // 下一帧用 Wound 片段长度覆盖
    }

    void 受击表现()
    {
        上次受击动画时间 = Time.time;
        if (!播("Wound", false))
        {
            // 没有 Wound 动作就退回待机（至少别卡住）
            播待机动画();
        }
    }

    // ============================================================ 死亡

    public void 进入死亡()
    {
        if (状态 == NpcAiState.死亡) return;
        状态 = NpcAiState.死亡;          // 直接赋值：死亡要能打断一切
        死亡时间 = Time.time;
        死亡已处理 = false;
        if (打印状态日志) Debug.Log("[AI] " + name + " → 死亡");
    }

    void 死亡中()
    {
        if (!死亡已处理)
        {
            死亡已处理 = true;
            死亡处理();
        }

        if (死亡后销毁延迟 > 0f && Time.time - 死亡时间 >= 死亡后销毁延迟)
        {
            // 会重生的（练功木桩那类）不销毁
            if (自己 == null || 自己.定义 == null || !自己.定义.死亡后立即重生)
                Destroy(gameObject);
        }
    }

    protected virtual void 死亡处理()
    {
        // 有 Death 就播；野兽那类**没有死亡动画**的靠粒子消散
        bool 有死亡动画 = 播("Death", false);

        if (!有死亡动画)
        {
            播待机动画();
            // 【关键】没有死亡动画时，模型要**在特效炸开的那一刻就地消失** ——
            // 否则它会保持站姿杵在那儿好几秒（消亡延迟那么久），和粒子对不上，很出戏
            if (没有死亡动画就立即隐藏模型) 隐藏模型();
        }

        if (死亡时关闭碰撞)
        {
            var col = GetComponent<Collider>();
            if (col != null) col.enabled = false;
        }

        播放消散特效();
    }

    /// <summary>把所有渲染体关掉：模型「就地消失」，交给粒子演</summary>
    protected void 隐藏模型()
    {
        foreach (var r in GetComponentsInChildren<Renderer>(true))
            if (r != null) r.enabled = false;
        模型已隐藏 = true;
    }

    /// <summary>恢复被 <see cref="隐藏模型"/> 关掉的渲染体（重生时用）</summary>
    protected void 显示模型()
    {
        if (!模型已隐藏) return;
        foreach (var r in GetComponentsInChildren<Renderer>(true))
            if (r != null) r.enabled = true;
        模型已隐藏 = false;
    }

    /// <summary>
    /// 在尸体位置放消散特效。
    ///
    /// **默认用代码生成的通用消散**，不需要任何美术资产、按体型自动缩放，谁都能用。
    /// 只有显式配了 <see cref="死亡消散特效路径"/> 时才走资产包里的 prefab。
    ///
    /// 两种通用消散（<see cref="死亡粒子被玩家吸收"/> 切换）：
    ///   · **吸收版**（默认，<see cref="NpcAbsorbEffect"/>）：炸开 → 落地 → 停 1 秒 → 飞向玩家消失
    ///   · 老版（<see cref="NpcDissolveEffect"/>）：粒子各自随机淡出
    /// </summary>
    protected void 播放消散特效()
    {
        // 用身躯包围盒定「从哪炸」和「多大」——
        // 从身体中心炸开，而不是贴地面长出来
        Vector3 位置 = transform.position + Vector3.up * 0.5f;
        float 身躯半径 = 0.4f;
        if (NpcBodyBounds.取(gameObject, out var b))
        {
            位置 = b.center;
            身躯半径 = Mathf.Max(0.08f, Mathf.Max(b.extents.x, b.extents.z));
        }
        float 半径 = 身躯半径 * Mathf.Max(0.2f, 死亡消散特效缩放);

        if (!string.IsNullOrEmpty(死亡消散特效路径))
        {
            var prefab = Resources.Load<GameObject>(死亡消散特效路径);
            if (prefab != null)
            {
                var fx = Instantiate(prefab, 位置, Quaternion.identity);
                fx.name = "NpcDeathFx_" + name;
                if (!Mathf.Approximately(死亡消散特效缩放, 1f))
                    fx.transform.localScale *= 死亡消散特效缩放;
                Destroy(fx, Mathf.Max(0.5f, 死亡消散特效存活));
                return;
            }
            Debug.LogWarning("[AI] " + name + " 找不到死亡消散特效：" + 死亡消散特效路径
                + "，改用内置的通用消散", this);
        }

        if (死亡粒子被玩家吸收)
        {
            // ★ 用户要的：落到地上 → 停 1 秒 → 飞向玩家、在玩家身上消失（被吸收）
            //
            // 【注意传的是「身躯半径」，**不乘 死亡消散特效缩放**】
            // 那个 3 倍缩放是给老的「爆发式消散」用的（炸得又大又远、然后各自淡出）。
            // 吸收版的尺寸/散布全部由 NpcAbsorbEffect 自己那套参数按身躯半径算。
            //
            // 【粒子数按境界走】用户要求「npc 境界越高，炸出来的粒子数量越多」——
            // 死亡消散粒子数 填了就用它（单个 NPC 自己定），否则按境界自动算。
            int 境界 = 自己 != null && 自己.定义 != null ? 自己.定义.境界 : 1;
            int 数量 = 死亡消散粒子数 > 0 ? 死亡消散粒子数 : NpcAbsorbEffect.按境界算粒子数(境界);
            NpcAbsorbEffect.播放(位置, 身躯半径, 死亡消散颜色, 数量, transform, 境界);
            return;
        }

        NpcDissolveEffect.播放(位置, 半径, 死亡消散颜色, 死亡消散粒子数, Mathf.Max(0.5f, 死亡消散特效存活));
    }

    void 处理重生(NpcInstance 谁)
    {
        死亡已处理 = false;
        状态 = NpcAiState.待机;
        当前出手 = null;
        下次可出手时间 = 0f;

        显示模型();       // 死亡时藏起来的模型要还回来

        if (死亡时关闭碰撞)
        {
            var col = GetComponent<Collider>();
            if (col != null) col.enabled = true;
        }
        播待机动画();
    }

    // ============================================================ ASCII 别名

    public NpcAiState State => 状态;
    public NpcInstance Self => 自己;
    public PlayerVitals PlayerVitals => 玩家生命;
    public float DistanceToPlayer => 到玩家距离;
    public void ForceState(NpcAiState s) => 进入状态(s);
    public void NotifyDamaged(AttackResult r) => 收到伤害通知(r);
    public void EnterDeath() => 进入死亡();
}
