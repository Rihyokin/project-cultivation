using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;

/// <summary>
/// 暂停菜单。按 ESC 开关，样式照参考图：
///   暗色半透明底 + 大字「暂停」+ 三个浅色按钮（设置 / 返回主界面 / 退出游戏）。
///
/// 【UI 是运行时自己建的】—— 不依赖场景里预先摆好的物件，
/// 所以只要把这个组件挂到场景里任意一个物体上就能用（见菜单
/// Cultivation/安装暂停菜单）。这样也不会因为场景被重建而丢。
///
/// 暂停靠 Time.timeScale = 0，恢复时还原成 1（记着原值，兼容将来可能的子弹时间）。
/// </summary>
[DisallowMultipleComponent]
public class PauseMenuUI : MonoBehaviour
{
    [Header("按键")]
    public KeyCode 暂停键 = KeyCode.Escape;

    [Header("外观（对着参考图调的，可按需改）")]
    public Color 底色 = new Color(0f, 0f, 0f, 0.72f);
    public Color 标题色 = new Color(0.90f, 0.90f, 0.90f, 1f);
    public Color 按钮色 = new Color(0.88f, 0.88f, 0.88f, 1f);
    public Color 按钮字色 = new Color(0.12f, 0.12f, 0.12f, 1f);
    public Vector2 按钮尺寸 = new Vector2(230f, 52f);
    public float 按钮间距 = 18f;

    [Header("字体")]
    [Tooltip("中文字体。留空则自动去 Assets/Fonts 里找 —— 用 Cultivation/安装暂停菜单 会自动填好")]
    public Font 字体;

    [Header("行为")]
    [Tooltip("勾上后暂停时不改变 timeScale（只显示界面）")]
    public bool 不改时间缩放 = false;

    [Tooltip("返回主界面的场景名")]
    public string 主界面场景 = "StartScene";

    GameObject 菜单根;
    bool 已暂停;
    float 原时间缩放 = 1f;

    /// <summary>外部查询用</summary>
    public bool IsPaused => 已暂停;

    void Awake()
    {
        建界面();
        设显示(false);
    }

    void OnDestroy()
    {
        // 组件被销毁时别把游戏卡在暂停状态
        if (已暂停 && !不改时间缩放) Time.timeScale = 原时间缩放;
    }

    void Update()
    {
        if (!Input.GetKeyDown(暂停键)) return;

        // 有别的全屏界面开着时（角色面板 / 设施界面），ESC 的语义是"关那个界面"，
        // 不该弹出暂停菜单 —— 否则一次 ESC 会同时关界面 + 暂停游戏。
        if (!已暂停 && UiEscRegistry.有别的界面开着()) return;

        // 【刚关过界面也不行】
        // 多个脚本的 Update 顺序不定：如果关界面的那个先跑，上面那行看到的
        // 已经是"没有界面开着"，暂停菜单就会跟着弹出来。
        // 看一眼"是不是刚刚才关过" —— 这个判断与执行顺序无关。
        if (!已暂停 && UiEscRegistry.刚关闭过()) return;

        切换();
    }

    public void 切换() { if (已暂停) 继续(); else 暂停(); }

    public void 暂停()
    {
        if (已暂停) return;
        已暂停 = true;

        原时间缩放 = Time.timeScale;
        // 【别把 0 当成"原来的值"】
        // 如果此刻角色面板/设施界面已经把它设成 0 了，存下来就是 0，
        // 于是退出暂停时恢复成 0 —— 游戏永远卡在暂停。
        if (原时间缩放 <= 0.001f) 原时间缩放 = 1f;

        if (!不改时间缩放) Time.timeScale = 0f;
        设显示(true);
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
        Debug.Log("[PauseMenuUI] 已暂停");
    }

    public void 继续()
    {
        if (!已暂停) return;
        已暂停 = false;
        // 双保险：绝不恢复成 0
        if (!不改时间缩放) Time.timeScale = 原时间缩放 > 0.001f ? 原时间缩放 : 1f;
        设显示(false);
        Debug.Log("[PauseMenuUI] 已继续（timeScale=" + Time.timeScale + "）");
    }

    void 设显示(bool 显示)
    {
        if (菜单根 != null && 菜单根.activeSelf != 显示) 菜单根.SetActive(显示);
    }

    // ---------------------------------------------------------------- 功能

    void 点设置()
    {
        // 暂时只打日志。设置面板以后单独做，这里留个明确的接入口。
        Debug.Log("[PauseMenuUI] 点了「设置」——设置面板尚未实现，先留空");
        // TODO: 打开设置面板（音量/画质/按键）
    }

    /// <summary>
    /// 保存到当前槽位（还没选过档就存 0 号）。
    /// 右上角弹「保存中…」→ 等一小会儿 → 弹「已保存」。
    /// </summary>
    void 点保存() => StartCoroutine(保存流程());

