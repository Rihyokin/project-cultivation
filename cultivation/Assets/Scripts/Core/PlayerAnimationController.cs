using UnityEngine;

/// <summary>
/// 角色动画控制。
/// 每帧把 PlayerController 的实时水平速度喂给 Animator 的 Speed 参数，
/// 由 AnimatorController 里的 1D Blend Tree 自动在 idle / walk / run 之间过渡。
/// 本脚本只负责"喂参数"，不含任何位移逻辑。
/// </summary>
public class PlayerAnimationController : MonoBehaviour
{
    [Header("引用（留空自动查找）")]
    [Tooltip("速度来源。留空则在自己身上找 PlayerController")]
    public PlayerController playerController;

    [Tooltip("要驱动的 Animator。留空则在子物体里找（通常挂在模型根节点上）")]
    public Animator animator;

    [Header("参数")]
    [Tooltip("Blend Tree 使用的 float 参数名，需与 AnimatorController 里一致")]
    public string speedParameter = "Speed";

    [Tooltip("速度参数的平滑时间（秒）。0 = 不平滑直接喂原始速度")]
    public float speedSmoothTime = 0.08f;

    [Tooltip("动画整体播放速度倍率。脚步打滑时可微调这个值")]
    public float playbackScale = 1f;

    [Header("凭虚御风")]
    [Tooltip("御风状态来源。留空则自动在本体找 YufengFlight")]
    public YufengFlight 御风;

    [Tooltip("御风流程中的 bool 参数名")]
    public string flyingParameter = "Flying";

    [Tooltip("御风移动中的 bool 参数名")]
    public string flyMovingParameter = "FlyMoving";

    [Tooltip("升空 / 落地的 Trigger 参数名")]
    public string takeOffTrigger = "TakeOff";
    public string landTrigger = "Land";

    int flyingHash, flyMovingHash, takeOffHash, landHash;

    [Header("技能动作（普攻 / 施法 共用这一套）")]
    [Tooltip("控制器里那个「动作」状态的触发参数名（PlayerLocomotion 里叫 Action）")]
    public string 动作参数 = "Action";

    [Tooltip("控制器里「动作」状态的**占位片段**。运行时靠它做索引，把状态里的片段换成想播的。\n" +
             "留空会自动按名字找 普攻_远程_01")]
    public AnimationClip 占位动作片段;

    [Tooltip("动作状态在控制器里的名字")]
    public string 动作状态名 = "Action";

    [Header("动作期间的贴地补偿（治「忽然跳一下」和「陷进地里」）")]
    [Tooltip("外部动画（重定向过来的）整体高度和玩家骨架差很多 —— 一播动作人就被抬起来或者陷进地里。\n" +
             "开启后：播动作期间**量「整个人最低的那一点（蒙皮网格最低顶点）」偏离了动作开始前多少，\n" +
             "就把模型根反向平移多少**，让最低点始终踩在同一个地面高度上。\n" +
             "御风流程中会自动跳过（那套高度归 YufengFlight 管）。")]
    public bool 动作锁髋骨高度 = true;

    [Tooltip("量「人陷进去 / 悬空多少」用的蒙皮网格。留空会自动收集 Animator 下面全部 SkinnedMeshRenderer")]
    public SkinnedMeshRenderer[] 取样网格;

    [Tooltip("【判据骨骼】贴地判断只统计**主要受这些骨骼驱动**的顶点（脚底 / 脚趾 / 小腿）。\n" +
             "必须这么限：人物身上挂着**垂到脚边的披风、佩剑**，它们的顶点才是模型的全局最低点。\n" +
             "拿「全局最低顶点」当判据会去钉住剑尖，结果脚离地、整个人悬空 ✗\n" +
             "（实测：最低点恒在 Y=−0.64、离左脚 0.9 米远，就是垂饰）")]
    public HumanBodyBones[] 判据骨骼 =
    {
        HumanBodyBones.LeftToes, HumanBodyBones.RightToes,
        HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot,
        HumanBodyBones.LeftLowerLeg, HumanBodyBones.RightLowerLeg,
    };

