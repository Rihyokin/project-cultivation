using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// **角色遮挡描边**：检测「摄像机 → 角色」的连线中间有没有挡住的东西，
/// 一旦被挡就给角色加一圈**穿墙可见的描边**（外加一层半透明剪影），
/// 方便角色被楼阁树木挡住时还能看清位置并继续操作。
///
/// ## 和之前「场景透视」方案的区别
///
/// | | 场景透视（已废弃） | 本方案 |
/// |---|---|---|
/// | 改谁 | 给场景里 248 个环境件换材质 | **只给角色自己挂一份副本** |
/// | 影响场景 | 会（整个环境的材质都被换掉） | **完全不碰** |
/// | 被挡时看到 | 遮挡物变透明的一小块窗口 | 角色的**描边 + 剪影**透过遮挡物显形 |
///
/// 上一版为了让遮挡物变透明，必须在运行时替换环境材质，副作用大且判据很难调对
/// （见开发注意事项 §31）。改成描边之后，**场景一个组件都不用动**。
///
/// ## 描边是怎么画出来的
///
/// 在角色每个渲染器下面挂**两个自己的副本**，共用两份材质：
///
/// · **外圈** `Cultivation/CharacterOutlineRim`：网格沿法线外扩一圈、只画背面
///   （`Cull Front`）→ 一个放大的色块；
/// · **剪影** `Cultivation/CharacterOutlineFill`：把原网格盖在上面遮住中间
///   → 只剩外扩出来的一圈 = 描边。
///
/// 两份材质都是 `ZTest Always` + `ZWrite Off`，队列分别是 `Overlay`(4000) 与
/// `Overlay+1`(4001) 保证「先画外圈、再画剪影」，所以**一定能穿过遮挡物看到**，
/// 而且自己不会挡住任何东西。
///
/// > 拆成两个 shader 而不是一个双 Pass 的 shader，是因为这个版本的 ShaderLab
/// > **不接受把 `#pragma surface` 包在 `Pass { }` 里**（报 unexpected TOK_PASS）。
/// > 项目里能编译的 `GhostSpirit.shader` 用的是「CGPROGRAM 直接放 SubShader、
/// > 渲染状态放 SubShader 级」的写法，这里照抄那个模式。
///
/// ## 遮挡是每帧射线检测的
///
/// 从相机朝角色身上几个高度各打一条射线，只要有一条在到达角色之前撞到别的东西，
/// 就判定为被遮挡。角色自己的碰撞体（`CharacterController` 等）会被跳过。
/// 描边用淡入淡出，不会"啪"地闪出来。
/// </summary>
[DisallowMultipleComponent]
public class OcclusionOutline : MonoBehaviour
{
    [Header("接线（留空自动找）")]
    [Tooltip("摄像机。留空则用 Camera.main")]
    public Camera 摄像机;

    [Tooltip("被观察的角色。留空则找场景里的 PlayerVitals")]
    public Transform 角色;

    [Tooltip("角色身高（米）。用来决定往哪几个高度打射线")]
    public float 角色高度 = 1.8f;

    [Header("描边外观")]
    [Tooltip("描边颜色（外圈）")]
    public Color 描边颜色 = new Color(1f, 0.82f, 0.25f, 1f);

    [Tooltip("描边宽度（米）。沿法线外扩多少。角色在屏幕上越小就要调越大 —— " +
             "默认镜头下角色约 35px 高，0.08m 大约画出 2px 的边")]
    [Range(0f, 0.3f)]
    public float 描边宽度 = 0.08f;

    [Tooltip("剪影填充色。A = 不透明度，0 = 只要一圈边、中间完全透")]
    public Color 剪影颜色 = new Color(1f, 0.82f, 0.25f, 0.35f);

    [Tooltip("淡入淡出速度（每秒）。0 = 立即生效")]
    public float 淡入速度 = 7f;

    [Header("遮挡检测")]
    [Tooltip("往角色这几个高度比例打射线（0=脚底，1=头顶）。任何一条被挡就显示描边")]
    public float[] 采样高度 = { 0.15f, 0.5f, 0.85f };

    [Tooltip("哪些层算遮挡物。建议排除角色自己所在的层")]
    public LayerMask 检测层 = ~0;

