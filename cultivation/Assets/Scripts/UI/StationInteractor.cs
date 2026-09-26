using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// <summary>
/// 设施交互（挂在玩家身上）。
///
/// 做三件事：
///   1. 每帧找【离玩家最近、且在交互范围内】的那台设施
///   2. 靠近时在物件头顶显示「右键打开XX」的提示
///   3. 右键 → 打开对应界面（UI 还没设计，先用空白幕布占位）
///
/// ============================================================
/// 和锁定 NPC 的右键不冲突
/// ============================================================
///   NpcTargeting 也用右键（鼠标右键锁定敌人）。
///   所以这里提供了 本次右键已被占用 这个静态标记：
///   站在设施旁边按右键时，NpcTargeting 会跳过这一帧的锁定，
///   免得"想开炼丹炉结果锁了个敌人"。
///   （NpcTargeting 那边已经加了一行检查）
///
/// ============================================================
/// 空白幕布
/// ============================================================
///   真正的 UI 还没做，所以这里临时生成一张半透明黑色全屏幕布 + 标题 + 关闭按钮。
///   等美术/布局定下来之后：
///     · 把做好的界面挂到 StationInteractable.界面预制体 上
///     · 这里会自动优先用那个预制体，不再用占位幕布
/// </summary>
[DisallowMultipleComponent]
public class StationInteractor : MonoBehaviour
{
    [Header("按键")]
    public KeyCode 交互键 = KeyCode.Mouse1;      // 右键
    public KeyCode 关闭键 = KeyCode.Escape;

    [Header("字体")]
    [Tooltip("中文字体。留空中文会变成方块 —— 用 Cultivation/设施/安装交互 会自动填")]
    public Font 字体;

    [Header("占位幕布外观")]
    public Color 幕布底色 = new Color(0.05f, 0.06f, 0.09f, 0.92f);
    public Color 标题色 = new Color(0.92f, 0.90f, 0.82f);
    public Color 提示色 = new Color(1f, 0.95f, 0.6f, 1f);

    [Header("提示")]
    [Tooltip("靠近提示相对物件头顶再往上多少")]
    public float 提示抬高 = 0.6f;

    /// <summary>这一帧的右键是不是被设施吃掉了（给 NpcTargeting 用，兜底）</summary>
    public static bool 本次右键已被占用 { get; private set; }

    /// <summary>
    /// 当前有没有设施界面开着。
    /// 【暂停菜单】和【NPC 锁定】都要看它：
    ///   · 暂停菜单：界面开着时 ESC 应该是"关界面"，不该弹出暂停菜单
    ///   · NPC 锁定：界面开着时鼠标不该穿透到场景里去点建筑/锁敌人
    /// </summary>
    public static bool 有界面打开 { get; private set; }

    /// <summary>
    /// 最近一次关闭界面的时刻（unscaledTime）。
    ///
    /// 【为什么需要】关界面的键和暂停菜单的键都是 ESC，而且两个脚本的 Update
    /// 执行顺序不定。如果 StationInteractor 先跑：它关掉界面、把 有界面打开 置 false，
    /// 紧接着 PauseMenuUI 跑，看到 有界面打开 == false 就顺手把暂停菜单弹出来了 ——
    /// 一次 ESC 干了三件事。
    /// 记一个时间戳，让暂停菜单知道"刚刚才关过界面"，这一下 ESC 不该再响应。
    /// 用 unscaledTime 是因为暂停菜单会把 timeScale 设成 0。
    /// </summary>
    public static float 上次关闭界面时间 { get; private set; } = -99f;

    /// <summary>当前打开着的界面（null = 没开）</summary>
    public GameObject 当前界面 { get; private set; }
    public StationInteractable 当前设施 { get; private set; }
    public bool 界面已打开 => 当前界面 != null;

    readonly List<StationInteractable> 已知设施 = new List<StationInteractable>();
    float 重扫计时;
    float 关闭冷却;      // 关界面后短暂屏蔽右键，防止同一串点击立刻又开一个
    StationInteractable 最近设施;
    Camera 主相机;

    // 占位幕布
    GameObject 幕布根;
    Text 幕布标题;
    Text 幕布副标题;

    // 头顶提示
    Text 提示文字;
    Text 提示键字;            // ★ 键帽上的字（F / 右键）—— 2026-09-26 合并建筑与 NPC 交互时加的
    GameObject 提示根;

    void Awake()
    {
        主相机 = Camera.main;
    }

