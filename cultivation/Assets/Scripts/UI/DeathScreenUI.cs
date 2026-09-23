using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 死亡界面：全屏半透明幕布 + 「杀伐道途，终是棋差一招」 + 「你死了」 + 「重生」按钮。
///
/// 和 `暂停菜单` 一样是**运行时自己搭 Canvas**（项目没导 TMP，统一用 uGUI 传统 Text + SimHei）。
/// 搭好之后默认隐藏，由 <see cref="PlayerDeathSequence"/> 在玩家死亡 2 秒后打开。
///
/// **按钮只发事件，不自己决定去哪复活** —— 复活的逻辑在 <see cref="PlayerDeathSequence"/> 里。
/// </summary>
public class DeathScreenUI : MonoBehaviour
{
    [Header("字体")]
    [Tooltip("中文字体。留空会自动去 Assets/Fonts 找 SimHei")]
    public Font 字体;

    [Header("配色（照着概念图）")]
    public Color 幕布色 = new Color(0f, 0f, 0f, 0.62f);          // 半透明，能看见背后的场景
    public Color 标题色 = new Color(0.96f, 0.96f, 0.94f, 1f);
    public Color 副标题色 = new Color(0.88f, 0.88f, 0.86f, 1f);
    public Color 按钮底色 = new Color(1f, 0.92f, 0.10f, 1f);      // 概念图里那个亮黄
    public Color 按钮字色 = new Color(0.10f, 0.10f, 0.10f, 1f);

    [Header("排版")]
    public string 标题文本 = "杀伐道途，终是棋差一招";
    public string 副标题文本 = "你死了";
    public string 按钮文本 = "重生";
    public int 标题字号 = 64;
    public int 副标题字号 = 30;
    public int 按钮字号 = 34;
    public Vector2 按钮尺寸 = new Vector2(210f, 84f);
    public float 标题Y = 150f;          // 相对屏幕中心的偏移
    public float 副标题Y = 60f;
    public float 按钮Y = -90f;

    /// <summary>点了「重生」</summary>
    public event System.Action 点了重生;

    Canvas 画布;

    /// <summary>界面是不是开着</summary>
    public bool 已显示 => 画布 != null && 画布.gameObject.activeSelf;

    void Awake()
    {
        if (字体 == null) 取默认字体();
        if (字体 == null) Debug.LogError("[死亡界面] 找不到中文字体，文字会显示成方块", this);
        搭界面();
        隐藏();
    }

    void 取默认字体()
    {
#if UNITY_EDITOR
        字体 = UnityEditor.AssetDatabase.LoadAssetAtPath<Font>("Assets/Fonts/SimHei.ttf");
#endif
        if (字体 == null)
        {
            var 全部 = Resources.FindObjectsOfTypeAll<Font>();
            foreach (var f in 全部)
                if (f != null && f.name.ToLowerInvariant().Contains("simhei")) { 字体 = f; return; }
        }
    }

    public void 显示()
    {
        if (画布 != null) 画布.gameObject.SetActive(true);
    }

    public void 隐藏()
    {
        if (画布 != null) 画布.gameObject.SetActive(false);
    }

    // ============================================================ 搭界面

    void 搭界面()
    {
        var 根 = new GameObject("DeathScreenCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        根.transform.SetParent(transform, false);
        画布 = 根.GetComponent<Canvas>();
        画布.renderMode = RenderMode.ScreenSpaceOverlay;
        画布.sortingOrder = 4000;                 // 盖过暂停菜单(3000)和 HUD

        var 缩放 = 根.GetComponent<CanvasScaler>();
        缩放.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        缩放.referenceResolution = new Vector2(1920f, 1080f);
        缩放.matchWidthOrHeight = 0.5f;

        // 全屏半透明幕布（射线挡住，防止点穿到背后的 HUD）
        var 幕布 = UIBuildUtils.CreateImage("幕布", 根.transform, 幕布色);
        幕布.raycastTarget = true;          // 挡住射线，防止点穿到背后的 HUD / 世界
        UIBuildUtils.Stretch(幕布.rectTransform);

        // 标题
        var 标题 = UIBuildUtils.CreateText("标题", 根.transform, 字体, 标题文本, 标题字号,
            TextAnchor.MiddleCenter, 标题色);
        摆(标题.rectTransform, 标题Y, new Vector2(1400f, 120f));
        UIBuildUtils.AddOutline(标题.rectTransform, new Color(0f, 0f, 0f, 0.55f));

        // 副标题
        var 副标题 = UIBuildUtils.CreateText("副标题", 根.transform, 字体, 副标题文本, 副标题字号,
            TextAnchor.MiddleCenter, 副标题色);
        摆(副标题.rectTransform, 副标题Y, new Vector2(800f, 60f));

        // 重生按钮（自己搭，好控制那个亮黄底 + 黑字）
        var 按钮图 = UIBuildUtils.CreateImage("重生按钮", 根.transform, 按钮底色);
        摆(按钮图.rectTransform, 按钮Y, 按钮尺寸);
        // ★★★ 一定要开：UIBuildUtils.CreateImage 默认 raycastTarget=false（那是 HUD 的省性能约定），
        // 不开的话射线**打不到按钮**，表现就是"按钮点不了"（而且 Console 一点报错都没有）。
        按钮图.raycastTarget = true;

        var 按钮 = 按钮图.gameObject.AddComponent<Button>();
        按钮.targetGraphic = 按钮图;
        var 配色 = 按钮.colors;
        配色.normalColor = Color.white;
        配色.highlightedColor = new Color(1f, 1f, 1f, 1f);
        配色.pressedColor = new Color(0.85f, 0.85f, 0.85f, 1f);
        按钮.colors = 配色;
        按钮.onClick.AddListener(() => 点了重生?.Invoke());

        var 按钮文字 = UIBuildUtils.CreateText("文字", 按钮图.transform, 字体, 按钮文本, 按钮字号,
            TextAnchor.MiddleCenter, 按钮字色);
        UIBuildUtils.Stretch(按钮文字.rectTransform);
    }

    /// <summary>放在屏幕中心 + 纵向偏移处</summary>
    static void 摆(RectTransform rt, float 中心Y偏移, Vector2 尺寸)
    {
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = 尺寸;
        rt.anchoredPosition = new Vector2(0f, 中心Y偏移);
    }

    // ---- ASCII 别名 ----
    public event System.Action OnRespawnClicked { add { 点了重生 += value; } remove { 点了重生 -= value; } }
    public void Show() => 显示();
    public void Hide() => 隐藏();
    public bool IsShown => 已显示;
}
