using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// **坐骑系统**：按 <see cref="坐骑键"/>（默认 E）上/下坐骑。
///
/// ## 四个状态（用户 2026-09-23 定）
///
/// | 状态 | 坐骑 | 玩家 |
/// |---|---|---|
/// | 上坐骑中 | `Birth`（2.67s） | 御风_升空（0.90s）→ 御风_Idle |
/// | 坐骑待机 | `Idle`（循环） | 御风_Idle（循环） |
/// | 坐骑行进中 | `FightRun`（循环） | 御风_前进（循环） |
/// | 下坐骑中 | `LeveUp`（2.03s）+ 消散 | 御风_Idle → 御风_落地（0.70s） |
///
/// ## 时间对齐
///
/// 两段动画**各自按原生速度播**，短的那个先放完就停在结束状态等长的那个 ——
/// 不去拉伸动画速度（那会明显变形）。所以：
///
/// · 上坐骑总时长 = max(御风_升空 0.90, Birth 2.67) = **2.67s**，
///   玩家 0.90s 就升到位、转御风_Idle，一直等到坐骑出生完；
/// · 下坐骑总时长 = max(LeveUp 2.03, 御风_落地 0.70) = **2.03s**，
///   玩家的落地段**推迟到 2.03−0.70 = 1.33s 才触发**，让"脚踩到地面"和
///   "坐骑消散完"同时发生。
///
/// ## 和「凭虚御风」的关系（用户原话）
///
/// > 「骑乘坐骑时无法开始御风，但是同样不会掉高度，除此之外只是借用了动画」
///
/// 所以：
/// * 骑乘期间 **屏蔽 Shift 起飞**（<see cref="YufengFlight.禁止切换"/>）；
/// * 高度由本组件自己管（<see cref="角色目标高度"/>），**不消耗灵气**、**不走重力**；
/// * 玩家动画只是**借用**御风的四个片段（升空/Idle/前进/落地），
///   通过 <see cref="PlayerAnimationController"/> 的外部驱动接口播。
///
/// ## 坐骑摆位
///
/// `applyRootMotion = False`，所以坐骑不会被动画带着跑，位置完全由本组件决定：
/// 出生动画就在骑乘位播放，播完它自然已经在待机位置上 ✓
/// 偏移/缩放是**从用户摆好的场景里量出来的**，见 <see cref="坐骑摆位表"/>。
/// </summary>
[DisallowMultipleComponent]
public class MountRider : MonoBehaviour
{
    public enum 骑乘状态
    {
        未骑乘,
        上坐骑中,
        坐骑待机,
        坐骑行进中,
        下坐骑中,
    }

    [Header("接线（留空自动找）")]
    [Tooltip("角色面板数据，从它的「当前坐骑」读要骑哪只")]
    public UIPanelData 面板数据;

    [Tooltip("玩家移动控制器")]
    public PlayerController 控制器;

    [Tooltip("凭虚御风（骑乘期间要屏蔽它）")]
    public YufengFlight 御风;

    [Tooltip("玩家动画控制器（借它的御风片段）")]
    public PlayerAnimationController 动画;

    [Header("按键")]
    [Tooltip("上/下坐骑的按键")]
    public KeyCode 坐骑键 = KeyCode.E;

    [Tooltip("骑乘期间是否允许再次按键下坐骑")]
    public bool 允许下坐骑 = true;

    [Header("动画时间（秒）——默认值就是资源里的实际时长")]
    [Tooltip("玩家 御风_升空 的时长")]
    public float 玩家升空时长 = 0.90f;

    [Tooltip("坐骑 Birth 的时长。上坐骑总时长 = max(升空, 这个)")]
    public float 坐骑出生时长 = 2.67f;

    [Tooltip("玩家 御风_落地 的时长")]
    public float 玩家落地时长 = 0.70f;

    [Tooltip("坐骑 LeveUp 的时长。下坐骑总时长 = max(落地, 这个)")]
    public float 坐骑消散时长 = 2.03f;

