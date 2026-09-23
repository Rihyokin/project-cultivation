using System;
using UnityEngine;

/// <summary>
/// 功法「太虚炼气诀」提供的普攻方法（功法表 `普攻方法id = basic_remoteattack_01`）。
///
/// ## 行为（按策划说明）
///
/// · 按攻击键 → 对**锁定单位**出手（锁定来源 <see cref="NpcTargeting.LockedNpc"/>）
/// · 播**角色攻击动作**，在 **40%** 这个节点生成特效飞弹
/// · 冷却 **3 秒**，**攻速会同步缩放动画播放时间和冷却**
///   （攻速 2 → 动画 0.6s、冷却 1.5s；策划的例子是 1s/4s → 0.5s/2s，同一个系数）
/// · 出生点：**右手骨骼末端**
/// · 飞行：**持续追踪**，直到命中 / 锁定单位超出**神识范围** / 超过 **8 秒**
/// · 命中：对锁定单位造成**特殊普通攻击**
/// · 阻挡：**无法被场景阻挡**（飞弹零物理）；只检测「拦截层」——
///   技能神通 / 法宝 / 灵阵 放那一层就能把它拦下来
///
/// ## 和其它普攻方法的区别
///
/// `basic_sword_01` 是「飞剑自己飞出去穿目标」，**玩家角色不播动画**；
/// 这个是「角色做动作 + 动作到 40% 时射出一发追踪弹」，所以它依赖
/// <see cref="PlayerAnimationController.播动作"/>（普攻/施法共用那套）。
///
/// ## 术语
///
/// 伤害目标统一叫**锁定单位**（见 <see cref="ICombatTarget"/>）——
/// 以后 NPC 也可能是召唤单位帮玩家打，所以别写死"玩家/敌人"。
/// </summary>
public class BasicRemoteAttack01 : MonoBehaviour
{
    [Header("功法接线")]
    [Tooltip("本普攻方法在功法表「普攻方法id」里的标识")]
    public string 方法id = "basic_remoteattack_01";

    [Header("攻击动作")]
    [Tooltip("出手时播的角色动作。人形片段，会自动重定向到玩家骨架")]
    public AnimationClip 普攻动作;

    [Tooltip("在动画的百分之多少处生成飞弹（0.4 = 40%）")]
    [Range(0.05f, 0.95f)]
    public float 出手进度 = 0.4f;

    [Header("冷却（会被攻速缩放）")]
    [Tooltip("基础冷却（秒）。实际冷却 = 这个 ÷ 攻速")]
    public float 基础冷却 = 3f;

    [Header("出生点 = 右手骨骼末端")]
    [Tooltip("从哪根骨骼出膛。默认右手中指末端（= 右手骨骼的最末端）")]
    public HumanBodyBones 出生骨骼 = HumanBodyBones.RightMiddleDistal;

    [Tooltip("骨骼找不到时的退路")]
    public HumanBodyBones 备用出生骨骼 = HumanBodyBones.RightHand;

    [Tooltip("沿骨骼自身坐标系再加一点偏移（让飞弹离手一点，别嵌进模型里）")]
    public Vector3 出生偏移 = new Vector3(0f, 0f, 0.15f);

    [Header("飞弹外观")]
    public string 弹道特效路径 = "QFX/ProjectilesFX/VFX_Prefabs/Projectiles_Particles/VFX_Priest_Projectile_Only";
    public string 闪光特效路径 = "QFX/ProjectilesFX/VFX_Prefabs/Flashes/VFX_Priest_Flash";
    public string 命中特效路径 = "QFX/ProjectilesFX/VFX_Prefabs/Impacts/VFX_Priest_Impact";

    [Tooltip("弹体缩放")]
    public float 弹体缩放 = 0.65f;

    [Tooltip("闪光存活（秒）")]
    public float 闪光特效存活 = 0.8f;

    [Tooltip("命中特效缩放")]
    public float 命中特效缩放 = 1.25f;

    [Tooltip("命中特效存活（秒）")]
    public float 命中特效存活 = 2f;

