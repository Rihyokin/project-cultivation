using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// **修炼小屋界面**（三个页签：闭关修炼 / 境界突破 / 转修功法）。
///
/// 布局照策划的三张稿：
///
/// ```
/// ┌──────────┬────────────────────────────────────┐
/// │ 闭关修炼 │   [功法信息]        当前境界         │
/// │ 境界突破 │                    ▓▓▓▓▓▓░░░░ 进度条 │
/// │ 转修功法 │   剩余修炼次数      [修炼一次][闭关] │
/// └──────────┴────────────────────────────────────┘
/// ```
///
/// · **闭关修炼**：显示当前功法 / 当前境界 + 进度条 / 剩余修炼次数，两个按钮「修炼一次」「闭关」
/// · **境界突破**：突破所需物品信息，两个按钮「提交物品」「确认突破」
/// · **转修功法**：已学功法列表（点选），「转修后境界预估」，按钮「确认转修」
///
/// 数据全部来自 <see cref="PlayerCultivation"/>，本组件只负责显示和按钮。
///
/// 【装配】挂在一个 prefab 上，把这个 prefab 塞进修炼小屋的
/// `StationInteractable.界面预制体` —— `StationInteractor` 右键交互时会自动实例化它。
/// 也支持直接放在场景里（那时靠 `显示()/隐藏()` 手动控制）。
/// </summary>
public class CultivationUI : MonoBehaviour
{
    [Header("字体（留空自动找 SimHei）")]
    public Font 字体;

    [Header("数据")]
    [Tooltip("玩家修为。留空则运行时自动找")]
    public PlayerCultivation 修为;

    [Tooltip("**兜底**用的功法列表。正常从 UIPanelData.已学功法 读（那才是玩法上的真来源），这里只在没有 UIPanelData 时用")]
    public List<GongFaDefinition> 全部功法 = new List<GongFaDefinition>();

    [Header("配色（照概念图：灰底 + 深灰面板）")]
    public Color 幕布色 = new Color(0f, 0f, 0f, 0.55f);
    public Color 主面板色 = new Color(0.72f, 0.72f, 0.72f, 1f);
    public Color 侧栏色 = new Color(0.82f, 0.82f, 0.82f, 1f);
    public Color 标签色 = new Color(0.55f, 0.55f, 0.55f, 1f);
    public Color 标签选中色 = new Color(0.34f, 0.34f, 0.34f, 1f);
    public Color 面板色 = new Color(0.58f, 0.58f, 0.58f, 1f);
    public Color 字色 = new Color(0.12f, 0.12f, 0.12f, 1f);
    public Color 进度底色 = new Color(0.45f, 0.45f, 0.45f, 1f);
    public Color 进度填充色 = new Color(0.32f, 0.62f, 0.36f, 1f);

    [Header("尺寸")]
    public Vector2 主面板尺寸 = new Vector2(1120f, 660f);
    public float 侧栏宽 = 200f;

    /// <summary>界面是否开着</summary>
    public bool 已显示 => 画布 != null && 画布.gameObject.activeSelf;

    Canvas 画布;
    int 当前页 = 0;

    // 侧栏
    readonly Image[] 页签图 = new Image[3];
    readonly Text[] 页签字 = new Text[3];
    readonly GameObject[] 页 = new GameObject[3];

    // 闭关修炼页
    Text 功法信息;
    Text 境界名;
    Text 境界等级;
    Image 进度填充;
    Text 进度文字;
    Text 剩余次数;

    // 境界突破页
    Text 突破物品信息;
    Text 突破提示;
    readonly List<string> 已提交 = new List<string>();

    // 转修功法页
    readonly List<Image> 功法行图 = new List<Image>();
    readonly List<Text> 功法行字 = new List<Text>();
    readonly List<GongFaDefinition> 功法行 = new List<GongFaDefinition>();
    Text 转修预估;
    Image 选中行图;
    int 选中功法 = -1;

    static Sprite 方图缓存;