    [Tooltip("上坐骑时玩家升空的高度过渡曲线（0→1）。直线就够")]
    public AnimationCurve 升空曲线 = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Tooltip("下坐骑时玩家下落的高度过渡曲线（1→0）")]
    public AnimationCurve 落地曲线 = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);

    [Header("坐骑朝向（锁定 + 移动时）")]
    [Tooltip("锁定管理器。留空则自动在本体找 NpcTargeting。\n" +
             "用来判断「有没有锁定」—— 有锁定且行进中时，坐骑朝移动方向、玩家继续正面锁敌")]
    public NpcTargeting 目标管理器;

    [Tooltip("跑动动作名的备用列表（某只坐骑配的跑动动作在它控制器里叫别的名字时用）")]
    public string[] 跑动动作备用 = { "FightRun" };

    [Tooltip("消失动作名的备用列表。\n" +
             "★ 用户 2026-09-23 定：下坐骑播的是「死/消散」，**不该叫 LeveUp**（那是升级）。\n" +
             "但碧水兽控制器里的状态实际写的字面是 `Die`（不是 dead），所以主名用 Die，\n" +
             "这里把 dead / LeveUp 都留着兜底 —— 换控制器时不用改代码")]
    public string[] 消失动作备用 = { "dead", "Dead", "LeveUp" };

    /// <summary>单只坐骑的摆位。**每只坐骑一条**，不再有全局值可退。</summary>
    [System.Serializable]
    public class 坐骑摆位
    {
        [Tooltip("坐骑id，和 坐骑表.csv 第一列一致，例如 mount_chibang_01")]
        public string 坐骑id = "";
        [Tooltip("坐骑根相对**角色根**的本地偏移（用角色自己的朝向算，不是坐骑朝向）")]
        public Vector3 偏移 = new Vector3(0.29f, -3.95f, 0.24f);
        [Tooltip("相对角色朝向的额外旋转（欧拉角）")]
        public Vector3 朝向 = Vector3.zero;
        [Tooltip("这只坐骑的缩放（各只模型大小不同，必须一只一只给）")]
        public float 缩放 = 3.8762f;
    }

    [Tooltip("★ 每只坐骑的摆位，**一只一条，缺了会报 warning**。\n" +
             "用户 2026-09-23 定：不要全局偏移 —— 坐骑模型大小/骨架差别很大\n" +
             "（灵翅是长在背上的翅膀、碧水兽是悬浮的水兽、其余是狼/鹿/马/龙），\n" +
             "一个全局值只能对一只合适，调一只就会连带动到其他所有只。\n" +
             "填法：从场景里摆好的实例量出来，别手填（量法见 开发注意事项 §34.9/§34.14）")]
    public 坐骑摆位[] 坐骑摆位表 = new 坐骑摆位[0];

    /// <summary>
    /// 单只坐骑的四个阶段动作。**同一个动作可以兼任多个阶段，靠速度区分**。
    ///
    /// 用户 2026-09-23 定的用法（上古神龙）：它只有一个动作 `CINEMA_4D___`（环绕飞行），
    /// 同时当 idle / birth / run / dead 用 —— idle 0.5 倍速、run 1 倍速、birth 与 dead 1.7 倍速。
    /// 「**最好不要拆开，只改变速度就可以了**」—— 所以这里只配名字和速度，不复制片段。
    /// </summary>
    [System.Serializable]
    public class 坐骑动作
    {
        [Tooltip("坐骑id，和 坐骑表.csv 第一列一致")]
        public string 坐骑id = "";
        [Tooltip("待机动作名")]
        public string 待机动作 = "Idle";
        [Tooltip("待机播放速度")]
        public float 待机速度 = 1f;
        [Tooltip("移动动作名")]
        public string 跑动动作 = "Run";
        [Tooltip("移动播放速度")]
        public float 跑动速度 = 1f;
        [Tooltip("出生动作名。**留空 = 这只没有出生动画**，走「粒子 + 从小变大」保底")]
        public string 出生动作 = "Birth";
        [Tooltip("出生播放速度")]
        public float 出生速度 = 1f;
        [Tooltip("消失（下坐骑）动作名。不叫 LeveUp —— 那是升级")]
        public string 消失动作 = "Die";
        [Tooltip("消失播放速度")]
        public float 消失速度 = 1f;
    }

    [Tooltip("★ 每只坐骑的动作配置，一只一条。\n" +
             "支持「一个动作兼四个阶段」：把四个名字填成同一个、只改速度即可（上古神龙就是这样）。\n" +
             "缺了不会崩，会按 Idle/Run/Birth/Die 全 1 倍速兜底")]
    public 坐骑动作[] 坐骑动作表 = new 坐骑动作[0];

    [Header("个别坐骑的行为覆盖")]
    [Tooltip("这些坐骑**朝向时刻跟玩家、永远待在背后/身周**，不参与「行进时朝移动方向」那套。\n" +
             "· 灵翅 mount_chibang_01：长在背上的翅膀，不可能自己转头\n" +
             "· 上古神龙 mount_shenlong_01：用户 2026-09-23 定「和翅膀类似，是相对角色的位置卡死的，\n" +
             "  其实更类似一个环身的特效」—— 它是绕着角色转的特效，位置和朝向都锁死在角色身上")]
    public string[] 朝向始终跟玩家的坐骑 = { "mount_chibang_01", "mount_shenlong_01" };

    [Tooltip("坐骑转向速度（度/秒）。停步后转回玩家朝向、以及朝移动方向转，都走这个速度")]
    public float 坐骑转向速度 = 240f;

    float 当前坐骑Yaw;
    bool 坐骑Yaw已初始化;

    [Header("高度与移动")]
    [Tooltip("骑乘时角色根抬离地面的高度（米）。上坐骑就是升到这个高度")]
    public float 骑乘高度 = 5.64f;

    [Tooltip("骑乘时的水平移动速度（米/秒）")]
    public float 坐骑移动速度 = 9f;

    [Tooltip("速度超过多少算「行进中」（低于它播待机）")]
    public float 行进判定速度 = 0.2f;

    [Header("下坐骑的消散特效")]
    public Color 消散颜色 = new Color(0.55f, 0.9f, 1f, 1f);

    [Tooltip("消散半径（米）")]
    public float 消散半径 = 2.5f;

    [Header("提示")]
    [Tooltip("没装备坐骑时的提示（可留空）")]
    public UIPanelHint 提示组件;

    [Header("调试")]
    public bool 打印日志 = true;

    // ============================================================ 对外状态

    /// <summary>当前状态</summary>
    public 骑乘状态 状态 { get; private set; } = 骑乘状态.未骑乘;

    /// <summary>是否骑乘中（上坐骑中 / 待机 / 行进 / 下坐骑中 都算）</summary>
    public bool 骑乘中 => 状态 != 骑乘状态.未骑乘;

    /// <summary>是否已经稳定骑上去了（待机 / 行进）</summary>
    public bool 已骑稳 => 状态 == 骑乘状态.坐骑待机 || 状态 == 骑乘状态.坐骑行进中;

    /// <summary>正在播上/下坐骑的过渡动画</summary>
    public bool 过渡中 => 状态 == 骑乘状态.上坐骑中 || 状态 == 骑乘状态.下坐骑中;

    /// <summary>骑着的坐骑定义</summary>
    public MountDefinition 坐骑定义 { get; private set; }

    /// <summary>坐骑实例（没骑时为 null）</summary>
    public GameObject 坐骑实例 { get; private set; }

    /// <summary>给 PlayerController 用的：角色根应该待的高度</summary>
    public float 角色目标高度 { get; private set; }

    /// <summary>骑乘时的水平移动速度，给 PlayerController 用</summary>
    public float 骑乘速度 => 坐骑移动速度;

    /// <summary>是否在行进（速度超过阈值）</summary>
    public bool 行进中 { get; private set; }

    /// <summary>状态变化回调（参数为新状态）</summary>
    public event System.Action<骑乘状态> 状态变化;

    // ============================================================ 内部

    float 过渡计时;
    float 落地触发时刻;
    bool 落地已触发;
    NpcAnimator 坐骑动画;
    CharacterController cc;

    void Awake()
    {
        解析引用();
        角色目标高度 = transform.position.y;
    }

    void OnDisable()
    {
        // 组件被禁用（换功法 / 关神通）时把坐骑收掉，别留一只僵在那
        if (坐骑实例 != null) 收掉坐骑(false);
        解除御风屏蔽();
    }

    void 解析引用()
    {
        if (cc == null) cc = GetComponent<CharacterController>();
        if (控制器 == null) 控制器 = GetComponent<PlayerController>();
        if (御风 == null) 御风 = GetComponent<YufengFlight>();
        if (动画 == null) 动画 = GetComponent<PlayerAnimationController>();
        if (面板数据 == null)
        {
            var ui = FindObjectOfType<CharacterPanelUI>();
            if (ui != null) 面板数据 = ui.GetComponent<UIPanelData>();
        }
        if (面板数据 == null) 面板数据 = FindObjectOfType<UIPanelData>();
    }

    void Update()
    {
        if (面板数据 == null) 解析引用();

        if (Input.GetKeyDown(坐骑键)) 按了坐骑键();

        switch (状态)
        {
            case 骑乘状态.上坐骑中: 更新上坐骑(); break;
            case 骑乘状态.下坐骑中: 更新下坐骑(); break;
            case 骑乘状态.坐骑待机:
            case 骑乘状态.坐骑行进中: 更新骑乘中(); break;
        }
    }

    void 按了坐骑键()
    {
        if (状态 == 骑乘状态.未骑乘) 尝试上坐骑();
        else if (已骑稳 && 允许下坐骑) 开始下坐骑();
    }

    // ============================================================ 上坐骑

    void 尝试上坐骑()
    {
        解析引用();

        // ★ 御风流程中不能召唤坐骑（用户 2026-09-23 要求）
        // 御风有自己一整套高度控制（YufengFlight.地面高度 + 高度偏移），
        // 和坐骑的高度管理会互相打架 —— 而且骑乘期间本来就禁止起飞（见 锁住御风），
        // 两者必须互斥，否则会出现"飞行中上坐骑、状态谁也不管谁"的叠加态。
        if (御风 != null && 御风.御风流程中)
        {
            if (提示组件 != null) 提示组件.Show("御风中无法召唤坐骑，先落地");
            else if (打印日志) Debug.Log("[坐骑] 御风流程中（升空/御风/落地），不能召唤坐骑", this);
            return;
        }

        var m = 面板数据 != null ? 面板数据.当前坐骑 : null;
        if (m == null)
        {
            if (提示组件 != null) 提示组件.Show("还没有装备坐骑");
            else if (打印日志) Debug.Log("[坐骑] 没有装备坐骑，先在上面的坐骑页点「装备」", this);
            return;
        }

        var 预制 = m.加载模型();
        if (预制 == null)
        {
            Debug.LogWarning("[坐骑] 加载不到模型：" + m.模型资源路径, this);
            return;
        }

        坐骑定义 = m;
        坐骑实例 = Instantiate(预制);
        坐骑实例.name = "坐骑_" + m.坐骑名称;
        坐骑实例.transform.localScale = Vector3.one * 实用缩放();

        // ★★ 必须关掉坐骑身上的碰撞体 ★★
        // 坐骑 prefab 根上带一个 CapsuleCollider，而骑手是 CharacterController ——
        // 两者套在一起会互相顶，实测**下坐骑时人被弹到 33 米高**（速度远超高度控制的 ±10m/s）。
        // 坐骑本来也不该挡住自己的骑手。
        foreach (var c in 坐骑实例.GetComponentsInChildren<Collider>(true)) c.enabled = false;

        // ★★ 必须**删掉**旧版 Animation 组件（只 enabled=false 不够）★★
        // 翅膀 prefab 根上同时挂着 Animator（Chibang 控制器）和旧版 Animation（2 个片段）。
        // Unity 里 Animator 和 Animation 是**互斥**的：只要 Animation 组件**存在**，
        // Mecanim 就不绑定 clip —— 实测 `GetCurrentAnimatorClipInfo(0).Length == 0`，
        // 状态机的 normalizedTime 照常推进，但**一根骨骼都不动** ✗
        // 只把 enabled 置 false 是没用的（试过），组件在就不行，必须销毁。
        foreach (var a in 坐骑实例.GetComponentsInChildren<Animation>(true)) Destroy(a);

        坐骑动画 = 坐骑实例.GetComponentInChildren<NpcAnimator>();
        if (坐骑动画 == null)
            Debug.LogWarning("[坐骑]「" + m.坐骑名称 + "」上没有 NpcAnimator，播不了动作", this);

        // ★ 出生动画就在骑乘位播放：applyRootMotion=False，所以播完它自然已经在待机位置上
        起始高度 = transform.position.y;
        角色目标高度 = 起始高度 + 骑乘高度;
        // ★ 用「最终骑乘高度」摆位 —— 整段出生动画里坐骑不动，升上来的是它自己的动画
        摆坐骑(角色目标高度);
        // 【不能在这里直接播】NpcAnimator 要等 Start 才「已就绪」，而 Instantiate 当帧 Start 还没跑，
        // PlayAction 会返回 false —— 表现就是**碧水兽卡在默认姿势不动**
        //（用户 2026-09-23 报的「birth 动画卡住了」就是这个）。
        // 改成每帧重试，成功为止，见 更新上坐骑()。
        出生已开播 = false;
        保底特效已放 = false;
        已播动作名 = null;              // 换了坐骑要重新播一次
        出生动作时长 = 0f;              // 换坐骑要重新量
        消失动作时长 = 0f;
        无出生动画 = !有出生动画();     // 没有出生动作的坐骑 → 走「粒子 + 从小变大」保底

        锁住御风();
        角色目标高度 = 起始高度 + 骑乘高度;
        if (动画 != null)
        {
            动画.设置外部御风(true, false);
            动画.触发升空();
        }

        过渡计时 = 0f;
        切到(骑乘状态.上坐骑中);
        if (打印日志) Debug.Log("[坐骑] 上坐骑：「" + m.坐骑名称 + "」出生 " + 坐骑出生时长
            + "s ／ 玩家升空 " + 玩家升空时长 + "s", this);
    }

    float 起始高度;
    bool 出生已开播;
    bool 无出生动画;          // 这只坐骑的动作配置里没有出生动作（或控制器里没有）→ 走保底（粒子 + 缩放）
    bool 保底特效已放;
    float 出生动作时长;       // 0 = 还没量到（量法：片段长度 / 出生速度）
    float 消失动作时长;       // 0 = 还没量到（量法：片段长度 / 消失速度）

    /// <summary>播坐骑动作。NpcAnimator 没就绪时返回 false，调用方可以重试</summary>
    bool 播坐骑动作(string 名, bool 循环)
    {
        if (坐骑动画 == null || string.IsNullOrEmpty(名)) return false;
        // 先查可用动作表再播 —— 否则 NpcAnimator 会为每个找不到的名字打一条警告，
        // 而"主名没有、走备用名"是常态（碧水兽的 Run 就是这种情况），会刷屏
        var 表 = 坐骑动画.AvailableActions;
        if (表 == null || System.Array.IndexOf(表, 名) < 0) return false;
        return 坐骑动画.PlayAction(名, 循环);
    }

    /// <summary>这只坐骑有没有出生动画（= 它那条动作配置里填了出生动作，且控制器里真有）</summary>
    bool 有出生动画()
    {
        if (坐骑动画 == null) return false;
        return 有动作(坐骑动画, 取动作().出生动作);
    }

    /// <summary>取这只坐骑那条动作配置。缺了就按标准名字全 1 倍速兜底（并报一次 warning）</summary>
    坐骑动作 取动作()
    {
        string id = 坐骑定义 != null ? 坐骑定义.坐骑id : null;
        if (!string.IsNullOrEmpty(id) && 坐骑动作表 != null)
            foreach (var a in 坐骑动作表)
                if (a != null && a.坐骑id == id) return a;

        if (已报缺失动作的坐骑id != id)
        {
            已报缺失动作的坐骑id = id;
            Debug.LogWarning("[坐骑] 坐骑动作表 里没有「"
                + (坐骑定义 != null ? 坐骑定义.坐骑名称 + "（" + id + "）" : "未知坐骑")
                + "」这一条，按 Idle/Run/Birth/Die 全 1 倍速兜底", this);
        }
        return 兜底动作;
    }

    string 已报缺失动作的坐骑id;

    static readonly 坐骑动作 兜底动作 = new 坐骑动作 { 坐骑id = "(兜底)" };

    /// <summary>设坐骑的播放速度。同一动作兼多阶段时，阶段之间的区别全靠它。</summary>
    void 设动作速度(float 速度)
    {
        if (坐骑实例 == null) return;
        var an = 坐骑实例.GetComponentInChildren<Animator>();
        if (an != null) an.speed = 速度 > 0.0001f ? 速度 : 1f;
    }

    /// <summary>
    /// 量「这个动作在这个速度下要播多久」= 片段长度 / 速度。
    /// 取动作长度 是按 **clip 名** 查的，查不到返回 0 → 调用方用配置的兜底时长。
    /// </summary>
    float 取动作时长(string 动作名, float 速度)
    {
        if (坐骑动画 == null || string.IsNullOrEmpty(动作名)) return 0f;
        float L = 坐骑动画.取动作长度(动作名);
        return L > 0.01f ? L / Mathf.Max(0.0001f, 速度) : 0f;
    }

    /// <summary>已经播上的动作名（用来判断"这一帧还需不需要重播"）</summary>
    string 已播动作名;

    /// <summary>先问再播。直接 PlayAction 缺动作时会刷 warning，这里静默跳过</summary>
    static bool 有动作(NpcAnimator 动画器, string 名)
    {
        if (动画器 == null || string.IsNullOrEmpty(名)) return false;
        var 表 = 动画器.AvailableActions;
        return 表 != null && System.Array.IndexOf(表, 名) >= 0;
    }

    /// <summary>
    /// 播动作，主名不行就依次试备用名，**返回真正播上的那个名字**（要拿它去查片段长度）。
    /// 备用的存在意义：不同美术给的控制器里同一个概念叫法不统一
    ///（碧水兽的跑动状态就叫 FightRun 而不是 Run）。
    /// </summary>
    string 播坐骑动作取名(string 主名, bool 循环, string[] 备用)
    {
        if (播坐骑动作(主名, 循环)) return 主名;
        if (备用 != null)
            foreach (var 备 in 备用)
                if (!string.IsNullOrEmpty(备) && 备 != 主名 && 播坐骑动作(备, 循环)) return 备;
        return null;
    }

    bool 播坐骑动作带备用(string 主名, bool 循环, string[] 备用)
        => 播坐骑动作取名(主名, 循环, 备用) != null;

    void 更新上坐骑()
    {
        过渡计时 += Time.deltaTime;

        var 动 = 取动作();

        // 出生动作等到 NpcAnimator 就绪再播（Instantiate 当帧它是没就绪的）
        if (!出生已开播)
        {
            if (无出生动画)
            {
                // ★ 保底：没有 Birth 的坐骑用「粒子 + 淡入」顶上（用户 2026-09-23 要求）
                出生已开播 = true;
                if (!保底特效已放 && 坐骑实例 != null)
                {
                    保底特效已放 = true;
                    NpcDissolveEffect.播放(坐骑实例.transform.position + Vector3.up * 出生保底特效抬高,
                                          出生保底特效半径, 出生保底特效颜色);
                }
            }
            else if (播坐骑动作(动.出生动作, false))
            {
                出生已开播 = true;
                // 时长按「片段长度 / 速度」算，这样 birth 提速到 1.7 倍时整套过渡也跟着变短
                出生动作时长 = 取动作时长(动.出生动作, 动.出生速度);
            }
        }

        // ★ 出生阶段的速度：一个动作兼多阶段时，阶段之间的区别**全靠速度**
        //（上古神龙：同一个环绕飞行，birth 用 1.7 倍、idle 用 0.5 倍）
        if (!无出生动画 && 出生已开播) 设动作速度(动.出生速度);

        // 出生期间坐骑摆在哪：
        //   · **有 Birth 动画**（碧水兽）→ 钉在**最终骑乘高度**
        //     因为 Birth 自己就会从下方升上来（首帧比末帧低 2.27 本地单位 ≈ 8.8m），
        //     跟着玩家升会和它自己的上升叠成两倍 ✗
        //   · **没有 Birth**（其余 7 只，走「从小变大」保底）→ **跟着玩家当前高度**
        //     因为保底没有任何"自己上升"的动作，钉在最终高度的话，
        //     翅膀会在玩家还在地面时就浮在**上方好几米**的地方长大 ✗
        //     （用户 2026-09-23 报的"生成好像也是在更上方的部分生成的"就是这个）
        摆坐骑(无出生动画 ? transform.position.y : 起始高度 + 骑乘高度);

        // 没有 Birth 时用「从小变大」代替淡入（用户 2026-09-23 选的效果）：
        // 起始很小 → 平滑长到正常体积。
        //
        // 【为什么不做位置补偿了】上一版按**绑定姿势的 localBounds** 反推补偿，
        // 实测仍有 0.197m 水平漂移（蒙皮网格的 localBounds 和实际 bounds 不一致）。
        // 现在干脆**不补**：让翅膀从「根的位置」往外长 —— 而坐骑根就落在**角色下背**，
        // 所以看起来就是**从背后长出来**，方向本身就是对的 ✓
        // 既不用和蒙皮包围盒较劲，也**不会有任何漂移**（因为压根没有补偿量可算错）。
        if (无出生动画 && 坐骑实例 != null)
        {
            float kk = 出生保底时长 > 0.001f ? Mathf.Clamp01(过渡计时 / 出生保底时长) : 1f;
            float s = Mathf.SmoothStep(0f, 1f, kk);
            float 倍率 = Mathf.Lerp(出生保底起始缩放, 1f, s);
            坐骑实例.transform.localScale = Vector3.one * (实用缩放() * 倍率);
        }

        // 玩家：0 → 升空时长 之间升到骑乘高度；升空动画放完就转御风_Idle
        float k = 玩家升空时长 > 0.001f ? Mathf.Clamp01(过渡计时 / 玩家升空时长) : 1f;
        角色目标高度 = 起始高度 + 骑乘高度 * 升空曲线.Evaluate(k);

        if (动画 != null && 过渡计时 >= 玩家升空时长)
            动画.设置外部御风(true, false);   // 已经切到 御风_Idle（Flying=1, FlyMoving=0）

        if (过渡计时 >= 上坐骑总时长)
        {
            // 保底路径要把缩放复原（摆坐骑 里那句只在缩放到 0 时才兜底）
            if (无出生动画 && 坐骑实例 != null)
                坐骑实例.transform.localScale = Vector3.one * 实用缩放();
            播坐骑动作("Idle", true);
            切到(骑乘状态.坐骑待机);
            if (打印日志) Debug.Log("[坐骑] 上坐骑完成 → 坐骑待机", this);
        }
    }

    /// <summary>上坐骑总时长：有出生动作就用它的时长（片段长/速度），没有就用保底时长</summary>
    float 上坐骑总时长 => Mathf.Max(玩家升空时长,
        无出生动画 ? 出生保底时长 : (出生动作时长 > 0.01f ? 出生动作时长 : 坐骑出生时长));

    /// <summary>这只坐骑实际用的缩放（来自它自己那条摆位）</summary>
    float 实用缩放()
    {
        float s = 取摆位().缩放;
        return s > 0.0001f ? s : 1f;      // 防呆：0/负数会让坐骑直接看不见
    }

    // （上坐骑总时长 见上面 —— 会按这只坐骑有没有 Birth 动画自动切换）

    [Header("没有 Birth 动画时的保底")]
    [Tooltip("保底淡入时长（秒）。有 Birth 的坐骑用不到这个")]
    public float 出生保底时长 = 1.2f;
    [Tooltip("保底粒子颜色")]
    public Color 出生保底特效颜色 = new Color(0.6f, 0.9f, 1f, 1f);
    [Tooltip("保底「从小变大」的起始体积倍率。0.05 = 一开始只有正常体积的 5%")]
    [Range(0f, 1f)]
    public float 出生保底起始缩放 = 0.05f;
    [Tooltip("保底粒子半径")]
    public float 出生保底特效半径 = 2f;
    [Tooltip("保底粒子相对坐骑根抬高多少")]
    public float 出生保底特效抬高 = 1f;

    // ============================================================ 骑乘中

    void 更新骑乘中()
    {
        // 高度就锁在这，不走重力、不消耗灵气
        角色目标高度 = 起始高度 + 骑乘高度;

        // 速度决定待机 / 行进
        float 速度 = 控制器 != null ? 控制器.CurrentSpeed : 0f;
        bool 该行进 = 速度 > 行进判定速度;
        if (该行进 != 行进中)
        {
            行进中 = 该行进;
            if (动画 != null) 动画.设置外部御风(true, 该行进);
            切到(该行进 ? 骑乘状态.坐骑行进中 : 骑乘状态.坐骑待机);
        }

        // ★ 每帧确保动作在播，而不是只在状态切换那一帧试一次。
        // 只在切换帧试一次的话，那一次失败就**永远不播**了 ——
        // 用户 2026-09-23 报的「翅膀没挂上 idle 和 run 的动画」就是这个原因
        //（NpcAnimator 要等 Start 才「已就绪」，外面也可能有时序问题）。
        //
        // ★★ 速度**每帧都要重设**，不能只在换动作时设一次：
        // 上古神龙的待机和跑动是**同一个动作**（CINEMA_4D___），换阶段时动作名没变，
        // "只在名字变了才重播"的判断会漏掉，速度就永远停在 idle 的 0.5 倍 ✗
        var 动 = 取动作();
        string 想要 = 该行进 ? 动.跑动动作 : 动.待机动作;
        if (已播动作名 != 想要 && 播坐骑动作带备用(想要, true, 跑动动作备用)) 已播动作名 = 想要;
        设动作速度(该行进 ? 动.跑动速度 : 动.待机速度);

        摆坐骑();
    }

    // ============================================================ 下坐骑

    void 开始下坐骑()
    {
        过渡计时 = 0f;
        起始高度 = 角色目标高度 - 骑乘高度;

        // ★ 下坐骑播的是「死/消散」，**不是 LeveUp**（用户 2026-09-23 明确纠正：LeveUp 是升级）。
        //   碧水兽控制器里这个状态的字面名是 `Die`，所以主名 Die、备用里留着 dead/LeveUp。
        //   时长按「片段长度 / 消失速度」算；量不到就退回配置的 坐骑消散时长。
        var 动 = 取动作();
        消失动作时长 = 0f;
        string 播上的 = 播坐骑动作取名(动.消失动作, false, 消失动作备用);
        if (!string.IsNullOrEmpty(播上的))
        {
            设动作速度(动.消失速度);
            消失动作时长 = 取动作时长(播上的, 动.消失速度);
        }

        落地触发时刻 = Mathf.Max(0f, 下坐骑总时长 - 玩家落地时长);
        落地已触发 = false;

        if (动画 != null) 动画.设置外部御风(true, false);

        切到(骑乘状态.下坐骑中);
        if (打印日志) Debug.Log("[坐骑] 下坐骑：消散 " + 实际消散时长 + "s（动作「" + 播上的 + "」"
            + 动.消失速度 + " 倍速）／ 玩家落地 " + 玩家落地时长 + "s（落地在 "
            + 落地触发时刻.ToString("F2") + "s 触发，同时收尾）", this);
    }

    void 更新下坐骑()
    {
        过渡计时 += Time.deltaTime;

        // 下落：从骑乘高度回到地面。用落地曲线（1→0）
        float k = 下坐骑总时长 > 0.001f ? Mathf.Clamp01(过渡计时 / 下坐骑总时长) : 1f;
        角色目标高度 = 起始高度 + 骑乘高度 * 落地曲线.Evaluate(k);

        // 落地动画推迟触发，让它和目标同时收尾
        if (!落地已触发 && 过渡计时 >= 落地触发时刻)
        {
            落地已触发 = true;
            角色目标高度 = 起始高度;
            if (动画 != null) 动画.触发落地();
        }

        // 坐骑消散：动画播到一半开始散，同时整体淡出
        摆坐骑();
        if (坐骑实例 != null && 实际消散时长 > 0.001f)
        {
            float 进度 = Mathf.Clamp01(过渡计时 / 实际消散时长);
            if (过渡计时 >= 实际消散时长 * 0.35f && !消散已放)
            {
                消散已放 = true;
                var 中心 = 坐骑实例.transform.position + Vector3.up * 1.5f;
                NpcDissolveEffect.播放(中心, 消散半径, 消散颜色);
            }
            // 后 60% 缩到 0，做出"消散"的收尾
            // ★ 必须用 实用缩放()（吃这只坐骑自己那条摆位），不能写死某个全局缩放 ——
            //   灵翅的覆盖缩放是 1，通用的是 3.8762；写成通用的会让翅膀一下坐骑就胀大 3.9 倍，
            //   而它的网格中心又高于根，于是"飞到上面去" ✗（用户 2026-09-23 报的）
            float 缩放 = 实用缩放() * (1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((进度 - 0.4f) / 0.6f)));
            坐骑实例.transform.localScale = Vector3.one * 缩放;
        }

        if (过渡计时 >= 下坐骑总时长)
        {
            收掉坐骑(true);
            if (动画 != null) 动画.设置外部御风(false, false);
            解除御风屏蔽();
            切到(骑乘状态.未骑乘);
            if (打印日志) Debug.Log("[坐骑] 下坐骑完成 → 未骑乘", this);
        }
    }

    bool 消散已放;
    /// <summary>消散实际用多久：量到动作时长就用它（片段长/速度），否则用配置值</summary>
    float 实际消散时长 => 消失动作时长 > 0.01f ? 消失动作时长 : 坐骑消散时长;
    float 下坐骑总时长 => Mathf.Max(玩家落地时长, 实际消散时长);

    void 收掉坐骑(bool 也放消散)
    {
        if (坐骑实例 == null) return;
        if (也放消散 && !消散已放)
        {
            var 中心 = 坐骑实例.transform.position + Vector3.up * 1.5f;
            NpcDissolveEffect.播放(中心, 消散半径, 消散颜色);
        }
        Destroy(坐骑实例);
        坐骑实例 = null;
        坐骑动画 = null;
        坐骑定义 = null;
       消散已放 = false;
    }

    // ============================================================ 工具

    /// <summary>按玩家当前位置摆（骑乘中/下坐骑时用）</summary>
    void 摆坐骑() => 摆坐骑(transform.position.y);

    /// <summary>
    /// 按指定的「角色高度」摆坐骑。
    ///
    /// 【为什么需要这个重载】上坐骑时玩家是从地面**升到**骑乘高度的，
    /// 而坐骑必须**从头到尾待在最终高度不动** —— 因为 Birth 动画自己就会
    /// 从下方升上来（实测 Birth 首帧比末帧低 2.27 本地单位 ≈ 8.8m）。
    /// 如果这里用玩家「当前」高度，坐骑会跟着在地面待一整段出生动画，
    /// 然后进待机时「啪」地跳上 5.64m ✗（用户 2026-09-23 报的"Birth 最后位置没对齐"就是这个）
    /// </summary>
    void 摆坐骑(float 角色Y)
    {
        if (坐骑实例 == null) return;
        var t = 坐骑实例.transform;

        // ---- 这只坐骑**自己那条**摆位（★ 每只各一条，没有全局值可退）----
        var 位 = 取摆位();
        Vector3 偏移 = 位.偏移;
        Vector3 朝向 = 位.朝向;
        float 缩放 = 实用缩放();

        // ---- 位置：始终挂在玩家根上，用玩家朝向算偏移 ----
        // 【为什么位置用玩家朝向、不用坐骑朝向】玩家必须始终坐在坐骑背上。
        // 如果偏移跟着坐骑自身的 yaw 走，坐骑一转向玩家就会从背上滑到侧面去 ✗
        var 基准 = transform.position;
        基准.y = 角色Y;
        t.position = 基准 + transform.rotation * 偏移;

        // ---- 朝向 ----
        // 两种行为：
        //   · 普通坐骑（§34.8）：行进中且有锁定 → 朝移动方向；否则跟玩家
        //   · **长在身上的**（翅膀那种）→ **时刻跟玩家**，不参与上面那套
        //     （翅膀是长在背上的，不可能自己转头；用户 2026-09-23 明确要求）
        if (目标管理器 == null) 目标管理器 = GetComponent<NpcTargeting>();

        // 换了一只坐骑就重新取一次初始朝向
        if (上次摆位实例 != 坐骑实例) { 上次摆位实例 = 坐骑实例; 坐骑Yaw已初始化 = false; }

        float 玩家Yaw = transform.eulerAngles.y;
        if (!坐骑Yaw已初始化) { 当前坐骑Yaw = 玩家Yaw; 坐骑Yaw已初始化 = true; }

        float 目标Yaw = 玩家Yaw;
        if (!朝向始终跟玩家)
        {
            bool 有锁定 = 目标管理器 != null && 目标管理器.LockedNpc != null;
            var 移动方向 = 控制器 != null ? 控制器.MoveDirection : Vector3.zero;
            移动方向.y = 0f;
            if (行进中 && 有锁定 && 移动方向.sqrMagnitude > 0.0001f)
                目标Yaw = Quaternion.LookRotation(移动方向.normalized, Vector3.up).eulerAngles.y;
        }

        // 「长在身上」的坐骑（翅膀）**不插值** —— 直接卡死在玩家朝向上。
        // 用户 2026-09-23：「应该是紧密的卡死在角色的后方的，而不是现在这样平滑的插值变化」
        if (朝向始终跟玩家)
        {
            当前坐骑Yaw = 玩家Yaw;
        }
        else
        {
            当前坐骑Yaw = 坐骑转向速度 <= 0f
                ? 目标Yaw
                : Mathf.MoveTowardsAngle(当前坐骑Yaw, 目标Yaw, 坐骑转向速度 * Time.deltaTime);
        }

        t.rotation = Quaternion.Euler(0f, 当前坐骑Yaw, 0f) * Quaternion.Euler(朝向);

        if (坐骑实例.transform.localScale.x <= 0.0001f)   // 被消散缩到 0 后别复活
            坐骑实例.transform.localScale = Vector3.one * 缩放;
    }

    GameObject 上次摆位实例;

    /// <summary>兜底摆位。**只有摆位表里漏配时才会用到** —— 值时一定是错的，好让人立刻发现。</summary>
    static readonly 坐骑摆位 兜底摆位 = new 坐骑摆位
    {
        坐骑id = "(兜底)",
        偏移 = Vector3.zero,
        朝向 = Vector3.zero,
        缩放 = 1f,
    };

    string 已报缺失的坐骑id;

    /// <summary>取这只坐骑自己那条摆位。★ 每只坐骑都必须有一条，没有全局值可退。</summary>
    坐骑摆位 取摆位()
    {
        string id = 坐骑定义 != null ? 坐骑定义.坐骑id : null;
        if (!string.IsNullOrEmpty(id) && 坐骑摆位表 != null)
            foreach (var b in 坐骑摆位表)
                if (b != null && b.坐骑id == id) return b;

        // 摆坐骑() 每帧都会调到这里，所以只报一次，别刷屏
        if (已报缺失的坐骑id != id)
        {
            已报缺失的坐骑id = id;
            Debug.LogWarning("[坐骑] 坐骑摆位表 里没有「"
                + (坐骑定义 != null ? 坐骑定义.坐骑名称 + "（" + id + "）" : "未知坐骑")
                + "」这一条，先用兜底摆位（偏移 0 / 缩放 1）—— 摆出来一定是错的，去 Inspector 补一条", this);
        }
        return 兜底摆位;
    }

    /// <summary>是不是「长在身上、朝向必须时刻跟玩家」的坐骑（翅膀）</summary>
    bool 朝向始终跟玩家 => 坐骑定义 != null && 朝向始终跟玩家的坐骑 != null
        && System.Array.IndexOf(朝向始终跟玩家的坐骑, 坐骑定义.坐骑id) >= 0;

    void 锁住御风()
    {
        if (御风 != null) 御风.禁止切换 = true;
    }

    void 解除御风屏蔽()
    {
        if (御风 != null) 御风.禁止切换 = false;
    }

    void 切到(骑乘状态 s)
    {
        if (状态 == s) return;
        状态 = s;
        状态变化?.Invoke(s);
    }

    /// <summary>外部强制下坐骑（死亡 / 传送 / 打开面板时用）</summary>
    public void 强制下坐骑()
    {
        if (状态 == 骑乘状态.未骑乘) return;
        收掉坐骑(false);
        if (动画 != null) 动画.设置外部御风(false, false);
        解除御风屏蔽();
        切到(骑乘状态.未骑乘);
    }

    /// <summary>上坐骑（按键之外的入口：UI 按钮 / 剧情 / 自动化测试都可以调）</summary>
    public void 上坐骑()
    {
        if (状态 == 骑乘状态.未骑乘) 尝试上坐骑();
    }

    /// <summary>下坐骑（按键之外的入口：UI 按钮 / 剧情 / 自动化测试都可以调）</summary>
    public void 下坐骑()
    {
        if (已骑稳 && 允许下坐骑) 开始下坐骑();
    }

    // ---- ASCII 别名 ----
    public 骑乘状态 State => 状态;
    public bool Riding => 骑乘中;
    public bool Settled => 已骑稳;
    public bool InTransition => 过渡中;
    public GameObject MountInstance => 坐骑实例;
    public MountDefinition MountDef => 坐骑定义;
}
