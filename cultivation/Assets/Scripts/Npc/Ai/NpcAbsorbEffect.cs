using UnityEngine;

/// <summary>
/// **打死怪之后「粒子被玩家吸收」的那套动画**（用户要求）。
///
/// 四段：
///   1. **炸开** —— 从身躯里炸出一批粒子（起手和老的消散一样）
///   2. **落地** —— 粒子往下掉，**碰到地面就停住**（不像老的那样各自淡出）
///   3. **等 1 秒** —— 全落地之后原地停 1 秒
///   4. **飞向玩家** —— 加速飞向玩家胸口，越飞越小，**贴到身上就消失** → "被玩家吸收"
///
/// ## 为什么这四段要自己写，不能用 ParticleSystem 的模块
///
/// 「**落地停住 → 再朝玩家飞**」是**逐粒子**的分段行为：
///   · `velocityOverLifetime` 是**整团一个向量**，做不到每颗粒子朝各自的方向飞向玩家
///   · 粒子的「落地」是**世界坐标上的一个碰撞点**，模块里没有
///
/// 所以这里把粒子**完全接管**：`startSpeed = 0` + `gravityModifier = 0`（粒子自己一步都不会动），
/// 每帧 `GetParticles` → 手动积分位置 → `SetParticles`。
/// 粒子上限只有几十个，这点开销可以忽略。
///
/// 材质 / 贴图复用 <see cref="NpcDissolveEffect.取材质"/>（同一套程序生成的柔边圆点）。
/// </summary>
public class NpcAbsorbEffect : MonoBehaviour
{
    [Header("1 炸开")]
    [Tooltip("**炸开高度（米）**：粒子的最高点比出生点高多少。\n\n" +
             "【为什么按「高度」而不是按「初速」定】\n" +
             "以前是 初速 = 身躯半径 × 系数。人的身躯半径只有 0.4 左右，算出来初速 0.9 米/秒，\n" +
             "在 重力 20 下只能弹起 **2 厘米** —— 等于没炸开，粒子看起来还是「直接落地」。\n" +
             "改成先定高度，初速由 `v = √(2gh)` 反推，**大小怪都能看到同样明显的炸开**。")]
    public float 爆开高度 = 0.40f;

    [Tooltip("**每高 1 级境界，炸开高度多这个米数**（「境界越高越有气势」的一部分，\n" +
             "另一部分是粒子数）。0.0035 → 境界 90 比境界 1 高 0.31 米")]
    public float 每级爆开高度 = 0.0035f;

    [Tooltip("**身躯越大的怪炸得越高**：再额外加 身躯半径 × 这个值。\n" +
             "宝箱怪那种胖怪会比人形怪多弹一截，但小怪也仍然有基础的 爆开高度")]
    public float 身躯高度加成 = 0.5f;

    [Tooltip("炸开的**仰角**：0 = 45°（最散、最矮），1 = 90°（纯上下、最高）。\n" +
             "0.7 ≈ 76°：主要是往上弹，横向只散开一点点")]
    [Range(0f, 1f)]
    public float 上抛比例 = 0.7f;

    [Tooltip("初始散布半径 = 身躯半径 × 这个值（粒子出生在身体里，别一出来就散开）")]
    public float 散开半径系数 = 0.7f;

    [Tooltip("**落点圆的半径 = 身躯半径 × 这个值** —— 「炸开范围」的主开关。\n\n" +
             "粒子的**横向初速会按预测落点反过来压**，让每颗粒子落在圈内随机远近处\n" +
             "（而不是全被夹到圈边上、落地变成一个圆环）。1.5 ≈ 圈比怪自己的占地大一半")]
    public float 落点半径系数 = 1.5f;

    [Header("2 落地")]
    [Tooltip("往下掉的加速度（米/秒²）。比真重力大一些，落地干脆")]
    public float 重力 = 20f;

    [Tooltip("最多掉这么久就强制落地（防止卡在斜坡 / 悬空）")]
    public float 最长下落时长 = 2.5f;

    [Tooltip("落点离地高度（米），别埋进地里")]
    public float 落地高度 = 0.06f;

    [Header("3 等待")]
    [Tooltip("全部落地之后停多久才开始飞向玩家（秒）。**用户定 1 秒**")]
    public float 落地等待 = 1f;