    void Update()
    {
        // 静态标记只在一帧内有效，每帧开头先清掉
        本次右键已被占用 = false;
        if (关闭冷却 > 0f) 关闭冷却 -= Time.deltaTime;

        // 定期重扫（物件可能在运行时被生成/销毁）
        重扫计时 += Time.deltaTime;
        if (重扫计时 >= 1f)
        {
            重扫计时 = 0f;
            重扫();
        }

        if (界面已打开)
        {
            // 界面开着：只处理关闭，不做任何别的交互
            if (Input.GetKeyDown(关闭键) || Input.GetKeyDown(交互键)) 关闭界面();
            return;
        }

        // 刚关掉界面的一小段时间内不再响应，否则"关掉的那一下"会顺手又开一个
        if (关闭冷却 > 0f) { 隐藏提示(); return; }

        找最近设施();
        更新头顶提示();

        if (最近设施 != null && Input.GetKeyDown(最近设施.取按键(交互键)))
        {
            本次右键已被占用 = true;          // 告诉 NpcTargeting：这帧别锁 NPC
            打开界面(最近设施);
        }
    }

    // ================================================================
    // 找设施
    // ================================================================

    void 重扫()
    {
        已知设施.Clear();
        foreach (var s in FindObjectsOfType<StationInteractable>())
            if (s != null && s.isActiveAndEnabled) 已知设施.Add(s);
    }

    void 找最近设施()
    {
        最近设施 = null;
        float 最近 = float.MaxValue;
        var 我 = transform.position;

        foreach (var s in 已知设施)
        {
            if (s == null) continue;
            var 差 = s.transform.position - 我;
            float 水平 = new Vector2(差.x, 差.z).magnitude;
            if (!s.玩家在范围内(水平, 差.y)) continue;
            // ★ 用户 2026-09-26：同时进范围时取「**离根节点更近**」的那个（原来比的是水平距离）
            float 直距 = 差.magnitude;
            if (直距 < 最近) { 最近 = 直距; 最近设施 = s; }
        }
    }

    // ================================================================
    // 头顶提示
    // ================================================================

    void 更新头顶提示()
    {
        bool 要显示 = 最近设施 != null && 最近设施.显示靠近提示;
        if (!要显示) { 隐藏提示(); return; }

        确保提示存在();
        if (提示根 == null) return;

        提示根.SetActive(true);
        // ★ 提示文案跟着"这件东西实际用哪个键"走（原来写死"右键"，建筑改成 F 后就对不上了）`n        提示文字.text = 最近设施.标题;`n        if (提示键字 != null) 提示键字.text = StationInteractable.按键名(最近设施.取按键(交互键));

        var 位 = 最近设施.transform.position;
        // 从物件顶上的渲染体算高度，算不出来就用固定值
        float 顶 = 1.8f + 提示抬高;
        var 渲染器 = 最近设施.GetComponentsInChildren<Renderer>();
        if (渲染器.Length > 0)
        {
            var b = 渲染器[0].bounds;
            for (int i = 1; i < 渲染器.Length; i++) b.Encapsulate(渲染器[i].bounds);
            顶 = (b.max.y - 最近设施.transform.position.y) + 提示抬高;
        }
        提示根.transform.position = 位 + Vector3.up * 顶;

        if (主相机 == null) 主相机 = Camera.main;
        if (主相机 != null)
            提示根.transform.rotation = Quaternion.LookRotation(
                提示根.transform.position - 主相机.transform.position, 主相机.transform.up);

        // 根据距离淡一点，远了不明显
        float 近 = Vector2.Distance(new Vector2(位.x, 位.z), new Vector2(transform.position.x, transform.position.z));
        float a = Mathf.Clamp01(1.4f - 近 / Mathf.Max(0.1f, 最近设施.交互距离));
        提示文字.color = new Color(提示色.r, 提示色.g, 提示色.b, Mathf.Clamp01(a + 0.25f));
    }

    void 隐藏提示()
    {
        if (提示根 != null && 提示根.activeSelf) 提示根.SetActive(false);
    }

    // 提示配色（对齐用户给的示意图：深色圆角底 + 亮键帽 + 浅色字）
    static readonly Color 提示底色 = new Color(0.06f, 0.07f, 0.10f, 0.92f);
    static readonly Color 键底色 = new Color(0.93f, 0.89f, 0.73f, 0.96f);
    static readonly Color 键字色 = new Color(0.10f, 0.10f, 0.12f, 1f);

