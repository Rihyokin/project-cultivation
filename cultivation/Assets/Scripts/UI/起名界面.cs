using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// **玩家起名界面**（主线第一幕用）。
///
/// 需求原文：「玩家起名，回车键确认，**5 个汉字以内**」。
///
/// 做法：复用黑幕那套视觉（全屏黑底 + 居中白字），中间一行是输入内容 + 闪烁光标，
/// 下面一行小字提示。输入不走 `InputField`（省得和中文输入法、UI 焦点打架），
/// 直接用 <c>Input.inputString</c> 收字符并自己过滤/限长。
///
/// 用法（协程，接着黑幕字幕往下排）：
/// ```csharp
/// yield return 黑幕字幕.说("这是一个漫长的故事", "讲述了一个少年如何一步步争渡成仙", "那个少年的名字叫做：");
/// string 名字 = null;
/// yield return 起名界面.取名("那个少年的名字叫做：", s => 名字 = s);
/// // 之后 名字 就是玩家输入（<=5 字），也可以随时读 起名界面.当前名字
/// ```
/// 输入规则：汉字 / 字母 / 数字都能收；最多 <see cref="最大字数"/> 个字符；
/// 退格删除；**回车（或小键盘回车）确认**；空名字不让确认。
/// </summary>
[DisallowMultipleComponent]
public class 起名界面 : MonoBehaviour
{
    public static 起名界面 实例 { get; private set; }

    [Header("外观")]
    public Font 字体;
    public int 标题字号 = 44;
    public int 输入字号 = 64;
    public int 提示字号 = 22;
    public Color 幕色 = Color.black;
    public Color 字色 = new Color(0.94f, 0.94f, 0.94f, 1f);
    public Color 提示色 = new Color(0.55f, 0.55f, 0.55f, 1f);

    [Header("规则")]
    [Tooltip("最多几个字符（用户要求 5 个汉字以内）")]
    public int 最大字数 = 5;

    [Tooltip("光标闪烁周期（秒）")]
    public float 光标周期 = 0.7f;

    /// <summary>玩家最终确定的名字（没确认过就是空串）</summary>
    public static string 当前名字 { get; private set; } = "";

    /// <summary>界面上正在输入的内容</summary>
    public string 缓冲 { get; private set; } = "";

    bool 在输入;
    bool 已确认;
    Canvas 画布;
    Image 幕;
    Text 标题文本;
    Text 输入文本;
    Text 光标;
    Text 提示文本;

    public bool 在取名 => 幕 != null && 幕.gameObject.activeSelf;

    // ============================================================ 门面

    public static 起名界面 确保()
    {
        if (实例 != null) return 实例;
        var go = new GameObject("起名界面");
        return go.AddComponent<起名界面>();
    }

    /// <summary>弹起名界面，等到玩家回车确认才返回；确认的名字通过回调给出</summary>
    public static IEnumerator 取名(string 标题, System.Action<string> 完成)
    {
        var ui = 确保();
        yield return ui.做取名(标题);
        if (完成 != null) 完成(ui.缓冲);
    }

    // ============================================================ 主流程

    public IEnumerator 做取名(string 标题)
    {
        搭界面();
        标题文本.text = 标题 ?? "";
        缓冲 = "";
        已确认 = false;
        在输入 = true;
        幕.gameObject.SetActive(true);
        画布.enabled = true;
        // 演出期间别让玩家乱跑
        黑幕字幕.开始演出();

        float 计时 = 0f;
        while (!已确认)
        {
            // ---- 收字符 ----
            string 收 = Input.inputString;
            for (int i = 0; i < 收.Length; i++)
            {
                char c = 收[i];
                if (c == '\b')                      // 退格
                {
                    if (缓冲.Length > 0) 缓冲 = 缓冲.Substring(0, 缓冲.Length - 1);
                }
                else if (c == '\n' || c == '\r')    // 回车确认
                {
                    if (缓冲.Length > 0) 已确认 = true;
                }
                else if (!char.IsControl(c) && 缓冲.Length < 最大字数)
                {
                    缓冲 += c;
                }
            }
            // 小键盘回车
            if (Input.GetKeyDown(KeyCode.KeypadEnter) && 缓冲.Length > 0) 已确认 = true;

            输入文本.text = 缓冲;

            // ---- 光标闪烁（用下划线，不占字符）----
            计时 += Time.unscaledDeltaTime;
            光标.enabled = (计时 % Mathf.Max(0.05f, 光标周期)) < 光标周期 * 0.5f;

            yield return null;
        }

        当前名字 = 缓冲;
        在输入 = false;
        Debug.Log("[起名] 玩家名字确定为「" + 当前名字 + "」");
        收起();
    }