    [Tooltip("【退路骨骼】一个蒙皮网格都拿不到时才用脚骨判断")]
    public HumanBodyBones 左脚 = HumanBodyBones.LeftFoot;
    public HumanBodyBones 右脚 = HumanBodyBones.RightFoot;
    public HumanBodyBones 髋骨 = HumanBodyBones.Hips;

    [Tooltip("补偿量的绝对值上限（米）。防止动画异常时把人整个埋进地里。\n" +
             "普攻_远程_01 那个重定向动画整体比玩家高 1.75 米，所以上限不能小于 2")]
    public float 补偿上限 = 2.5f;

    [Tooltip("补偿取向 0~1：\n" +
             "1 = 盯「脚底网格顶点」→ 既不陷地也不悬空（推荐，默认）；\n" +
             "0 = 盯「髋骨」→ 身体不窜高，但脚会被压进地里")]
    [Range(0f, 1f)]
    public float 补偿权重 = 1f;

    [Tooltip("动画把整个人**抬离地面**时（差 > 0），要不要也把人拽回地面？\n" +
             "− 关（默认）：只治「陷进地里」，悬空的动画原样保留。\n" +
             "− 开：连悬空也拉平 —— 但**普攻_远程_01 是拿嫦娥的浮空施法动作重定向来的**，\n" +
             "  它整段都悬在约 1 米高、腿是收起来的，硬拉回地面会变成「蹲/坐在地上」✗\n" +
             "  只有换成真正站在地上的动作（比如 WuNiang@Attack1）之后才建议打开。")]
    public bool 允许下拉 = false;

    [Tooltip("补偿基准往上找几层。0 = Animator 所在物件（推荐）")]
    public int 补偿根层数 = 0;

    Transform 髋骨节点;
    Transform 左脚节点;
    Transform 右脚节点;
    Transform 补偿根;
    Vector3 补偿根基准;
    float 已施加偏移Y;
    float 动作前髋骨世界Y;
    float 动作前参照Y;
    bool 髋骨已记录;
    float 待用基准参照Y;
    bool 有待用基准;
    bool 网格已收集;
    Mesh 烘焙网格;
    System.Collections.Generic.List<int>[] 候选索引;
    readonly System.Collections.Generic.List<Vector3> 顶点缓存 = new System.Collections.Generic.List<Vector3>(8192);

    int 动作参数Hash;
    AnimatorOverrideController 覆盖控制器;
    bool 动作已进入;
    float 动作开始时间;

    /// <summary>正在播技能动作（普攻 / 施法）</summary>
    public bool 动作播放中 { get; private set; }

    /// <summary>技能动作播完了（普攻靠它对齐"何时出弹"、施法靠它知道何时结算）</summary>
    public event System.Action 动作播完;

    /// <summary>
    /// 技能动作的速度倍率。**攻速会乘进来** ——
    /// 策划要求：攻速 2 时动画 1s → 0.5s，就是这个值。没在播动作时恒为 1。
    /// </summary>
    public float 动作速度倍率 { get; private set; } = 1f;

    /// <summary>当前正在播的技能动作片段</summary>
    public AnimationClip 当前动作 { get; private set; }

    /// <summary>当前喂给 Animator 的速度值，供调试/UI 读取</summary>
    public float CurrentAnimSpeed { get; private set; }

    int speedHash;
    float smoothSpeed;
    float speedVelocity;

    void Awake()
    {
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (playerController == null) playerController = GetComponent<PlayerController>();
        if (playerController == null) playerController = GetComponentInParent<PlayerController>();

        if (御风 == null) 御风 = GetComponent<YufengFlight>();
        if (御风 != null) 御风.状态变化 += OnFlightStateChanged;

        speedHash = Animator.StringToHash(speedParameter);
        flyingHash = Animator.StringToHash(flyingParameter);
        flyMovingHash = Animator.StringToHash(flyMovingParameter);
        takeOffHash = Animator.StringToHash(takeOffTrigger);
        landHash = Animator.StringToHash(landTrigger);

        if (animator != null)
        {
            // 位移由 CharacterController 负责，动画不要再叠加根运动
            animator.applyRootMotion = false;
        }

        动作参数Hash = Animator.StringToHash(动作参数);
        找占位片段();
    }

