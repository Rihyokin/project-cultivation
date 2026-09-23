using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 功法「太虚炼气诀」提供的普攻方法。由功法表的「普攻方法id = basic_sword_01」指向本组件。
///
/// 行为（按策划说明实现）：
///   · 没有飞剑时按需生成一把（模型用 base_sword）；
///   · 无目标时，飞剑在角色【右后方】、【剑尖向下】悬浮；
///   · 有 NPC 被锁定（鼠标指向它）时，按【鼠标右键】开始对它普攻；
///   · 飞剑先旋转让剑尖对准目标，然后飞过去【穿过】目标；
///   · 穿过后靠【转向速度】自然绕出一道弧线掉头，再冲回来穿第二次；
///   · 每次穿过都做一次命中判定，命中则按普攻伤害公式扣血；
///   · 目标死亡后自动锁定下一个（条件：好感度 &lt; 0），没有则返航悬浮；
///   · 攻击途中若锁定了新的敌人，立刻改为优先攻击新敌人。
/// </summary>
public class BasicSword01 : MonoBehaviour
{
    public enum SwordState { 悬浮, 飞行 }

    [Header("功法接线")]
    [Tooltip("本普攻方法在功法表「普攻方法id」里的标识。境界页靠它反查提供这个普攻的功法")]
    public string 方法id = "basic_sword_01";

    [Header("飞剑模型")]
    [Tooltip("飞剑外观资源，留空则用一把临时生成的方块代替（模型用 base_sword）")]
    public GameObject 剑模型资源;

    [Tooltip("模型自身的朝向修正。base_sword 的剑身沿本地 +Z、剑尖在 +Z 端（已用网格横截面计算确认），" +
             "所以保持 (0,0,0)。换别的剑模型时再按需调整")]
    // 【用户给定】base_sword 这个模型自身朝向不对，飞剑会指错方向 —— 加 225 度绕 X 的补偿。
    // 注意：本组件现在是 PlayerAbilityLoader **运行时反射 AddComponent** 装上的，
    // 所以这里的**代码默认值就是实际生效值**（改场景里的值没用，因为场景里没有它）。
    public Vector3 模型朝向补偿 = new Vector3(225f, 0f, 0f);

    [Header("悬浮（角色右后方 · 剑尖向下）")]
    [Tooltip("相对角色的本地偏移：+X 右、+Y 上、-Z 后")]
    public Vector3 悬浮偏移 = new Vector3(0.55f, 1.15f, -0.45f);

    [Tooltip("剑尖朝下时的旋转（模型剑身沿 +Z，故绕 X 转 90° 即朝下）")]
    public Vector3 剑尖向下旋转 = new Vector3(90f, 0f, 0f);

    [Tooltip("悬浮跟随的平滑速度")]
    public float 悬浮跟随速度 = 10f;

    [Header("飞行")]
    [Tooltip("飞行速度 (m/s)")]
    public float 飞行速度 = 14f;

    [Tooltip("转向速度（度/秒）。越小弧线越大、掉头越慢")]
    public float 转向速度 = 420f;

    [Tooltip("判定为「穿过目标」的球半径（米）")]
    public float 穿体判定半径 = 0.9f;

    [Tooltip("两次穿体判定的最小间隔（秒），防止一帧内重复触发")]
    public float 穿体最小间隔 = 0.15f;

    [Header("绕行（8 字轨迹）")]
    [Tooltip("8 字横向半宽（两个叶片左右各伸出去多远）")]
    public float 绕行长轴 = 3.5f;

    [Tooltip("8 字纵向半长（沿玩家→目标方向的前后幅度）")]
    public float 绕行短轴 = 2.2f;

    [Tooltip("跑完一整个 8 字需要的秒数。越小穿得越快")]
    public float 绕行周期 = 2.6f;


    [Header("神识索敌（普攻距离）")]
    [Tooltip("神识为 0 时的基础普攻距离（米）")]
    public float 基础索敌范围 = 4f;

    [Tooltip("每 1 点神识增加的距离（米）。这是 basic_sword_01 专属的换算方式，其它普攻方法可以给不同系数")]
    public float 每点神识范围 = 0.8f;

