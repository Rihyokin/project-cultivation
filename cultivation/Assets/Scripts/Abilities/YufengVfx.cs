using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 【凭虚御风】的特效。全部程序化生成，不依赖任何美术资源。
///
///   悬浮中   · 脚下旋绕的风环（半透明，缓慢自转）
///            · 身周向上飘散的气流粒子
///   升空瞬间 · 地面炸开一圈扩散的起势气环
///   落地瞬间 · 地面砸出一圈冲击环 + 少量尘土
///
/// 挂在与 YufengFlight 同一个物体上即可，其余自动接线。
/// </summary>
// 注意：这里【不要】用 [RequireComponent(typeof(YufengFlight))]。
// 御风关闭时要由 PlayerAbilityLoader 把这套组件一起卸掉，
// 而 RequireComponent 会让 Unity 拒绝卸载 YufengFlight（"because YufengVfx depends on it"）。
// 改成运行时容错：拿不到 YufengFlight 就自己停摆。
public class YufengVfx : MonoBehaviour
{
    [Header("风环")]
    public Color 风环颜色 = new Color(0.55f, 0.88f, 1f, 0.55f);
    [Tooltip("风环半径")]
    public float 风环半径 = 1.15f;
    [Tooltip("风环相对角色脚底的高度")]
    public float 风环高度 = 0.12f;
    [Tooltip("自转速度（度/秒）")]
    public float 风环自转 = 55f;
    [Tooltip("悬浮出现/消失的淡入淡出速度")]
    public float 风环淡入速度 = 4f;

    [Header("上升气流")]
    public Color 气流颜色 = new Color(0.75f, 0.95f, 1f, 0.75f);
    [Tooltip("同时存在的粒子数")]
    public int 气流数量 = 14;
    [Tooltip("粒子上升速度")]
    public float 气流上升速度 = 1.6f;
    [Tooltip("粒子生成范围半径")]
    public float 气流半径 = 0.85f;
    [Tooltip("粒子存活时间")]
    public float 气流寿命 = 1.1f;

    [Header("爆发")]
    public Color 升空颜色 = new Color(0.7f, 0.95f, 1f, 1f);
    public Color 落地颜色 = new Color(0.85f, 0.9f, 1f, 1f);
    [Tooltip("升空气环扩散到的最大半径")]
    public float 升空环半径 = 3.2f;
    [Tooltip("落地冲击环扩散到的最大半径")]
    public float 落地环半径 = 2.4f;

    YufengFlight 御风;

    // 风环
    Transform 风环;
    Material 风环材质;
    float 风环透明度;

    // 上升气流
    class 粒子 { public Transform t; public float 生命; public float 速度; public float 相位; public float 起始半径; }
    readonly List<粒子> 气流 = new List<粒子>();
    Material 气流材质;

    // 爆发环
    class 爆发 { public Transform t; public Material m; public float 生命; public float 时长; public float 最大半径; }
    readonly List<爆发> 爆发环 = new List<爆发>();

    static Mesh 圆环网格;
    static Mesh 方片网格;
    static Texture2D 光斑;

    void Awake()
    {
        // 拿不到御风组件就整个停摆 —— 两个组件是被 PlayerAbilityLoader 一起装卸的，
        // 中间可能有一帧只存在其中一个。
        御风 = GetComponent<YufengFlight>();
        if (御风 == null) { enabled = false; return; }

        BuildRings();
        BuildStream();
    }

    /// <summary>
    /// 订阅「状态变化」。
    ///
    /// 【为什么放在 OnEnable，而不是 Awake】`YufengFlight.状态变化` 是个 **C# 事件**，
    /// 而 **`enabled = false` 拦不住 C# 事件回调** —— Unity 只对消息（Update/OnTrigger…）
    /// 看 enabled，委托链是你自己挂的，它照调不误。
    ///
    /// 本组件是被 <see cref="PlayerAbilityLoader"/> **启停**的（换功法 / 开关神通），
    /// 订阅若留在 Awake、退订只留在 OnDestroy，停用期间回调就还活着 ✗
    /// —— 和 <see cref="BasicSword01"/> 那把废飞剑是同一个坑（见开发注意事项 §29）。
    /// </summary>
    void OnEnable()
    {
        if (御风 == null) 御风 = GetComponent<YufengFlight>();
        if (御风 != null) 御风.状态变化 += OnStateChanged;
    }

    void OnDestroy()
    {
        if (御风 != null) 御风.状态变化 -= OnStateChanged;
        收起特效();          // 组件被销毁时也把挂件收干净
    }

