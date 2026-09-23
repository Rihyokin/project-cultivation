using UnityEngine;

/// <summary>
/// 2.5D 俯视角角色移动控制。
/// WASD 相对摄像机方向移动（屏幕上=向远处），Shift 奔跑。
/// （装了【凭虚御风】时 Shift 改成**按一下切换飞行**，见 <see cref="YufengFlight"/>）
/// 移动交给 CharacterController，自动处理碰撞、台阶与贴地。
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    [Header("速度 (m/s)")]
    [Tooltip("正常行走速度")]
    public float walkSpeed = 1.4f;

    [Tooltip("按住 Shift 时的奔跑速度")]
    public float runSpeed = 6f;

    [Tooltip("起步加速度，越大越跟手")]
    public float acceleration = 30f;

    [Tooltip("松手后的减速度")]
    public float deceleration = 38f;

    [Header("转向")]
    [Tooltip("角色转向速度（度/秒）。设为 0 表示瞬间转向")]
    public float turnSpeed = 720f;

    [Header("重力")]
    [Tooltip("本作不做 Y 轴玩法，重力只用于让角色贴住地面。设为 0 完全禁用")]
    public float gravity = -25f;

    [Header("模型朝向补偿")]
    [Tooltip("模型视觉正面相对 transform.forward 的偏差（度）。\n" +
             "本模型 Player_Visual 未做旋转时正面朝 -X，所以需要 90 来让「朝向移动方向」看起来正确。\n" +
             "如果以后把 Player_Visual 的 Y 旋到 90（正面=forward），这里就改成 0。")]
    public float visualYawOffset = 90f;

    [Header("引用")]
    [Tooltip("用于决定 WASD 方向的摄像机。留空则自动取 Camera.main")]
    public Transform cameraTransform;

    [Tooltip("凭虚御风。留空则自动在本体找 YufengFlight；没有就不启用御风")]
    public YufengFlight 御风;

    [Tooltip("坐骑系统。留空则自动在本体找 MountRider；没有就不启用骑乘")]
    public MountRider 坐骑;

    [Tooltip("锁定/选中管理器。留空则自动在本体找 NpcTargeting")]
    public NpcTargeting 目标管理器;

    [Header("锁定时的朝向")]
    [Tooltip("锁定目标后，移动时朝向在「目标方向」与「移动方向」之间插值。\n" +
             "0 = 永远正面朝向目标（这时左右移动就得靠平移动画，而动画库里没有平移片段）\n" +
             "1 = 完全朝移动方向（等于不锁定）\n" +
             "默认 0.5：更偏向正面锁敌，同时还能复用现有的向前行走动画")]
    [Range(0f, 1f)]
    public float 移动时转向移动方向 = 0.5f;

    // ---- 对外只读状态，方便接动画 / UI ----
    /// <summary>当前朝向的移动方向（世界空间，已归一化；无输入时为零向量）</summary>
    public Vector3 MoveDirection { get; private set; }
    /// <summary>当前水平速度大小 (m/s)</summary>
    public float CurrentSpeed { get; private set; }
    /// <summary>0=静止 1=全速奔跑，可直接喂给 Animator 的 Blend Tree</summary>
    public float Speed01 { get; private set; }
    /// <summary>本帧是否按住了 Shift</summary>
    public bool IsRunning { get; private set; }
    /// <summary>本帧是否处于御风流程（升空/悬浮/落地）</summary>
    public bool IsFlying { get; private set; }

    CharacterController controller;
    Vector3 horizontalVelocity;
    float verticalVelocity;

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        if (御风 == null) 御风 = GetComponent<YufengFlight>();
        if (坐骑 == null) 坐骑 = GetComponent<MountRider>();
        if (目标管理器 == null) 目标管理器 = GetComponent<NpcTargeting>();
        if (cameraTransform == null && Camera.main != null)
            cameraTransform = Camera.main.transform;
    }

    void Update()
    {
        Vector3 desired = ReadMoveDirection(out bool running);
        MoveDirection = desired;
        IsRunning = running;

        // 凭虚御风生效时，Shift 从「按住奔跑」变成「按一下切换飞行」（见 YufengFlight）
        IsFlying = 御风 != null && 御风.御风流程中;
        bool 飞行中 = 御风 != null && 御风.御风中;
        // 骑乘中：不能起飞（YufengFlight.禁止切换 挡住），高度交给坐骑系统
        bool 骑乘中 = 坐骑 != null && 坐骑.骑乘中;

        // 水平速度：带加/减速，避免瞬间起停的僵硬感
        float targetSpeed = 骑乘中 ? 坐骑.骑乘速度
                         : (飞行中 ? 御风.飞行速度 : (running ? runSpeed : walkSpeed));
        Vector3 targetVelocity = desired * targetSpeed;
        float rate = desired.sqrMagnitude > 0.0001f ? acceleration : deceleration;
        horizontalVelocity = Vector3.MoveTowards(horizontalVelocity, targetVelocity, rate * Time.deltaTime);

        if (IsFlying)
            ApplyFlightHeight();
        else if (骑乘中)
            ApplyMountHeight();
        else
            ApplyGravity();

        Vector3 motion = horizontalVelocity;
        motion.y = verticalVelocity;
        controller.Move(motion * Time.deltaTime);

        FaceDirection(desired);

        CurrentSpeed = horizontalVelocity.magnitude;
        float 上限速度 = 骑乘中 ? Mathf.Max(0.01f, 坐骑.骑乘速度)
                       : (飞行中 ? Mathf.Max(0.01f, 御风.飞行速度) : runSpeed);
        Speed01 = 上限速度 > 0.0001f ? Mathf.Clamp01(CurrentSpeed / 上限速度) : 0f;
    }

    /// <summary>读输入并换算成相对摄像机的世界方向</summary>
    Vector3 ReadMoveDirection(out bool running)
    {
        running = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

        Vector2 raw = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
        if (raw.sqrMagnitude > 1f) raw.Normalize();   // 斜向不加速
        if (raw.sqrMagnitude < 0.0001f) return Vector3.zero;

        Vector3 forward = Vector3.forward;
        Vector3 right = Vector3.right;

        if (cameraTransform != null)
        {
            // 把摄像机朝向压到水平面，得到屏幕上的"上/右"
            forward = cameraTransform.forward;
            forward.y = 0f;
            right = cameraTransform.right;
            right.y = 0f;

            forward = forward.sqrMagnitude < 0.0001f ? Vector3.forward : forward.normalized;
            right = right.sqrMagnitude < 0.0001f ? Vector3.right : right.normalized;
        }

        return (forward * raw.y + right * raw.x).normalized;
    }

    /// <summary>御风时按 YufengFlight 的高度曲线悬浮，不走重力</summary>
    void ApplyFlightHeight()
    {
        float 目标Y = 御风.地面高度 + 御风.高度偏移;
        // 比例控制，避免直接除 dt 造成抖动
        verticalVelocity = Mathf.Clamp((目标Y - transform.position.y) * 8f, -10f, 10f);
    }

    void ApplyGravity()
    {
        if (Mathf.Approximately(gravity, 0f))
        {
            verticalVelocity = 0f;
            return;
        }

        // 落地时给一点向下压力，保证 isGrounded 稳定
        if (controller.isGrounded && verticalVelocity < 0f)
            verticalVelocity = -2f;

        verticalVelocity += gravity * Time.deltaTime;
    }

    /// <summary>
    /// 骑乘时的高度：由坐骑系统给目标高度，**不走重力**。
    /// 用户定的规则：「骑乘坐骑时无法开始御风，但是同样不会掉高度」——
    /// 所以这里只做比例控制把人拉到 <see cref="MountRider.角色目标高度"/>，不消耗灵气。
    /// </summary>
    void ApplyMountHeight()
    {
        float 目标Y = 坐骑.角色目标高度;
        // 比例控制，和 ApplyFlightHeight 同一套，避免直接除 dt 造成抖动
        verticalVelocity = Mathf.Clamp((目标Y - transform.position.y) * 8f, -10f, 10f);
    }

    /// <summary>
    /// 决定角色朝向。
    ///
    /// 有锁定 / 选中目标时：
    ///   · 站着不动 → 正面朝向目标；
    ///   · 移动中   → 在「目标方向」与「移动方向」之间插值，插值量随速度增长。
    ///
    /// 为什么用插值而不是死盯目标：动画库里【没有任何平移/后退行走片段】，
    /// 全是向前的。死盯目标的话，侧向移动只能播向前走的动画，看起来像贴地滑行。
    /// 插值让角色在实际移动时把身体带过去大半，现有的向前走动画就对上了。
    /// 如果以后补了平移素材，把 移动时转向移动方向 调到 0 即可改成严格正面锁敌。
    /// </summary>
    void FaceDirection(Vector3 moveDir)
    {
        Vector3 face = moveDir;

        var 目标 = 取锁定对象();
        if (目标 != null)
        {
            Vector3 toTarget = 目标.transform.position - transform.position;
            toTarget.y = 0f;

            if (toTarget.sqrMagnitude > 0.0001f)
            {
                toTarget.Normalize();

                if (moveDir.sqrMagnitude < 0.0001f)
                {
                    // 站定 → 正面朝向目标
                    face = toTarget;
                }
                else
                {
                    // 移动中 → 按速度在两者之间过渡
                    float 速度比 = Mathf.Clamp01(CurrentSpeed / Mathf.Max(0.01f, runSpeed));
                    float k = Mathf.Clamp01(移动时转向移动方向 * Mathf.Max(0.35f, 速度比));
                    face = Vector3.Slerp(toTarget, moveDir, k);
                }
            }
        }

        if (face.sqrMagnitude < 0.0001f) return;

        // 先转到目标朝向，再叠加模型自身的朝向偏差，
        // 保证「视觉上角色的正面」指向该方向，而不是 transform.forward。
        Quaternion target = Quaternion.LookRotation(face.normalized, Vector3.up) * Quaternion.Euler(0f, visualYawOffset, 0f);
        transform.rotation = turnSpeed <= 0f
            ? target
            : Quaternion.RotateTowards(transform.rotation, target, turnSpeed * Time.deltaTime);
    }

    /// <summary>优先取锁定目标，没有就取选中目标</summary>
    NpcInstance 取锁定对象()
    {
        if (目标管理器 == null) return null;
        var t = 目标管理器.LockedNpc;
        if (t != null && !t.IsDead) return t;
        t = 目标管理器.SelectedNpc;
        if (t != null && !t.IsDead) return t;
        return null;
    }
    // ---- ASCII 别名（内部中文命名，对外统一 ASCII，见项目约定）----
    /// <summary>锁定时「移动时转向移动方向」的插值量：0=始终正面锁敌，1=完全朝移动方向</summary>
    public float LockedTurnBlend
    {
        get => 移动时转向移动方向;
        set => 移动时转向移动方向 = Mathf.Clamp01(value);
    }
}