    void 确保提示存在()
    {
        if (提示根 != null) return;

        提示根 = new GameObject("StationHint", typeof(Canvas));
        // 【不挂成玩家的子物件】
        // 挂玩家下面的话，玩家的缩放/旋转会一起作用到它；而且 WorldSpace Canvas
        // 默认 scale=1 —— 150×40 的 canvas 在世界空间就是 150 米宽，字大得没边。
        // 改成独立场景物件 + 自己设缩放。
        var canvas = 提示根.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 200;
        var rt = 提示根.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(230f, 56f);
        rt.localScale = Vector3.one * 0.011f;     // 230 × 0.011 ≈ 世界 2.5 米宽

        // ---- 底板：圆角深色（就是示意图里「F 对话」那个小框）----
        var 底 = new GameObject("底", typeof(RectTransform));
        底.transform.SetParent(提示根.transform, false);
        var 底图 = 底.AddComponent<Image>();
        底图.sprite = 取圆角(); 底图.type = Image.Type.Sliced;
        底图.color = 提示底色; 底图.raycastTarget = false;
        拉伸(底.GetComponent<RectTransform>());

        // ---- 键帽：左边一个圆角亮块，里面写按键 ----
        var 帽 = new GameObject("键", typeof(RectTransform));
        帽.transform.SetParent(提示根.transform, false);
        var 帽图 = 帽.AddComponent<Image>();
        帽图.sprite = 取圆角(); 帽图.type = Image.Type.Sliced;
        帽图.color = 键底色; 帽图.raycastTarget = false;
        var 帽rt = 帽.GetComponent<RectTransform>();
        帽rt.anchorMin = new Vector2(0f, 0.5f); 帽rt.anchorMax = new Vector2(0f, 0.5f);
        帽rt.pivot = new Vector2(0f, 0.5f);
        帽rt.anchoredPosition = new Vector2(7f, 0f);
        帽rt.sizeDelta = new Vector2(42f, 42f);

        var k = new GameObject("键字", typeof(RectTransform));
        k.transform.SetParent(帽.transform, false);
        提示键字 = k.AddComponent<Text>();
        提示键字.font = 取字体();
        提示键字.fontSize = 26;
        提示键字.fontStyle = FontStyle.Bold;
        提示键字.alignment = TextAnchor.MiddleCenter;
        提示键字.color = 键字色;
        提示键字.raycastTarget = false;
        拉伸(k.GetComponent<RectTransform>());

        // ---- 动作文字：键帽右边 ----
        var t = new GameObject("Text", typeof(RectTransform));
        t.transform.SetParent(提示根.transform, false);
        提示文字 = t.AddComponent<Text>();
        提示文字.font = 取字体();
        提示文字.fontSize = 28;
        提示文字.alignment = TextAnchor.MiddleLeft;
        提示文字.color = 提示色;
        提示文字.raycastTarget = false;
        var trt = t.GetComponent<RectTransform>();
        trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
        trt.offsetMin = new Vector2(58f, 0f); trt.offsetMax = new Vector2(-8f, 0f);
    }

    static void 拉伸(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
    }