    [Tooltip("普攻距离上限，防止神识堆高后范围失控")]
    public float 索敌范围上限 = 40f;

    [Tooltip("锁定目标用的射线层级")]
    public LayerMask 锁定层级 = ~0;

    [Tooltip("开始攻击的按键")]
    public KeyCode 攻击键 = KeyCode.Mouse1;

    [Tooltip("锁定用的摄像机。留空则取 Camera.main")]
    public Camera 锁定相机;

    [Header("引用")]
    [Tooltip("玩家的战斗属性来源。留空则自动在自身找 PlayerCombatStats")]
    public PlayerCombatStats 玩家战斗属性;

    [Tooltip("选中/锁定管理器。留空则自动在自身找 NpcTargeting；找不到就退化成只用内部锁定")]
    public NpcTargeting 目标管理器;

    [Header("调试输出")]
    [Tooltip("把每次结算结果打到 Console")]
    public bool 打印战斗日志 = true;

    /// <summary>当前锁定的目标（由 NpcTargeting 统一管理）</summary>
    public NpcInstance 锁定目标 => 目标管理器 != null ? 目标管理器.LockedNpc : 内部锁定;

    /// <summary>没有 NpcTargeting 时的退化锁定（由 设置锁定目标 写入）</summary>
    NpcInstance 内部锁定;

    /// <summary>正在被攻击的目标</summary>
    public NpcInstance 攻击目标 { get; private set; }

    /// <summary>飞剑当前状态</summary>
    public SwordState 状态 { get; private set; } = SwordState.悬浮;

    /// <summary>是否正在攻击</summary>
    public bool 攻击中 => 状态 == SwordState.飞行;

    /// <summary>攻速系数（来自玩家属性汇总里的「攻速」）。1 = 标准速度</summary>
    public float 攻速系数 => 玩家战斗属性 != null
        ? Mathf.Max(0.1f, 玩家战斗属性.当前属性[AttributeType.AttackSpeed])
        : 1f;

    /// <summary>
    /// 攻速对【飞行速度】的增益系数 —— 只有攻速系数的一半。
    ///
    /// 直接用 攻速系数 会让攻速 2 时飞剑直接快一倍，视觉上冲得太猛。
    /// 这里把增益折半：攻速 1 → 1.0，攻速 2 → 1.5，攻速 3 → 2.0。
    /// 公式：1 + (攻速 - 1) × 0.5
    ///
    /// 注意：穿体间隔（真正的出手频率）用的仍是【完整的 攻速系数】，
    /// 不受这里折半影响 —— 攻速该提的 DPS 还是要提。
    /// </summary>
    public float 速度增益系数 => 1f + (攻速系数 - 1f) * 0.5f;

    /// <summary>实际飞行速度 = 基础飞行速度 × 速度增益系数（攻速的一半增益）</summary>
    public float 实际飞行速度 => 飞行速度 * 速度增益系数;

    /// <summary>实际穿体间隔 = 基础间隔 ÷ 完整攻速。攻速越高，出手越密</summary>
    public float 实际穿体间隔 => 穿体最小间隔 / Mathf.Max(0.1f, 攻速系数);

    // ---- ASCII 别名（内部中文命名，对外统一 ASCII，见项目约定）----
    public float AttackSpeedFactor => 攻速系数;
    public float SpeedBonusFactor => 速度增益系数;
    public float ActualFlySpeed => 实际飞行速度;
    public float SpiritSense => 当前神识;
    public float SenseRange => 索敌半径;
    public float UnlockRange => 脱锁距离;

    /// <summary>玩家当前神识（由 PlayerCombatStats 汇总）</summary>
    public float 当前神识 => 玩家战斗属性 != null ? 玩家战斗属性.当前神识 : 0f;

    /// <summary>
    /// 普攻距离 = 基础值 + 神识 × 系数（封顶）。
    /// 这是 basic_sword_01 的换算方式；换一个普攻方法就换一组系数即可。
    /// </summary>
    public float 索敌半径 => Mathf.Min(索敌范围上限, 基础索敌范围 + 当前神识 * 每点神识范围);

