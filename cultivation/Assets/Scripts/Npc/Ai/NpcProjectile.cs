using UnityEngine;

/// <summary>
/// 飞弹飞行器（我们自己的一套，不依赖特效包的脚本）。
///
/// 把「飞弹的 5 个要素」都做成了参数：
///
/// | 要素 | 怎么控制 |
/// |---|---|
/// | **1 出生点** | 由 <see cref="NpcAiBase.取出膛点"/> 决定（骨骼挂点 / 胸口），生成时传进来 |
/// | **2 飞行路径** | **直线匀速、不追踪** —— 起飞时锁定目标点，之后只按方向直线推进 |
/// | **3 命中效果** | 到达时抛 <see cref="到达时"/>，由调用方结算伤害 + 放命中特效 |
/// | **4 能否被阻挡** | 本组件**不和场景做碰撞**（= 无法被场景阻挡）；只检测 <see cref="拦截层"/> 上的东西 ——
///                       技能神通 / 法宝 / 灵阵 放到那一层就能拦下它；也可以直接调 <see cref="拦截"/> |
/// | **5 消灭点** | 飞出 <see cref="最大飞行距离"/> 就**停止并淡化消散**（不是被撞掉） |
///
/// 淡化用粒子消散（复用 <see cref="NpcDissolveEffect"/>）+ 自身缩小，所以是"淡出"而不是"啪一下消失"。
/// </summary>
public class NpcProjectile : MonoBehaviour
{
    /// <summary>到达 / 被拦截时的回调</summary>
    public delegate void 命中回调();

    [Header("飞行")]
    [Tooltip("没到达也要销毁的最长寿命（秒）")]
    public float 最长寿命 = 5f;

    [Tooltip("飞出这么远就停止并淡化消散（**消灭点**）。0 = 不限")]
    public float 最大飞行距离 = 0f;

    [Tooltip("到达目标点时回调。调用方在这里结算伤害 / 放命中特效")]
    public event 命中回调 到达时;

    [Header("拦截")]
    [Tooltip("能被哪些层拦下来（技能神通 / 法宝 / 灵阵 放到这层）。\n" +
             "0 = 谁都拦不住。**注意本组件不和场景碰撞**，所以墙挡不住它")]
    public LayerMask 拦截层 = 0;

    [Tooltip("检测拦截的球半径（米）")]
    public float 拦截半径 = 0.6f;

    [Tooltip("被拦截时的回调（可以用来播「被挡下」的特效）")]
    public event 命中回调 被拦截时;

    [Header("消灭点的淡化")]
    [Tooltip("淡化消散的时长（秒）")]
    public float 消散时长 = 0.55f;

    [Tooltip("消散粒子的颜色")]
    public Color 消散颜色 = new Color(0.85f, 0.9f, 1f, 0.9f);

    [Tooltip("消散粒子数量。0 = 按体积自动")]
    public int 消散粒子数 = 0;

    [Tooltip("要不要在消散时放粒子（只想让它静静消失就关掉）")]
    public bool 消散带粒子 = true;

    [Header("直线飞行模式（不追踪）")]
    [Tooltip("只在「判断有没有打到」时用，**不改变飞行方向** —— 这就是「不追踪」")]
    public Transform 追踪目标;

    [Tooltip("目标离弹道多近算命中（米）")]
    public float 命中半径 = 1.0f;

    [Header("追踪模式（太虚炼气诀的普攻飞弹用这个）")]
    [Tooltip("开启后**持续追踪目标**（会拐弯），直到命中 / 目标超出范围 / 超时。\n" +
             "关闭 = 直线匀速不追踪（白鹿精用这个）")]
    public bool 追踪 = false;

    [Tooltip("追踪时的最大转向速率（度/秒）。越小拐弯越笨")]
    public float 追踪转向速度 = 540f;

    [Tooltip("目标超出这个距离就放弃并消散（米）。**策划叫「神识范围」**。0 = 不限制")]
    public float 追踪终止距离 = 0f;

    [Tooltip("追踪最长持续多久（秒）就放弃并消散")]
    public float 最长追踪时间 = 8f;

    /// <summary>起飞时刻（追踪超时用）</summary>
    float 起飞时间;