    /// <summary>
    /// **被停用要收掉所有特效。**
    ///
    /// 【为什么需要】风环 / 上升气流 / 爆发环的**可见性和动画全部由 <see cref="Update"/> 驱动**，
    /// 而 Update 在 `enabled = false` 之后就不跑了 —— 没有任何人去收它们。
    ///
    /// 于是「**御风中换功法 / 关掉凭虚御风**」会**冻一个风环挂在脚下**（外加一排不动的上升气流）；
    /// 正好在落地那一瞬换功法，还会再冻一个爆发环 ✗
    ///
    /// 停用收干净，重新启用时 Update 会自己恢复
    /// （风环/气流是 <see cref="Awake"/> 里建好的，这里只是 SetActive(false)）✓
    /// </summary>
    void OnDisable()
    {
        if (御风 != null) 御风.状态变化 -= OnStateChanged;
        收起特效();
    }

    void 收起特效()
    {
        // 风环：藏起来并把透明度清零，重新启用且仍在御风中时会重新淡入
        if (风环 != null)
        {
            风环.gameObject.SetActive(false);
            风环透明度 = 0f;
        }

        for (int i = 0; i < 气流.Count; i++)
            if (气流[i].t != null) 气流[i].t.gameObject.SetActive(false);

        // 爆发环是一次性的，直接销毁（正常路径也是在 Update 里到点销毁）
        for (int i = 爆发环.Count - 1; i >= 0; i--)
        {
            if (爆发环[i].t != null)
            {
                if (Application.isPlaying) Destroy(爆发环[i].t.gameObject);
                else DestroyImmediate(爆发环[i].t.gameObject);
            }
            爆发环.RemoveAt(i);
        }
    }

    // ---------------------------------------------------------------- 构建