    /// <summary>脱锁距离。按需求与索敌半径统一为同一个值</summary>
    public float 脱锁距离 => 索敌半径;

    /// <summary>一次穿体命中后触发，方便接飘字 / 音效</summary>
    public event Action<NpcInstance, AttackResult> OnHitLanded;

    Transform 剑体;
    Vector3 飞行方向 = Vector3.forward;
    bool 上一帧在目标球内;
    float 上次穿体时间 = -99f;
    Vector3 悬浮速度;

    /// <summary>是否已进入 8 字绕行（false = 还在接近目标）</summary>
    bool 绕行中;

    /// <summary>8 字轨迹的相位（弧度）</summary>
    float 绕行相位;

    /// <summary>8 字平面的侧向轴（水平）</summary>
    Vector3 绕行侧轴 = Vector3.right;

    /// <summary>8 字平面的前后轴（水平，玩家→目标方向）</summary>
    Vector3 绕行前轴 = Vector3.forward;

    /// <summary>上次所在的 π 区间，用于判定「又穿过一次」</summary>
    int 上次绕行格;

    /// <summary>玩家是否下达过攻击指令且目标仍被锁定。锁定保留时用于「重新进入范围自动恢复攻击」</summary>

    /// <summary>直线冲刺的兜底修正计时（目标移动时每秒重新对准）</summary>
    float 上次修正时间;

    /// <summary>
    /// 玩家手动右键锁定的目标，允许先飞过去再开始判定距离。
    /// 否则右键点一个刚好超出身识范围的目标，会在同一帧被自动解锁，飞剑根本不动。
    /// 首次穿过后即恢复正常的距离判定。
    /// </summary>
    bool 越界追击;
    bool 交战中;

    void Awake()
    {
        if (玩家战斗属性 == null) 玩家战斗属性 = GetComponent<PlayerCombatStats>();
        if (锁定相机 == null) 锁定相机 = Camera.main;

        // 锁定统一由 NpcTargeting 管理（左键选中 / 右键锁定）
        if (目标管理器 == null) 目标管理器 = GetComponent<NpcTargeting>();
        if (目标管理器 != null) 目标管理器.LockChanged += 处理锁定变化;
    }

    void OnDestroy()
    {
        if (目标管理器 != null) 目标管理器.LockChanged -= 处理锁定变化;

        // 飞剑是运行时生成的【根节点对象】，组件被卸载（换功法）时得一起收掉，
        // 否则它会作为孤儿留在场景里。
        if (剑体 != null)
        {
            var go = 剑体.gameObject;
            剑体 = null;
            if (Application.isPlaying) Destroy(go); else DestroyImmediate(go);
        }
    }

    /// <summary>锁定发生变化：玩家手动右键 → 立刻开打；锁定清空 → 停止交战</summary>
    void 处理锁定变化(NpcInstance npc, bool byPlayerClick)
    {
        if (npc == null) { 交战中 = false; 进入返航(); return; }

        if (byPlayerClick)
        {
            // 鼠标直接锁定：不管好感度，直接打
            交战中 = true;
            越界追击 = true;          // 玩家点名的目标，先飞过去再说
            攻击目标 = npc;
            进入飞行();
            return;
        }

        // 攻击途中切到了新目标 → 优先打新的
        if (交战中 && npc != 攻击目标 && !npc.IsDead)
        {
            攻击目标 = npc;
            上一帧在目标球内 = false;
            if (!攻击中) 进入飞行();
        }
    }

    void Update()
    {
        维护交战状态();

        switch (状态)
        {
            case SwordState.悬浮: 更新悬浮(); break;
            case SwordState.飞行: 更新飞行(); break;
        }
    }

