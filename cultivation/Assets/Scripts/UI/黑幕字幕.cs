using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// **黑幕白字过场**（用户 2026-09-27 的主线需求）。
///
/// 需求原文：
///   · 黑幕白字**逐字打出**；**左键长按屏幕加速**；文字**尽量居中**；字号合适。
///   · 四幕里还要"**黑屏加白屏闪一下**"表示大师兄一刀把野猪劈了 → <see cref="闪白"/>。
///
/// 用法（协程，方便在任务/演出里顺序编排）：
/// ```csharp
/// yield return 黑幕字幕.确保().说(new[] {
///     "这是一个漫长的故事",
///     "讲述了一个少年如何一步步争渡成仙",
///     "那个少年的名字叫做：" });
/// ```
/// 每行打完停 <see cref="行间停顿"/> 秒再打下一行；**点一下**立刻打完当前行，
/// **按住左键**打字速度 ×<see cref="长按加速倍率"/>。
///
/// 顺带提供**演出锁**：<see cref="演出中"/> 为真时，别的地方（玩家移动/攻击）应该停手。
/// 用 <see cref="开始演出"/> / <see cref="结束演出"/> 成对增减（支持嵌套），
/// 过场协程里用 <c>using</c> 风格的 <see cref="演出区"/> 更省事。
/// </summary>
[DisallowMultipleComponent]
public class 黑幕字幕 : MonoBehaviour
{
    public static 黑幕字幕 实例 { get; private set; }

    [Header("外观")]
    public Font 字体;
    [Tooltip("字号（1080p 参考分辨率下，44 左右看着合适）")]
    public int 字号 = 44;
    public Color 幕色 = Color.black;
    public Color 字色 = new Color(0.94f, 0.94f, 0.94f, 1f);

    [Header("打字")]
    [Tooltip("每秒打几个字")]
    public float 每秒字数 = 18f;

    [Tooltip("按住左键时的加速倍率")]
    public float 长按加速倍率 = 6f;

    [Tooltip("每行打完停多久再换下一行（秒）")]
    public float 行间停顿 = 0.7f;

    [Tooltip("打完之后要不要等玩家点一下才结束这一行")]
    public bool 每行等点击 = false;

    Canvas 画布;
    Image 幕;
    Image 闪;
    Text 文本;
    bool 在打字;

    /// <summary>黑幕是否在显示</summary>
    public bool 幕在显示 => 幕 != null && 幕.gameObject.activeSelf;

    // ============================================================ 演出锁

    static int 锁层数;
    /// <summary>正在演出（过场/黑幕/强制对话…）—— 玩家移动与攻击应该听它的</summary>
    public static bool 演出中 => 锁层数 > 0;
    public static void 开始演出() => 锁层数++;
    public static void 结束演出() { 锁层数 = Mathf.Max(0, 锁层数 - 1); }
    public static void 强制解锁() { 锁层数 = 0; }

    // ============================================================ 门面

    public static 黑幕字幕 确保()
    {
        if (实例 != null) return 实例;
        var go = new GameObject("黑幕字幕");
        return go.AddComponent<黑幕字幕>();
    }

    /// <summary>打一段（每行一句），打完整段才返回</summary>
    public static IEnumerator 说(params string[] 行) => 确保().逐行打(行, 0f);

    public static void 清空() { if (实例 != null) 实例.立即清空(); }

    /// <summary>黑幕+白字一起收掉（露出场景）</summary>
    public static void 收幕() { if (实例 != null) 实例.收起(); }

    public static IEnumerator 闪白(float 时长 = 0.3f) => 确保().做闪白(时长);

    // ============================================================ 生命周期

    void Awake()
    {
        if (实例 != null && 实例 != this) { Destroy(gameObject); return; }
        实例 = this;
        搭界面();
        收起();
    }

    void OnDestroy() { if (实例 == this) 实例 = null; }

    void Update()
    {
        // 整段演出期间也上锁（防止黑幕期间玩家乱跑）
        if (幕在显示 && !演出中) 开始演出();
        else if (!幕在显示 && 演出中) 结束演出();
    }

    // ============================================================ 打字

    /// <summary>逐行打字。额外停顿 = 每行打完再多等这么久</summary>
    public IEnumerator 逐行打(string[] 行, float 额外停顿)
    {
        落下();
        for (int i = 0; i < 行.Length; i++)
        {
            立即清空();
            yield return 打一行(行[i]);
            float 停 = 行间停顿 + 额外停顿;
            if (停 > 0f) yield return new WaitForSeconds(停);
        }
    }

