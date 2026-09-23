using UnityEngine;

/// <summary>
/// 通用「消散」特效 —— **纯代码生成，不依赖任何美术资产**。
///
/// 效果：从身躯里**炸开一批粒子**，每颗粒子自己的寿命是随机的，
/// 于是它们**在不同时刻各自缩小、淡出**，看起来就是「随机逐渐消失」。
///
/// 为什么不用特效包里的资产：那些效果是为技能做的大场面（几十米），
/// 拿来当小动物的消散要么太大要么风格不搭；通用版按体型缩放，谁都能用。
///
/// 用法（<see cref="NpcAiBase.播放消散特效"/> 已经接好，一般不用手动调）：
/// <code>NpcDissolveEffect.播放(中心, 半径, 颜色, 粒子数);</code>
/// </summary>
public class NpcDissolveEffect : MonoBehaviour
{
    [Tooltip("炸开的初速 = 半径 × 这个系数")]
    public float 速度系数 = 3.2f;

    [Tooltip("粒子寿命下限/上限（秒）。**随机寿命就是「随机逐渐消失」的来源**")]
    public Vector2 寿命范围 = new Vector2(0.65f, 1.4f);

    [Tooltip("单颗粒子大小 = 半径 × 这个范围")]
    public Vector2 大小系数 = new Vector2(0.16f, 0.4f);

    [Tooltip("往下掉的幅度（正数往下）")]
    public float 重力 = 0.12f;

    // ============================================================ 播放

    /// <summary>在指定位置炸开一团消散粒子</summary>
    /// <param name="中心">从哪里炸（一般给身躯包围盒中心）</param>
    /// <param name="半径">身躯半径，用来定粒子大小、初速和散布范围</param>
    /// <param name="颜色">粒子颜色（会跟着各自的透明度淡出）</param>
    /// <param name="粒子数">数量，0 = 按半径自动算</param>
    /// <param name="存活">多少秒后销毁这个特效物件</param>
    public static NpcDissolveEffect 播放(Vector3 中心, float 半径, Color 颜色,
                                          int 粒子数 = 0, float 存活 = 0f)
    {
        半径 = Mathf.Max(0.08f, 半径);
        if (粒子数 <= 0) 粒子数 = Mathf.Clamp(Mathf.RoundToInt(半径 * 260f), 24, 160);

        var go = new GameObject("NpcDissolveFx", typeof(ParticleSystem));
        go.transform.position = 中心;

        var fx = go.AddComponent<NpcDissolveEffect>();
        fx.构建(go.GetComponent<ParticleSystem>(), 半径, 颜色, 粒子数);

        float 最长 = fx.寿命范围.y;
        Object.Destroy(go, 存活 > 0f ? 存活 : 最长 + 0.4f);
        return fx;
    }

    void 构建(ParticleSystem ps, float 半径, Color 颜色, int 粒子数)
    {
        // 改 main 之前先 Stop，否则 Unity 会警告
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.duration = 0.35f;
        main.loop = false;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(寿命范围.x, 寿命范围.y);
        main.startSpeed = new ParticleSystem.MinMaxCurve(半径 * 速度系数 * 0.5f, 半径 * 速度系数);
        main.startSize = new ParticleSystem.MinMaxCurve(半径 * 大小系数.x, 半径 * 大小系数.y);
        main.startColor = 颜色;
        main.gravityModifier = 重力;
        main.maxParticles = 粒子数 + 8;

        // 一次性炸开，不持续发射
        var emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)Mathf.Max(1, 粒子数)) });

        // 从身躯里散开
        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 半径 * 0.85f;
        shape.radiusThickness = 1f;

        // 随机逐渐消失：粒子一出场就是透明的，很快亮起来，
        // 中段保持，最后淡出；**再叠上各自随机的寿命**，于是它们在不同时刻依次消失
        var col = ps.colorOverLifetime;
        col.enabled = true;
        var 渐变 = new Gradient();
        渐变.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[]
            {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(1f, 0.1f),
                new GradientAlphaKey(1f, 0.4f),
                new GradientAlphaKey(0f, 1f),
            });
        col.color = new ParticleSystem.MinMaxGradient(渐变);

        // 一边飘一边缩小
        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, 1f), new Keyframe(0.55f, 0.85f), new Keyframe(1f, 0.1f)));

        // 稍微乱转一点，别像一堆整齐的球
        var rot = ps.rotationOverLifetime;
        rot.enabled = true;
        rot.z = new ParticleSystem.MinMaxCurve(-180f, 180f);

        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        if (renderer != null)
        {
            var m = 取材质();
            if (m != null) renderer.sharedMaterial = m;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sortingOrder = 150;      // 血条 100 之上、伤害飘字 200 之下
            renderer.alignment = ParticleSystemRenderSpace.View;
        }

        ps.Play();
    }

    // ============================================================ 材质与贴图

    static Material 缓存材质;
    static Texture2D 缓存贴图;

    /// <summary>柔边圆形贴图：中心白、边缘透明。程序生成，不需要美术资源</summary>
    static Texture2D 取贴图()
    {
        if (缓存贴图 != null) return 缓存贴图;

        const int S = 64;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        float 半 = (S - 1) * 0.5f;

        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float d = Mathf.Sqrt((x - 半) * (x - 半) + (y - 半) * (y - 半)) / 半;
                // 中心实、边缘柔和衰减
                float a = Mathf.Clamp01(1f - d);
                a = a * a * (3f - 2f * a);          // smoothstep，边缘更软
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        tex.Apply();
        缓存贴图 = tex;
        return tex;
    }

    /// <summary>
    /// 柔边圆点材质（**同一套给 <see cref="NpcAbsorbEffect"/> 复用**，免得两份贴图各画一遍）。
    /// 运行时生成一次，之后走缓存。
    /// </summary>
    public static Material 取材质()
    {
        if (缓存材质 != null) return 缓存材质;

        // 按顺序找一个能用的（不同渲染管线可用的不一样）
        Shader sh = Shader.Find("Sprites/Default")
                 ?? Shader.Find("Legacy Shaders/Particles/Alpha Blended")
                 ?? Shader.Find("Particles/Standard Unlit")
                 ?? Shader.Find("Unlit/Transparent");
        if (sh == null)
        {
            Debug.LogWarning("[消散] 找不到可用的粒子 Shader，消散会看不见");
            return null;
        }

        var m = new Material(sh) { name = "NpcDissolve(运行时生成)" };
        m.mainTexture = 取贴图();
        if (m.HasProperty("_Color")) m.SetColor("_Color", Color.white);
        if (m.HasProperty("_TintColor")) m.SetColor("_TintColor", Color.white);
        缓存材质 = m;
        return m;
    }

    // ---- ASCII 别名 ----
    public static NpcDissolveEffect Play(Vector3 center, float radius, Color color,
                                         int count = 0, float lifetime = 0f)
        => 播放(center, radius, color, count, lifetime);
}