    [Header("粒子（这个特效包的弹道靠外部驱动，playOnAwake=false）")]
    [Tooltip("每前进 1 米给「沿路粒子」补发几颗。\n" +
             "QFX 那些 `*_Along` 粒子 rateOverTime=0、burst=0 —— **必须由外部反复 Emit 才出效果**，\n" +
             "否则飞弹飞过去什么都没有（这就是「完全没有飞行轨迹」的原因）。0 = 不补发")]
    public float 每米补发粒子 = 8f;

    // ---- 运行时 ----
    ParticleSystem[] 全部粒子;
    ParticleSystem[] 沿路粒子;
    Vector3 目标点;
    Vector3 飞行方向;
    bool 直线模式;
    float 速度 = 15f;
    bool 已结束;
    float 已飞距离;

    /// <summary>已经飞了多远（米）</summary>
    public float 飞行距离 => 已飞距离;

    /// <summary>是否已经结束（到达 / 被拦截 / 消散完）</summary>
    public bool 已终止 => 已结束;

    /// <summary>命中点 —— 到达 / 被拦截时记下来，<see cref="到达时"/> 回调里可以读，用来把命中特效放准</summary>
    public Vector3 命中点 { get; private set; }

    /// <summary>飞行朝向（单位向量）。命中特效靠它摆正方向</summary>
    public Vector3 飞行朝向
        => 飞行方向.sqrMagnitude > 0.0001f ? 飞行方向.normalized : transform.forward;

    /// <summary>给一个已经存在的物件安排飞行（弹道 prefab 自带特效时用这个）</summary>
    public void 设置飞行(Vector3 到点, float 飞行速度)
    {
        Vector3 起点 = transform.position;
        目标点 = 到点;
        速度 = Mathf.Max(0.5f, 飞行速度);
        已结束 = false;
        已飞距离 = 0f;
        // 寿命按"飞完全程再留一点余量"算，避免设太小提前消失
        最长寿命 = Mathf.Max(1.5f, Vector3.Distance(起点, 到点) / 速度 + 2f);
    }

    /// <summary>
    /// **直线匀速、不追踪**：起飞时锁定方向，之后只沿这个方向直线推进。
    ///
    /// 命中判定是"目标还在不在弹道上" —— 玩家躲开了就打不中，弹道继续飞，
    /// 直到飞出 <see cref="最大飞行距离"/> 自行淡化消散（**消灭点**）。
    /// 这才是策划要的「直线飞行、不追踪」。
    /// </summary>
    public void 设置直线飞行(Vector3 方向, Transform 目标, float 飞行速度)
    {
        飞行方向 = 方向.sqrMagnitude > 0.0001f ? 方向.normalized : transform.forward;
        追踪目标 = 目标;
        目标点 = transform.position + 飞行方向 * 1000f;   // 直线模式下只是个"无限远"的占位
        速度 = Mathf.Max(0.5f, 飞行速度);
        直线模式 = true;
        已结束 = false;
        已飞距离 = 0f;
        最长寿命 = 8f;                                     // 直线模式靠 最大飞行距离 收尾
    }

    /// <summary>
    /// **持续追踪**：每帧把飞行方向往目标拐（限速转向），直到：
    ///   命中 / 目标超出 <see cref="追踪终止距离"/> / 超过 <see cref="最长追踪时间"/>
    ///
    /// 和 <see cref="设置直线飞行"/> 的区别：那个起飞就锁死方向，躲开就打不中；
    /// 这个会拐弯，但转向有上限，所以目标快速横向拉开时仍然能甩掉它。
    /// </summary>
    public void 设置追踪飞行(Transform 目标, float 飞行速度, float 转向速率 = 540f)
    {
        追踪 = true;
        追踪目标 = 目标;
        追踪转向速度 = 转向速率;
        速度 = Mathf.Max(0.5f, 飞行速度);
        直线模式 = true;                       // 复用"不飞到某个点"的那套推进逻辑
        起飞时间 = Time.time;
        已结束 = false;
        已飞距离 = 0f;
        最长寿命 = 30f;                        // 追踪靠 最长追踪时间 / 终止距离 收尾

        飞行方向 = 目标 != null
            ? (目标判定点(目标) - transform.position).normalized
            : transform.forward;
        if (飞行方向.sqrMagnitude < 0.0001f) 飞行方向 = transform.forward;
    }

    /// <summary>目标身上的判定点（胸口高度）</summary>
    Vector3 目标判定点(Transform t)
        => t.position + Vector3.up * 1.1f;