    IEnumerator 打一行(string 整句)
    {
        全句 = 整句 ?? "";
        文本.text = "";
        在打字 = true;

        float 出 = 0f;
        while (出 < 全句.Length)
        {
            // 长按左键 → 加速（用户要求）
            float 倍 = Input.GetMouseButton(0) ? Mathf.Max(1f, 长按加速倍率) : 1f;
            // 点一下 → 直接打完这一行
            if (Input.GetMouseButtonDown(0)) { 文本.text = 全句; break; }

            出 += 每秒字数 * 倍 * Time.unscaledDeltaTime;
            int 个数 = Mathf.Clamp(Mathf.FloorToInt(出), 0, 全句.Length);
            if (文本.text.Length != 个数) 文本.text = 全句.Substring(0, 个数);
            yield return null;
        }
        文本.text = 全句;

        if (每行等点击)
        {
            // 等一次"按下—抬起"，免得同一次点击被吃两次
            yield return null;
            while (!Input.GetMouseButtonDown(0)) yield return null;
        }
        在打字 = false;
    }

    [Tooltip("当前这一行的完整文本（打字机内部用）")]
    string 全句;

    public void 立即清空() { if (文本 != null) 文本.text = ""; }

    // ============================================================ 幕 / 闪白

    void 落下()
    {
        if (幕 != null) 幕.gameObject.SetActive(true);
        if (画布 != null) 画布.enabled = true;
    }

    public void 收起()
    {
        立即清空();
        if (幕 != null) 幕.gameObject.SetActive(false);
        if (画布 != null) 画布.enabled = false;
        结束演出();
        锁层数 = 0;
    }

    public IEnumerator 做闪白(float 时长)
    {
        落下();
        if (闪 == null) yield break;
        闪.gameObject.SetActive(true);
        // 黑 → 白 → 透明
        yield return 淡(闪, new Color(1f, 1f, 1f, 0f), Color.white, 时长 * 0.35f);
        yield return new WaitForSeconds(0.06f);
        yield return 淡(闪, Color.white, new Color(1f, 1f, 1f, 0f), 时长 * 0.6f);
        闪.gameObject.SetActive(false);
    }

    static IEnumerator 淡(Graphic g, Color 从, Color 到, float 时长)
    {
        if (g == null) yield break;
        float t = 0f;
        g.color = 从;
        while (t < 时长)
        {
            t += Time.unscaledDeltaTime;
            g.color = Color.Lerp(从, 到, Mathf.Clamp01(t / Mathf.Max(0.0001f, 时长)));
            yield return null;
        }
        g.color = 到;
    }

    // ============================================================ 搭界面

    void 搭界面()
    {
        画布 = gameObject.GetComponent<Canvas>();
        if (画布 == null) 画布 = gameObject.AddComponent<Canvas>();
        画布.renderMode = RenderMode.ScreenSpaceOverlay;
        画布.sortingOrder = 2900;                 // 高于对话框(2600)，低于暂停菜单(3000)
        if (gameObject.GetComponent<CanvasScaler>() == null)
        {
            var cs = gameObject.AddComponent<CanvasScaler>();
            cs.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            cs.referenceResolution = new Vector2(1920f, 1080f);
            cs.matchWidthOrHeight = 0.5f;
        }
        if (gameObject.GetComponent<GraphicRaycaster>() == null) gameObject.AddComponent<GraphicRaycaster>();

        // 黑幕（挡住整个画面，也吃掉点击 —— 但打字逻辑用的是全局 Input，照样能加速）
        幕 = 建图("黑幕", 幕色, 0);
        // 白闪（在黑幕之上）
        闪 = 建图("白闪", new Color(1f, 1f, 1f, 0f), 1);

        // 正文：居中，留左右边距，行距宽松
        文本 = 建字("字幕", 字号);
        文本.alignment = TextAnchor.MiddleCenter;
        文本.lineSpacing = 1.35f;
        var rt = (RectTransform)文本.transform;
        rt.anchorMin = new Vector2(0.12f, 0.35f);
        rt.anchorMax = new Vector2(0.88f, 0.65f);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    Image 建图(string 名, Color 色, int 序)
    {
        var go = new GameObject(名, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(transform, false);
        var img = go.GetComponent<Image>();
        img.color = 色;
        img.raycastTarget = true;                 // 吃掉点击，别点到后面的 UI
        var rt = (RectTransform)go.transform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        go.transform.SetSiblingIndex(Mathf.Clamp(序, 0, transform.childCount - 1));
        return img;
    }

    Text 建字(string 名, int 号)
    {
        var go = new GameObject(名, typeof(RectTransform), typeof(Text));
        go.transform.SetParent(transform, false);
        var t = go.GetComponent<Text>();
        t.font = 字体 != null ? 字体 : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        t.fontSize = 号;
        t.color = 字色;
        t.alignment = TextAnchor.MiddleCenter;
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        return t;
    }

    // ---- ASCII 别名 ----
    public static bool InCutscene => 演出中;
    public static 黑幕字幕 Ensure() => 确保();
}