    System.Collections.IEnumerator 保存流程()
    {
        int 槽位 = SaveSystem.当前槽位;
        if (槽位 < 0) 槽位 = 0;

        ToastUI.提示("保存中…", 1.0f);
        yield return new WaitForSecondsRealtime(0.35f);      // 让「保存中」露个脸（暂停时 timeScale=0，用 Realtime）

        var 数据 = SaveSystem.当前存档;
        if (数据 == null) 数据 = SaveSystem.建新档();
        SaveSystem.从角色采集(数据);
        数据.刷新时间戳();

        bool 成功 = SaveSystem.存档(槽位, 数据);
        if (成功)
        {
            SaveSystem.当前存档 = 数据;
            SaveSystem.当前槽位 = 槽位;
            ToastUI.提示("已保存到槽位 " + (槽位 + 1) + "（" + 数据.境界 + "）");
        }
        else
        {
            ToastUI.提示("保存失败，看 Console");
        }
    }

    void 点返回主界面()
    {
        Debug.Log("[PauseMenuUI] 返回主界面：" + 主界面场景);
        // 离开前一定要还原 timeScale，否则新场景一进去就是静止的
        Time.timeScale = 1f;
        已暂停 = false;
        if (Application.CanStreamedLevelBeLoaded(主界面场景))
            SceneManager.LoadScene(主界面场景);
        else
            Debug.LogError("[PauseMenuUI] 场景「" + 主界面场景 + "」没进 Build Settings，加载不了");
    }

    void 点退出游戏()
    {
        Debug.Log("[PauseMenuUI] 退出游戏");
        Time.timeScale = 1f;
        已暂停 = false;
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;   // 编辑器里停播放
#else
        Application.Quit();
#endif
    }

    // ---------------------------------------------------------------- 建界面

    void 建界面()
    {
        if (菜单根 != null) return;

        // 场景里没有 EventSystem 的话按钮点不动，补一个
        if (FindObjectOfType<EventSystem>() == null)
        {
            var es = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            es.transform.SetParent(transform, false);
        }

        // 画布：最上层，别被别的 UI 盖住
        菜单根 = new GameObject("PauseMenuCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        菜单根.transform.SetParent(transform, false);
        var canvas = 菜单根.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 3000;                 // 高于角色面板等

        var scaler = 菜单根.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        // 暗色底
        var 底 = 建块("暗色底", 菜单根.transform, 底色);
        拉满(底);

        // 标题「暂停」
        var 标题 = 建文字("标题", 菜单根.transform, "暂停", 84, 标题色);
        var 标题rt = 标题.rectTransform;
        标题rt.anchorMin = 标题rt.anchorMax = new Vector2(0.5f, 1f);
        标题rt.pivot = new Vector2(0.5f, 1f);
        标题rt.anchoredPosition = new Vector2(0f, -140f);   // 参考图里标题偏上
        标题rt.sizeDelta = new Vector2(600f, 110f);

        // 三个按钮，纵向排在标题下面
        string[] 文案 = { "保存游戏", "设置", "返回主界面", "退出游戏" };
        System.Action[] 动作 = { 点保存, 点设置, 点返回主界面, 点退出游戏 };

        float 起始Y = -300f;
        for (int i = 0; i < 文案.Length; i++)
        {
            float y = 起始Y - i * (按钮尺寸.y + 按钮间距);
            建按钮("按钮_" + 文案[i], 菜单根.transform, 文案[i], new Vector2(0f, y), 动作[i]);
        }
    }

    Image 建块(string 名, Transform 父, Color 色)
    {
        var go = new GameObject(名, typeof(Image));
        go.transform.SetParent(父, false);
        var img = go.GetComponent<Image>();
        img.color = 色;
        img.raycastTarget = true;    // 挡住底下的点击，暂停时不误触游戏
        return img;
    }

    Text 建文字(string 名, Transform 父, string 内容, int 字号, Color 色)
    {
        var go = new GameObject(名, typeof(Text));
        go.transform.SetParent(父, false);
        var t = go.GetComponent<Text>();
        t.text = 内容;
        t.font = 取字体();
        t.fontSize = 字号;
        t.color = 色;
        t.alignment = TextAnchor.MiddleCenter;
        t.raycastTarget = false;
        return t;
    }

    void 建按钮(string 名, Transform 父, string 文案, Vector2 位置, System.Action 点击)
    {
        var go = new GameObject(名, typeof(Image), typeof(Button));
        go.transform.SetParent(父, false);
        var img = go.GetComponent<Image>();
        img.color = 按钮色;
        img.raycastTarget = true;                  // CreateImage 默认可能是 false，画面上会点不动

        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = 位置;
        rt.sizeDelta = 按钮尺寸;

        var 文字 = 建文字("文字", go.transform, 文案, 30, 按钮字色);
        拉满(文字.rectTransform);

        var btn = go.GetComponent<Button>();
        btn.targetGraphic = img;
        btn.transition = Selectable.Transition.ColorTint;
        var 颜色 = btn.colors;
        颜色.normalColor = Color.white;
        颜色.highlightedColor = new Color(0.80f, 0.85f, 1.0f);
        颜色.pressedColor = new Color(0.65f, 0.72f, 0.95f);
        颜色.selectedColor = Color.white;
        颜色.fadeDuration = 0.06f;
        btn.colors = 颜色;
        btn.onClick.AddListener(() => 点击());
    }

    static void 拉满(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    static void 拉满(Image img) { 拉满(img.rectTransform); }

    Font 取字体()
    {
        // 没接字体就直接返回 null —— uGUI 会退回内置字体，英文能显示、中文是方块。
        // 所以安装脚本会把 Assets/Fonts/SimHei.ttf assign 到这个字段上。
        return 字体 != null ? 字体 : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    }
}
