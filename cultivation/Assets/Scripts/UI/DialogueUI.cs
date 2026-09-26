using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// **对话框 UI** —— 布局照用户给的示意图：
///   · 左侧一整条「人物立绘」大图（约 30% 宽，从下方 25% 一直到屏幕底）
///   · 右侧一整条「玩家立绘」
///   · 下方偏左灰底框：左上角深色「NPC名字」标签 + 框内「对话内容」
///   · 灰底框右侧竖排「回答1 / 回答2 / 回答3」按钮
///
/// 三个设计要点：
///   ① **界面是运行时自己搭出来的**（不依赖预制体 / 不依赖美术），立绘补图后自动生效：
///      往 `Assets/resources/立绘/<名字>.png` 一放，对话表 `立绘` 列写上名字即可。
///   ② **情绪是可扩展的**：对话表 `情绪` 列 + `情绪强度` 列 →
///      <see cref="播放情绪"/>。内置 震动/冒泡/发怒/害羞/惊讶，
///      新情绪既能写进 <see cref="自定义情绪"/> 注册表，也能在这里加一段表现，**不用改数据表结构**。
///   ③ **条件 / 任务接口**：候选段由 <see cref="DialogueDatabase.取段"/> 结合
///      <see cref="对话标记"/> 过滤（需要标记 / 排除标记 / 优先），任务管理器只管加删标记。
/// </summary>
[DisallowMultipleComponent]
public class DialogueUI : MonoBehaviour
{
    // ============================================================ 单例 & 开关

    public static DialogueUI 实例 { get; private set; }

    public static bool 正在显示 => 实例 != null && 实例.根 != null && 实例.根.activeSelf;

    /// <summary>当前正在对话的 NPC（没有就是 null）。别的系统可以拿它做"对话中就别干别的"</summary>
    public static NpcDialogue 当前NPC { get; private set; }

    /// <summary>
    /// 自定义情绪注册表：名字 → (界面, 强度) 的表现。
    /// 任务/剧情想在不动这张表结构的前提下加新情绪，就往这里塞：
    /// <code>DialogueUI.自定义情绪["彩虹"] = (ui, 强度) => ui.冒泡(强度);</code>
    /// </summary>
    public static readonly Dictionary<string, Action<DialogueUI, float>> 自定义情绪
        = new Dictionary<string, Action<DialogueUI, float>>();

    // ============================================================ 可调外观

    [Header("外观（留空/默认即可，示意图形状）")]
    public Font 字体;

    public Color 立绘底色 = new Color(0.93f, 0.90f, 0.43f, 0.92f);      // 示意图里的黄块 = 立绘占位
    public Color 文本框底色 = new Color(0.62f, 0.62f, 0.62f, 0.92f);
    public Color 名字底色 = new Color(0.24f, 0.24f, 0.26f, 0.95f);
    public Color 文字色 = new Color(0.06f, 0.06f, 0.08f, 1f);
    public Color 名字字色 = new Color(0.96f, 0.96f, 0.96f, 1f);
    public Color 回答底色 = new Color(0.80f, 0.80f, 0.80f, 0.96f);
    public Color 回答悬停色 = new Color(0.95f, 0.92f, 0.70f, 1f);

    [Header("行为")]
    [Tooltip("没有回答的段落，按这个键继续下一段")]
    public KeyCode 继续键 = KeyCode.F;

    // ============================================================ 内部引用

    GameObject 根;
    RectTransform 根RT;
    Image 左立绘, 右立绘;
    Text 左立绘提示, 右立绘提示;
    Image 文本框底;
    Text 名字文本, 内容文本;
    RectTransform 回答列;
    readonly List<GameObject> 回答按钮 = new List<GameObject>();

    string 当前NpcId = "";
    int 当前分段;

    // 打开那一帧不要再吃同一个 F 键
    int 打开帧 = -1;

    // ============================================================ 对外接口

    /// <summary>取（没有就建）对话框实例</summary>
    public static DialogueUI 确保()
    {
        if (实例 != null) return 实例;
        var go = new GameObject("对话界面");
        return go.AddComponent<DialogueUI>();
    }