    [Header("飞弹飞行")]
    [Tooltip("飞行速度（米/秒）")]
    public float 飞弹速度 = 18f;

    [Tooltip("追踪转向速率（度/秒）。越小越容易被绕开")]
    public float 追踪转向速度 = 540f;

    [Tooltip("追踪最长持续多久（秒）就放弃并消散")]
    public float 最长追踪时间 = 8f;

    [Tooltip("能被哪些层拦下来（技能神通 / 法宝 / 灵阵）。0 = 谁都拦不住")]
    public LayerMask 拦截层 = 0;

    [Header("神识范围（追踪终止距离）")]
    [Tooltip("神识为 0 时的基础范围（米）")]
    public float 基础神识范围 = 4f;

    [Tooltip("每 1 点神识增加多少米")]
    public float 每点神识范围 = 0.8f;

    [Tooltip("范围上限")]
    public float 神识范围上限 = 40f;

    [Header("伤害规则")]
    [Tooltip("策划要求：命中造成**特殊普通攻击**")]
    public DamageNature 伤害属性 = DamageNature.特殊;
    public AttackKind 攻击类别 = AttackKind.普通攻击;
    public float 技能倍率 = 1f;

    [Header("按键")]
    public KeyCode 攻击键 = KeyCode.Mouse1;

    [Tooltip("**自动出手**：只要锁定了单位、冷却好了，就自己一直打，不用按键")]
    public bool 自动出手 = true;

    [Tooltip("按键也还能触发（自动出手关掉后就是纯手动）。按住 = 连发，只点一下 = 单发")]
    public bool 按住连发 = true;

    [Header("引用（留空自动找）")]
    public PlayerCombatStats 玩家战斗属性;
    public NpcTargeting 目标管理器;
    public PlayerAnimationController 动画;

    [Header("调试")]
    public bool 打印战斗日志 = true;

    // ============================================================ 对外状态

    /// <summary>**锁定单位** —— 当前要打的目标（没锁定就是 null）</summary>
    public ICombatTarget 锁定单位
    {
        get
        {
            var npc = 目标管理器 != null ? 目标管理器.LockedNpc : null;
            if (npc == null || npc.IsDead) return null;
            return 缓存 ??= new NpcTarget(npc);
        }
    }

    /// <summary>缓存适配器，避免每次攻击都 new（锁定的目标没变就复用）</summary>
    NpcTarget 缓存;
    int 缓存目标id;

    /// <summary>攻速系数（来自玩家属性汇总）。1 = 标准</summary>
    public float 攻速系数 => 玩家战斗属性 != null
        ? Mathf.Max(0.1f, 玩家战斗属性.当前属性[AttributeType.AttackSpeed])
        : 1f;

    /// <summary>实际冷却 = 基础冷却 ÷ 攻速</summary>
    public float 实际冷却 => 基础冷却 / Mathf.Max(0.1f, 攻速系数);

    /// <summary>冷却还剩几秒</summary>
    public float 冷却剩余 => Mathf.Max(0f, 下次可出手时间 - Time.time);

    /// <summary>现在能不能出手</summary>
    public bool 可以出手 => 锁定单位 != null && 冷却剩余 <= 0f && !出手动作中;

    /// <summary>正在做攻击动作</summary>
    public bool 出手动作中 { get; private set; }

    /// <summary>上一次出手的结果（调试用）</summary>
    public AttackResult 上次结果 { get; private set; }

    /// <summary>飞弹命中锁定单位时触发（接飘字 / 音效）</summary>
    public event Action<ICombatTarget, AttackResult> 命中时;

    /// <summary>神识范围 = 追踪终止距离（超出就放弃）</summary>
    public float 神识范围 => Mathf.Min(神识范围上限, 基础神识范围 + 当前神识 * 每点神识范围);

    float 当前神识 => 玩家战斗属性 != null ? 玩家战斗属性.当前神识 : 0f;

    float 下次可出手时间;
    bool 本轮已出弹;

    // ---- ASCII 别名 ----
    public ICombatTarget LockedUnit => 锁定单位;
    public float AttackSpeedFactor => 攻速系数;
    public float ActualCooldown => 实际冷却;
    public float CooldownRemaining => 冷却剩余;
    public bool CanAttack => 可以出手;