    void BuildRings()
    {
        var go = new GameObject("YufengWindRing");
        go.transform.SetParent(transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = RingMesh();
        var mat = MakeMaterial(风环颜色, 光斑贴图());
        风环材质 = mat;
        go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        go.transform.localScale = Vector3.one * 风环半径;
        风环 = go.transform;
        go.SetActive(false);
    }

    void BuildStream()
    {
        气流材质 = MakeMaterial(气流颜色, 光斑贴图());
        for (int i = 0; i < 气流数量; i++)
        {
            var go = new GameObject("Air" + i);
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = QuadMesh();
            go.AddComponent<MeshRenderer>().sharedMaterial = 气流材质;
            var p = new 粒子
            {
                t = go.transform,
                生命 = Random.Range(0f, 气流寿命),
                速度 = 气流上升速度 * Random.Range(0.7f, 1.35f),
                相位 = Random.Range(0f, Mathf.PI * 2f),
                起始半径 = Random.Range(0.25f, 气流半径),
            };
            go.transform.localScale = Vector3.one * Random.Range(0.07f, 0.14f);
            气流.Add(p);
        }
    }

    void OnStateChanged(YufengFlight.FlightState s)
    {
        if (s == YufengFlight.FlightState.升空) SpawnBurst(升空颜色, 升空环半径, 0.55f);
        else if (s == YufengFlight.FlightState.落地) SpawnBurst(落地颜色, 落地环半径, 0.45f);
    }

    void SpawnBurst(Color c, float maxRadius, float duration)
    {
        var go = new GameObject("YufengBurst");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = Vector3.zero;
        go.AddComponent<MeshFilter>().sharedMesh = RingMesh();
        var mat = MakeMaterial(c, 光斑贴图());
        go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        go.SetActive(false);   // 用 MeshRenderer 的 enabled 控制
        go.GetComponent<MeshRenderer>().enabled = true;
        爆发环.Add(new 爆发 { t = go.transform, m = mat, 生命 = duration, 时长 = duration, 最大半径 = maxRadius });
    }

    // ---------------------------------------------------------------- 每帧

    void Update()
    {
        float dt = Time.deltaTime;
        var cam = Camera.main;
        bool 在飞 = 御风 != null && 御风.御风流程中;

        // ---- 风环 ----
        if (风环 != null)
        {
            风环透明度 = Mathf.MoveTowards(风环透明度, 在飞 ? 1f : 0f, 风环淡入速度 * dt);
            bool 可见 = 风环透明度 > 0.01f;
            风环.gameObject.SetActive(可见);
            if (可见)
            {
                // 贴在角色脚下（角色被代码抬高了，所以环要跟着走）
                float 脚底 = 御风.地面高度 + 御风.高度偏移;
                风环.position = new Vector3(transform.position.x, 脚底 + 风环高度, transform.position.z);
                风环.Rotate(Vector3.up, 风环自转 * dt, Space.World);
                var c = 风环颜色; c.a *= 风环透明度;
                风环材质.color = c;
            }
        }

        // ---- 上升气流 ----
        if (气流材质 != null)
        {
            var c = 气流颜色; c.a *= 在飞 ? 1f : 0f;
            气流材质.color = c;
            for (int i = 0; i < 气流.Count; i++)
            {
                var p = 气流[i];
                if (!在飞) { p.t.gameObject.SetActive(false); continue; }
                p.t.gameObject.SetActive(true);

                p.生命 += dt;
                if (p.生命 > 气流寿命) { p.生命 = 0f; p.相位 = Random.Range(0f, Mathf.PI * 2f); p.起始半径 = Random.Range(0.25f, 气流半径); }

                float k = p.生命 / 气流寿命;              // 0→1
                float 半径 = p.起始半径 * (1f - k * 0.35f);
                float ang = p.相位 + k * 2.2f;
                float 底 = 御风.地面高度 + 御风.高度偏移 - 0.4f;
                p.t.position = new Vector3(
                    transform.position.x + Mathf.Cos(ang) * 半径,
                    底 + k * p.速度 * 气流寿命,
                    transform.position.z + Mathf.Sin(ang) * 半径);
                p.t.localScale = Vector3.one * Mathf.Lerp(0.13f, 0.02f, k);
                if (cam != null) p.t.rotation = Quaternion.LookRotation(p.t.position - cam.transform.position, cam.transform.up);
            }
        }

        // ---- 爆发环 ----
        for (int i = 爆发环.Count - 1; i >= 0; i--)
        {
            var b = 爆发环[i];
            b.生命 -= dt;
            if (b.生命 <= 0f)
            {
                Destroy(b.t.gameObject);
                爆发环.RemoveAt(i);
                continue;
            }
            float k = 1f - b.生命 / b.时长;                 // 0→1
            float 半径 = Mathf.Lerp(0.2f, b.最大半径, EaseOut(k));
            b.t.position = new Vector3(transform.position.x, 御风.地面高度 + 0.06f, transform.position.z);
            b.t.localScale = Vector3.one * 半径;
            var c = b.m.color; c.a = (1f - k) * 0.9f; b.m.color = c;
        }
    }

    static float EaseOut(float x) => 1f - Mathf.Pow(1f - x, 2.4f);

    // ---------------------------------------------------------------- 资源

    static Material MakeMaterial(Color c, Texture2D tex)
    {
        var m = new Material(Shader.Find("Sprites/Default"));
        m.mainTexture = tex;
        m.color = c;
        return m;
    }

    /// <summary>一个平躺在 XZ 平面的圆环（外径 1、内径 0.78），配合 localScale 缩放</summary>
    static Mesh RingMesh()
    {
        if (圆环网格 != null) return 圆环网格;
        const int N = 72;
        const float outer = 1f, inner = 0.78f;
        var m = new Mesh { name = "YufengRing" };
        var verts = new Vector3[N * 2];
        var tris = new int[N * 6];
        for (int i = 0; i < N; i++)
        {
            float a = (float)i / N * Mathf.PI * 2f;
            float c = Mathf.Cos(a), s = Mathf.Sin(a);
            verts[i * 2] = new Vector3(c * outer, 0f, s * outer);
            verts[i * 2 + 1] = new Vector3(c * inner, 0f, s * inner);
        }
        for (int i = 0; i < N; i++)
        {
            int n = (i + 1) % N, t = i * 6;
            tris[t] = i * 2; tris[t + 1] = n * 2; tris[t + 2] = n * 2 + 1;
            tris[t + 3] = i * 2; tris[t + 4] = n * 2 + 1; tris[t + 5] = i * 2 + 1;
        }
        m.vertices = verts; m.triangles = tris;
        m.RecalculateBounds();
        圆环网格 = m;
        圆环网格.hideFlags = HideFlags.HideAndDontSave;
        return 圆环网格;
    }

    static Mesh QuadMesh()
    {
        if (方片网格 != null) return 方片网格;
        var m = new Mesh { name = "YufengQuad" };
        m.vertices = new[]
        {
            new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
            new Vector3(-0.5f,  0.5f, 0f), new Vector3(0.5f,  0.5f, 0f)
        };
        m.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
        m.triangles = new[] { 0, 2, 1, 2, 3, 1 };
        m.RecalculateBounds();
        方片网格 = m;
        方片网格.hideFlags = HideFlags.HideAndDontSave;
        return 方片网格;
    }

    static Texture2D 光斑贴图()
    {
        if (光斑 != null) return 光斑;
        const int S = 64;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        var px = new Color32[S * S];
        float half = S * 0.5f;
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float dx = (x + 0.5f - half) / half, dy = (y + 0.5f - half) / half;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01(1f - r); a *= a;
                px[y * S + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        tex.SetPixels32(px); tex.Apply();
        tex.hideFlags = HideFlags.HideAndDontSave;
        光斑 = tex;
        return 光斑;
    }
}