    /// <summary>打开某个 NPC 的对话（对话表里没写这个 NPC 就直接不开，并给一条日志）</summary>
    public static void 打开(NpcDialogue 来源)
    {
        if (来源 == null) return;
        var db = DialogueDatabase.取();
        string npcId = 来源.取NpcId();
        if (db == null)
        {
            Debug.LogWarning("[对话] 找不到对话库（Assets/resources/对话/对话库.asset），先跑菜单：修仙/对话系统/收集对话资产");
            return;
        }
        int 段 = db.最小分段(npcId);
        if (段 <= 0)
        {
            Debug.Log("[对话] " + 来源.gameObject.name + "（" + npcId + "）在对话表里没有分段，先不开框");
            return;
        }
        var ui = 确保();
        ui.开始(来源, npcId, 段);
    }

    public static void 关闭()
    {
        if (实例 == null) return;
        实例.收起来();
    }

    // ============================================================ 生命周期

    void Awake()
    {
        if (实例 != null && 实例 != this) { Destroy(gameObject); return; }
        实例 = this;
        搭界面();
        收起来();
    }

    void OnDestroy() { if (实例 == this) { 实例 = null; 当前NPC = null; } }

    void 开始(NpcDialogue 来源, string npcId, int 段)
    {
        当前NPC = 来源;
        NpcDialogue.当前对话中 = 来源;
        当前NpcId = npcId;
        打开帧 = Time.frameCount;
        if (根 != null) 根.SetActive(true);
        显示分段(段);
    }

    void 收起来()
    {
        if (根 != null) 根.SetActive(false);
        清回答();
        当前NPC = null;
        NpcDialogue.当前对话中 = null;
    }

    /// <summary>显示某一段（找不到这一段就收起来）</summary>
    public void 显示分段(int 段)
    {
        var db = DialogueDatabase.取();
        var d = db != null ? db.取段(当前NpcId, 段) : null;
        if (d == null) { 收起来(); return; }

        当前分段 = 段;
        名字文本.text = d.取说话人(当前NPC != null ? 当前NPC.gameObject.name : "");
        内容文本.text = d.文本;
        贴立绘(左立绘, 左立绘提示, d.立绘, "人物立绘");
        贴立绘(右立绘, 右立绘提示, d.玩家立绘, "玩家立绘");
        建回答(d);
        播放情绪(d.情绪, d.情绪强度);
    }

    void 贴立绘(Image 图, Text 提示, string 资源名, string 占位字)
    {
        Sprite sp = null;
        if (!string.IsNullOrEmpty(资源名)) sp = Resources.Load<Sprite>("立绘/" + 资源名);
        图.sprite = sp;
        图.color = sp != null ? Color.white : 立绘底色;
        提示.text = sp != null ? "" : 占位字;
    }

    // ============================================================ 回答按钮

    void 清回答()
    {
        for (int i = 0; i < 回答按钮.Count; i++) if (回答按钮[i] != null) Destroy(回答按钮[i]);
        回答按钮.Clear();
    }

    void 建回答(DialogueDefinition d)
    {
        清回答();
        int n = d.回答数;
        if (n <= 0)
        {
            // 没有回答 → 给一个「继续」：能进下一段就进，否则结束
            var db = DialogueDatabase.取();
            int 下一段 = 当前分段 + 1;
             bool 有下一段 = db != null && 下一段 <= db.最大分段(当前NpcId) && db.取段(当前NpcId, 下一段) != null;
            if (有下一段) 加按钮("继续 ▸", () => 显示分段(下一段));
            else 加按钮("结束", () => 收起来());
            return;
        }
        for (int i = 1; i <= n; i++)
        {
            string 文字 = d.取回答(i);
            int 跳 = d.取跳转(i);
            int 序号 = i;
            加按钮(文字, () => { if (跳 > 0) 显示分段(跳); else 收起来(); });
        }
    }