    [Header("4 飞向玩家")]
    [Tooltip("飞向玩家时的加速度（米/秒²）")]
    public float 吸附加速度 = 60f;

    [Tooltip("飞向玩家时的速度上限（米/秒）")]
    public float 吸附最快 = 16f;

    [Tooltip("离玩家多近就算「吸到身上」并消失（米）")]
    public float 到达距离 = 0.5f;

    [Tooltip("**靠近时减速的系数**：期望速度 = min(吸附最快, 距离 × 这个值)。\n\n" +
             "【为什么要有】不加的话粒子会以 16 米/秒从玩家**旁边擦过去**，" +
             "绕一圈再回来（实测：距离 2.02 → 3.06 → 才吸掉）✗\n" +
             "加上之后越近越慢，是「被吸住」而不是「弹过去」")]
    public float 减速系数 = 6f;

    [Tooltip("吸附末段把粒子缩到多小（1 = 不缩）。越小越像「被吸进去」")]
    [Range(0.05f, 1f)]
    public float 吸附末端缩放 = 0.25f;

    [Tooltip("飞向玩家最多飞这么久（秒），到点还没吸完就直接收掉")]
    public float 吸附最长时长 = 4f;

    [Header("外观")]
    [Tooltip("**单颗粒子大小 = 身躯半径 × 这个范围。**\n\n" +
             "以前是 0.16~0.4（还要再乘那个 3 倍缩放）→ 一颗半米多，像一团团棉花。\n" +
             "现在按身躯半径直接算，默认就是些小光点")]
    public Vector2 大小系数 = new Vector2(0.10f, 0.22f);

    [Tooltip("打印阶段切换日志")]
    public bool 打印日志 = false;

    // ============================================================ 运行时

    enum 阶段 { 下落, 等待, 吸附, 收尾 }

    ParticleSystem ps;
    ParticleSystem.Particle[] 缓冲;
    Vector3[] 速度;
    float[] 原始大小;
    bool[] 已落地;
    float 半径;
    int 境界 = 1;
    Transform 玩家;
    Vector3 目标点;
    float 地面Y;
    float 阶段时间;
    bool 已给初速;
    阶段 当前阶段 = 阶段.下落;

    /// <summary>被兜底夹回圈内的粒子数（调试用：正常应该接近 0）</summary>
    public int 被拉回圈内数 { get; private set; }

    // ============================================================ 粒子数：按境界

    /// <summary>
    /// **粒子数按 NPC 的境界走**（用户要求：「npc 境界越高，炸出来的粒子数量越多」）。
    ///
    /// `数量 = 基础 + 每级 × 境界`，再夹到 [最少, 最多]。实测出来的梯度：
    ///
    /// | 境界 | 粒子数 |
    /// |---|---|
    /// | 1 | ~15 |
    /// | 20 | ~39 |
    /// | 50 | ~76 |
    /// | 81 | ~115 |
    /// | 90 | ~126 |
    ///
    /// 单个 NPC 想自己定数量的，填 <see cref="NpcAiBase.死亡消散粒子数"/> 覆盖这里。
    /// </summary>
    public static int 按境界算粒子数(int 境界)
    {
        境界 = Mathf.Clamp(境界, 1, 90);
        return Mathf.Clamp(Mathf.RoundToInt(基础粒子数 + 每级粒子数 * 境界), 最少粒子数, 最多粒子数);
    }

    /// <summary>境界 0 时的基础粒子数</summary>
    public const int 基础粒子数 = 14;

    /// <summary>每 1 级境界多加几颗</summary>
    public const float 每级粒子数 = 1.25f;

    /// <summary>下限 / 上限</summary>
    public const int 最少粒子数 = 16, 最多粒子数 = 150;

    // ============================================================ 入口