    void OnDestroy()
    {
        if (御风 != null) 御风.状态变化 -= OnFlightStateChanged;
        if (烘焙网格 != null) Destroy(烘焙网格);
    }

    /// <summary>御风状态变化时打对应的 Trigger，让状态机精确切换</summary>
    void OnFlightStateChanged(YufengFlight.FlightState s)
    {
        if (animator == null) return;
        if (s == YufengFlight.FlightState.升空) animator.SetTrigger(takeOffHash);
        else if (s == YufengFlight.FlightState.落地) animator.SetTrigger(landHash);
    }

    public bool PlayAction(AnimationClip clip, float speedScale = 1f) => 播动作(clip, speedScale);
    public void StopAction() => 停止动作();
    public bool IsActionPlaying => 动作播放中;
    public float ActionProgress => 动作进度;

    // ============================================================ 技能动作（普攻 / 施法共用）

    void 找占位片段()
    {
        if (占位动作片段 != null || animator == null) return;
        var 基 = animator.runtimeAnimatorController;
        if (基 == null) return;
        foreach (var c in 基.animationClips)
            if (c != null && c.name == "普攻_远程_01") { 占位动作片段 = c; return; }
        if (基.animationClips.Length > 0) 占位动作片段 = 基.animationClips[0];
    }

    /// <summary>
    /// **播一段技能动作。普攻和施法共用这一个入口。**
    ///
    /// 做法：控制器里有个 `Action` 状态（AnyState + Trigger 进入、播完自动回 Locomotion），
    /// 这里用 AnimatorOverrideController 把状态里的**占位片段换成传进来的片段** ——
    /// 所以**任意片段都能播，不用给每个技能往控制器里加状态**。
    ///
    /// 焚天炎术以后就在技能开始时调 `播动作(技能动作2)` 即可。
    /// </summary>
    /// <param name="片段">要播的动作（人形片段，会自动重定向到玩家骨架）</param>
    /// <param name="速度倍率">播放速度倍数。**攻速传进来**，1 = 原速</param>
    public bool 播动作(AnimationClip 片段, float 速度倍率 = 1f)
    {
        if (animator == null || 片段 == null)
        {
            Debug.LogWarning("[动画] 播动作失败：片段为空或没有 Animator", this);
            return false;
        }
        var 基 = animator.runtimeAnimatorController;
        if (基 == null) { Debug.LogWarning("[动画] 播动作失败：没有 AnimatorController", this); return false; }

        找占位片段();
        if (占位动作片段 == null)
        {
            Debug.LogWarning("[动画] 找不到占位动作片段，没法播技能动作", this);
            return false;
        }

        if (覆盖控制器 == null || 覆盖控制器.runtimeAnimatorController != 基)
            覆盖控制器 = new AnimatorOverrideController(基);

        覆盖控制器[占位动作片段] = 片段;
        animator.runtimeAnimatorController = 覆盖控制器;

        当前动作 = 片段;
        动作速度倍率 = Mathf.Max(0.05f, 速度倍率);
        动作播放中 = true;
        动作已进入 = false;
        动作开始时间 = Time.time;
        animator.speed = Mathf.Max(0.01f, playbackScale * 动作速度倍率);
        记录髋骨高度();
        animator.SetTrigger(动作参数Hash);
        return true;
    }

    /// <summary>打断当前技能动作（比如施法被打断）</summary>
    public void 停止动作()
    {
        if (animator != null) animator.ResetTrigger(动作参数Hash);
        动作播放中 = false;
        动作已进入 = false;
        当前动作 = null;
        动作速度倍率 = 1f;
    }

