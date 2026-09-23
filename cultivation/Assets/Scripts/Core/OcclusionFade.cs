using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// **遮挡透视**：把「摄像机 → 角色」这条连线当轴心撑出一个圆柱，
/// 落在圆柱里的场景物件变透明（默认 70% 透视），方便角色被楼阁树木挡住时还能看清和操作。
///
/// ## 怎么做到「只处理这一块区域」
///
/// 判断是**逐像素**在 shader 里做的（见 <c>Assets/Shaders/OcclusionFade.shader</c>）：
/// 每个像素自己算「离摄像机-角色连线的径向距离」，只有圆柱内的像素才降 alpha。
/// 所以圆柱外的画面**和加这个功能之前一模一样**，不需要按物体去挑，
/// 也不会因为物体一半在圆柱里就整块变透明。
///
/// ## 为什么是运行时换材质，而不是改材质资产
///
/// 透明度是**每个 draw call** 的属性，内置 shader 没法用 MaterialPropertyBlock 逐物体改，
/// 所以必须换成支持逐像素透视的 shader。这里**只在运行时**给场景里的 renderer 换：
///
/// · 不动任何 <c>.mat</c> 资产（不会影响别的场景 —— 见开发注意事项 §5.9 / §30.2）
/// · 不动预制体，也不写回场景，**退出 Play 自动还原**
/// · 同一个原始材质**只生成一份**替换材质并共用，所以合批不会被打散
///
/// ## 边缘为什么要羽化
///
/// 圆柱边界如果按硬阈值切，会看到一道生硬的分界线。这里用
/// <c>smoothstep(半径, 半径+羽化宽度, 径向距离)</c> 做渐变，
/// 圆柱中心最透、到边缘平滑回到不透明，所以看起来是「柔和地淡开」而不是「切了一刀」。
///
/// ## 不处理的材质
///
/// 只替换 shader 与 <c>Legacy Shaders/Diffuse</c> 系列等价的材质（那套是纯 Lambert，
/// 可以一比一复刻）。树冠的 <c>Nature/Tree Creator Leaves</c> 带风吹顶点动画，
/// 换掉会丢摇摆效果，所以**跳过**，会在 Console 里列出来。
/// </summary>
[DisallowMultipleComponent]
public class OcclusionFade : MonoBehaviour
{
    [Header("接线（留空自动找）")]
    [Tooltip("摄像机。留空则用 Camera.main")]
    public Camera 摄像机;

    [Tooltip("被观察的角色。留空则找场景里的 PlayerVitals")]
    public Transform 角色;

    [Tooltip("对着角色的哪个高度瞄准（0=脚底，1=胸口）")]
    public float 瞄准高度 = 1.0f;

    [Header("圆柱")]
    [Tooltip("圆柱半径（米）。以连线为轴心，半径内的物件才会被透视")]
    public float 半径 = 2.2f;

    [Tooltip("边缘羽化宽度（米）。越大过渡越柔和，0 = 硬边")]
    public float 羽化宽度 = 1.4f;

    [Header("透视强度")]
    [Tooltip("0 = 不透视；0.7 = 70% 透视（物件只剩 30% 不透明度）")]
    [Range(0f, 0.95f)]
    public float 透视强度 = 0.7f;

    [Tooltip("启用/停用时的淡入淡出速度（每秒）。0 = 立即生效")]
    public float 淡入速度 = 6f;

    [Header("沿轴范围（0 = 摄像机，1 = 角色）")]
    [Tooltip("靠近摄像机这一端从哪开始生效，避免贴着相机的物件也被透视")]
    [Range(0f, 0.5f)]
    public float 起点留白 = 0.05f;

    [Tooltip("靠近角色这一端到哪结束，避免角色脚下的物件也被透视")]
    [Range(0.5f, 1f)]
    public float 终点留白 = 0.92f;

    [Header("作用对象")]
    [Tooltip("参与透视的根物体。留空则自动收集根名字里含下面关键字的")]
    public List<Transform> 作用根 = new List<Transform>();

    [Tooltip("自动收集作用根时用的关键字")]
    public string[] 根名关键字 = { "Building", "building", "Tree", "tree", "stone" };

    [Header("调试")]
    [Tooltip("打印换了多少材质、跳过了哪些 shader")]
    public bool 打印日志 = true;

    // ============================================================ 内部

    /// <summary>原始材质 → 替换材质。同一个原始材质共用一份，保住合批</summary>
    readonly Dictionary<Material, Material> 替换表 = new Dictionary<Material, Material>();

    /// <summary>跳过的 shader（带顶点动画之类，换了会丢效果）</summary>
    readonly HashSet<string> 跳过的Shader = new HashSet<string>();

    Shader 透视Shader;
    bool 已建立;
    float 当前强度;

    static readonly int IdA = Shader.PropertyToID("_OcclA");
    static readonly int IdB = Shader.PropertyToID("_OcclB");
    static readonly int IdParams = Shader.PropertyToID("_OcclParams");
    static readonly int IdParams2 = Shader.PropertyToID("_OcclParams2");

    /// <summary>可以一比一复刻的 shader（都是 Lambert + _MainTex * _Color）</summary>
    static readonly HashSet<string> 可替换Shader = new HashSet<string>
    {
        "Legacy Shaders/Diffuse",
        "Legacy Shaders/Transparent/Cutout/Diffuse",
        "Legacy Shaders/Transparent/Diffuse",
    };

    /// <summary>是否启用（供外部开关，例如打开面板时临时关掉）</summary>
    public bool 启用 = true;

    void Awake()
    {
        解析引用();
        建立替换();
    }

    void OnEnable()
    {
        if (!已建立) { 解析引用(); 建立替换(); }
    }