    void Awake()
    {
        if (玩家战斗属性 == null) 玩家战斗属性 = GetComponent<PlayerCombatStats>();
        if (目标管理器 == null) 目标管理器 = GetComponent<NpcTargeting>();
        if (动画 == null) 动画 = GetComponent<PlayerAnimationController>();
        if (动画 == null) 动画 = GetComponentInChildren<PlayerAnimationController>();

        // 【为什么要有这段】本组件是 PlayerAbilityLoader 在**运行时反射 AddComponent** 装上的，
        // 所以拿不到 Inspector 里拖的引用 —— 动作片段只能靠 Resources 自己加载。
        // 想换动作：换 Assets/resources/技能动作/普攻_远程_01.anim，或者运行时给 普攻动作 赋值。
        if (普攻动作 == null)
            普攻动作 = Resources.Load<AnimationClip>("技能动作/普攻_远程_01");
        if (普攻动作 == null)
            Debug.LogWarning("[basic_remoteattack_01] 找不到动作 Assets/resources/技能动作/普攻_远程_01.anim，" +
                             "会退化成「直接出弹、不播动画」", this);
    }

    void Update()
    {
        // 目标换了就丢掉缓存的适配器，保证 锁定单位 跟着走
        var npc = 目标管理器 != null ? 目标管理器.LockedNpc : null;
        int id = npc != null ? npc.GetInstanceID() : 0;
        if (id != 缓存目标id) { 缓存目标id = id; 缓存 = null; }

        // 动作播到 出手进度 → 出弹
        if (出手动作中 && !本轮已出弹)
        {
            float 进度 = 动画 != null ? 动画.动作进度 : 1f;
            if (进度 >= 出手进度 || !(动画 != null && 动画.动作播放中))
            {
                本轮已出弹 = true;
                生成飞弹();
            }
        }

        if (出手动作中 && (动画 == null || !动画.动作播放中))
        {
            出手动作中 = false;
            下次可出手时间 = Time.time + 实际冷却;
        }

        // 输入
        // 出手：**自动出手**开着就自己一直打；关掉才看按键
        if (可以出手 && (自动出手 || AttackPressed())) 出手();
    }

    bool AttackPressed() => 按住连发 ? Input.GetKey(攻击键) : Input.GetKeyDown(攻击键);

    /// <summary>出手一次（外部系统 / 测试可以直接调）</summary>
    public bool 出手()
    {
        if (!可以出手) return false;

        出手动作中 = true;
        本轮已出弹 = false;
        当前锁定 = 锁定单位;

        if (动画 != null && 普攻动作 != null)
        {
            // ★ 攻速直接决定动画播放速度：攻速 2 → 动画快一倍（1.2s → 0.6s）
            if (!动画.播动作(普攻动作, 攻速系数))
            {
                // 播不出来（没接控制器）就别卡住，直接出弹
                本轮已出弹 = true;
                生成飞弹();
            }
        }
        else
        {
            本轮已出弹 = true;
            生成飞弹();
        }
        return true;
    }

    ICombatTarget 当前锁定;

    // ============================================================ 出弹

    void 生成飞弹()
    {
        var 目标 = 当前锁定 ?? 锁定单位;
        if (目标 == null || 目标.已倒下) return;
        if (玩家战斗属性 == null)
        {
            Debug.LogWarning("[basic_remoteattack_01] 没有 PlayerCombatStats，算不了伤害", this);
            return;
        }

        Vector3 出膛点 = 取出生点();
        Vector3 方向 = (目标.判定点 - 出膛点);
        if (方向.sqrMagnitude < 0.0001f) 方向 = transform.forward;
        方向.Normalize();

        // 1) 枪口闪光（纯装饰）
        if (!string.IsNullOrEmpty(闪光特效路径))
            生成装饰特效(闪光特效路径, 出膛点, 方向, 闪光特效存活, 1f);

        // 2) 弹道（**追踪**）
        var 飞 = NpcProjectile.发射(弹道特效路径, 出膛点, 目标.判定点, 飞弹速度, 弹体缩放,
                                    0f, 拦截层, null);
        if (飞 == null) return;

        飞.追踪终止距离 = 神识范围;        // 锁定单位超出神识范围 → 放弃
        飞.最长追踪时间 = 最长追踪时间;     // 超过 8 秒 → 放弃
        飞.设置追踪飞行(目标.根, 飞弹速度, 追踪转向速度);
        飞.到达时 += () => 结算命中(目标, 飞);

        if (打印战斗日志)
            Debug.Log("[basic_remoteattack_01] 对锁定单位「" + 目标.名字 + "」出弹"
                + "（攻速 " + 攻速系数.ToString("0.##") + "｜冷却 " + 实际冷却.ToString("0.##") + "s"
                + "｜神识范围 " + 神识范围.ToString("0.##") + "m）", this);
    }