    /// <summary>
    /// 判断动作播完。
    /// 不能只看"当前不是 Action" —— 刚 SetTrigger 时过渡还没走完，会有一两帧仍在 Locomotion，
    /// 所以要先确认**进过** Action，再看它离开。
    /// </summary>
    void 维护技能动作()
    {
        if (!动作播放中) return;

        var 状态 = animator.GetCurrentAnimatorStateInfo(0);
        if (状态.IsName(动作状态名)) { 动作已进入 = true; return; }

        if (!动作已进入)
        {
            if (Time.time - 动作开始时间 > 1.5f)
            {
                Debug.LogWarning("[动画] 动作状态「" + 动作状态名 + "」一直没进入 —— 检查控制器里有没有这个状态、以及触发参数「" + 动作参数 + "」", this);
                停止动作();
            }
            return;
        }

        动作播放中 = false;
        动作速度倍率 = 1f;
        当前动作 = null;
        动作播完?.Invoke();
    }

    /// <summary>当前动作播到几成（0~1）。用来对齐"动画 40% 时出弹"这类节点</summary>
    public float 动作进度
    {
        get
        {
            if (!动作播放中 || animator == null) return 0f;
            var 状态 = animator.GetCurrentAnimatorStateInfo(0);
            if (!状态.IsName(动作状态名)) return 动作已进入 ? 1f : 0f;
            return Mathf.Clamp01(状态.normalizedTime);
        }
    }

    /// <summary>
    /// **判据高度 = 两只脚（含脚趾 / 小腿）的蒙皮顶点里最低的那个 Y**。
    ///
    /// 【为什么不是全部顶点】模型身上挂着垂到脚边的披风 / 佩剑，
    /// 它们的顶点才是全局最低点；钉住它们 = 脚离地、人整个悬空（实测最低点恒在 Y=−0.64、
    /// 离左脚 0.9 米远，明显是垂饰而不是脚）。所以先用**骨骼权重**把"归脚管的顶点"筛出来。
    ///
    /// 【为什么不用骨骼本身】脚骨只是脚踝，会随脚踝转动失真 ——
    /// 焚天炎术施法：最低脚底顶点 −0.274 / 最低脚骨 −0.064 / 最低趾骨 −0.080。
    /// 真正会穿进地面的是网格，所以判据还是网格顶点，只是限定在脚那一撮。
    /// </summary>
    float 取参照Y()
    {
        if (取样网格 != null && 取样网格.Length > 0)
        {
            if (烘焙网格 == null) 烘焙网格 = new Mesh { name = "动作贴地采样" };

            float 最低 = float.MaxValue;
            bool 有 = false;
            for (int i = 0; i < 取样网格.Length; i++)
            {
                var s = 取样网格[i];
                if (s == null || !s.enabled || !s.gameObject.activeInHierarchy) continue;

                s.BakeMesh(烘焙网格);
                烘焙网格.GetVertices(顶点缓存);          // 用 List 重载，避免每帧 GC
                var 矩阵 = s.transform.localToWorldMatrix;

                var 索引 = 候选索引 != null && i < 候选索引.Length ? 候选索引[i] : null;
                if (索引 != null && 索引.Count > 0)
                {
                    for (int k = 0; k < 索引.Count; k++)
                    {
                        int v = 索引[k];
                        if (v < 0 || v >= 顶点缓存.Count) continue;
                        float y = 矩阵.MultiplyPoint3x4(顶点缓存[v]).y;
                        if (y < 最低) 最低 = y;
                    }
                }
                else
                {
                    for (int v = 0; v < 顶点缓存.Count; v++)
                    {
                        float y = 矩阵.MultiplyPoint3x4(顶点缓存[v]).y;
                        if (y < 最低) 最低 = y;
                    }
                }
                有 = true;
            }
            if (有 && 最低 < float.MaxValue) return 最低;
        }

        // 退路：一个蒙皮网格都没有，只能拿脚骨凑合
        if (左脚节点 != null && 右脚节点 != null)
            return Mathf.Min(左脚节点.position.y, 右脚节点.position.y);
        if (左脚节点 != null) return 左脚节点.position.y;
        if (右脚节点 != null) return 右脚节点.position.y;
        return 髋骨节点 != null ? 髋骨节点.position.y : transform.position.y;
    }