    public void 收起()
    {
        在输入 = false;
        if (幕 != null) 幕.gameObject.SetActive(false);
        if (画布 != null) 画布.enabled = false;
        黑幕字幕.结束演出();
    }

    /// <summary>外部直接设定名字（读档时用）</summary>
    public static void 设定名字(string 名字) { 当前名字 = 名字 ?? ""; }

    // ============================================================ 搭界面

    void Awake()
    {
        if (实例 != null && 实例 != this) { Destroy(gameObject); return; }
        实例 = this;
        搭界面();
        收起();
    }

    void OnDestroy() { if (实例 == this) 实例 = null; }

    void 搭界面()
    {
        if (画布 != null) return;

        画布 = gameObject.GetComponent<Canvas>();
        if (画布 == null) 画布 = gameObject.AddComponent<Canvas>();
        画布.renderMode = RenderMode.ScreenSpaceOverlay;
        画布.sortingOrder = 2950;                  // 在字幕(2900)之上、暂停菜单(3000)之下
        if (gameObject.GetComponent<CanvasScaler>() == null)
        {
            var cs = gameObject.AddComponent<CanvasScaler>();
            cs.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            cs.referenceResolution = new Vector2(1920f, 1080f);
            cs.matchWidthOrHeight = 0.5f;
        }
        if (gameObject.GetComponent<GraphicRaycaster>() == null) gameObject.AddComponent<GraphicRaycaster>();

        // 黑幕
        var 幕go = new GameObject("黑幕", typeof(RectTransform), typeof(Image));
        幕go.transform.SetParent(transform, false);
        幕 = 幕go.GetComponent<Image>();
        幕.color = 幕色;
        幕.raycastTarget = true;
        拉满((RectTransform)幕go.transform);

        // 标题（偏上）
        标题文本 = 建字("标题", 标题字号, 字色);
        var 标题rt = (RectTransform)标题文本.transform;
        标题rt.anchorMin = new Vector2(0.1f, 0.62f);
        标题rt.anchorMax = new Vector2(0.9f, 0.72f);
        标题rt.offsetMin = Vector2.zero; 标题rt.offsetMax = Vector2.zero;

        // 输入内容（正中）+ 光标
        输入文本 = 建字("输入", 输入字号, 字色);
        var 输入rt = (RectTransform)输入文本.transform;
        输入rt.anchorMin = new Vector2(0.1f, 0.44f);
        输入rt.anchorMax = new Vector2(0.9f, 0.58f);
        输入rt.offsetMin = Vector2.zero; 输入rt.offsetMax = Vector2.zero;

        光标 = 建字("光标", 输入字号, 字色);
        光标.text = "_";
        var 光标rt = (RectTransform)光标.transform;
        光标rt.anchorMin = new Vector2(0.1f, 0.44f);
        光标rt.anchorMax = new Vector2(0.9f, 0.58f);
        光标rt.offsetMin = Vector2.zero; 光标rt.offsetMax = Vector2.zero;
        // 输入和光标并排：输入左对齐推到中间，光标紧跟在它右边 —— 简单起见让光标居中偏右一点
        输入rt.pivot = new Vector2(0.5f, 0.5f);
        光标rt.pivot = new Vector2(0f, 0.5f);
        光标rt.anchorMin = 光标rt.anchorMax = new Vector2(0.62f, 0.51f);
        光标rt.sizeDelta = new Vector2(40f, 输入字号 + 10f);
        输入rt.anchorMin = 输入rt.anchorMax = new Vector2(0.60f, 0.51f);
        输入rt.sizeDelta = new Vector2(560f, 输入字号 + 16f);
        输入文本.alignment = TextAnchor.MiddleRight;

        // 提示（偏下）
        提示文本 = 建字("提示", 提示字号, 提示色);
        提示文本.text = "输入名字，回车确认（最多 " + 最大字数 + " 个字）";
        var 提示rt = (RectTransform)提示文本.transform;
        提示rt.anchorMin = new Vector2(0.1f, 0.30f);
        提示rt.anchorMax = new Vector2(0.9f, 0.38f);
        提示rt.offsetMin = Vector2.zero; 提示rt.offsetMax = Vector2.zero;
    }

    Text 建字(string 名, int 号, Color 色)
    {
        var go = new GameObject(名, typeof(RectTransform), typeof(Text));
        go.transform.SetParent(transform, false);
        var t = go.GetComponent<Text>();
        t.font = 字体 != null ? 字体 : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        t.fontSize = 号;
        t.color = 色;
        t.alignment = TextAnchor.MiddleCenter;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        return t;
    }

    static void 拉满(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
    }

    // ---- ASCII 别名 ----
    public static string PlayerName => 当前名字;
    public static IEnumerator AskName(string title, System.Action<string> done) => 取名(title, done);
}