    void 加按钮(string 文字, Action 点击)
    {
        var go = new GameObject("回答" + (回答按钮.Count + 1), typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(回答列, false);
        var rt = (RectTransform)go.transform;
        rt.sizeDelta = new Vector2(0f, 74f);
        var le = go.AddComponent<LayoutElement>();
        le.preferredHeight = 74f;
        le.minHeight = 52f;

        var img = go.GetComponent<Image>();
        img.color = 回答底色;
        img.raycastTarget = true;

        var btn = go.GetComponent<Button>();
        var 颜色 = btn.colors;
        颜色.normalColor = Color.white;
        颜色.highlightedColor = 回答悬停色;
        颜色.pressedColor = new Color(0.7f, 0.68f, 0.55f, 1f);
        btn.colors = 颜色;
        btn.onClick.AddListener(() => { if (点击 != null) 点击(); });

        var t = new GameObject("字", typeof(RectTransform), typeof(Text)).GetComponent<Text>();
        t.transform.SetParent(go.transform, false);
        var trt = (RectTransform)t.transform;
        trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
        trt.offsetMin = new Vector2(10f, 4f); trt.offsetMax = new Vector2(-10f, -4f);
        t.font = 取字体();
        t.fontSize = 26;
        t.alignment = TextAnchor.MiddleLeft;
        t.color = 文字色;
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Truncate;
        t.text = 文字;
        t.raycastTarget = false;

        回答按钮.Add(go);
    }

    // ============================================================ 每帧

    void Update()
    {
        if (根 == null || !根.activeSelf) return;
        if (Time.frameCount == 打开帧) return;   // 打开它的那一下 F 别顺手关掉

        if (Input.GetKeyDown(KeyCode.Escape)) { 收起来(); return; }

        // 没有回答的段落：按 F / 空格 继续
        if (回答按钮.Count == 1 && 回答按钮[0] != null && (Input.GetKeyDown(继续键) || Input.GetKeyDown(KeyCode.Space)))
        {
            var btn = 回答按钮[0].GetComponent<Button>();
            if (btn != null) btn.onClick.Invoke();
        }
    }

    // ============================================================ 情绪特效（可扩展）

    /// <summary>
    /// 播放情绪。名字来自对话表 `情绪` 列；<see cref="自定义情绪"/> 里注册过的优先。
    /// 强度来自 `情绪强度` 列（1 = 标准，0.5 = 弱一半）。
    /// </summary>
    public void 播放情绪(string 名, float 强度)
    {
        if (string.IsNullOrEmpty(名) || 名 == "无" || 名 == "0") return;
        if (强度 <= 0f) 强度 = 1f;

        Action<DialogueUI, float> 动作;
        if (自定义情绪.TryGetValue(名, out 动作) && 动作 != null) { 动作(this, 强度); return; }

        switch (名)
        {
            case "震动": case "震惊": case "惊讶": StartCoroutine(震动(强度)); break;
            case "冒泡": case "冒泡泡": StartCoroutine(冒泡(强度)); break;
            case "发怒": case "生气": StartCoroutine(闪色(强度, new Color(1f, 0.35f, 0.30f, 1f))); break;
            case "害羞": StartCoroutine(闪色(强度, new Color(1f, 0.62f, 0.78f, 1f))); break;
            default: Debug.Log("[对话] 未知情绪「" + 名 + "」——可以用 DialogueUI.自定义情绪 注册，或在这里加一段表现"); break;
        }
    }

    /// <summary>震动：整个对话框 + 立绘一起抖（表示震惊）</summary>
    public IEnumerator 震动(float 强度)
    {
        if (根RT == null) yield break;
        Vector2 原 = 根RT.anchoredPosition;
        float t = 0f, 时长 = 0.45f;
        float 幅度 = 16f * Mathf.Clamp(强度, 0.2f, 3f);
        while (t < 时长)
        {
            t += Time.unscaledDeltaTime;
            float 衰减 = 1f - Mathf.Clamp01(t / 时长);
            根RT.anchoredPosition = 原 + UnityEngine.Random.insideUnitCircle * 幅度 * 衰减;
            yield return null;
        }
        根RT.anchoredPosition = 原;
    }

    /// <summary>冒泡：从人物立绘下方冒出一串泡泡</summary>
    public IEnumerator 冒泡(float 强度)
    {
        int 个数 = Mathf.Clamp(Mathf.RoundToInt(6f * 强度), 2, 16);
        for (int i = 0; i < 个数; i++)
        {
            StartCoroutine(一颗泡(i * 0.08f, 强度));
        }
        yield return null;
    }

    IEnumerator 一颗泡(float 延迟, float 强度)
    {
        if (左立绘 == null) yield break;
        yield return new WaitForSecondsRealtime(延迟);
        var go = new GameObject("泡", typeof(RectTransform), typeof(Image));
        go.transform.SetParent(左立绘.transform, false);
        var rt = (RectTransform)go.transform;
        float 大小 = UnityEngine.Random.Range(16f, 34f) * Mathf.Clamp(强度, 0.4f, 2f);
        rt.sizeDelta = new Vector2(大小, 大小);
        float x = UnityEngine.Random.Range(-0.3f, 0.3f);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f + x, 0.12f);
        rt.anchoredPosition = Vector2.zero;
        var img = go.GetComponent<Image>();
        img.sprite = 取圆点();
        img.raycastTarget = false;
        img.color = new Color(1f, 1f, 1f, 0.75f);

        float t = 0f, 时长 = 1.5f;
        while (t < 时长)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / 时长);
            rt.anchoredPosition = new Vector2(Mathf.Sin((k * 3f + x) * 6.28f) * 14f, k * 260f);
            var c = img.color; c.a = 0.75f * (1f - k); img.color = c;
            var s = 大小 * (0.8f + 0.4f * k); rt.sizeDelta = new Vector2(s, s);
            yield return null;
        }
        Destroy(go);
    }

    /// <summary>闪一下颜色（发怒/害羞共用）</summary>
    public IEnumerator 闪色(float 强度, Color 色)
    {
        if (文本框底 == null) yield break;
        Color 原 = 文本框底色;
        float t = 0f, 时长 = 0.55f;
        while (t < 时长)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Sin(Mathf.Clamp01(t / 时长) * Mathf.PI);   // 0→1→0
            文本框底.color = Color.Lerp(原, 色, k * Mathf.Clamp(强度, 0.2f, 1.5f));
            yield return null;
        }
        文本框底.color = 原;
    }

    static Sprite 圆点缓存;
    static Sprite 取圆点()
    {
        if (圆点缓存 != null) return 圆点缓存;
        const int N = 64;
        var tex = new Texture2D(N, N, TextureFormat.RGBA32, false);
        float r = N * 0.5f;
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(r, r));
                float a = Mathf.Clamp01((r - d) / 2.5f);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        tex.Apply();
        圆点缓存 = Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f));
        return 圆点缓存;
    }

    // ============================================================ 搭界面（照示意图）

    Font 取字体()
    {
        if (字体 != null) return 字体;
        return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    }

    static Image 建图(string 名, Transform 父, Color 色)
    {
        var go = new GameObject(名, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(父, false);
        var img = go.GetComponent<Image>();
        img.color = 色;
        img.raycastTarget = false;
        return img;
    }

    static Text 建字(string 名, Transform 父, Font 字体, int 字号, Color 色, TextAnchor 对齐)
    {
        var go = new GameObject(名, typeof(RectTransform), typeof(Text));
        go.transform.SetParent(父, false);
        var t = go.GetComponent<Text>();
        t.font = 字体;
        t.fontSize = 字号;
        t.color = 色;
        t.alignment = 对齐;
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        return t;
    }

    static void 铺满(RectTransform rt, Vector4 边距)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(边距.x, 边距.w);
        rt.offsetMax = new Vector2(-边距.y, -边距.z);
    }

    static void 摆块(RectTransform rt, float x0, float y0, float x1, float y1)
    {
        rt.anchorMin = new Vector2(x0, y0);
        rt.anchorMax = new Vector2(x1, y1);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    void 搭界面()
    {
        var 画布 = gameObject.GetComponent<Canvas>();
        if (画布 == null) 画布 = gameObject.AddComponent<Canvas>();
        画布.renderMode = RenderMode.ScreenSpaceOverlay;
        画布.sortingOrder = 900;
        if (gameObject.GetComponent<CanvasScaler>() == null)
        {
            var cs = gameObject.AddComponent<CanvasScaler>();
            cs.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            cs.referenceResolution = new Vector2(1920f, 1080f);
            cs.matchWidthOrHeight = 0.5f;
        }
        if (gameObject.GetComponent<GraphicRaycaster>() == null) gameObject.AddComponent<GraphicRaycaster>();
        if (FindObjectOfType<EventSystem>() == null)
        {
            var es = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            es.transform.SetParent(null);
        }

        根 = new GameObject("对话根", typeof(RectTransform));
        根.transform.SetParent(transform, false);
        根RT = (RectTransform)根.transform;
        铺满(根RT, Vector4.zero);

        // 左：人物立绘（0 → 29.8% 宽，从底到 74.7% 高）
        左立绘 = 建图("人物立绘", 根.transform, 立绘底色);
        摆块((RectTransform)左立绘.transform, 0f, 0f, 0.298f, 0.747f);
        左立绘提示 = 建字("人物立绘字", 左立绘.transform, 取字体(), 34, 文字色, TextAnchor.MiddleCenter);
        铺满((RectTransform)左立绘提示.transform, Vector4.zero);
        左立绘提示.text = "人物立绘";

        // 右：玩家立绘（77.4% → 1）
        右立绘 = 建图("玩家立绘", 根.transform, 立绘底色);
        摆块((RectTransform)右立绘.transform, 0.774f, 0f, 1f, 0.747f);
        右立绘提示 = 建字("玩家立绘字", 右立绘.transform, 取字体(), 30, 文字色, TextAnchor.MiddleCenter);
        铺满((RectTransform)右立绘提示.transform, Vector4.zero);
        右立绘提示.text = "玩家立绘";

        // 中下：灰底对话框（13.2% → 68.4% 宽，底 → 27.7% 高）
        文本框底 = 建图("对话框", 根.transform, 文本框底色);
        摆块((RectTransform)文本框底.transform, 0.132f, 0f, 0.684f, 0.277f);
        文本框底.raycastTarget = true;

        // 名字标签（框内左上角）
        var 名字底 = 建图("名字底", 文本框底.transform, 名字底色);
        var 名字RT = (RectTransform)名字底.transform;
        名字RT.anchorMin = 名字RT.anchorMax = new Vector2(0f, 1f);
        名字RT.pivot = new Vector2(0f, 1f);
        名字RT.anchoredPosition = new Vector2(18f, -12f);
        名字RT.sizeDelta = new Vector2(240f, 56f);
        名字文本 = 建字("名字", 名字底.transform, 取字体(), 28, 名字字色, TextAnchor.MiddleLeft);
        铺满((RectTransform)名字文本.transform, new Vector4(18f, 10f, 0f, 0f));

        // 正文
        内容文本 = 建字("对话内容", 文本框底.transform, 取字体(), 32, 文字色, TextAnchor.MiddleCenter);
        铺满((RectTransform)内容文本.transform, new Vector4(38f, 38f, 76f, 26f));

        // 右：回答列（72.9% → 88.5% 宽，10% → 27.7% 高）
        var 列 = new GameObject("回答列", typeof(RectTransform), typeof(VerticalLayoutGroup));
        列.transform.SetParent(根.transform, false);
        var 列RT = (RectTransform)列.transform;
        摆块(列RT, 0.729f, 0.098f, 0.885f, 0.277f);
        回答列 = 列RT;
        var vlg = 列.GetComponent<VerticalLayoutGroup>();
        vlg.spacing = 14f;
        vlg.childAlignment = TextAnchor.UpperLeft;
        vlg.childControlWidth = true;
        vlg.childControlHeight = false;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
    }
}
