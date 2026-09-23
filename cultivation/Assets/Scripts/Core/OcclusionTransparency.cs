using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 【通用装配 · 挂在主相机上】把「摄像机 ↔ 主角」连线挡住的建筑/场景**变透明**，
/// 免得镜头被墙挡住看不见主角。
///
/// 做法：每帧从相机朝主角身上几个高度点打射线 → 命中的渲染器（不是主角自己的）
/// 换成半透明材质；不再遮挡的换回原材质。
///
/// ★ 原材质缓存着，临时透明材质按「源材质」复用（不每帧 new Material），
///   离场时把原 sharedMaterials 原样还回去，所以**不污染资产**。
/// </summary>
[DisallowMultipleComponent]
public class OcclusionTransparency : MonoBehaviour
{
    [Tooltip("主角根。留空自动找 PlayerVitals，再退化为名为 Player 的根")]
    public Transform 主角;

    [Tooltip("遮挡时的透明度")]
    [Range(0.05f, 0.9f)] public float 透明度 = 0.22f;

    [Tooltip("朝主角身上打几条射线（覆盖身高，避免只挡到腿时看不见）")]
    public int 射线数 = 5;

    [Tooltip("主角脚底到头顶的高度（米）")]
    public float 身高 = 1.8f;

    [Tooltip("参与透视的层（默认全部；UI 层会被自动排除）")]
    public LayerMask 层 = ~0;

    [Tooltip("拿开遮挡后保持多久再恢复（秒），防止边缘来回闪")]
    public float 保持 = 0.12f;

    [Tooltip("打印调试日志")]
    public bool 打印日志 = false;

    readonly Dictionary<Renderer, Material[]> _原材质 = new Dictionary<Renderer, Material[]>();
    readonly Dictionary<Renderer, float> _保持到 = new Dictionary<Renderer, float>();
    readonly Dictionary<Material, Material> _透明缓存 = new Dictionary<Material, Material>();
    readonly List<Renderer> _本帧 = new List<Renderer>();
    readonly List<Renderer> _待恢复 = new List<Renderer>();

    void LateUpdate()
    {
        var cam = GetComponent<Camera>();
        if (cam == null || !cam.isActiveAndEnabled) return;

        if (主角 == null)
        {
            var 命 = FindObjectOfType<PlayerVitals>();
            if (命 != null) 主角 = 命.transform;
            else { var g = GameObject.Find("Player"); if (g != null) 主角 = g.transform; }
            if (主角 == null) return;
        }

        // ---- 收集本帧挡在连线上的渲染器 ----
        _本帧.Clear();
        int n = Mathf.Max(1, 射线数);
        var 脚 = 主角.position;
        for (int i = 0; i < n; i++)
        {
            float f = n <= 1 ? 0.55f : i / (float)(n - 1);
            var 点 = 脚 + Vector3.up * (身高 * Mathf.Lerp(0.08f, 0.95f, f));
            var 向 = 点 - cam.transform.position;
            float 距 = 向.magnitude;
            if (距 < 0.01f) continue;
            var 命中s = Physics.RaycastAll(cam.transform.position, 向 / 距, 距, 层, QueryTriggerInteraction.Ignore);
            foreach (var h in 命中s)
            {
                // ★ 碰撞体在根、网格在子物体上的情况很常见（环境 FBX 就是：根挂 BoxCollider、
                //   三个子物体各带一个网格）。只找 GetComponentInParent 会漏掉它们 ✗
                var 渲染s = h.collider.GetComponentsInChildren<Renderer>();
                if (渲染s.Length == 0)
                {
                    var 父 = h.collider.GetComponentInParent<Renderer>();
                    if (父 != null) 渲染s = new[] { 父 };
                }
                foreach (var r in 渲染s)
                {
                    if (r == null) continue;
                    if (r.transform.IsChildOf(主角)) continue;      // 主角自己不淡
                    if (r is ParticleSystemRenderer) continue;
                    if (!_本帧.Contains(r)) _本帧.Add(r);
                }
            }
        }

        // ---- 本帧命中的：换成透明，并续上保持时间 ----
        foreach (var r in _本帧)
        {
            if (!_原材质.ContainsKey(r))
            {
                var 原 = r.sharedMaterials;
                if (原 == null || 原.Length == 0) continue;
                _原材质[r] = 原;
                var 换 = new Material[原.Length];
                for (int i = 0; i < 原.Length; i++) 换[i] = 取透明(原[i], r);
                r.sharedMaterials = 换;
                if (打印日志) Debug.Log("[遮挡透视] 变透明：" + r.name, r);
            }
            _保持到[r] = Time.unscaledTime + Mathf.Max(0f, 保持);
        }

        // ---- 过期的：恢复原材质 ----
        _待恢复.Clear();
        foreach (var kv in _保持到)
            if (Time.unscaledTime > kv.Value) _待恢复.Add(kv.Key);
        foreach (var r in _待恢复)
        {
            if (r != null && _原材质.TryGetValue(r, out var 原))
            {
                if (r.sharedMaterials != null && r.sharedMaterials.Length == 原.Length) r.sharedMaterials = 原;
                else if (r != null) r.sharedMaterials = 原;
            }
            _原材质.Remove(r);
            _保持到.Remove(r);
            if (打印日志) Debug.Log("[遮挡透视] 恢复：" + (r == null ? "(已销毁)" : r.name), r);
        }
    }

    Material 取透明(Material 源, Renderer 主)
    {
        if (源 == null) return null;
        if (_透明缓存.TryGetValue(源, out var 有) && 有 != null) return 有;

        var sh = Shader.Find("Legacy Shaders/Transparent/Diffuse");
        if (sh == null) sh = Shader.Find("Legacy Shaders/Transparent/Cutout/Diffuse");
        if (sh == null) sh = Shader.Find("Unlit/Transparent");
        var m = new Material(sh) { name = "透明_" + 源.name };
        if (源.HasProperty("_MainTex")) m.mainTexture = 源.mainTexture;
        Color c = 源.HasProperty("_Color") ? 源.GetColor("_Color") : Color.white;
        m.color = new Color(c.r, c.g, c.b, 透明度);
        if (m.HasProperty("_MainTex")) m.mainTextureScale = 源.mainTextureScale;
        _透明缓存[源] = m;
        return m;
    }

    void OnDisable()
    {
        foreach (var kv in _原材质) if (kv.Key != null) kv.Key.sharedMaterials = kv.Value;
        _原材质.Clear(); _保持到.Clear();
    }
}