    [Tooltip("隔几帧检测一次。1 = 每帧（角色小、射线少，一般负担得起）")]
    [Range(1, 10)]
    public int 检测间隔帧 = 1;

    [Header("调试")]
    [Tooltip("勾上 = 不管有没有被挡都显示描边（用来确认描边本身画得对）")]
    public bool 调试_强制显示;

    [Tooltip("打印每次遮挡状态变化的日志")]
    public bool 打印日志;

    // ============================================================ 内部

    const string 副本名 = "遮挡描边";

    readonly List<Renderer> 副本 = new List<Renderer>();
    Material 圈材质;
    Material 剪影材质;
    float 当前强度;
    bool 已建立;
    bool 上次被挡;
    int 帧计数;

    /// <summary>是否启用（供外部开关）</summary>
    public bool 启用 = true;

    void Awake()
    {
        解析引用();
        建立副本();
    }

    void OnEnable()
    {
        if (!已建立) { 解析引用(); 建立副本(); }
    }

    void OnDisable()
    {
        当前强度 = 0f;
        应用(0f);
    }

    void OnDestroy()
    {
        if (圈材质 != null) Destroy(圈材质);
        if (剪影材质 != null) Destroy(剪影材质);
    }

    void 解析引用()
    {
        if (摄像机 == null) 摄像机 = Camera.main;
        if (角色 == null)
        {
            var 玩家 = FindObjectOfType<PlayerVitals>();
            if (玩家 != null) 角色 = 玩家.transform;
        }
    }

    void 建立副本()
    {
        var 圈Shader = Shader.Find("Cultivation/CharacterOutlineRim");
        var 剪影Shader = Shader.Find("Cultivation/CharacterOutlineFill");
        if (圈Shader == null || 剪影Shader == null)
        {
            Debug.LogError("[遮挡描边] 找不到 shader：「Cultivation/CharacterOutlineRim」=" +
                (圈Shader != null) + "，「Cultivation/CharacterOutlineFill」=" + (剪影Shader != null) +
                "。确认 Assets/Shaders/CharacterOutlineRim.shader 与 CharacterOutlineFill.shader 已导入且没有编译错误。", this);
            return;
        }
        if (角色 == null)
        {
            Debug.LogWarning("[遮挡描边] 没找到角色，功能不生效。", this);
            return;
        }

        圈材质 = new Material(圈Shader) { name = "遮挡描边·圈（运行时）" };
        剪影材质 = new Material(剪影Shader) { name = "遮挡描边·剪影（运行时）" };
        应用(0f);

        // ★ 先列出原件再建副本，否则遍历会把自己刚建的副本也当成原件（无限套娃）
        var 原件 = new List<Renderer>(角色.GetComponentsInChildren<SkinnedMeshRenderer>(true));
        foreach (var mr in 角色.GetComponentsInChildren<MeshRenderer>(true)) 原件.Add(mr);

        int 建了 = 0, 跳过 = 0;
        foreach (var 源 in 原件)
        {
            if (源 == null) continue;
            // 跳过副本自己（名字带前缀），以及上一轮可能残留的副本
            bool 是副本 = 源.name.StartsWith(副本名);
            for (var p = 源.transform.parent; !是副本 && p != null; p = p.parent)
                if (p.name.StartsWith(副本名)) 是副本 = true;
            if (是副本) { 跳过++; continue; }

            建了 += 造(源, 圈材质, "·圈") ? 1 : 0;
            建了 += 造(源, 剪影材质, "·剪影") ? 1 : 0;
        }

        已建立 = true;
        Debug.Log("[遮挡描边] 角色「" + 角色.name + "」：原件 " + 原件.Count + " 个 → 描边副本 " + 建了 + " 个"
            + (跳过 > 0 ? "，跳过 " + 跳过 + " 个" : "") + "，共用 2 份材质。", this);
    }