    /// <summary>从蒙皮网格里挑出"主要归脚 / 脚趾 / 小腿管"的顶点序号</summary>
    System.Collections.Generic.List<int> 收集脚部顶点(SkinnedMeshRenderer s)
    {
        var 结果 = new System.Collections.Generic.List<int>(1024);
        if (s == null || animator == null) return 结果;

        var mesh = s.sharedMesh;
        var 骨 = s.bones;
        if (mesh == null || 骨 == null || 骨.Length == 0) return 结果;

        var 权重 = mesh.boneWeights;
        if (权重 == null || 权重.Length == 0) return 结果;      // 空 = 退化成"全部顶点"

        var 判据 = new System.Collections.Generic.HashSet<int>();
        var 表 = (判据骨骼 != null && 判据骨骼.Length > 0) ? 判据骨骼 : null;
        if (表 != null)
        {
            foreach (var b in 表)
            {
                var tr = animator.GetBoneTransform(b);
                if (tr == null) continue;
                for (int i = 0; i < 骨.Length; i++)
                    if (骨[i] == tr) { 判据.Add(i); break; }
            }
        }
        if (判据.Count == 0) return 结果;

        for (int v = 0; v < 权重.Length; v++)
        {
            var w = 权重[v];
            int 主 = 0;
            float 最大 = -1f;
            if (w.weight0 > 最大) { 最大 = w.weight0; 主 = 0; }
            if (w.weight1 > 最大) { 最大 = w.weight1; 主 = 1; }
            if (w.weight2 > 最大) { 最大 = w.weight2; 主 = 2; }
            if (w.weight3 > 最大) { 最大 = w.weight3; 主 = 3; }

            int 骨号 = 主 == 0 ? w.boneIndex0 : 主 == 1 ? w.boneIndex1 : 主 == 2 ? w.boneIndex2 : w.boneIndex3;
            if (判据.Contains(骨号)) 结果.Add(v);
        }
        return 结果;
    }

    /// <summary>动作开始前记下髋骨的世界 Y、整个人最低点的世界 Y，以及"补偿根"的基准位置</summary>
    void 记录髋骨高度()
    {
        if (!动作锁髋骨高度 || animator == null) return;

        // 【坑·已修】以前只在"两只脚都拿不到"时才会给 髋骨节点 赋值，
        // 于是正常情况（脚拿得到）髋骨节点恒为 null → LateUpdate 开头就 return，
        // 补偿**一次都没跑过**（实测 已施加偏移Y 全程恒为 0）。三个引用都要各自独立兜底。
        if (!网格已收集)
        {
            if (取样网格 == null || 取样网格.Length == 0)
                取样网格 = animator.GetComponentsInChildren<SkinnedMeshRenderer>(true);

            候选索引 = new System.Collections.Generic.List<int>[取样网格 != null ? 取样网格.Length : 0];
            int 合计 = 0;
            for (int i = 0; i < 候选索引.Length; i++)
            {
                候选索引[i] = 收集脚部顶点(取样网格[i]);
                合计 += 候选索引[i].Count;
            }
            网格已收集 = true;

            if (合计 == 0 && 取样网格 != null && 取样网格.Length > 0)
                Debug.LogWarning("[动画] 没能按骨骼权重筛出「脚部顶点」，贴地判据会退化成整网格最低点 ——" +
                                 "身上披风 / 佩剑垂到脚边时人会悬空。检查 判据骨骼 配置。", this);
            else if (取样网格 != null && 取样网格.Length > 0)
                Debug.Log("[动画] 贴地判据 = " + 合计 + " 个脚部顶点 / " + 取样网格.Length + " 个蒙皮网格", this);
        }
        if (左脚节点 == null) 左脚节点 = animator.GetBoneTransform(左脚);
        if (右脚节点 == null) 右脚节点 = animator.GetBoneTransform(右脚);
        if (髋骨节点 == null) 髋骨节点 = animator.GetBoneTransform(髋骨);

        bool 有网格 = 取样网格 != null && 取样网格.Length > 0;
        if (!有网格 && 髋骨节点 == null && 左脚节点 == null && 右脚节点 == null) return;

        // 【坑·已修】这里以前是 `补偿根 = 髋骨节点`（髋骨自己！），于是每帧既在动髋骨、
        // 动画又在写髋骨 → 两边打架 → 数值发散（实测 Hips Y 起伏炸到 127 米）。
        // 补偿根必须是**动画不写的那个物件** —— 默认取 Animator 所在物件（= Player_Visual）。
        补偿根 = animator.transform;
        for (int i = 0; i < Mathf.Max(0, 补偿根层数) && 补偿根.parent != null; i++) 补偿根 = 补偿根.parent;
        if (补偿根 == null) 补偿根 = animator.transform;

        // 上一次留下的偏移先还回去，基准才是干净的
        if (Mathf.Abs(已施加偏移Y) > 1e-5f)
        {
            补偿根.position -= Vector3.up * 已施加偏移Y;
            已施加偏移Y = 0f;
        }

        补偿根基准 = 补偿根.position;
        动作前髋骨世界Y = 髋骨节点 != null ? 髋骨节点.position.y : 取参照Y();
        动作前参照Y = 有待用基准 ? 待用基准参照Y : 取参照Y();
        髋骨已记录 = true;
    }

