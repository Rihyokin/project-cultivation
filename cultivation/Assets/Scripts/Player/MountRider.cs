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
/// 偏移/缩放是**从用户摆好的场景里量出来的**，见 <see cref="坐骑相对偏移"/>。
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

    [Header("坐骑摆位（从场景里量出来的）")]
    [Tooltip("坐骑根相对角色根的本地偏移。\n" +
             "来源：用户 2026-09-23 在 Sect 场景摆好的位置 —— 坐骑网格中心比玩家网格中心低 2.549m、\n" +
             "水平几乎重合。y 用 -4.85 是实测微调值：场景里量的是 -5.64，但运行时玩家网格中心\n" +
             "比场景里高 0.79m（因为摆位时挪动过 Player_Visual），补偿后才是 2.549 的目标差")]
    public Vector3 坐骑相对偏移 = new Vector3(0.29f, -4.85f, 0.24f);

    [Tooltip("坐骑缩放。场景里摆的是 3.8762")]
    public float 坐骑缩放 = 3.8762f;

    [Tooltip("坐骑相对角色的额外旋转（欧拉角）")]
    public Vector3 坐骑朝向 = Vector3.zero;

    [Header("坐骑朝向（锁定 + 移动时）")]
    [Tooltip("锁定管理器。留空则自动在本体找 NpcTargeting。\n" +
             "用来判断「有没有锁定」—— 有锁定且行进中时，坐骑朝移动方向、玩家继续正面锁敌")]
    public NpcTargeting 目标管理器;

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
        坐骑实例.transform.localScale = Vector3.one * 坐骑缩放;

        // ★★ 必须关掉坐骑身上的碰撞体 ★★
        // 坐骑 prefab 根上带一个 CapsuleCollider，而骑手是 CharacterController ——
        // 两者套在一起会互相顶，实测**下坐骑时人被弹到 33 米高**（速度远超高度控制的 ±10m/s）。
        // 坐骑本来也不该挡住自己的骑手。
        foreach (var c in 坐骑实例.GetComponentsInChildren<Collider>(true)) c.enabled = false;

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

    /// <summary>播坐骑动作。NpcAnimator 没就绪时返回 false，调用方可以重试</summary>
    bool 播坐骑动作(string 名, bool 循环)
    {
        if (坐骑动画 == null) return false;
        return 坐骑动画.PlayAction(名, 循环);
    }

    void 更新上坐骑()
    {
        过渡计时 += Time.deltaTime;

        // 出生动作等到 NpcAnimator 就绪再播（Instantiate 当帧它是没就绪的）
        if (!出生已开播)
            出生已开播 = 播坐骑动作("Birth", false);

        // 出生期间坐骑**固定在最终骑乘高度**（跟着玩家升会和自己动画的上升叠成两倍）
        摆坐骑(起始高度 + 骑乘高度);

        // 玩家：0 → 升空时长 之间升到骑乘高度；升空动画放完就转御风_Idle
        float k = 玩家升空时长 > 0.001f ? Mathf.Clamp01(过渡计时 / 玩家升空时长) : 1f;
        角色目标高度 = 起始高度 + 骑乘高度 * 升空曲线.Evaluate(k);

        if (动画 != null && 过渡计时 >= 玩家升空时长)
            动画.设置外部御风(true, false);   // 已经切到 御风_Idle（Flying=1, FlyMoving=0）

        if (过渡计时 >= 上坐骑总时长)
        {
            if (坐骑动画 != null) 坐骑动画.PlayAction("Idle", true);
            切到(骑乘状态.坐骑待机);
            if (打印日志) Debug.Log("[坐骑] 上坐骑完成 → 坐骑待机", this);
        }
    }

    float 上坐骑总时长 => Mathf.Max(玩家升空时长, 坐骑出生时长);

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
            if (坐骑动画 != null) 坐骑动画.PlayAction(该行进 ? "FightRun" : "Idle", true);
            if (动画 != null) 动画.设置外部御风(true, 该行进);
            切到(该行进 ? 骑乘状态.坐骑行进中 : 骑乘状态.坐骑待机);
        }
        摆坐骑();
    }

    // ============================================================ 下坐骑

    void 开始下坐骑()
    {
        过渡计时 = 0f;
        起始高度 = 角色目标高度 - 骑乘高度;
        落地触发时刻 = Mathf.Max(0f, 下坐骑总时长 - 玩家落地时长);
        落地已触发 = false;

        if (坐骑动画 != null) 坐骑动画.PlayAction("LeveUp", false);
        if (动画 != null) 动画.设置外部御风(true, false);

        切到(骑乘状态.下坐骑中);
        if (打印日志) Debug.Log("[坐骑] 下坐骑：消散 " + 坐骑消散时长 + "s ／ 玩家落地 "
            + 玩家落地时长 + "s（落地在 " + 落地触发时刻.ToString("F2") + "s 触发，同时收尾）", this);
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
        if (坐骑实例 != null && 坐骑消散时长 > 0.001f)
        {
            float 进度 = Mathf.Clamp01(过渡计时 / 坐骑消散时长);
            if (过渡计时 >= 坐骑消散时长 * 0.35f && !消散已放)
            {
                消散已放 = true;
                var 中心 = 坐骑实例.transform.position + Vector3.up * 1.5f;
                NpcDissolveEffect.播放(中心, 消散半径, 消散颜色);
            }
            // 后 60% 缩到 0，做出"消散"的收尾
            float 缩放 = 坐骑缩放 * (1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((进度 - 0.4f) / 0.6f)));
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
    float 下坐骑总时长 => Mathf.Max(玩家落地时长, 坐骑消散时长);

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

        // ---- 位置：始终挂在玩家根上，用玩家朝向算偏移 ----
        // 【为什么位置用玩家朝向、不用坐骑朝向】玩家必须始终坐在坐骑背上。
        // 如果偏移跟着坐骑自身的 yaw 走，坐骑一转向玩家就会从背上滑到侧面去 ✗
        var 基准 = transform.position;
        基准.y = 角色Y;
        t.position = 基准 + transform.rotation * 坐骑相对偏移;

        // ---- 朝向：和玩家**分开**算 ----
        // 用户 2026-09-23 要的行为：
        //   · 行进中 **且玩家有锁定** → 坐骑朝**移动方向**（玩家自己继续正面锁敌）
        //   · 其他情况（没锁定 / 停下来）→ 坐骑跟**玩家朝向**
        // 因为是 MoveTowardsAngle，"移动停止后坐骑再转回玩家朝向"是自然发生的 ✓
        if (目标管理器 == null) 目标管理器 = GetComponent<NpcTargeting>();

        // 换了一只坐骑就重新取一次初始朝向
        if (上次摆位实例 != 坐骑实例) { 上次摆位实例 = 坐骑实例; 坐骑Yaw已初始化 = false; }

        float 玩家Yaw = transform.eulerAngles.y;
        if (!坐骑Yaw已初始化) { 当前坐骑Yaw = 玩家Yaw; 坐骑Yaw已初始化 = true; }

        float 目标Yaw = 玩家Yaw;
        bool 有锁定 = 目标管理器 != null && 目标管理器.LockedNpc != null;
        var 移动方向 = 控制器 != null ? 控制器.MoveDirection : Vector3.zero;
        移动方向.y = 0f;
        if (行进中 && 有锁定 && 移动方向.sqrMagnitude > 0.0001f)
            目标Yaw = Quaternion.LookRotation(移动方向.normalized, Vector3.up).eulerAngles.y;

        当前坐骑Yaw = 坐骑转向速度 <= 0f
            ? 目标Yaw
            : Mathf.MoveTowardsAngle(当前坐骑Yaw, 目标Yaw, 坐骑转向速度 * Time.deltaTime);

        t.rotation = Quaternion.Euler(0f, 当前坐骑Yaw, 0f) * Quaternion.Euler(坐骑朝向);

        if (坐骑实例.transform.localScale.x <= 0.0001f)   // 被消散缩到 0 后别复活
            坐骑实例.transform.localScale = Vector3.one * 坐骑缩放;
    }

    GameObject 上次摆位实例;

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
