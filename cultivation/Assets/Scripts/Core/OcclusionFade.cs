using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// **遮挡透视**：把挡住「摄像机看角色」的东西变透明（默认 70% 透视），
/// 方便角色被楼阁树木挡住时还能看清和操作。
///
/// ## 判据的演进（前两版都是错的，改对了两次）
///
/// | 版本 | 判据 | 为什么错 |
/// |---|---|---|
/// | v1 | 相机→角色连线撑**圆柱**，圆柱内的都透 | 轴心在角色胸口高度、离地约 2m，**角色脚下的地面落在圆柱半径内**被透掉，可它没挡住角色 |
/// | v2 | 以角色为圆心的**屏幕圆盘** | 圆盘会一直延伸到脚下甚至更下面，那一圈地面又落进去了 |
/// | **v3** | **角色的屏幕矩形**（脚→头，外加留白）+ **必须在角色前面** | 地面在矩形**外面**，天然排除 |
///
/// v3 的两条同时满足才算遮挡物：
///
/// 1. **在屏幕上与角色矩形重叠**（矩形由角色实际屏幕尺寸驱动，镜头拉远拉近都自动适配）；
/// 2. **比角色更靠近相机**。
///
/// 几何上等价于「以相机为顶点、穿过角色屏幕轮廓的锥体，在角色深度处截断」，
/// 边缘再用 <c>smoothstep</c> 在矩形外做渐变，所以是柔和的窗口而不是硬边。
///
/// ## 怎么做到「只处理这一块区域」
///
/// 判断是**逐像素**在 shader 里做的（见 <c>Assets/Shaders/OcclusionFade.shader</c>）。
/// 没被判定为遮挡物的像素，alpha 保持原值、混合结果和不透明完全一致，
/// 所以窗口外的画面**和加这个功能之前一模一样**，不需要按物体去挑。
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

    [Tooltip("角色身高（米）。用来算屏幕矩形和观察锚点")]
    public float 角色高度 = 1.8f;

    [Header("透视窗口（都相对于角色在屏幕上的高度）")]
    [Tooltip("横向留白：矩形左右各扩出「角色屏幕高 × 这个值」。越大窗口越宽")]
    public float 横向留白 = 0.35f;

    [Tooltip("纵向留白：矩形上下各扩出「角色屏幕高 × 这个值」")]
    public float 纵向留白 = 0.08f;

    [Tooltip("边缘羽化系数：越大边缘越柔和")]
    public float 羽化系数 = 0.55f;

    [Tooltip("羽化下限（屏幕高比例），避免角色极小时羽化退化")]
    public float 最小羽化 = 0.005f;

    [Header("透视强度")]
    [Tooltip("0 = 不透视；0.7 = 70% 透视（只剩 30% 不透明度）")]
    [Range(0f, 0.95f)]
    public float 透视强度 = 0.7f;

    [Tooltip("启用/停用时的淡入淡出速度（每秒）。0 = 立即生效")]
    public float 淡入速度 = 6f;

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

    static readonly int IdRect = Shader.PropertyToID("_OcclRect");
    static readonly int IdParams = Shader.PropertyToID("_OcclParams");

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
        关闭();
    }

    void 关闭()
    {
        Shader.SetGlobalVector(IdRect, new Vector4(0f, 0f, -1f, -1f));   // min > max = 空矩形
        Shader.SetGlobalVector(IdParams, new Vector4(0f, 0.01f, 0f, 0f));
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

        int 根数 = 0, 图元 = 0;
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
            根数++;
        }

        已建立 = true;
        if (打印日志)
        {
            Debug.Log("[遮挡透视] 作用根 " + 根数 + " 个，换了 " + 图元 + " 个渲染体，"
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

        if (摄像机 == null || 角色 == null || 当前强度 <= 0.0005f) { 关闭(); return; }

        // 角色的屏幕矩形 = 脚 → 头。**这就是「挡住摄像机拍角色」的判据**：
        // 地面在角色脚下、落在矩形外面，所以不会被透掉。
        var 脚 = 摄像机.WorldToViewportPoint(角色.position);
        var 头 = 摄像机.WorldToViewportPoint(角色.position + Vector3.up * 角色高度);
        float 屏幕高 = Mathf.Abs(头.y - 脚.y);
        if (脚.z <= 0.01f || 头.z <= 0.01f || 屏幕高 <= 0.0005f) { 关闭(); return; }

        var 锚点 = 角色.position + Vector3.up * (角色高度 * 0.5f);
        float 距相机 = Vector3.Distance(摄像机.transform.position, 锚点);

        float 纵横 = Mathf.Max(0.1f, 摄像机.aspect);
        float 半宽 = 屏幕高 * Mathf.Max(0f, 横向留白);
        float 纵留 = 屏幕高 * Mathf.Max(0f, 纵向留白);
        float 中x = (脚.x + 头.x) * 0.5f;

        float minX = 中x - 半宽, maxX = 中x + 半宽;
        float minY = Mathf.Min(脚.y, 头.y) - 纵留;
        float maxY = Mathf.Max(脚.y, 头.y) + 纵留;

        // ★ x 乘上纵横比：这样 shader 里两轴都以「屏幕高」为单位，宽屏下窗口形状才对
        Shader.SetGlobalVector(IdRect, new Vector4(minX * 纵横, minY, maxX * 纵横, maxY));
        Shader.SetGlobalVector(IdParams, new Vector4(
            距相机,
            Mathf.Max(最小羽化, 屏幕高 * Mathf.Max(0f, 羽化系数)),
            当前强度,
            1f));
    }

    /// <summary>外部开关（比如打开角色面板时可以临时关掉）</summary>
    public void 设置启用(bool 开) => 启用 = 开;

    // ---- ASCII 别名 ----
    public Camera TargetCamera { get => 摄像机; set => 摄像机 = value; }
    public Transform TargetCharacter { get => 角色; set => 角色 = value; }
    public float SideMargin { get => 横向留白; set => 横向留白 = value; }
    public float FeatherScale { get => 羽化系数; set => 羽化系数 = value; }
    public float Strength { get => 透视强度; set => 透视强度 = value; }
    public bool Enabled { get => 启用; set => 启用 = value; }
}