    /// <summary>飞弹到达 → 对锁定单位结算一次</summary>
    void 结算命中(ICombatTarget 目标, NpcProjectile 飞)
    {
        if (目标 == null || 目标.已倒下) return;

        var 规则 = new AttackSpec(伤害属性, 攻击类别, false, 技能倍率);
        var 结果 = 目标.受到攻击(玩家战斗属性, 规则, this);
        上次结果 = 结果;

        if (打印战斗日志)
            Debug.Log("[basic_remoteattack_01] 命中「" + 目标.名字 + "」 " + 结果, this);

        // 命中特效：放在**弹道的真实命中点**，朝向来向（和 NPC 那边同一套做法）
        if (结果.命中 && !string.IsNullOrEmpty(命中特效路径))
        {
            Vector3 命中点 = 飞 != null ? Vector3.Lerp(飞.命中点, 目标.判定点, 0.85f) : 目标.判定点;
            Vector3 朝向 = 飞 != null ? -飞.飞行朝向 : Vector3.forward;
            生成装饰特效(命中特效路径, 命中点, 朝向, 命中特效存活, 命中特效缩放);
        }

        命中时?.Invoke(目标, 结果);
    }

    /// <summary>右手骨骼末端（拿不到就用备用骨骼，再不行用胸口）</summary>
    public Vector3 取出生点()
    {
        var an = 动画 != null ? 动画.GetComponentInChildren<Animator>() : GetComponentInChildren<Animator>();
        if (an != null)
        {
            var 骨 = an.GetBoneTransform(出生骨骼) ?? an.GetBoneTransform(备用出生骨骼);
            if (骨 != null) return 骨.TransformPoint(出生偏移);
        }
        return transform.position + Vector3.up * 1.2f + transform.forward * 0.3f;
    }

    GameObject 生成装饰特效(string 路径, Vector3 位置, Vector3 朝向, float 存活, float 缩放)
    {
        if (string.IsNullOrEmpty(路径)) return null;
        var prefab = Resources.Load<GameObject>(路径);
        if (prefab == null)
        {
            Debug.LogWarning("[basic_remoteattack_01] 找不到特效：" + 路径, this);
            return null;
        }
        Vector3 前 = 朝向.sqrMagnitude > 0.0001f ? 朝向.normalized : Vector3.forward;
        // 竖直方向要换 up 轴，否则 Quaternion.LookRotation(up, up) 是退化调用（会报错 + 朝向随机）
        Quaternion 旋转 = Mathf.Abs(Vector3.Dot(前, Vector3.up)) > 0.999f
            ? Quaternion.LookRotation(前, Vector3.forward)
            : Quaternion.LookRotation(前, Vector3.up);
        var fx = Instantiate(prefab, 位置, 旋转);
        fx.name = "PlayerFx_" + prefab.name;
        if (!Mathf.Approximately(缩放, 1f)) fx.transform.localScale *= 缩放;
        Destroy(fx, Mathf.Max(0.2f, 存活));
        return fx;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.magenta;
        Gizmos.DrawWireSphere(transform.position, 神识范围);
        var 目标 = 锁定单位;
        if (目标 != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawLine(取出生点(), 目标.判定点);
            Gizmos.DrawWireSphere(目标.判定点, 0.4f);
        }
    }
}
