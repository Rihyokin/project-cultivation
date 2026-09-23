using UnityEngine;

/// <summary>
/// 【basic_sword_01 专属】命中特效：飞剑穿过敌人并命中时，在穿过点炸开一小簇火花。
/// 完全程序化生成（不需要任何美术资源 / 粒子系统 asset）：
///   · 冲击光环 —— 一圈快速扩散并淡出的亮环，最抓眼
///   · 光晕     —— 中心一团大光斑
///   · 斩击光条 —— 顺着剑身方向拉长的一道亮光
///   · 火花     —— 十几颗小光点沿剑的垂直方向飞散，带重力与阻尼
///   · 暴击时更暖、更大、火花更多
///
/// 用法：BasicSword01HitEffect.Spawn(位置, 飞行方向, 是否暴击);
/// </summary>
public class BasicSword01HitEffect : MonoBehaviour
{
    /// <summary>播放一次命中特效</summary>
    public static void Spawn(Vector3 position, Vector3 direction, bool crit)
    {
        var go = new GameObject(crit ? "HitFX_Crit" : "HitFX");
        go.transform.position = position;

        var fx = go.AddComponent<BasicSword01HitEffect>();
        fx.crit = crit;
        fx.dir = direction.sqrMagnitude > 1e-4f ? direction.normalized : Vector3.forward;
        fx.Build();
    }

    bool crit;
    Vector3 dir = Vector3.forward;

    // 整体规模系数：暴击更大
    float K => crit ? 1.45f : 1f;

    Transform halo;      Material haloMat;   float haloLife;
    Transform glow;      Material glowMat;   float glowLife;
    Transform streak;    Material streakMat; float streakLife;
    Transform[] sparks;  Material sparkMat;  Vector3[] sparkVel; float sparkLife;

    const float 光环时长 = 0.34f;
    const float 光晕时长 = 0.42f;
    const float 光条时长 = 0.30f;
    float 火花时长;

    // ---------------------------------------------------------------- 贴图

    static Texture2D 光斑;
    /// <summary>64x64 径向渐变（中心实、边缘透明）</summary>
    static Texture2D 取光斑()
    {
        if (光斑 != null) return 光斑;
        const int S = 64;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        var px = new Color32[S * S];
        float half = S * 0.5f;
        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                float dx = (x + 0.5f - half) / half;
                float dy = (y + 0.5f - half) / half;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01(1f - r);
                a *= a;
                px[y * S + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        }
        tex.SetPixels32(px); tex.Apply();
        tex.hideFlags = HideFlags.HideAndDontSave;
        光斑 = tex;
        return 光斑;
    }

    static Texture2D 光环贴图;
    /// <summary>一张「空心环」贴图：半径在 0.62~1.0 之间的亮环，用来做冲击波</summary>
    static Texture2D 取光环()
    {
        if (光环贴图 != null) return 光环贴图;
        const int S = 128;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        var px = new Color32[S * S];
        float half = S * 0.5f;
        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                float dx = (x + 0.5f - half) / half;
                float dy = (y + 0.5f - half) / half;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                // 环带：中心 0.78，向内外衰减
                float a = Mathf.Clamp01(1f - Mathf.Abs(r - 0.78f) / 0.24f);
                a *= a;
                px[y * S + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        }
        tex.SetPixels32(px); tex.Apply();
        tex.hideFlags = HideFlags.HideAndDontSave;
        光环贴图 = tex;
        return 光环贴图;
    }

    // ---------------------------------------------------------------- 构建

    void Build()
    {
        var 主色 = crit ? new Color(1f, 0.78f, 0.28f) : new Color(0.55f, 0.90f, 1f);
        float k = K;

        // ---- 冲击光环 ----
        halo = 建元素("Halo", 主色, 1f, 取光环(), true);
        halo.localScale = Vector3.one * 0.35f * k;
        haloLife = 光环时长;

        // ---- 中心光晕 ----
        glow = 建元素("Glow", new Color(1f, 1f, 1f), 1f, 取光斑(), false);
        glow.localScale = Vector3.one * 0.32f * k;
        glowLife = 光晕时长;

        // ---- 斩击光条 ----
        streak = 建元素("Streak", 主色, 1f, 取光斑(), false);
        streakLife = 光条时长;

        // ---- 火花 ----
        int n = crit ? 22 : 14;
        sparks = new Transform[n];
        sparkVel = new Vector3[n];
        sparkMat = 建材质(crit ? new Color(1f, 0.72f, 0.26f) : new Color(0.88f, 0.98f, 1f), 1f, 取光斑());

        for (int i = 0; i < n; i++)
        {
            var s = new GameObject("Spark" + i);
            s.transform.SetParent(transform, false);
            s.AddComponent<MeshFilter>().sharedMesh = 方片();
            s.AddComponent<MeshRenderer>().sharedMaterial = sparkMat;
            s.transform.localScale = Vector3.one * Random.Range(0.10f, 0.20f) * k;
            s.transform.position = transform.position;

            Vector3 away = Vector3.ProjectOnPlane(Random.onUnitSphere, dir);
            if (away.sqrMagnitude < 1e-4f) away = Vector3.up;
            away.Normalize();
            float speed = Random.Range(3.2f, 7.5f) * k;
            sparkVel[i] = (away + dir * Random.Range(-0.35f, 0.85f)).normalized * speed;

            sparks[i] = s.transform;
        }
        火花时长 = crit ? 0.60f : 0.50f;
        sparkLife = 火花时长;
    }