    /// <summary>
    /// 动作期间把整个人按回原来的地面高度。
    /// LateUpdate 跑在 Animator 之后，所以这里是"动画算完我再压回去"。
    /// </summary>
    void LateUpdate()
    {
        if (!动作锁髋骨高度) return;

        维护贴地补偿();
        记录空闲基准();
    }

    /// <summary>
    /// 【坑·已修】基准必须在**上一次"空闲帧"**就量好，不能等到 播动作 里再量 ——
    /// 播动作 会 `animator.runtimeAnimatorController = 覆盖控制器`，这会让 Animator 重绑、
    /// 姿态被搅一下，那一刻量到的脚底高度比真正站着时高 0.166 米，
    /// 于是整套补偿都把人物托高 0.166 米（实测：残差恒定 +0.166，两个动作都一样）。
    /// 现在改成：**只要没在播动作、也没在御风，每帧把脚底高度记下来**，
    /// 播动作 时直接取上一帧空闲时的那个值 —— 与调用时机无关。
    /// 必须放在 <see cref="维护贴地补偿"/> **之后**：那一帧如果刚收招，
    /// 偏移是在里面还回去的，先采样就会把"还带着偏移"的高度当成地面。
    /// </summary>
    void 记录空闲基准()
    {
        if (动作播放中) return;
        if (御风 != null && 御风.御风流程中) return;

        待用基准参照Y = 取参照Y();
        有待用基准 = true;
    }