    /// <summary>在指定位置放一团「会被玩家吸收」的死亡粒子。</summary>
    /// <param name="中心">从哪炸（一般给身躯包围盒中心）</param>
    /// <param name="半径">身躯半径：定粒子大小、初速、散布</param>
    /// <param name="颜色">粒子颜色（**全程不淡出**，整颗飞进玩家身上才消失）</param>
    /// <param name="粒子数">0 = 按境界自动算（<see cref="按境界算粒子数"/>）</param>
    /// <param name="要忽略的">做落地射线时**跳过它自己**（尸体的碰撞体）</param>
    /// <param name="境界">NPC 境界：**越高粒子越多、炸开越有力**</param>
    public static NpcAbsorbEffect 播放(Vector3 中心, float 半径, Color 颜色,
                                        int 粒子数 = 0, Transform 要忽略的 = null, int 境界 = 1)
    {
        半径 = Mathf.Max(0.08f, 半径);
        if (粒子数 <= 0) 粒子数 = 按境界算粒子数(境界);

        var go = new GameObject("NpcAbsorbFx", typeof(ParticleSystem));
        go.transform.position = 中心;

        var fx = go.AddComponent<NpcAbsorbEffect>();
        fx.构建(go.GetComponent<ParticleSystem>(), 半径, 颜色, 粒子数, 要忽略的, 境界);

        // 兜底：整段流程最多这么久，之后强制销毁（正常路径会自己提前销毁）
        Object.Destroy(go, fx.最长下落时长 + fx.落地等待 + fx.吸附最长时长 + 2f);
        return fx;
    }