    void OnDisable()
    {
        // 关掉时立刻收回效果，免得留一帧半透明的场景
        当前强度 = 0f;
        喂参数(Vector3.zero, Vector3.zero, 0f, 0);
    }

    void 解析引用()
    {
        if (摄像机 == null) 摄像机 = Camera.main;
        if (角色 == null)
        {
            var 玩家 = FindObjectOfType<PlayerVitals>();
            if (玩家 != null) 角色 = 玩家.transform;
        }
        if (作用根.Count == 0) 自动收集作用根();
    }

    void 自动收集作用根()
    {
        var 场景 = gameObject.scene;
        if (!场景.IsValid()) return;
        foreach (var go in 场景.GetRootGameObjects())
        {
            if (go == gameObject) continue;
            foreach (var 关键 in 根名关键字)
            {
                if (string.IsNullOrEmpty(关键)) continue;
                if (go.name.Contains(关键)) { 作用根.Add(go.transform); break; }
            }
        }
    }

    void 建立替换()
    {
        透视Shader = Shader.Find("Cultivation/OcclusionFade");
        if (透视Shader == null)
        {
            Debug.LogError("[遮挡透视] 找不到 shader「Cultivation/OcclusionFade」，功能不生效。" +
                           "确认 Assets/Shaders/OcclusionFade.shader 已导入且没有编译错误。", this);
            return;
        }
        if (作用根.Count == 0)
        {
            Debug.LogWarning("[遮挡透视] 没有作用根，没东西可透视。", this);
            已建立 = true;
            return;
        }

        int 换过 = 0, 图元 = 0;
        foreach (var 根 in 作用根)
        {
            if (根 == null) continue;
            foreach (var r in 根.GetComponentsInChildren<MeshRenderer>(true))
            {
                // 玩家 / NPC 不参与（他们是被观察对象，不是遮挡物）
                if (r.GetComponentInParent<PlayerVitals>() != null) continue;

                var 原 = r.sharedMaterials;
                var 新 = new Material[原.Length];
                bool 变了 = false;
                for (int i = 0; i < 原.Length; i++)
                {
                    var m = 原[i];
                    if (m == null) { 新[i] = null; continue; }

                    if (m.shader == null || !可替换Shader.Contains(m.shader.name))
                    {
                        if (m.shader != null) 跳过的Shader.Add(m.shader.name);
                        新[i] = m;
                        continue;
                    }

                    if (!替换表.TryGetValue(m, out var nm))
                    {
                        nm = new Material(透视Shader) { name = m.name + "（透视）" };
                        nm.CopyPropertiesFromMaterial(m);   // 把 _MainTex / _Color / _Cutoff 带过来
                        nm.renderQueue = 2450;              // AlphaTest：不透明画完再混合
                        替换表[m] = nm;
                    }
                    新[i] = nm;
                    变了 = true;
                }
                if (变了) { r.sharedMaterials = 新; 图元++; }
            }
            换过++;
        }

        已建立 = true;
        if (打印日志)
        {
            Debug.Log("[遮挡透视] 作用根 " + 换过 + " 个，换了 " + 图元 + " 个渲染体，"
                + "生成替换材质 " + 替换表.Count + " 份（原始材质共用，保住合批）", this);
            if (跳过的Shader.Count > 0)
                Debug.Log("[遮挡透视] 跳过的 shader（换了会丢顶点动画等效果）："
                    + string.Join("、", 跳过的Shader), this);
        }
    }

    void LateUpdate()
    {
        // 相机跟随时相机位置也是每帧变的，所以放在 LateUpdate
        if (摄像机 == null) 摄像机 = Camera.main;
        if (角色 == null)
        {
            var 玩家 = FindObjectOfType<PlayerVitals>();
            if (玩家 != null) 角色 = 玩家.transform;
        }

        float 目标 = (启用 && 摄像机 != null && 角色 != null) ? 透视强度 : 0f;
        当前强度 = 淡入速度 > 0f
            ? Mathf.MoveTowards(当前强度, 目标, 淡入速度 * Time.unscaledDeltaTime)
            : 目标;

        if (摄像机 == null || 角色 == null)
        {
            喂参数(Vector3.zero, Vector3.zero, 0f, 0);
            return;
        }

        var a = 摄像机.transform.position;
        var b = 角色.position + Vector3.up * 瞄准高度;
        喂参数(a, b, 当前强度, 当前强度 > 0.0005f ? 1 : 0);
    }

    void 喂参数(Vector3 a, Vector3 b, float 强度, int 开)
    {
        Shader.SetGlobalVector(IdA, new Vector4(a.x, a.y, a.z, 1f));
        Shader.SetGlobalVector(IdB, new Vector4(b.x, b.y, b.z, 1f));
        Shader.SetGlobalVector(IdParams, new Vector4(
            Mathf.Max(0.01f, 半径),
            Mathf.Max(0f, 羽化宽度),
            强度,
            终点留白));
        Shader.SetGlobalVector(IdParams2, new Vector4(
            Mathf.Clamp01(起点留白),
            开,
            0f, 0f));
    }

    /// <summary>外部开关（比如打开角色面板时可以临时关掉）</summary>
    public void 设置启用(bool 开) => 启用 = 开;

    // ---- ASCII 别名 ----
    public Camera TargetCamera { get => 摄像机; set => 摄像机 = value; }
    public Transform TargetCharacter { get => 角色; set => 角色 = value; }
    public float Radius { get => 半径; set => 半径 = value; }
    public float Feather { get => 羽化宽度; set => 羽化宽度 = value; }
    public float Strength { get => 透视强度; set => 透视强度 = value; }
    public bool Enabled { get => 启用; set => 启用 = value; }
}