    void 维护贴地补偿()
    {
        if (!髋骨已记录) return;
        if (补偿根 == null) return;

        // 【坑·已修】御风期间**不做这个补偿**。
        // 补偿的用意是"把重定向过来的动作拉回原来的站立高度"，
        // 但御风升空 / 落地时 YufengFlight 正在**故意改变高度**（0.85 秒过渡），
        // 补偿会把这段高度冻住、和飞行系统打架 —— 表现就是"在升空/落地时放普攻，
        // 高度会错乱一下" ✗
        if (御风 != null && 御风.御风流程中)
        {
            if (髋骨已记录 && Mathf.Abs(已施加偏移Y) > 1e-5f)
                补偿根.position -= Vector3.up * 已施加偏移Y;   // 把加过的还回去
            已施加偏移Y = 0f;
            髋骨已记录 = false;
            return;
        }

        if (!动作播放中)
        {
            髋骨已记录 = false;
            // 动作结束：只把**我加过的那部分**减掉，不覆盖别人的位置
            if (Mathf.Abs(已施加偏移Y) > 1e-5f) 补偿根.position -= Vector3.up * 已施加偏移Y;
            已施加偏移Y = 0f;
            return;
        }

        // 【为什么这么算】直接锁 Hips 的本地 Y 只治了 19%（实测），因为位移主要来自
        // 父级链或骨骼旋转。所以改成：**量"整个人最低点"的世界 Y 偏离了多少，就把整个模型根反向平移多少** ——
        // 这样不管位移从哪来都能抵消。
        //
        // 减掉 已施加偏移Y 是为了避免自我反馈：上帧我加过的偏移不算在"动画本身"的位移里。
        // 【两个偏差按权重混合】
        //   差_参照：网格最低点偏离了多少（推荐，权重 1）
        //   差_髋  ：髋骨偏离了多少（权重 0，只保证身体不窜高，脚会进地里）
        float 已施加 = 已施加偏移Y;
        float 髋现在 = 髋骨节点 != null ? 髋骨节点.position.y : 取参照Y();
        float 差髋 = (髋现在 - 已施加) - 动作前髋骨世界Y;
        float 差脚 = (取参照Y() - 已施加) - 动作前参照Y;
        float w = Mathf.Clamp01(补偿权重);
        float 差 = Mathf.Clamp(差髋 * (1f - w) + 差脚 * w, -补偿上限, 补偿上限);

        // 【只往上顶，不往下拉】差 > 0 = 判据比基准**高**了 = 动画把人抬离地面。
        // 默认不管这种：普攻_远程_01 整段悬空约 1 米，硬拉回来会变成蹲姿（实测髋骨 −0.84 米）。
        // 治「陷进地里」只需要差 < 0 的那一半。
        if (差 > 0f && !允许下拉) 差 = 0f;

        float 需要 = -差;

        // 【坑·已修】以前是**绝对赋值** 补偿根.localPosition = 补偿根基准 + up*偏移 ——
        // 那会把**别人**对位置的改动一起抹掉 ✗
        // 御风升空/落地时 YufengFlight 会改 Player_Visual 的本地位置，
        // 结果在飞行动作里放普攻，高度就"错乱一下" ✗
        // 现在只施加**增量**（需要 - 已施加），别人的改动原样保留 ✓
        //
        // 【坑·已修】增量必须加在**世界坐标**上：判据（取参照Y）量的是世界 Y，
        // 如果加在 localPosition 上、而补偿根有缩放（Player_Visual 就不是 1），
        // 那么加 1 米本地位移换来的世界位移不是 1 米 —— 收敛点虽然还是对的，
        // 但每帧只走一部分，快速动作（普攻 0.2 秒抬 1.75 米）就会留下 0.18 米的滞后残差。
        float 增量 = 需要 - 已施加偏移Y;
        if (Mathf.Abs(增量) > 1e-5f) 补偿根.position += Vector3.up * 增量;
        已施加偏移Y = 需要;
    }

    void Update()
    {
        if (animator == null) return;
        if (animator.runtimeAnimatorController == null) return;   // 没挂控制器就不动

        float target = playerController != null ? playerController.CurrentSpeed : 0f;

        smoothSpeed = speedSmoothTime > 0f
            ? Mathf.SmoothDamp(smoothSpeed, target, ref speedVelocity, speedSmoothTime)
            : target;

        CurrentAnimSpeed = smoothSpeed;

        animator.SetFloat(speedHash, smoothSpeed);

        // ★ 攻速倍率乘进来 ——
        // 以前这里只写 playbackScale，所以外面想用 animator.speed 做"攻速加快动画"会被每帧覆盖。
        animator.speed = Mathf.Max(0.01f, playbackScale * (动作播放中 ? 动作速度倍率 : 1f));

        维护技能动作();

        // 御风参数
        if (御风 != null)
        {
            animator.SetBool(flyingHash, 御风.御风流程中);
            animator.SetBool(flyMovingHash, smoothSpeed > 0.15f);
        }
    }
}