    /// <summary>
    /// 处理「普攻距离」与「脱锁距离」两档：
    ///   · 目标在索敌半径内      → 攻击（飞出去穿）
    ///   · 出了索敌半径          → 飞剑返航悬浮，但【锁定保留】（红环与血条不消失）
    ///   · 重新回到索敌半径内    → 自动恢复攻击
    ///   · 超过脱锁距离          → 解除锁定，红环与血条一并消失
    /// </summary>
    void 维护交战状态()
    {
        if (!交战中) return;

        var 锁定 = 锁定目标;

        // 目标没了或死了
        if (锁定 == null || 锁定.IsDead)
        {
            var 下一个 = 找下一个目标();          // 自动索敌仍要求好感度 < 0
            if (下一个 == null) { 交战中 = false; 进入返航(); return; }
            if (目标管理器 != null) 目标管理器.Lock(下一个, false);   // 红环跟着换过去
            攻击目标 = 下一个;
            上一帧在目标球内 = false;
            进入飞行();
            return;
        }

        float 距离 = Vector3.Distance(transform.position, 锁定.transform.position);

        if (距离 > 脱锁距离 && !越界追击)
        {
            // 走太远了：连锁定一起解除
            交战中 = false;
            攻击目标 = null;
            进入返航();
            if (目标管理器 != null) 目标管理器.ClearLock();
            return;
        }

        if (距离 <= 索敌半径)
        {
            // 在普攻距离内：没在打就补一次（覆盖「离开后又走回来」的情况）
            if (!攻击中) { 攻击目标 = 锁定; 上一帧在目标球内 = false; 进入飞行(); }
        }
        else
        {
            // 出了普攻距离：飞剑回身边待命，锁定保留。
            // 但「越界追击」期间不能返航 —— 那是玩家点名要打的目标，得先飞过去。
            if (攻击中 && !越界追击) 进入返航();
        }
    }

    // ------------------------------------------------------------ 状态切换

    void 确保飞剑存在()
    {
        if (剑体 != null) return;

        // 【坑·已修】本组件现在是 PlayerAbilityLoader 在**运行时反射 AddComponent** 装上的
        //（为了让功法切换能自动换普攻），所以拿不到 Inspector 里拖的模型引用 ✗
        // 结果走了"没接模型就生成一个方块"的兜底 —— 转修到青云剑诀后飞出来的是一块 cube ✗
        // 这里自动从 Resources 兜底加载。
        if (剑模型资源 == null) 剑模型资源 = Resources.Load<GameObject>("Weapons/base_sword");

        var root = new GameObject("飞剑_basic_sword_01");
        GameObject visual;
        if (剑模型资源 != null)
        {
            visual = Instantiate(剑模型资源, root.transform);
        }
        else
        {
            // 没接模型时用一根细长方块代替，保证逻辑可跑
            visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            visual.transform.SetParent(root.transform, false);
            visual.transform.localScale = new Vector3(0.08f, 0.08f, 1.0f);
            var col = visual.GetComponent<Collider>();
            if (col != null) Destroy(col);
        }
        visual.name = "Model";
        visual.transform.localPosition = Vector3.zero;
        visual.transform.localRotation = Quaternion.Euler(模型朝向补偿);
        visual.transform.localScale = Vector3.one;

        剑体 = root.transform;
        剑体.position = 目标悬浮位置();
        剑体.rotation = 悬浮旋转();
    }

    void 进入飞行()
    {
        确保飞剑存在();
        if (攻击目标 == null) { 进入返航(); return; }
        状态 = SwordState.飞行;
        飞行方向 = (取瞄准点(攻击目标) - 剑体.position).normalized;
        上一帧在目标球内 = false;
        绕行中 = false;         // 先走接近，靠近后再切入 8 字
    }

    void 进入返航()
    {
        攻击目标 = null;
        状态 = SwordState.悬浮;
        悬浮速度 = Vector3.zero;
        绕行中 = false;
    }

    // ------------------------------------------------------------ 悬浮

    Vector3 目标悬浮位置()
    {
        return transform.position + transform.rotation * 悬浮偏移;
    }

    Quaternion 悬浮旋转()
    {
        // 保持角色的朝向，但剑尖朝下
        return Quaternion.Euler(剑尖向下旋转.x, transform.eulerAngles.y, 剑尖向下旋转.z);
    }