    /// <summary>
    /// 生成一发飞弹。
    /// <paramref name="弹道特效路径"/> 留空 = 只有一个看不见的飞行体（逻辑照样跑）。
    /// </summary>
    public static NpcProjectile 发射(string 弹道特效路径, Vector3 起点, Vector3 目标点,
                                     float 速度, float 弹体缩放, float 最大飞行距离,
                                     LayerMask 拦截层, 命中回调 到达时)
    {
        var prefab = string.IsNullOrEmpty(弹道特效路径) ? null : Resources.Load<GameObject>(弹道特效路径);

        var go = prefab != null
            ? Instantiate(prefab, 起点, Quaternion.identity)
            : new GameObject("NpcProjectile(无特效)");

        go.transform.position = 起点;
        if (!Mathf.Approximately(弹体缩放, 1f)) go.transform.localScale *= 弹体缩放;

        Vector3 方向 = 目标点 - 起点;
        if (方向.sqrMagnitude > 0.0001f) go.transform.rotation = Quaternion.LookRotation(方向.normalized, Vector3.up);

        var p = go.GetComponent<NpcProjectile>();
        if (p == null) p = go.AddComponent<NpcProjectile>();
        p.最大飞行距离 = 最大飞行距离;
        p.拦截层 = 拦截层;
        p.设置飞行(目标点, 速度);

        if (到达时 != null) p.到达时 += 到达时;
        return p;
    }

    void Awake() => 起播粒子();

    /// <summary>
    /// 把弹道 prefab 里的粒子全部启动。
    ///
    /// **关键**：这个包的粒子 `playOnAwake = false`、`rateOverTime = 0`，
    /// 主系统和 `Trail` 靠 1 个 burst，而 `*_Along`（沿路火花/烟/光）**burst = 0** ——
    /// 它们是靠包里的武器脚本**反复调 `Emit(1)`** 才有的。
    /// 我们不用那个脚本，所以得自己：①全部 `Play` ②沿路的按距离补发。
    /// </summary>
    void 起播粒子()
    {
        全部粒子 = GetComponentsInChildren<ParticleSystem>(true);
        var 沿路 = new System.Collections.Generic.List<ParticleSystem>();

        foreach (var ps in 全部粒子)
        {
            var m = ps.main;
            m.playOnAwake = true;
            ps.Play(true);

            // 自己不会出粒子的（rate 和 burst 都是 0）→ 归到"沿路补发"
            if (ps.emission.rateOverTime.constantMax <= 0.001f && ps.emission.burstCount == 0)
                沿路.Add(ps);
        }
        沿路粒子 = 沿路.ToArray();
    }

    // ============================================================ 每帧