    void 构建(ParticleSystem 系统, float 身躯半径, Color 颜色, int 数量, Transform 要忽略的, int 境界)
    {
        ps = 系统;
        半径 = 身躯半径;
        this.境界 = Mathf.Clamp(境界, 1, 90);
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.duration = 0.1f;
        main.loop = false;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;   // ★ 世界空间：我们自己写世界坐标
        main.startSpeed = 0f;            // ★ 粒子自己不动，全靠我们每帧积分
        main.startLifetime = 60f;        // 长寿命；什么时候"消失"由我们说了算
        main.startSize = new ParticleSystem.MinMaxCurve(半径 * 大小系数.x, 半径 * 大小系数.y);
        main.startColor = 颜色;
        main.gravityModifier = 0f;       // ★ 重力也自己算
        main.maxParticles = 数量 + 8;

        var emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)Mathf.Max(1, 数量)) });

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 半径 * Mathf.Max(0.05f, 散开半径系数);   // 出生在身体里，别一出来就散开
        shape.radiusThickness = 1f;

        // **不淡出**：整段动画里粒子都是实的
        //（老消散的"各自随机淡出"就是靠 colorOverLifetime，这里必须关掉）
        var col = ps.colorOverLifetime;
        col.enabled = false;

        // 也省掉 sizeOverLifetime —— 吸附段的缩小是我们按距离自己写的
        var sol = ps.sizeOverLifetime;
        sol.enabled = false;

        var rot = ps.rotationOverLifetime;
        rot.enabled = true;
        rot.z = new ParticleSystem.MinMaxCurve(-180f, 180f);

        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        if (renderer != null)
        {
            var m = NpcDissolveEffect.取材质();
            if (m != null) renderer.sharedMaterial = m;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sortingOrder = 150;      // 血条 100 之上、伤害飘字 200 之下
            renderer.alignment = ParticleSystemRenderSpace.View;
        }

        缓冲 = new ParticleSystem.Particle[数量 + 8];
        速度 = new Vector3[数量 + 8];
        原始大小 = new float[数量 + 8];
        已落地 = new bool[数量 + 8];

        玩家 = 取玩家();
        地面Y = 取地面高度(transform.position, 半径, 要忽略的, 玩家);
        目标点 = 玩家 != null ? 玩家.position + Vector3.up * 0.9f
                             : transform.position + Vector3.up;

        ps.Play();
    }

    static Transform 取玩家()
    {
        var v = Object.FindObjectOfType<PlayerVitals>();
        return v != null ? v.transform : null;
    }

    /// <summary>从中心往下打射线找准地面高度（跳过尸体自己 / 玩家）</summary>
    static float 取地面高度(Vector3 中心, float 半径, Transform 要忽略的, Transform 玩家)
    {
        Vector3 起点 = 中心 + Vector3.up * (半径 + 0.4f);
        float 最好 = float.MaxValue;
        float 结果 = 中心.y - 半径;        // 兜底：打不到地面就用身体底部

        foreach (var h in Physics.RaycastAll(起点, Vector3.down, 起点.y + 300f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (h.collider == null) continue;
            var t = h.collider.transform;
            if (要忽略的 != null && (t == 要忽略的 || t.IsChildOf(要忽略的))) continue;
            if (玩家 != null && (t == 玩家 || t.IsChildOf(玩家))) continue;
            if (h.distance < 最好) { 最好 = h.distance; 结果 = h.point.y; }
        }
        return 结果;
    }

    // ============================================================ 主循环

    void Update()
    {
        if (ps == null) return;
        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        int n = ps.GetParticles(缓冲);
        if (n <= 0)
        {
            if (已给初速) 结束();          // 粒子已经全没了
            return;
        }

        if (!已给初速) { 赋初速(n); 已给初速 = true; }

        阶段时间 += dt;
        switch (当前阶段)
        {
            case 阶段.下落: 推进下落(n, dt); break;
            case 阶段.等待:
                if (阶段时间 >= 落地等待) 切到(阶段.吸附);
                break;
            case 阶段.吸附: 推进吸附(n, dt); break;
            case 阶段.收尾: 推进收尾(n, dt); break;
        }

        ps.SetParticles(缓冲, n);

        if (ps.particleCount == 0) 结束();
    }

    void 切到(阶段 s)
    {
        当前阶段 = s;
        阶段时间 = 0f;
        if (打印日志) Debug.Log("[吸收] → " + s, this);
    }

    void 结束()
    {
        if (打印日志) Debug.Log("[吸收] 完成，销毁特效物件", this);
        Destroy(gameObject);
    }

    /// <summary>
    /// 第一帧：给每颗粒子一个「朝外 + 往上」的初速。
    ///
    /// **这里做的是一次「反推」**：先按 <see cref="爆开高度"/> 定好这颗粒子要弹多高，
    /// 用 `vy = √(2gh)` 反推竖直初速；再算出落下需要多久、横向会飘多远，
    /// **如果会飘出「落点圆」就把横向初速压回来**，让它落在圈内随机远近处。
    ///
    /// 这么写的好处：落地时**不需要**再硬把粒子拉回圈里（那样全挤在圈边上、像个圆环），
    /// 每颗粒子的落点是「随机散在圈里」的，更像自然的炸开。
    /// </summary>
    void 赋初速(int n)
    {
        float g = Mathf.Max(1f, 重力);
        float 圈 = 半径 * Mathf.Max(0.1f, 落点半径系数);
        // 境界 / 体型 → 这次炸开要弹多高
        float 目标高度 = Mathf.Max(0.02f, 爆开高度)
                       + Mathf.Max(0f, 每级爆开高度) * (境界 - 1)
                       + 半径 * Mathf.Max(0f, 身躯高度加成);
        // 仰角：上抛比例 0 → 45°（最散），1 → 90°（纯上下）
        float 仰角 = Mathf.Lerp(45f, 90f, Mathf.Clamp01(上抛比例)) * Mathf.Deg2Rad;
        float sin = Mathf.Sin(仰角), cos = Mathf.Cos(仰角);

        for (int i = 0; i < n; i++)
        {
            // 水平方向：随机一圈
            float 角 = Random.Range(0f, Mathf.PI * 2f);
            Vector2 平 = new Vector2(Mathf.Cos(角), Mathf.Sin(角));

            // 每颗粒子高矮不一，整团才有层次
            float 高度 = 目标高度 * Random.Range(0.55f, 1.15f);
            float vy = Mathf.Sqrt(2f * g * 高度);
            float vh = vy / Mathf.Max(0.05f, sin) * cos;      // 横向初速

            // 预测落点：上升 vy/g，再从「出生高度 + 弹起高度」落到地面
            float 出生离地 = Mathf.Max(0f, 缓冲[i].position.y - (地面Y + 落地高度));
            float 滞空 = vy / g + Mathf.Sqrt(2f * (出生离地 + 高度) / g);
            // 圈内随机远近（0.25~1.0 倍圈半径）；**先扣掉出生时就有的横向偏移**，
            // 否则从球外侧出生的粒子会从「已经偏出去」的位置再飘一段，落到圈外。
            float 出生偏移 = new Vector2(缓冲[i].position.x - transform.position.x,
                                         缓冲[i].position.z - transform.position.z).magnitude;
            float 想落 = Mathf.Max(0f, 圈 * Random.Range(0.25f, 1f) - 出生偏移);
            if (vh * 滞空 > 想落) vh = 想落 / Mathf.Max(0.01f, 滞空);

            速度[i] = new Vector3(平.x * vh, vy, 平.y * vh);
            原始大小[i] = 缓冲[i].startSize;
            已落地[i] = false;
            缓冲[i].velocity = Vector3.zero;      // 不让 ParticleSystem 再积分一次
        }
    }

    void 推进下落(int n, float dt)
    {
        bool 全落地 = true;
        bool 超时 = 阶段时间 >= 最长下落时长;
        float 落点Y = 地面Y + 落地高度;
        float 圈 = 半径 * Mathf.Max(0.1f, 落点半径系数);     // 「原地一小圈」的半径
        Vector3 圆心 = transform.position;

        for (int i = 0; i < n; i++)
        {
            if (已落地[i]) continue;

            Vector3 v = 速度[i];
            v.y -= 重力 * dt;
            Vector3 p = 缓冲[i].position + v * dt;

            if (p.y <= 落点Y || 超时)
            {
                // ★ 兜底：正常情况下落点已经在圈内（见 赋初速 的反推），
                //   只有地形高低差之类的意外才会走到这里被拉回圈边。
                Vector3 平 = new Vector3(p.x - 圆心.x, 0f, p.z - 圆心.z);
                if (平.magnitude > 圈) { 平 = 平.normalized * 圈; 被拉回圈内数++; }
                p = new Vector3(圆心.x + 平.x, 落点Y, 圆心.z + 平.z);

                速度[i] = Vector3.zero;
                已落地[i] = true;
            }
            else 速度[i] = v;

            缓冲[i].position = p;
            缓冲[i].velocity = Vector3.zero;
        }

        for (int i = 0; i < n; i++) if (!已落地[i]) { 全落地 = false; break; }

        if (全落地 || 超时)
        {
            if (打印日志) Debug.Log("[吸收] 全部落地（超时=" + 超时 + "）", this);
            切到(阶段.等待);
        }
    }

    void 推进吸附(int n, float dt)
    {
        if (玩家 == null) { 切到(阶段.收尾); return; }     // 玩家没了 → 改成原地缩没
        目标点 = 玩家.position + Vector3.up * 0.9f;

        if (阶段时间 >= 吸附最长时长) { 收掉全部(n); return; }

        for (int i = 0; i < n; i++)
        {
            Vector3 p = 缓冲[i].position;
            Vector3 向 = 目标点 - p;
            float d = 向.magnitude;

            if (d <= 到达距离)
            {
                缓冲[i].remainingLifetime = 0f;           // ★ 在玩家身上消失 = 被吸收
                continue;
            }

            Vector3 v = Vector3.MoveTowards(速度[i], 向 / d * 期望速度(d), 吸附加速度 * dt);
            速度[i] = v;
            缓冲[i].position = p + v * dt;
            缓冲[i].velocity = Vector3.zero;

            // 越靠近越小 → "被吸进去"的感觉
            float k = Mathf.Clamp01(d / 3f);
            缓冲[i].startSize = 原始大小[i] * Mathf.Lerp(吸附末端缩放, 1f, k);
        }
    }

    /// <summary>期望速度：远处全速，近处按距离减速 —— 免得从玩家旁边擦过去绕圈</summary>
    float 期望速度(float 距离)
        => Mathf.Min(吸附最快, 距离 * Mathf.Max(0.1f, 减速系数));

    void 推进收尾(int n, float dt)
    {
        // 玩家不在了：原地缩小消失，不做无限等待
        float 缩 = Mathf.Clamp01(1f - 阶段时间 / 0.6f);
        for (int i = 0; i < n; i++)
        {
            缓冲[i].startSize = 原始大小[i] * 缩;
            if (缩 <= 0.01f) 缓冲[i].remainingLifetime = 0f;
        }
        if (缩 <= 0.01f) 结束();
    }

    void 收掉全部(int n)
    {
        for (int i = 0; i < n; i++) 缓冲[i].remainingLifetime = 0f;
    }

    // ---- ASCII 别名 ----
    public static NpcAbsorbEffect Play(Vector3 center, float radius, Color color,
                                       int count = 0, Transform ignore = null, int level = 1)
        => 播放(center, radius, color, count, ignore, level);
    public static int CountByLevel(int level) => 按境界算粒子数(level);
}