    /// <summary>按源渲染器造一个副本（骨骼共用，所以变形完全同步）</summary>
    bool 造(Renderer 源, Material 材质, string 后缀)
    {
        var go = new GameObject(副本名 + 后缀);
        go.transform.SetParent(源.transform, false);   // 跟着原件走，本位姿为 0
        go.layer = 源.gameObject.layer;

        if (源 is SkinnedMeshRenderer smr)
        {
            var d = go.AddComponent<SkinnedMeshRenderer>();
            d.sharedMesh = smr.sharedMesh;
            d.bones = smr.bones;                        // ★ 共用骨骼 → 变形同步
            d.rootBone = smr.rootBone;
            d.updateWhenOffscreen = smr.updateWhenOffscreen;
            d.localBounds = smr.localBounds;
            d.quality = smr.quality;
            d.sharedMaterials = new Material[] { 材质 };
            去投影(d);
            副本.Add(d);
            go.SetActive(false);
            return true;
        }

        var mf = 源.GetComponent<MeshFilter>();
        if (mf == null || mf.sharedMesh == null) { Destroy(go); return false; }
        go.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
        var mr2 = go.AddComponent<MeshRenderer>();
        mr2.sharedMaterials = new Material[] { 材质 };
        去投影(mr2);
        副本.Add(mr2);
        go.SetActive(false);
        return true;
    }

    static void 去投影(Renderer r)
    {
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
    }

    void LateUpdate()
    {
        if (摄像机 == null) 摄像机 = Camera.main;
        if (!已建立 || 角色 == null || 摄像机 == null) return;

        // 隔几帧检测一次
        if (帧计数++ % Mathf.Max(1, 检测间隔帧) == 0)
        {
            bool 被挡 = 有遮挡();
            if (打印日志 && 被挡 != 上次被挡)
                Debug.Log("[遮挡描边] " + (被挡 ? "检测到遮挡 → 显示描边" : "遮挡消失 → 收起描边"), this);
            上次被挡 = 被挡;
        }

        float 目标 = (启用 && (调试_强制显示 || 上次被挡)) ? 1f : 0f;
        当前强度 = 淡入速度 > 0f
            ? Mathf.MoveTowards(当前强度, 目标, 淡入速度 * Time.unscaledDeltaTime)
            : 目标;

        应用(当前强度);
    }

    /// <summary>相机到角色之间有东西挡着吗</summary>
    bool 有遮挡()
    {
        if (摄像机 == null || 角色 == null) return false;

        var 相机位 = 摄像机.transform.position;
        foreach (var k in 采样高度)
        {
            var 目标点 = 角色.position + Vector3.up * (角色高度 * Mathf.Clamp01(k));
            var 方向 = 目标点 - 相机位;
            float 距离 = 方向.magnitude;
            if (距离 < 0.05f) continue;

            RaycastHit 命中;
            // 留 5cm 余量，免得打到角色自己的碰撞体
            if (!Physics.Raycast(相机位, 方向 / 距离, out 命中, 距离 - 0.05f,
                                 检测层, QueryTriggerInteraction.Ignore))
                continue;

            // 打到自己身上的部件（含各骨骼上的碰撞体）不算遮挡
            if (命中.collider.transform.IsChildOf(角色)) continue;
            return true;
        }
        return false;
    }

    void 应用(float 强度)
    {
        bool 开 = 强度 > 0.001f;
        for (int i = 0; i < 副本.Count; i++)
            if (副本[i] != null && 副本[i].gameObject.activeSelf != 开)
                副本[i].gameObject.SetActive(开);

        if (!开) return;

        if (圈材质 != null)
        {
            var a = 描边颜色; a.a *= 强度;
            圈材质.SetColor("_OutlineColor", a);
            圈材质.SetFloat("_OutlineWidth", 描边宽度);
        }
        if (剪影材质 != null)
        {
            var b = 剪影颜色; b.a *= 强度;
            剪影材质.SetColor("_FillColor", b);
        }
    }

    /// <summary>外部开关</summary>
    public void 设置启用(bool 开) => 启用 = 开;

    // ---- ASCII 别名 ----
    public Camera TargetCamera { get => 摄像机; set => 摄像机 = value; }
    public Transform TargetCharacter { get => 角色; set => 角色 = value; }
    public Color OutlineColor { get => 描边颜色; set => 描边颜色 = value; }
    public Color FillColor { get => 剪影颜色; set => 剪影颜色 = value; }
    public float OutlineWidth { get => 描边宽度; set => 描边宽度 = value; }
    public bool Enabled { get => 启用; set => 启用 = value; }
    public bool ForceShow { get => 调试_强制显示; set => 调试_强制显示 = value; }
}