    void Update()
    {
        if (已结束) return;

        float 本帧能走 = 速度 * Time.deltaTime;

        // ---- 直线模式（含追踪）：靠飞行方向推进 ----
        if (直线模式)
        {
            // 【追踪】每帧把方向往目标拐一点（限速），所以是"会拐弯但拐不快"
            if (追踪 && 追踪目标 != null)
            {
                Vector3 期望 = (目标判定点(追踪目标) - transform.position);
                // 【坑·已修】以前这里写 期望.y = 0f（那是给白鹿精"贴地平飞"写的），
                // 结果玩家飞到天上后，飞弹只会水平绕圈、永远不爬升也不下降，追不到地上的单位。
                // 追踪必须是**真正的三维方向** —— 该抬头就抬头、该俯冲就俯冲。
                if (期望.sqrMagnitude > 0.0001f)
                    飞行方向 = Vector3.RotateTowards(飞行方向, 期望.normalized,
                        追踪转向速度 * Mathf.Deg2Rad * Time.deltaTime, 0f).normalized;

                // 目标跑出神识范围 → 放弃
                if (追踪终止距离 > 0f
                    && Vector3.Distance(transform.position, 目标判定点(追踪目标)) > 追踪终止距离)
                { 消散消失(false); return; }

                // 追太久 → 放弃
                if (Time.time - 起飞时间 > Mathf.Max(0.5f, 最长追踪时间))
                { 消散消失(false); return; }
            }

            transform.position += 飞行方向 * 本帧能走;
            // 朝向跟着飞行方向（不追踪时方向恒定，等于没变）
            if (飞行方向.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.LookRotation(飞行方向, Vector3.up);
            已飞距离 += 本帧能走;
            补发沿路粒子(本帧能走);

            if (追踪目标 != null
                && Vector3.Distance(transform.position, 目标判定点(追踪目标)) <= Mathf.Max(0.2f, 命中半径))
            {
                到达();
                return;
            }

            if (拦截层.value != 0 && 被拦住了()) { 拦截(); return; }

            if (最大飞行距离 > 0f && 已飞距离 >= 最大飞行距离) { 消散消失(false); return; }

            最长寿命 -= Time.deltaTime;
            if (最长寿命 <= 0f) 消散消失(false);
            return;
        }

        Vector3 差 = 目标点 - transform.position;
        float 距离 = 差.magnitude;

        if (距离 <= Mathf.Max(0.08f, 本帧能走))
        {
            已飞距离 += 距离;
            transform.position = 目标点;
            到达();
            return;
        }

        transform.position += 差 / 距离 * 本帧能走;
        补发沿路粒子(本帧能走);
        已飞距离 += 本帧能走;

        // ---- 要素 4：能不能被拦下来 ----
        if (拦截层.value != 0 && 被拦住了())
        {
            拦截();
            return;
        }

        // ---- 要素 5：飞出范围 → 自行淡化消散 ----
        if (最大飞行距离 > 0f && 已飞距离 >= 最大飞行距离)
        {
            消散消失(false);
            return;
        }

        最长寿命 -= Time.deltaTime;
        if (最长寿命 <= 0f) 消散消失(false);
    }

    float 补发累计;

    /// <summary>按飞过的距离给「沿路粒子」补发（跟速度无关，所以快慢都均匀）</summary>
    void 补发沿路粒子(float 本帧距离)
    {
        if (沿路粒子 == null || 沿路粒子.Length == 0 || 每米补发粒子 <= 0f) return;

        补发累计 += 本帧距离 * 每米补发粒子;
        int 次 = Mathf.FloorToInt(补发累计);
        if (次 <= 0) return;
        补发累计 -= 次;
        if (次 > 8) 次 = 8;                       // 保险，别一帧喷太多

        for (int i = 0; i < 次; i++)
            foreach (var ps in 沿路粒子)
                if (ps != null) ps.Emit(1);
    }

    bool 被拦住了()
        => Physics.OverlapSphere(transform.position, Mathf.Max(0.05f, 拦截半径), 拦截层,
                                 QueryTriggerInteraction.Collide).Length > 0;

    /// <summary>到达目标点</summary>
    void 到达()
    {
        已结束 = true;
        命中点 = transform.position;      // ★ 记下来，命中特效要放这儿（不是目标身上）
        到达时?.Invoke();
        Destroy(gameObject);
    }

    /// <summary>被技能神通 / 法宝 / 灵阵 拦下来。外部也可以直接调它强制拦下</summary>
    public void 拦截()
    {
        if (已结束) return;
        消散消失(true);
    }

    /// <summary>停止飞行 + 淡化消散（消灭点）</summary>
    void 消散消失(bool 是被拦截)
    {
        if (已结束) return;
        已结束 = true;
        命中点 = transform.position;

        if (是被拦截) 被拦截时?.Invoke();

        if (消散带粒子)
            NpcDissolveEffect.播放(transform.position, 取自身半径(), 消散颜色, 消散粒子数,
                                    Mathf.Max(0.4f, 消散时长 + 0.4f));

        StartCoroutine(淡出然后销毁());
    }

    float 取自身半径()
    {
        var rs = GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return 0.25f;
        var b = rs[0].bounds;
        for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
        return Mathf.Max(0.12f, Mathf.Max(b.extents.x, Mathf.Max(b.extents.y, b.extents.z)));
    }

    System.Collections.IEnumerator 淡出然后销毁()
    {
        float 总 = Mathf.Max(0.05f, 消散时长);
        Vector3 原缩放 = transform.localScale;
        float t = 0f;
        while (t < 总)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(1f - t / 总);
            transform.localScale = 原缩放 * k;
            yield return null;
        }
        Destroy(gameObject);
    }

    // ---- ASCII 别名 ----
    public event 命中回调 OnArrive { add { 到达时 += value; } remove { 到达时 -= value; } }
    public event 命中回调 OnBlocked { add { 被拦截时 += value; } remove { 被拦截时 -= value; } }
    public void Block() => 拦截();
    public float TraveledDistance => 已飞距离;
}