    Transform 建元素(string name, Color color, float alpha, Texture2D tex, bool isHalo)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = 方片();
        var mat = 建材质(color, alpha, tex);
        go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        if (isHalo) haloMat = mat; else if (name == "Glow") glowMat = mat; else streakMat = mat;
        return go.transform;
    }

    static Material 建材质(Color c, float a, Texture2D tex)
    {
        var m = new Material(Shader.Find("Sprites/Default"));
        m.mainTexture = tex;
        m.color = new Color(c.r, c.g, c.b, a);
        return m;
    }

    static Mesh 方片Mesh;
    static Mesh 方片()
    {
        if (方片Mesh != null) return 方片Mesh;
        var m = new Mesh { name = "FXQuad" };
        m.vertices = new[]
        {
            new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
            new Vector3(-0.5f,  0.5f, 0f), new Vector3(0.5f,  0.5f, 0f)
        };
        m.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
        m.triangles = new[] { 0, 2, 1, 2, 3, 1 };
        m.RecalculateBounds();
        方片Mesh = m;
        方片Mesh.hideFlags = HideFlags.HideAndDontSave;
        return 方片Mesh;
    }

    // ---------------------------------------------------------------- 动画

    void Update()
    {
        float dt = Time.deltaTime;
        var cam = Camera.main;
        if (cam == null) { Destroy(gameObject); return; }
        Vector3 相机朝向 = (transform.position - cam.transform.position).normalized;
        float k = K;

        // 冲击光环：从小迅速扩到很大并淡出
        if (haloLife > 0f)
        {
            haloLife -= dt;
            float t = Mathf.Clamp01(haloLife / 光环时长);
            float d = Mathf.Lerp(2.6f, 0.25f, t) * k;
            halo.localScale = Vector3.one * d;
            halo.rotation = Quaternion.LookRotation(相机朝向, cam.transform.up);
            if (haloMat != null) haloMat.color = SetA(haloMat.color, t * t);
        }
        else if (halo != null) halo.gameObject.SetActive(false);

        // 光晕：先胀后收
        if (glowLife > 0f)
        {
            glowLife -= dt;
            float t = Mathf.Clamp01(glowLife / 光晕时长);
            float d = Mathf.Lerp(1.15f, 0.30f, t) * k;
            glow.localScale = Vector3.one * d;
            glow.rotation = Quaternion.LookRotation(相机朝向, cam.transform.up);
            if (glowMat != null) glowMat.color = SetA(glowMat.color, Mathf.Min(1f, t * 1.6f));
        }
        else if (glow != null) glow.gameObject.SetActive(false);

        // 斩击光条：沿飞行方向拉长，快速消失
        if (streakLife > 0f)
        {
            streakLife -= dt;
            float t = Mathf.Clamp01(streakLife / 光条时长);
            float len = Mathf.Lerp(3.0f, 0.8f, t) * k;
            float wid = Mathf.Lerp(0.30f, 0.90f, t);
            streak.localScale = new Vector3(len, wid, 1f);
            Vector3 right = Vector3.Cross(dir, 相机朝向);
            if (right.sqrMagnitude < 1e-5f) right = cam.transform.right;
            streak.rotation = Quaternion.LookRotation(相机朝向, right.normalized) * Quaternion.Euler(0f, 0f, 90f);
            if (streakMat != null) streakMat.color = SetA(streakMat.color, t * 1.0f);
        }
        else if (streak != null) streak.gameObject.SetActive(false);

        // 火花
        if (sparkLife > 0f)
        {
            sparkLife -= dt;
            float t = Mathf.Clamp01(sparkLife / 火花时长);
            for (int i = 0; i < sparks.Length; i++)
            {
                if (sparks[i] == null) continue;
                sparkVel[i] += Vector3.down * 11f * dt;
                sparkVel[i] *= Mathf.Pow(0.06f, dt);
                sparks[i].position += sparkVel[i] * dt;
                sparks[i].localScale = Vector3.one * Mathf.Lerp(0.01f, 0.20f * k, t);
                sparks[i].rotation = Quaternion.LookRotation(相机朝向, cam.transform.up);
            }
            if (sparkMat != null) sparkMat.color = SetA(sparkMat.color, t);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    static Color SetA(Color c, float a) => new Color(c.r, c.g, c.b, Mathf.Clamp01(a));
}