    /// <summary>圆角方块 sprite（带 9 宫格 border，缩放不走形）。程序生成一次、所有提示共用</summary>
    static Sprite 圆角精灵;
    static Sprite 取圆角()
    {
        if (圆角精灵 != null) return 圆角精灵;
        const int N = 32; const float r = 10f;
        var tex = new Texture2D(N, N, TextureFormat.RGBA32, false);
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float dx = Mathf.Max(0f, Mathf.Max(r - (x + 0.5f), (x + 0.5f) - (N - r)));
                float dy = Mathf.Max(0f, Mathf.Max(r - (y + 0.5f), (y + 0.5f) - (N - r)));
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(r - d + 0.5f)));
            }
        tex.Apply();
        圆角精灵 = Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), 100f, 0,
            SpriteMeshType.FullRect, new Vector4(r + 2f, r + 2f, r + 2f, r + 2f));
        return 圆角精灵;
    }

    // ================================================================
    // 打开 / 关闭界面
    // ================================================================

    public void 打开界面(StationInteractable 设施)
    {
        if (设施 == null || 界面已打开) return;
        当前设施 = 设施;

        // 有做好的界面就用它，没有就用占位幕布
        if (设施.界面预制体 != null)
        {
            当前界面 = Instantiate(设施.界面预制体);
            当前界面.name = "StationUI_" + 设施.类型;
        }
        else
        {
            当前界面 = 建占位幕布(设施);
        }

        有界面打开 = true;
        本次右键已被占用 = true;      // 开界面这一帧不要再被别处当成"锁敌人"

        Debug.Log($"[StationInteractor] 打开【{设施.标题}】界面" +
                  (设施.界面预制体 == null ? "（UI 尚未设计，用空白幕布占位）" : ""), 设施);
    }

    public void 关闭界面()
    {
        有界面打开 = false;
        上次关闭界面时间 = Time.unscaledTime;      // 让暂停菜单知道"刚关过"，这一下 ESC 别再响
        UiEscRegistry.记录关闭();                    // 统一走协调器，和角色面板同一个记录
        if (当前界面 != null) Destroy(当前界面);
        当前界面 = null;
        关闭冷却 = 0.25f;            // 关掉之后短暂不响应右键，免得同一串点击又开一个
        if (当前设施 != null) Debug.Log($"[StationInteractor] 关闭【{当前设施.标题}】界面", 当前设施);
        当前设施 = null;
    }

    /// <summary>
    /// 临时占位幕布：全屏半透明黑 + 标题 + 一句说明 + 关闭按钮。
    /// 【这整块等真 UI 做好后会被替换掉】
    /// </summary>
    GameObject 建占位幕布(StationInteractable 设施)
    {
        幕布根 = new GameObject("StationUIPlaceholder", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = 幕布根.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 2500;                  // 低于暂停菜单(3000)，高于其它 UI

        var scaler = 幕布根.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        if (FindObjectOfType<EventSystem>() == null)
        {
            var es = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            es.transform.SetParent(幕布根.transform, false);
        }

        // 幕布
        var 底 = new GameObject("幕布", typeof(Image));
        底.transform.SetParent(幕布根.transform, false);
        var img = 底.GetComponent<Image>();
        img.color = 幕布底色;
        img.raycastTarget = true;                    // 挡住底下的点击
        拉满(img.rectTransform);

        // 标题
        var 标 = new GameObject("标题", typeof(Text));
        标.transform.SetParent(幕布根.transform, false);
        幕布标题 = 标.GetComponent<Text>();
        幕布标题.text = 设施.标题;
        幕布标题.font = 取字体();
        幕布标题.fontSize = 72;
        幕布标题.color = 标题色;
        幕布标题.alignment = TextAnchor.UpperCenter;
        幕布标题.raycastTarget = false;
        var 标rt = 标.GetComponent<RectTransform>();
        标rt.anchorMin = 标rt.anchorMax = new Vector2(0.5f, 1f);
        标rt.pivot = new Vector2(0.5f, 1f);
        标rt.anchoredPosition = new Vector2(0f, -120f);
        标rt.sizeDelta = new Vector2(900f, 110f);

        // 副标题（说明这是占位）
        var 副 = new GameObject("副标题", typeof(Text));
        副.transform.SetParent(幕布根.transform, false);
        幕布副标题 = 副.GetComponent<Text>();
        幕布副标题.text = "（界面尚未设计，这里是空白幕布占位）\n右键 或 ESC 关闭";
        幕布副标题.font = 取字体();
        幕布副标题.fontSize = 30;
        幕布副标题.color = new Color(标题色.r, 标题色.g, 标题色.b, 0.7f);
        幕布副标题.alignment = TextAnchor.MiddleCenter;
        幕布副标题.raycastTarget = false;
        var 副rt = 副.GetComponent<RectTransform>();
        副rt.anchorMin = 副rt.anchorMax = new Vector2(0.5f, 0.5f);
        副rt.pivot = new Vector2(0.5f, 0.5f);
        副rt.anchoredPosition = Vector2.zero;
        副rt.sizeDelta = new Vector2(900f, 140f);

        // 关闭按钮
        建按钮(幕布根.transform, "关闭", new Vector2(0f, 140f));

        return 幕布根;
    }

    void 建按钮(Transform 父, string 文案, Vector2 位置)
    {
        var go = new GameObject("按钮_" + 文案, typeof(Image), typeof(Button));
        go.transform.SetParent(父, false);
        var img = go.GetComponent<Image>();
        img.color = new Color(0.85f, 0.85f, 0.85f, 1f);
        img.raycastTarget = true;

        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = 位置;
        rt.sizeDelta = new Vector2(220f, 56f);

        var 文 = new GameObject("文字", typeof(RectTransform));
        文.transform.SetParent(go.transform, false);
        // 【不要 AddComponent<Text>()】—— 上面 if 里已经挂了 Text 时再加会报
        // "Can't add 'Text' because a 'Text' is already added"，
        // 而且返回的引用是 null，紧接着就 NullReferenceException。
        var t = 文.AddComponent<Text>();
        t.text = 文案;
        t.font = 取字体();
        t.fontSize = 30;
        t.color = new Color(0.12f, 0.12f, 0.12f, 1f);
        t.alignment = TextAnchor.MiddleCenter;
        t.raycastTarget = false;
        拉满(t.rectTransform);

        var btn = go.GetComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(关闭界面);
    }

    static void 拉满(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    Font 取字体()
    {
        return 字体 != null ? 字体 : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    }

    // ---- ASCII 别名 ----
    public static bool RightClickConsumed => 本次右键已被占用;
    public bool IsOpen => 界面已打开;
    public StationInteractable CurrentStation => 当前设施;
}