    void 更新悬浮()
    {
        if (剑体 == null) { 确保飞剑存在(); return; }
        // 限速：飞剑被打飞很远后返航时，SmoothDamp 首帧会冲得极快，看起来像瞬移。
        // 用 maxSpeed 把返航速度卡在飞行速度以内，视觉上才是「飞回来」而不是「闪回来」。
        剑体.position = Vector3.SmoothDamp(剑体.position, 目标悬浮位置(), ref 悬浮速度,
                                         1f / Mathf.Max(1f, 悬浮跟随速度), 实际飞行速度, Time.deltaTime);
        剑体.rotation = Quaternion.Slerp(剑体.rotation, 悬浮旋转(), Time.deltaTime * 6f);
    }

    // ------------------------------------------------------------ 飞行

    void 更新飞行()
    {
        if (剑体 == null) { 进入返航(); return; }
        if (攻击目标 == null) { 进入返航(); return; }

        Vector3 目标点 = 取瞄准点(攻击目标);

        // 进入 8 字后就完全交给轨迹方程
        if (绕行中) { 更新绕行(目标点); return; }

        // ---- 阶段一：直线冲刺 ----
        // 不做任何转向，就是一条直线冲过去；穿过目标的那一刻再切到 8 字。
        // 关键：双纽线在 t = 0 处【正好是目标点】，切换时剑已经在那个点上，
        // 所以位置天然连续，不会再出现「飞到一半瞬移到目标旁边」。

        剑体.rotation = Quaternion.LookRotation(飞行方向);
        剑体.position += 飞行方向 * 实际飞行速度 * Time.deltaTime;

        // 穿过判定：进入目标球体的瞬间算「完成一次穿过」
        float 距离 = Vector3.Distance(剑体.position, 目标点);
        bool 在球内 = 距离 <= 穿体判定半径;
        if (在球内 && !上一帧在目标球内 && Time.time - 上次穿体时间 >= 实际穿体间隔)
        {
            上次穿体时间 = Time.time;
            执行穿体结算(攻击目标);

            if (攻击目标 != null && 攻击目标.IsDead)
            {
                var 下一个 = 找下一个目标();
                if (下一个 == null) { 交战中 = false; 进入返航(); return; }
                攻击目标 = 下一个;
                if (目标管理器 != null) 目标管理器.Lock(下一个, false);
                上一帧在目标球内 = false;
                return;
            }

            越界追击 = false;         // 已经打到了，之后按正常距离判定

            // 穿过之后：顺着刚才的飞行方向开始画 8 字
            开始绕行(目标点, 飞行方向);
            return;
        }
        上一帧在目标球内 = 在球内;

        // 兜底：目标移动导致直线一直打不中时，每秒重新对准一次
        if (Time.time - 上次修正时间 > 1.2f)
        {
            上次修正时间 = Time.time;
            Vector3 期望 = 目标点 - 剑体.position;
            if (期望.sqrMagnitude > 1e-6f) 飞行方向 = 期望.normalized;
        }
    }

    // ------------------------------------------------------------ 8 字绕行

    /// <summary>
    /// 双纽线（8 字）参数方程。曲线恰好两次经过目标点（t = 0 与 t = π）：
    ///     点(t) = 目标点 + 侧轴 · A·sin(t) + 前轴 · B·sin(t)·cos(t)
    /// 两个叶片分别落在 侧轴 的正负两侧，所以是横躺的 8 字，平铺在地面上。
    ///
    /// 之前用「横向偏移 + 限速转向」做不出 8 字：限速转向掉头永远走最短路径，
    /// 无论怎么翻偏移符号都在同一侧绕圈。这里直接把轨迹参数化，绕行方向就完全可控了。
    /// </summary>
    Vector3 绕行点(float t, Vector3 目标点)
    {
        return 目标点
             + 绕行侧轴 * (绕行长轴 * Mathf.Sin(t))
             + 绕行前轴 * (绕行短轴 * Mathf.Sin(t) * Mathf.Cos(t));
    }