    /// <summary>1×1 白图。**进度条必须给 sprite** —— Image 没 sprite 时 fillAmount 是无效的</summary>
    static Sprite 取方图()
    {
        if (方图缓存 != null) return 方图缓存;
        var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
        var px = new Color32[16];
        for (int i = 0; i < px.Length; i++) px[i] = new Color32(255, 255, 255, 255);
        tex.SetPixels32(px);
        tex.Apply();
        tex.hideFlags = HideFlags.HideAndDontSave;
        方图缓存 = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 100f);
        方图缓存.hideFlags = HideFlags.HideAndDontSave;
        return 方图缓存;
    }

    void Awake()
    {
        if (字体 == null) 找字体();
        搭界面();

        // 【不要在这里 隐藏()】StationInteractor.打开界面 是把本预制体 Instantiate 出来用的，
        // 关界面时直接 Destroy —— 也就是说"被实例化出来 = 就是要显示"。
        // 在 Awake 里隐藏会导致右键交互后一片空白 ✗
        显示();
    }

    void 找字体()
    {
#if UNITY_EDITOR
        字体 = UnityEditor.AssetDatabase.LoadAssetAtPath<Font>("Assets/Fonts/SimHei.ttf");
#endif
        if (字体 == null)
            foreach (var f in Resources.FindObjectsOfTypeAll<Font>())
                if (f != null && f.name.ToLowerInvariant().Contains("simhei")) { 字体 = f; return; }
    }

    void 取数据()
    {
        if (修为 == null) 修为 = FindObjectOfType<PlayerCultivation>();
    }

    public void 显示()
    {
        取数据();
        刷新();
        if (画布 != null) 画布.gameObject.SetActive(true);
    }

    public void 隐藏()
    {
        if (画布 != null) 画布.gameObject.SetActive(false);
    }

    public void 切换页(int 页号)
    {
        当前页 = Mathf.Clamp(页号, 0, 页.Length - 1);
        for (int i = 0; i < 页.Length; i++)
        {
            if (页[i] != null) 页[i].SetActive(i == 当前页);
            if (页签图[i] != null) 页签图[i].color = (i == 当前页) ? 标签选中色 : 标签色;
            if (页签字[i] != null) 页签字[i].color = (i == 当前页) ? new Color(0.95f, 0.95f, 0.95f) : 字色;
        }
        刷新();
    }

    // ============================================================ 刷新

    void 刷新()
    {
        取数据();
        if (修为 == null)
        {
            if (功法信息 != null) 功法信息.text = "功法信息\n（找不到 PlayerCultivation）";
            return;
        }

        // ---- 闭关修炼 ----
        var g = 修为.当前功法;
        if (功法信息 != null)
            功法信息.text = "功法信息\n\n" + (g != null ? g.功法名称 : "（未修炼功法）")
                + "\n品阶：" + (g != null ? g.功法品阶.ToString() : "—")
                + "\n难度：" + (g != null ? g.难度等级.ToString() : "—")
                + "（K=" + 修为.难度系数.ToString("0.0") + "）";

        if (境界名 != null) 境界名.text = 修为.境界名;
        if (境界等级 != null) 境界等级.text = "第 " + 修为.等级 + " 级 / 共 90 级";

        if (进度填充 != null) 进度填充.fillAmount = 修为.进度;
        if (进度文字 != null)
            进度文字.text = 修为.已满级
                ? "已至巅峰"
                : "进度：" + 修为.本级已积累 + " / " + 修为.升级所需灵气
                  + "（" + (修为.进度 * 100f).ToString("0.#") + "%）"
                  + "　总灵气 " + 修为.总灵气;

        if (剩余次数 != null)
            剩余次数.text = "剩余修炼次数：" + 修为.修炼次数
                + "　（单次 +" + 修为.单次修炼灵气.ToString("0.#") + " 灵气）";

        // ---- 境界突破 ----
        if (突破物品信息 != null)
        {
            string 材 = 修为.突破材料;
            if (string.IsNullOrEmpty(材))
                材 = 修为.突破类型 == BreakthroughKind.大境界突破 ? "大境界突破材料（**策划还没定名**）"
                    : "破境丹（**策划还没定名**）";
            突破物品信息.text = "突破所需物品\n\n" + 材 + " × " + 修为.材料数量
                + "\n\n当前：" + 修为.境界名 + "（第 " + 修为.等级 + " 级）"
                + "\n类型：" + 修为.突破类型
                + "\n\n所需灵气：" + 修为.升级所需灵气 + " 基准"
                + "\n已积累：" + 修为.本级已积累 + "（" + (修为.进度 * 100f).ToString("0.#") + "%）";
        }
        if (突破提示 != null) 突破提示.text = 已提交.Count > 0 ? "已提交 " + 已提交.Count + " 件" : "";

        // ---- 转修功法 ----
        刷新功法列表();
        if (转修预估 != null)
        {
            var 目标 = 选中功法 >= 0 && 选中功法 < 功法行.Count ? 功法行[选中功法] : null;
            转修预估.text = 目标 != null
                ? "转修后境界预估：" + 修为.预估转修后境界(目标)
                  + "\n（" + 修为.当前功法.功法名称 + " K=" + 修为.难度系数.ToString("0.0")
                  + " → " + 目标.功法名称 + " K=" + (目标.难度等级 / 100f).ToString("0.0") + "）"
                : "转修后境界预估：—\n（先在右边选一门功法）";
        }
    }

    /// <summary>
    /// **能转修的功法 = 已学会的**。以 UIPanelData.已学功法 为准；
    /// 那里空的话退回本组件上的兜底列表（免得界面一片空）。
    /// </summary>
    List<GongFaDefinition> 取可转修功法()
    {
        取数据();
        var 面板 = 修为 != null ? 修为.面板数据 : null;
        if (面板 != null && 面板.已学功法 != null && 面板.已学功法.Count > 0)
        {
            var 出 = new List<GongFaDefinition>();
            foreach (var g in 面板.已学功法) if (g != null) 出.Add(g);
            if (出.Count > 0) return 出;
        }
        return 全部功法 != null ? 全部功法 : new List<GongFaDefinition>();
    }

    void 刷新功法列表()
    {
        // **「已学会的功法」以 UIPanelData 为准**（那才是玩法上的真来源）；没有就退回兜底列表
        var 可转修 = 取可转修功法();

        for (int i = 0; i < 功法行字.Count && i < 可转修.Count; i++)
        {
            功法行[i] = 可转修[i];
            bool 是当前 = 修为 != null && 修为.当前功法 == 可转修[i];
            var d = 可转修[i];
            功法行字[i].text = (是当前 ? "▶ " : "   ") + (d != null ? d.功法名称 : "?")
                + "　" + (d != null ? d.功法品阶.ToString() : "")
                + "　K=" + (d != null ? (d.难度等级 / 100f).ToString("0.0") : "?");
            功法行字[i].color = 是当前 ? new Color(0.15f, 0.35f, 0.15f) : 字色;
            功法行图[i].color = (i == 选中功法) ? new Color(0.80f, 0.80f, 0.55f) : 面板色;
        }
    }

    // ============================================================ 按钮动作

    void 点修炼一次()
    {
        if (修为 == null) return;
        float 得 = 修为.修炼一次();
        if (得 <= 0f && 突破提示 != null) 突破提示.text = "没有修炼次数了（去打怪）";
        刷新();
    }

    void 点闭关()
    {
        if (修为 == null) return;
        long 共 = 修为.闭关全部修炼();
        if (共 <= 0 && 突破提示 != null) 突破提示.text = "没有修炼次数了（去打怪）";
        刷新();
    }

    void 点提交物品()
    {
        // 物品系统还没有突破材料的定义，这里先做「记录提交」的壳
        已提交.Add(修为 != null ? 修为.突破材料 : "");
        刷新();
    }

    void 点确认突破()
    {
        if (修为 == null) return;
        if (修为.已满级) { if (突破提示 != null) 突破提示.text = "已经满级了"; return; }
        if (修为.进度 < 1f)
        {
            if (突破提示 != null) 突破提示.text = "灵气还不够（" + (修为.进度 * 100f).ToString("0.#") + "%），先去闭关修炼";
            return;
        }
        if (已提交.Count <= 0)
        {
            if (突破提示 != null) 突破提示.text = "还没提交突破物品";
            return;
        }
        // 灵气够了 + 交过物品 → 放行一级（真正的消耗等物品表定了再接）
        修为.设置总灵气((long)((修为.取累计(修为.等级) + 1) * 修为.难度系数));
        已提交.Clear();
        if (突破提示 != null) 突破提示.text = "突破成功！";
        刷新();
    }

    /// <summary>
    /// 转修页的反馈。转修页没有独立的提示条（突破页有 <c>突破提示</c>），
    /// 所以消息就写在「转修后境界预估」那一行上。
    ///
    /// 【为什么要加】以前这里**什么都不写**：
    ///   · 没先点功法行 → 只把小字改成"先选一门功法"，很容易被当成"点了没反应"
    ///   · 转修成功   → 界面上一点提示都没有，用户会以为没生效
    /// 现在明确写出结果。
    /// </summary>
    void 转修反馈(string 消息)
    {
        if (转修预估 != null) 转修预估.text = 消息;
    }

    void 点确认转修()
    {
        if (修为 == null) return;
        var 目标 = 选中功法 >= 0 && 选中功法 < 功法行.Count ? 功法行[选中功法] : null;
        if (目标 == null) { 转修反馈("请先在上面点一门功法"); return; }
        if (目标 == 修为.当前功法) { 转修反馈("已经在修「" + (目标 != null ? 目标.功法名称 : "?") + "」了"); return; }

        bool 成功 = 修为.转修功法(目标);
        刷新();                                   // 刷新会把预估重算一遍
        转修反馈(成功
            ? "已转修到「" + 目标.功法名称 + "」　当前境界：" + 修为.境界名
            : "转修失败（没找到功法定义？）");       // ★ 放在刷新之后，否则会被盖掉
    }

    // ============================================================ 搭界面

    void 搭界面()
    {
        var 根 = new GameObject("CultivationCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        根.transform.SetParent(transform, false);
        画布 = 根.GetComponent<Canvas>();
        画布.renderMode = RenderMode.ScreenSpaceOverlay;
        画布.sortingOrder = 2500;                      // 低于暂停菜单(3000)、高于 HUD
        var 缩放 = 根.GetComponent<CanvasScaler>();
        缩放.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        缩放.referenceResolution = new Vector2(1920f, 1080f);
        缩放.matchWidthOrHeight = 0.5f;

        var 幕布 = UIBuildUtils.CreateImage("幕布", 根.transform, 幕布色);
        幕布.raycastTarget = true;                     // 挡射线
        UIBuildUtils.Stretch(幕布.rectTransform);

        var 主 = UIBuildUtils.CreateImage("主面板", 根.transform, 主面板色);
        主.raycastTarget = true;
        居中(主.rectTransform, 主面板尺寸);

        // ---- 左侧栏：三个页签 ----
        string[] 页名 = { "闭关修炼", "境界突破", "转修功法" };
        for (int i = 0; i < 3; i++)
        {
            int 号 = i;
            var 块 = UIBuildUtils.CreateImage("页签_" + 页名[i], 主.transform, 标签色);
            块.raycastTarget = true;
            // 【坑】这里原来的 y = 12 + i*76 是"从底往上排"，结果三个页签跑到了左下角、
            // 而且顺序是反的。改成**从顶部往下排**：i=0（闭关修炼）在最上面 ✓
            靠左(块.rectTransform, 主面板尺寸, 12f,
                 主面板尺寸.y - 12f - 64f - i * 76f, 侧栏宽 - 24f, 64f);
            var 按钮 = 块.gameObject.AddComponent<Button>();
            按钮.targetGraphic = 块;
            按钮.onClick.AddListener(() => 切换页(号));
            页签图[i] = 块;
            var t = UIBuildUtils.CreateText("文字", 块.transform, 字体, 页名[i], 24, TextAnchor.MiddleCenter, 字色);
            UIBuildUtils.Stretch(t.rectTransform);
            页签字[i] = t;
        }

        // ---- 内容区 ----
        var 内容 = UIBuildUtils.CreateRect("内容区", 主.transform);
        内容.anchorMin = new Vector2(0f, 0f);
        内容.anchorMax = new Vector2(1f, 1f);
        内容.offsetMin = new Vector2(侧栏宽, 12f);
        内容.offsetMax = new Vector2(-12f, -12f);

        页[0] = 搭闭关页(内容);
        页[1] = 搭突破页(内容);
        页[2] = 搭转修页(内容);

        切换页(0);
    }

    // ---- 页 1：闭关修炼 ----
    GameObject 搭闭关页(RectTransform 父)
    {
        var 页根 = UIBuildUtils.CreateRect("页_闭关修炼", 父);
        UIBuildUtils.Stretch(页根);
        var w = 主面板尺寸.x - 侧栏宽 - 24f;
        var h = 主面板尺寸.y - 24f;

        // 功法信息（左上）
        var 信息块 = UIBuildUtils.CreateImage("功法信息", 页根, 面板色);
        靠左(信息块.rectTransform, new Vector2(w, h), 0f, h - 200f, 260f, 180f);
        功法信息 = UIBuildUtils.CreateText("文字", 信息块.transform, 字体, "功法信息", 17,
            TextAnchor.UpperLeft, 字色);
        内边距(功法信息.rectTransform, 14f);

        // 当前境界（中上）
        境界名 = UIBuildUtils.CreateText("当前境界", 页根, 字体, "炼气第1层", 52,
            TextAnchor.MiddleCenter, 字色);
        靠左(境界名.rectTransform, new Vector2(w, h), 260f, h - 240f, w - 260f, 90f);
        境界等级 = UIBuildUtils.CreateText("等级", 页根, 字体, "", 20, TextAnchor.MiddleCenter,
            new Color(0.25f, 0.25f, 0.25f));
        靠左(境界等级.rectTransform, new Vector2(w, h), 260f, h - 300f, w - 260f, 30f);

        // 进度条
        var 条底 = UIBuildUtils.CreateImage("进度底", 页根, 进度底色);
        条底.sprite = 取方图();
        靠左(条底.rectTransform, new Vector2(w, h), 0f, 150f, w, 34f);
        var 填充 = UIBuildUtils.CreateImage("进度填充", 条底.transform, 进度填充色);
        填充.sprite = 取方图();
        填充.type = Image.Type.Filled;                 // ★ 必须配 sprite，否则 fillAmount 无效
        填充.fillMethod = Image.FillMethod.Horizontal;
        填充.fillOrigin = (int)Image.OriginHorizontal.Left;
        填充.fillAmount = 0f;
        UIBuildUtils.Stretch(填充.rectTransform);
        进度填充 = 填充;
        进度文字 = UIBuildUtils.CreateText("进度文字", 页根, 字体, "进度：0 / 100", 18,
            TextAnchor.MiddleCenter, 字色);
        靠左(进度文字.rectTransform, new Vector2(w, h), 0f, 118f, w, 28f);

        // 剩余修炼次数（左下）
        剩余次数 = UIBuildUtils.CreateText("剩余次数", 页根, 字体, "剩余修炼次数：0", 20,
            TextAnchor.UpperLeft, 字色);
        靠左(剩余次数.rectTransform, new Vector2(w, h), 0f, 40f, w * 0.5f, 60f);

        // 两个按钮（右下）
        建按钮(页根, "修炼一次", new Vector2(w * 0.50f, 20f), new Vector2(190f, 60f), 点修炼一次);
        建按钮(页根, "闭关", new Vector2(w * 0.50f + 210f, 20f), new Vector2(190f, 60f), 点闭关);

        return 页根.gameObject;
    }

    // ---- 页 2：境界突破 ----
    GameObject 搭突破页(RectTransform 父)
    {
        var 页根 = UIBuildUtils.CreateRect("页_境界突破", 父);
        UIBuildUtils.Stretch(页根);
        var w = 主面板尺寸.x - 侧栏宽 - 24f;
        var h = 主面板尺寸.y - 24f;

        var 物品块 = UIBuildUtils.CreateImage("突破所需物品信息", 页根, 面板色);
        靠左(物品块.rectTransform, new Vector2(w, h), 0f, 0f, w * 0.52f, h);
        突破物品信息 = UIBuildUtils.CreateText("文字", 物品块.transform, 字体, "突破所需物品", 19,
            TextAnchor.UpperLeft, 字色);
        内边距(突破物品信息.rectTransform, 20f);

        建按钮(页根, "提交物品", new Vector2(w * 0.62f, 100f), new Vector2(300f, 62f), 点提交物品);
        建按钮(页根, "确认突破", new Vector2(w * 0.62f, 24f), new Vector2(300f, 62f), 点确认突破);

        突破提示 = UIBuildUtils.CreateText("提示", 页根, 字体, "", 18, TextAnchor.UpperLeft,
            new Color(0.55f, 0.15f, 0.15f));
        靠左(突破提示.rectTransform, new Vector2(w, h), w * 0.56f, 180f, w * 0.44f, 60f);

        return 页根.gameObject;
    }

    // ---- 页 3：转修功法 ----
    GameObject 搭转修页(RectTransform 父)
    {
        var 页根 = UIBuildUtils.CreateRect("页_转修功法", 父);
        UIBuildUtils.Stretch(页根);
        var w = 主面板尺寸.x - 侧栏宽 - 24f;
        var h = 主面板尺寸.y - 24f;

        var 列表块 = UIBuildUtils.CreateImage("当前学会的功法", 页根, 面板色);
        靠左(列表块.rectTransform, new Vector2(w, h), 0f, 86f, w, h - 86f);

        var 标题 = UIBuildUtils.CreateText("标题", 列表块.transform, 字体, "当前学会的功法", 22,
            TextAnchor.UpperLeft, 字色);
        // 【坑·已修】标题和功法行都是**列表块的子物体**，锚的是列表块左下角，
        // 不是页根 —— 列表块高 = h−86 = 550。
        //   标题原来在 h−86−46 = 504（框顶 550 往下 46，对的）
        //   但第一行原来在 h−86−56 = 494，和标题的 504~536 **重叠** → 标题被盖住
        // 现在：标题仍在 504，行从 450 起（标题底 504 − 缝 8 − 行高 46），不再重叠。
        靠左(标题.rectTransform, new Vector2(w, h - 86f), 18f, h - 132f, w - 36f, 32f);

        // 功法行（先按 全部功法 建好；运行时刷新文字与选中态）
        int 行数 = Mathf.Max(0, 取可转修功法().Count);
        for (int i = 0; i < 行数; i++)
        {
            int 号 = i;
            float y = h - 186f - i * 54f;
            if (y < 10f) break;
            var 行图 = UIBuildUtils.CreateImage("功法行" + i, 列表块.transform, 面板色);
            行图.raycastTarget = true;
            靠左(行图.rectTransform, new Vector2(w, h - 86f), 14f, y, w - 28f, 46f);
            var 按钮 = 行图.gameObject.AddComponent<Button>();
            按钮.targetGraphic = 行图;
            按钮.onClick.AddListener(() => { 选中功法 = 号; 刷新(); });
            var t = UIBuildUtils.CreateText("文字", 行图.transform, 字体, "", 19,
                TextAnchor.MiddleLeft, 字色);
            内边距(t.rectTransform, 16f);
            功法行图.Add(行图);
            功法行字.Add(t);
            功法行.Add(null);
        }

        var 预估块 = UIBuildUtils.CreateImage("转修后境界预估", 页根, 面板色);
        靠左(预估块.rectTransform, new Vector2(w, h), 0f, 12f, w * 0.66f, 62f);
        转修预估 = UIBuildUtils.CreateText("文字", 预估块.transform, 字体, "转修后境界预估：—", 19,
            TextAnchor.MiddleLeft, 字色);
        内边距(转修预估.rectTransform, 18f);

        建按钮(页根, "确认转修", new Vector2(w * 0.70f, 12f), new Vector2(240f, 62f), 点确认转修);

        return 页根.gameObject;
    }

    // ============================================================ 小工具

    void 建按钮(RectTransform 父, string 名, Vector2 位置, Vector2 尺寸, UnityEngine.Events.UnityAction 动作)
    {
        var 图 = UIBuildUtils.CreateImage(名, 父, new Color(0.66f, 0.66f, 0.66f, 1f));
        // ★ UIBuildUtils.CreateImage 默认 raycastTarget=false（HUD 的省性能约定），
        //   按钮不开这个就点不动，而且 Console 一点报错都没有。
        图.raycastTarget = true;
        图.rectTransform.anchorMin = new Vector2(0f, 0f);
        图.rectTransform.anchorMax = new Vector2(0f, 0f);
        图.rectTransform.pivot = new Vector2(0f, 0f);
        图.rectTransform.sizeDelta = 尺寸;
        图.rectTransform.anchoredPosition = 位置;
        var 按钮 = 图.gameObject.AddComponent<Button>();
        按钮.targetGraphic = 图;
        var 配色 = 按钮.colors;
        配色.normalColor = Color.white;
        配色.highlightedColor = new Color(0.92f, 0.92f, 0.92f, 1f);
        配色.pressedColor = new Color(0.78f, 0.78f, 0.78f, 1f);
        按钮.colors = 配色;
        按钮.onClick.AddListener(动作);
        var t = UIBuildUtils.CreateText("文字", 图.transform, 字体, 名, 24, TextAnchor.MiddleCenter, 字色);
        UIBuildUtils.Stretch(t.rectTransform);
    }

    void 居中(RectTransform rt, Vector2 尺寸)
    {
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = 尺寸;
        rt.anchoredPosition = Vector2.zero;
    }

    /// <summary>在「以左下角为基准、尺寸 = 参考尺寸」的坐标系里摆一个元素</summary>
    static void 靠左(RectTransform rt, Vector2 参考尺寸, float x, float y, float w, float h)
    {
        rt.anchorMin = new Vector2(0f, 0f);
        rt.anchorMax = new Vector2(0f, 0f);
        rt.pivot = new Vector2(0f, 0f);
        rt.sizeDelta = new Vector2(w, h);
        rt.anchoredPosition = new Vector2(x, y);
    }

    static void 内边距(RectTransform rt, float 边)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(边, 边);
        rt.offsetMax = new Vector2(-边, -边);
    }

    // ---- ASCII 别名 ----
    public void Show() => 显示();
    public void Hide() => 隐藏();
    public void SwitchTab(int index) => 切换页(index);
    public bool IsShown => 已显示;
}