    void 开始绕行(Vector3 目标点, Vector3 冲刺方向)
    {
        // 8 字平面用【穿过时的飞行方向】来定，这样 8 字是顺着冲刺方向铺开的，
        // 而不是固定朝着玩家。压平 y 让 8 字躺在地面上。
        Vector3 前 = 冲刺方向;
        前.y = 0f;
        if (前.sqrMagnitude < 1e-6f) 前 = transform.forward;
        绕行前轴 = 前.normalized;

        Vector3 侧 = Vector3.Cross(绕行前轴, Vector3.up);
        if (侧.sqrMagnitude < 1e-6f) 侧 = transform.right;
        绕行侧轴 = 侧.normalized;

        // 相位固定从 0 起 —— 双纽线在 t = 0 处正好是目标点，
        // 而此刻剑刚穿过目标，所以切换点完全重合，不会瞬移。
        绕行相位 = 0f;
        上次绕行格 = 0;
        绕行中 = true;
        上一帧在目标球内 = true;
    }

    void 更新绕行(Vector3 目标点)
    {
        // 原来只除 绕行周期 —— 结果是【攻速调高了也看不出来】：
        // 飞剑大部分时间在绕行，而绕行的快慢完全由这个固定周期决定，
        // 攻速只影响前面那一下直线冲刺，视觉上几乎没变化。
        // 现在除以 攻速系数，攻速 2 就是绕得快一倍，和冲刺速度保持一致。
        绕行相位 += 2f * Mathf.PI / Mathf.Max(0.15f, 绕行周期 / 速度增益系数) * Time.deltaTime;
        if (绕行相位 > Mathf.PI * 200f) 绕行相位 -= Mathf.PI * 200f;

        Vector3 新点 = 绕行点(绕行相位, 目标点);
        Vector3 位移 = 新点 - 剑体.position;
        剑体.position = 新点;
        if (位移.sqrMagnitude > 1e-8f)
            剑体.rotation = Quaternion.LookRotation(位移.normalized);

        // 穿过判定：相位每跨过一个 π，曲线就正好经过目标点一次
        int 格 = Mathf.FloorToInt(绕行相位 / Mathf.PI);
        if (格 != 上次绕行格)
        {
            上次绕行格 = 格;
            if (Time.time - 上次穿体时间 >= 实际穿体间隔)
            {
                上次穿体时间 = Time.time;
                执行穿体结算(攻击目标);
                越界追击 = false;

                if (攻击目标 != null && 攻击目标.IsDead)
                {
                    var 下一个 = 找下一个目标();
                    if (下一个 == null) { 交战中 = false; 进入返航(); }
                    else
                    {
                        攻击目标 = 下一个;
                        if (目标管理器 != null) 目标管理器.Lock(下一个, false);
                        绕行中 = false;      // 换目标后重新走一遍接近
                    }
                }
            }
        }
    }

    [Header("瞄准")]
    [Tooltip("瞄准点相对目标脚底的高度（大致取半身高）")]
    public float 瞄准高度 = 0.9f;

    [Tooltip("瞄准点跟随目标的平滑速度。越大越贴，越小越顺滑")]
    public float 瞄准平滑速度 = 14f;

    Vector3 平滑瞄准点;
    int 上次瞄准目标 = 0;

    /// <summary>
    /// 取要穿/要绕的那个点。
    ///
    /// 【为什么不用 col.bounds.center】
    /// 那是物理系统 / 蒙皮结算出来的值，和渲染帧不同步：
    /// 敌人一移动，它就是一跳一跳的。而绕行的 8 字轨迹是【锚在这个点上的】，
    /// 于是剑也跟着一跳一跳 —— 看起来「像是有两把剑」。
    ///
    /// 现在改成：
    ///   · 用 transform.position + 固定高度（跟着渲染帧走，连续）
    ///   · 再做一次指数平滑（换目标时直接跳过去，不插值）
    /// 目标快速移动时剑会有一点"拖后"，但绝不会抖。
    /// </summary>
    Vector3 取瞄准点(NpcInstance npc)
    {
        if (npc == null) return 剑体 != null ? 剑体.position : transform.position;

        Vector3 生 = npc.transform.position + Vector3.up * 瞄准高度;

        // 换目标（或第一次）时直接落位，不要从上一个目标的点滑过去
        int id = npc.GetInstanceID();
        if (id != 上次瞄准目标)
        {
            上次瞄准目标 = id;
            平滑瞄准点 = 生;
            return 平滑瞄准点;
        }

        // 指数平滑，和帧率无关
        float k = 1f - Mathf.Exp(-Mathf.Max(0.01f, 瞄准平滑速度) * Time.deltaTime);
        平滑瞄准点 = Vector3.Lerp(平滑瞄准点, 生, k);
        return 平滑瞄准点;
    }

    void 执行穿体结算(NpcInstance npc)
    {
        if (npc == null || 玩家战斗属性 == null) return;

        var result = npc.ReceiveBasicAttack(玩家战斗属性);
        if (打印战斗日志)
            Debug.Log("[basic_sword_01] 对 " + npc.DisplayName + " " + result, npc);

        // 命中才出特效（被闪避就什么都不放，这样「有没有打中」一眼能看出来）
        if (result.命中)
        {
            Vector3 方向 = 剑体 != null ? 剑体.forward : transform.forward;
            BasicSword01HitEffect.Spawn(取瞄准点(npc), 方向, result.暴击);
        }

        OnHitLanded?.Invoke(npc, result);
    }

    // ------------------------------------------------------------ 锁定与索敌

    // ============================================================
    //  ASCII 公开接口
    //  组件内部按项目习惯用中文命名，但外部系统（UI 按钮、其他脚本、
    //  以及自动化测试）用 ASCII 名字调用更稳妥，这里提供一层薄包装。
    // ============================================================

    /// <summary>当前锁定的目标</summary>
    public NpcInstance LockedTarget => 锁定目标;
    public NpcInstance AttackTarget => 攻击目标;
    public bool IsAttacking => 攻击中;

    /// <summary>Set the locked target (used by UI selection / external systems).</summary>
    public void SetLockedTarget(NpcInstance npc) => 设置锁定目标(npc);

    /// <summary>Begin attacking the currently locked target.</summary>
    public bool BeginAttack()
    {
        if (锁定目标 == null) return false;
        攻击目标 = 锁定目标;
        进入飞行();
        return true;
    }

    /// <summary>Stop attacking; the sword returns to its idle hover.</summary>
    public void StopAttack() => 停止攻击();


    void 更新锁定()
    {
        // 锁定现在统一由 NpcTargeting 处理（左键选中 / 右键锁定），这里不再自己射线检测
        if (目标管理器 != null) return;

        if (锁定相机 == null) return;
        var ray = 锁定相机.ScreenPointToRay(Input.mousePosition);
        if (Physics.Raycast(ray, out var hit, 200f, 锁定层级))
        {
            var npc = hit.collider.GetComponentInParent<NpcInstance>();
            if (npc != null && !npc.IsDead) { 内部锁定 = npc; return; }
        }
    }

    NpcInstance 找下一个目标()
    {
        NpcInstance 最佳 = null;
        float 最近 = float.MaxValue;
        foreach (var npc in FindObjectsOfType<NpcInstance>())
        {
            if (npc == null || npc.IsDead || !npc.是敌对目标) continue;   // 自动索敌仍要求好感度 < 0
            float d = Vector3.Distance(transform.position, npc.transform.position);
            if (d > 索敌半径 || d >= 最近) continue;
            最近 = d; 最佳 = npc;
        }
        return 最佳;
    }

    /// <summary>手动设置锁定目标（走 NpcTargeting，会同步红环与血条）</summary>
    public void 设置锁定目标(NpcInstance npc)
    {
        if (npc == null) { 内部锁定 = null; if (目标管理器 != null) 目标管理器.ClearLock(); return; }
        if (npc.IsDead) return;

        if (目标管理器 != null) 目标管理器.Lock(npc, byPlayerClick: true);
        else 内部锁定 = npc;
    }

    /// <summary>停止攻击，飞剑返航悬浮</summary>
    public void 停止攻击()
    {
        进入返航();
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, 索敌半径);
        var p = transform.position + transform.rotation * 悬浮偏移;
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(p, 0.2f);
        if (攻击目标 != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawLine(剑体 != null ? 剑体.position : transform.position, 取瞄准点(攻击目标));
            Gizmos.DrawWireSphere(取瞄准点(攻击目标), 穿体判定半径);
        }
    }
}
