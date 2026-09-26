using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// <summary>
/// 按 lore/UI Concept Sketch 生成角色面板 UI。
/// 可重复执行：会先删掉旧的 CharacterUI 再重建，不会重复堆积节点。
/// 菜单：修仙 / 构建角色面板 UI
/// </summary>
public static class CharacterPanelBuilder
{
    const string FontPath = "Assets/Fonts/SimHei.ttf";
    const string RootName = "CharacterUI";

    // 参考分辨率
    static readonly Vector2 RefResolution = new Vector2(1920f, 1080f);

    // 中文菜单给人在编辑器里点；ASCII 菜单给自动化调用（exec 脚本传中文会被编码破坏）
    [MenuItem("修仙/构建角色面板 UI")]
    [MenuItem("Cultivation/Build Character Panel UI")]
    public static void BuildFromMenu()
    {
        Build(true);
    }

    public static CharacterPanelUI Build(bool overwrite)
    {
        var font = AssetDatabase.LoadAssetAtPath<Font>(FontPath);
        if (font == null)
        {
            Debug.LogError("[CharacterPanelBuilder] 找不到中文字体：" + FontPath);
            return null;
        }

        // ---- 清理旧节点 ----
        var old = GameObject.Find(RootName);
        if (old != null)
        {
            if (!overwrite) return old.GetComponent<CharacterPanelUI>();
            Object.DestroyImmediate(old);
        }

        EnsureEventSystem();

        // ---- Canvas ----
        var canvasGo = new GameObject(RootName, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = RefResolution;
        scaler.matchWidthOrHeight = 0.5f;

        var panelUi = canvasGo.AddComponent<CharacterPanelUI>();
        var data = canvasGo.AddComponent<UIPanelData>();

        // ★ 编辑器里默认不显示这个画布（摆场景时角色面板铺在视图上很挡），运行时自动打开
        canvasGo.AddComponent<UICanvasBoot>();

        // PanelRoot：随开关显隐
        var panelRoot = UIBuildUtils.CreateRect("PanelRoot", canvasGo.transform);
        UIBuildUtils.Stretch(panelRoot);
        panelUi.panelRoot = panelRoot.gameObject;

        // ---- 暗底：铺满屏幕。点空白处收起面板 ----
        var dim = UIBuildUtils.CreateImage("Dim", panelRoot, new Color(0f, 0f, 0f, 0.62f));
        UIBuildUtils.Stretch(dim.rectTransform);
        dim.raycastTarget = true;                       // 必须接射线，否则点不到
        var dimBtn = dim.gameObject.AddComponent<Button>();
        dimBtn.transition = Selectable.Transition.None;
        dimBtn.onClick.AddListener(() => panelUi.SetOpen(false));

        // ---- 窗口：只占屏幕中间，不再铺满 ----
        var window = UIBuildUtils.CreateImage("Window", panelRoot, new Color(0.13f, 0.13f, 0.13f, 0.98f));
        window.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
        window.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        window.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        window.rectTransform.anchoredPosition = Vector2.zero;
        window.rectTransform.sizeDelta = new Vector2(1440f, 860f);
        UIBuildUtils.AddOutline(window.rectTransform, new Color(0.45f, 0.45f, 0.45f, 1f));

        // ================= 提示条 =================
        // 装备栏满 / 等待选槽位 之类的反馈
        var hintBg = UIBuildUtils.CreateImage("HintBar", window.rectTransform, new Color(0.10f, 0.10f, 0.10f, 0.92f));
        hintBg.rectTransform.anchorMin = new Vector2(0f, 0f);
        hintBg.rectTransform.anchorMax = new Vector2(1f, 0f);
        hintBg.rectTransform.pivot = new Vector2(0.5f, 0f);
        hintBg.rectTransform.offsetMin = new Vector2(202f, 18f);
        hintBg.rectTransform.offsetMax = new Vector2(-16f, 18f + 36f);

        var hintText = UIBuildUtils.CreateText("Text", hintBg.rectTransform, font, "", 18,
                                               TextAnchor.MiddleCenter, new Color(0.98f, 0.92f, 0.70f));
        UIBuildUtils.Stretch(hintText.rectTransform, 6f);

        var panelHint = canvasGo.AddComponent<UIPanelHint>();
        panelHint.data = data;
        panelHint.label = hintText;
        panelHint.background = hintBg;

        // ================= 侧边栏 =================
        var sidebar = UIBuildUtils.CreateImage("Sidebar", window.rectTransform, UIBuildUtils.ColorSidebar);
        sidebar.rectTransform.anchorMin = new Vector2(0f, 0f);
        sidebar.rectTransform.anchorMax = new Vector2(0f, 1f);
        sidebar.rectTransform.pivot = new Vector2(0f, 0.5f);
        sidebar.rectTransform.offsetMin = new Vector2(16f, 62f);
        sidebar.rectTransform.offsetMax = new Vector2(186f, -16f);

        string[] tabNames = { "背包", "境界", "神通", "法宝", "灵阵", "战阵", "坐骑" };
        var tabButtons = new List<Button>();
        var tabBgs = new List<Image>();
        for (int i = 0; i < tabNames.Length; i++)
        {
            var btn = UIBuildUtils.CreateButton("Tab_" + tabNames[i], sidebar.rectTransform, font, tabNames[i], 24);
            var rt = btn.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.offsetMin = new Vector2(14f, 0f);
            rt.offsetMax = new Vector2(-14f, 0f);
            rt.anchoredPosition = new Vector2(0f, -16f - i * 66f);
            rt.sizeDelta = new Vector2(rt.sizeDelta.x, 54f);
            tabButtons.Add(btn);
            tabBgs.Add(btn.GetComponent<Image>());
        }

        // ================= 内容区 =================
        var content = UIBuildUtils.CreateRect("Content", window.rectTransform);
        content.anchorMin = new Vector2(0f, 0f);
        content.anchorMax = new Vector2(1f, 1f);
        content.offsetMin = new Vector2(202f, 62f);      // 底部给提示条让位
        content.offsetMax = new Vector2(-16f, -16f);

        var pages = new List<GameObject>();
        // 顺序必须和上面的 tabNames 一一对应
        pages.Add(BuildInventoryPage(content, font, data, out var invList, out var invInfo));
        pages.Add(BuildRealmPage(content, font, data, out var attrList, out var realmBar));
        pages.Add(BuildAbilityPage(content, font, data, "神通", out var abilActive, out var abilList, out var abilInfo));
        pages.Add(BuildTreasurePage(content, font, data, out var treActive, out var treList, out var treInfo));
        pages.Add(BuildArrayPage(content, font, data, out var arrActive, out var arrList, out var arrInfo));
        pages.Add(BuildSpiritFormationPage(content, font, data, out var spiritBar, out var spiritList, out var spiritInfo));
        pages.Add(BuildMountPage(content, font, data, out var mountPage, out var mountList, out var mountInfo));

        // ================= 接线 =================
        panelUi.tabs.Clear();
        for (int i = 0; i < tabButtons.Count; i++)
        {
            panelUi.tabs.Add(new TabBinding
            {
                button = tabButtons[i],
                page = pages[i],
                background = tabBgs[i],
            });
        }
        panelUi.defaultTab = CharacterTab.境界;

        attrList.source = data.玩家属性;
        realmBar.data = data;

        abilList.SetEntries(data.GetAbilities());
        treList.SetEntries(data.GetTreasures());
        arrList.SetEntries(data.GetSpiritArrays());
        invList.SetEntries(data.GetItems());
        spiritList.SetEntries(data.GetSpirits());
        mountList.SetEntries(data.GetMounts());

        // 数据驱动：列表跟随 UIPanelData.Changed 自动刷新（装备变更 / 被动启停）
        abilList.data = data; abilList.source = ListSource.神通;
        treList.data = data;  treList.source = ListSource.法宝;
        arrList.data = data;  arrList.source = ListSource.灵阵;
        invList.data = data;  invList.source = ListSource.物品;
        spiritList.data = data; spiritList.source = ListSource.战阵成员;
        mountList.data = data;  mountList.source = ListSource.坐骑;

        // 信息栏要能读写被动神通状态
        abilInfo.data = data;
        treInfo.data = data;
        arrInfo.data = data;
        invInfo.data = data;
        spiritInfo.data = data;
        mountInfo.data = data;

        // 神通/法宝/灵阵 三页共用同一套主动技能逻辑，各自跑一份
        abilActive.data = data; abilActive.infoTarget = abilInfo;
        treActive.data = data; treActive.infoTarget = treInfo;
        arrActive.data = data; arrActive.infoTarget = arrInfo;

        // 战阵页：九宫格 + 成员列表
        spiritBar.data = data; spiritBar.infoTarget = spiritInfo;
        spiritList.infoTarget = spiritInfo;
        spiritBar.Refresh();

        // 坐骑页：列表选中 → 信息栏 + 上方 3D 预览
        mountList.infoTarget = mountInfo;
        mountPage.data = data;
        mountList.Select(mountList.Selected);

        // 立刻切到默认页。Start() 在编辑态不会执行，不在这里切的话
        // 5 个页面会全部处于激活状态、在编辑器里叠成一团。
        panelUi.ShowTab(panelUi.defaultTab);

        EditorUtility.SetDirty(canvasGo);
        Debug.Log("[CharacterPanelBuilder] 角色面板已生成。");
        return panelUi;
    }

    static void EnsureEventSystem()
    {
        if (Object.FindObjectOfType<EventSystem>() != null) return;
        new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
    }

    // ---------------------------------------------------------------- 通用块

    /// <summary>一个带标题的灰底板</summary>
    static RectTransform CreatePanel(string name, Transform parent, Font font, string title, Color color)
    {
        var img = UIBuildUtils.CreateImage(name, parent, color);
        if (!string.IsNullOrEmpty(title))
        {
            var t = UIBuildUtils.CreateText("Title", img.rectTransform, font, title, 20,
                                            TextAnchor.UpperCenter, new Color(0.25f, 0.25f, 0.25f));
            t.rectTransform.anchorMin = new Vector2(0f, 1f);
            t.rectTransform.anchorMax = new Vector2(1f, 1f);
            t.rectTransform.pivot = new Vector2(0.5f, 1f);
            t.rectTransform.offsetMin = new Vector2(0f, -32f);
            t.rectTransform.offsetMax = Vector2.zero;
        }
        return img.rectTransform;
    }

    /// <summary>在父节点里放一个面板，用归一化锚点定位</summary>
    static RectTransform PlacePanel(RectTransform panel, Vector2 aMin, Vector2 aMax)
    {
        panel.anchorMin = aMin;
        panel.anchorMax = aMax;
        panel.offsetMin = new Vector2(6f, 6f);
        panel.offsetMax = new Vector2(-6f, -6f);
        return panel;
    }

    /// <summary>可滚动的竖向列表容器，返回内部 Content</summary>
    /// <param name="带滚动条">true = 右侧再配一根竖向滚动条（真灵列表这种几百行的要）</param>
    static RectTransform CreateScrollList(string name, Transform parent, Font font,
                                          Color bg, string title, out ScrollRect scroll,
                                          bool 带滚动条 = false)
    {
        var panel = CreatePanel(name, parent, font, title, bg);
        var viewport = UIBuildUtils.CreateRect("Viewport", panel);
        UIBuildUtils.Stretch(viewport);
        viewport.offsetMax = new Vector2(0f, -34f);      // 让出标题
        viewport.gameObject.AddComponent<RectMask2D>();

        var content = UIBuildUtils.CreateRect("Content", viewport);
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.offsetMin = new Vector2(0f, 0f);
        content.offsetMax = new Vector2(0f, 0f);

        UIBuildUtils.AddVerticalLayout(content, 4f, new RectOffset(6, 6, 6, 6));
        var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scroll = panel.gameObject.AddComponent<ScrollRect>();
        scroll.content = content;
        scroll.viewport = viewport;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 24f;

        // 滚动条要放在视口**之后**建，才能在射线检测里压住视口（不然点到的是列表行）
        if (带滚动条) UIBuildUtils.AddVerticalScrollbar(scroll);

        return content;
    }

    /// <summary>六边形排布的 6 格主动技能装备位</summary>
    static UIActiveSkillBar CreateActiveSkillBar(RectTransform parent, Font font)
    {
        var root = UIBuildUtils.CreateRect("ActiveSkillBar", parent);
        var bar = root.gameObject.AddComponent<UIActiveSkillBar>();

        var hint = UIBuildUtils.CreateText("Hint", root, font, "主动技能装备（神通 / 法宝 / 灵阵 共用）", 18,
                                           TextAnchor.LowerCenter, UIBuildUtils.ColorTextOnDark);
        hint.rectTransform.anchorMin = new Vector2(0f, 0f);
        hint.rectTransform.anchorMax = new Vector2(1f, 0f);
        hint.rectTransform.pivot = new Vector2(0.5f, 0f);
        hint.rectTransform.offsetMin = new Vector2(0f, 0f);
        hint.rectTransform.offsetMax = new Vector2(0f, 28f);

        // 3 行 × 2 列，中间一行更靠外，形成六边形轮廓
        float[] xs = { -60f, 60f, -110f, 110f, -60f, 60f };
        float[] ys = { 88f, 88f, 0f, 0f, -88f, -88f };
        const float size = 84f;

        for (int i = 0; i < 6; i++)
        {
            var slotRt = UIBuildUtils.CreateRect("Slot_" + i, root);
            slotRt.anchorMin = new Vector2(0.5f, 0.5f);
            slotRt.anchorMax = new Vector2(0.5f, 0.5f);
            slotRt.pivot = new Vector2(0.5f, 0.5f);
            slotRt.anchoredPosition = new Vector2(xs[i], ys[i]);
            slotRt.sizeDelta = new Vector2(size, size);

            var bg = slotRt.gameObject.AddComponent<Image>();
            bg.color = UIBuildUtils.ColorSlot;
            var btn = slotRt.gameObject.AddComponent<Button>();
            btn.targetGraphic = bg;

            var icon = UIBuildUtils.CreateImage("Icon", slotRt, new Color(0.9f, 0.9f, 0.9f, 1f));
            UIBuildUtils.Stretch(icon.rectTransform, 6f);

            // 名字挂在格子正下方，撑满格子宽度，方便看清装了什么
            var label = UIBuildUtils.CreateText("Label", slotRt, font, "", 15,
                                                TextAnchor.UpperCenter, UIBuildUtils.ColorText);
            label.rectTransform.anchorMin = new Vector2(0f, 0f);
            label.rectTransform.anchorMax = new Vector2(1f, 0f);
            label.rectTransform.pivot = new Vector2(0.5f, 1f);
            label.rectTransform.offsetMin = new Vector2(-8f, -26f);
            label.rectTransform.offsetMax = new Vector2(8f, -2f);
            label.horizontalOverflow = HorizontalWrapMode.Overflow;

            // 右上角小叉：清空该格
            var clearBtn = UIBuildUtils.CreateButton("Clear", slotRt, font, "×", 18);
            var clearRt = clearBtn.GetComponent<RectTransform>();
            clearRt.anchorMin = new Vector2(1f, 1f);
            clearRt.anchorMax = new Vector2(1f, 1f);
            clearRt.pivot = new Vector2(1f, 1f);
            clearRt.anchoredPosition = new Vector2(2f, 2f);
            clearRt.sizeDelta = new Vector2(24f, 24f);

            var slot = slotRt.gameObject.AddComponent<UIActiveSkillSlot>();
            slot.background = bg;
            slot.icon = icon;
            slot.label = label;
            slot.button = btn;
            slot.clearButton = clearBtn;
            slot.index = i;
            bar.slots.Add(slot);
        }

        // 名字标签统一提到 bar 根节点下、并放到最后（最上层渲染）。
        // 留在各自格子里的话，会被相邻格子的底板盖住，只露出后半截。
        for (int i = 0; i < bar.slots.Count; i++)
        {
            var slot = bar.slots[i];
            if (slot == null || slot.label == null) continue;

            var slotRt2 = (RectTransform)slot.transform;
            var lt = slot.label.rectTransform;
            lt.SetParent(root, false);
            lt.anchorMin = new Vector2(0.5f, 0.5f);
            lt.anchorMax = new Vector2(0.5f, 0.5f);
            lt.pivot = new Vector2(0.5f, 1f);
            lt.anchoredPosition = slotRt2.anchoredPosition + new Vector2(0f, -size * 0.5f - 2f);
            lt.sizeDelta = new Vector2(size + 46f, 22f);
            lt.SetAsLastSibling();
        }

        // 清空按钮也要提到最上层：否则会被上面那排格子的名字标签盖住
        for (int i = 0; i < bar.slots.Count; i++)
        {
            var slot = bar.slots[i];
            if (slot == null || slot.clearButton == null) continue;

            var slotRt2 = (RectTransform)slot.transform;
            var ct = (RectTransform)slot.clearButton.transform;
            ct.SetParent(root, false);
            ct.anchorMin = new Vector2(0.5f, 0.5f);
            ct.anchorMax = new Vector2(0.5f, 0.5f);
            ct.pivot = new Vector2(0.5f, 0.5f);
            ct.anchoredPosition = slotRt2.anchoredPosition + new Vector2(size * 0.5f - 4f, size * 0.5f - 4f);
            ct.sizeDelta = new Vector2(24f, 24f);
            ct.SetAsLastSibling();
        }
        return bar;
    }

    /// <summary>一个信息栏（图标 + 名称 + 介绍）</summary>
    static UIEntryInfo CreateEntryInfo(RectTransform parent, Font font, string title,
                                       bool withIcon, out RectTransform root)
    {
        var panel = CreatePanel("Info", parent, font, title, UIBuildUtils.ColorPanelAlt);
        root = panel;
        var info = panel.gameObject.AddComponent<UIEntryInfo>();

        float top = -38f;
        if (withIcon)
        {
            var icon = UIBuildUtils.CreateImage("Icon", panel, new Color(0.62f, 0.62f, 0.62f, 1f));
            icon.rectTransform.anchorMin = new Vector2(0f, 1f);
            icon.rectTransform.anchorMax = new Vector2(0f, 1f);
            icon.rectTransform.pivot = new Vector2(0f, 1f);
            icon.rectTransform.offsetMin = new Vector2(10f, 0f);
            icon.rectTransform.offsetMax = new Vector2(10f + 120f, 0f);
            icon.rectTransform.sizeDelta = new Vector2(120f, 120f);
            icon.rectTransform.anchoredPosition = new Vector2(10f, top - 6f);
            info.iconImage = icon;
            top -= 134f;
        }

        var nameT = UIBuildUtils.CreateText("Name", panel, font, "", 22, TextAnchor.UpperLeft, UIBuildUtils.ColorText);
        nameT.rectTransform.anchorMin = new Vector2(0f, 1f);
        nameT.rectTransform.anchorMax = new Vector2(1f, 1f);
        nameT.rectTransform.pivot = new Vector2(0.5f, 1f);
        nameT.rectTransform.offsetMin = new Vector2(12f, top - 28f);
        nameT.rectTransform.offsetMax = new Vector2(-12f, top);
        info.nameText = nameT;

        var tierT = UIBuildUtils.CreateText("Tier", panel, font, "", 16, TextAnchor.UpperRight, new Color(0.35f, 0.35f, 0.35f));
        tierT.rectTransform.anchorMin = new Vector2(0f, 1f);
        tierT.rectTransform.anchorMax = new Vector2(1f, 1f);
        tierT.rectTransform.pivot = new Vector2(0.5f, 1f);
        tierT.rectTransform.offsetMin = new Vector2(12f, top - 28f);
        tierT.rectTransform.offsetMax = new Vector2(-12f, top);
        info.tierText = tierT;

        // 主动 / 被动 标签
        var kindT = UIBuildUtils.CreateText("Kind", panel, font, "", 16,
                                            TextAnchor.UpperRight, new Color(0.45f, 0.45f, 0.45f));
        kindT.rectTransform.anchorMin = new Vector2(0f, 1f);
        kindT.rectTransform.anchorMax = new Vector2(1f, 1f);
        kindT.rectTransform.pivot = new Vector2(0.5f, 1f);
        kindT.rectTransform.offsetMin = new Vector2(12f, top - 56f);
        kindT.rectTransform.offsetMax = new Vector2(-12f, top - 28f);
        info.kindText = kindT;

        // 启用 / 停用按钮（只对被动神通出现）
        var actBtn = UIBuildUtils.CreateButton("Action", panel, font, "启用", 16);
        var actRt = actBtn.GetComponent<RectTransform>();
        actRt.anchorMin = new Vector2(1f, 1f);
        actRt.anchorMax = new Vector2(1f, 1f);
        actRt.pivot = new Vector2(1f, 1f);
        actRt.anchoredPosition = new Vector2(-12f, top - 60f);
        actRt.sizeDelta = new Vector2(88f, 30f);
        info.actionButton = actBtn;
        info.actionLabel = actBtn.GetComponentInChildren<Text>();
        actBtn.gameObject.SetActive(false);

        var descT = UIBuildUtils.CreateText("Desc", panel, font, "", 17, TextAnchor.UpperLeft, UIBuildUtils.ColorText);
        descT.rectTransform.anchorMin = new Vector2(0f, 0f);
        descT.rectTransform.anchorMax = new Vector2(1f, 1f);
        descT.rectTransform.offsetMin = new Vector2(12f, 12f);
        descT.rectTransform.offsetMax = new Vector2(-12f, top - 34f);
        info.descriptionText = descT;

        return info;
    }

    // ---------------------------------------------------------------- 各页面

    static GameObject BuildRealmPage(RectTransform parent, Font font, UIPanelData data,
                                     out UIAttributeList attrList, out UIRealmBar realmBar)
    {
        var page = UIBuildUtils.CreateRect("Page_境界", parent);
        UIBuildUtils.Stretch(page);

        // 左：角色属性列举
        var attrContent = CreateScrollList("AttrList", page, font, UIBuildUtils.ColorPanel,
                                           "角色属性列举", out _);
        var attrPanel = attrContent.parent.parent as RectTransform;
        PlacePanel(attrPanel, new Vector2(0f, 0f), new Vector2(0.30f, 1f));
        attrList = attrPanel.gameObject.AddComponent<UIAttributeList>();
        attrList.container = attrContent;
        attrList.font = font;
        attrList.source = data.玩家属性;

        // 右：上=功法展示，下=境界展示
        var gongFa = CreatePanel("GongFaShow", page, font, "功法展示", UIBuildUtils.ColorPanel);
        PlacePanel(gongFa, new Vector2(0.32f, 0.42f), new Vector2(1f, 1f));

        var gongFaInfo = gongFa.gameObject.AddComponent<UIEntryInfo>();
        var gfName = UIBuildUtils.CreateText("Name", gongFa, font, "（未设定当前功法）", 26, TextAnchor.UpperLeft, UIBuildUtils.ColorText);
        gfName.rectTransform.anchorMin = new Vector2(0f, 1f);
        gfName.rectTransform.anchorMax = new Vector2(1f, 1f);
        gfName.rectTransform.pivot = new Vector2(0.5f, 1f);
        gfName.rectTransform.offsetMin = new Vector2(20f, -72f);
        gfName.rectTransform.offsetMax = new Vector2(-20f, -40f);
        var gfTier = UIBuildUtils.CreateText("Tier", gongFa, font, "", 18, TextAnchor.UpperRight, new Color(0.35f, 0.35f, 0.35f));
        gfTier.rectTransform.anchorMin = new Vector2(0f, 1f);
        gfTier.rectTransform.anchorMax = new Vector2(1f, 1f);
        gfTier.rectTransform.pivot = new Vector2(0.5f, 1f);
        gfTier.rectTransform.offsetMin = new Vector2(20f, -72f);
        gfTier.rectTransform.offsetMax = new Vector2(-20f, -40f);
        var gfDesc = UIBuildUtils.CreateText("Desc", gongFa, font, "", 19, TextAnchor.UpperLeft, UIBuildUtils.ColorText);
        gfDesc.rectTransform.anchorMin = new Vector2(0f, 0f);
        gfDesc.rectTransform.anchorMax = new Vector2(1f, 1f);
        gfDesc.rectTransform.offsetMin = new Vector2(20f, 16f);
        gfDesc.rectTransform.offsetMax = new Vector2(-20f, -80f);
        gongFaInfo.nameText = gfName;
        gongFaInfo.tierText = gfTier;
        gongFaInfo.descriptionText = gfDesc;
        gongFaInfo.emptyHint = "（未设定当前功法）";

        var realmPanel = CreatePanel("RealmShow", page, font, "境界展示", UIBuildUtils.ColorPanel);
        PlacePanel(realmPanel, new Vector2(0.32f, 0f), new Vector2(1f, 0.40f));

        var realmName = UIBuildUtils.CreateText("RealmName", realmPanel, font, "", 30, TextAnchor.MiddleCenter, UIBuildUtils.ColorText);
        realmName.rectTransform.anchorMin = new Vector2(0f, 0.62f);
        realmName.rectTransform.anchorMax = new Vector2(1f, 1f);
        realmName.rectTransform.offsetMin = new Vector2(20f, 0f);
        realmName.rectTransform.offsetMax = new Vector2(-20f, -34f);

        // 进度条：底槽 + 填充
        var track = UIBuildUtils.CreateImage("Track", realmPanel, new Color(0.92f, 0.92f, 0.92f, 1f));
        track.rectTransform.anchorMin = new Vector2(0f, 0.40f);
        track.rectTransform.anchorMax = new Vector2(1f, 0.40f);
        track.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        track.rectTransform.offsetMin = new Vector2(20f, -8f);
        track.rectTransform.offsetMax = new Vector2(-20f, 8f);

        var fill = UIBuildUtils.CreateImage("Fill", track.rectTransform, UIBuildUtils.ColorAccent);
        fill.rectTransform.anchorMin = new Vector2(0f, 0f);
        fill.rectTransform.anchorMax = new Vector2(0f, 1f);
        fill.rectTransform.pivot = new Vector2(0f, 0.5f);
        fill.rectTransform.offsetMin = new Vector2(0f, 0f);
        fill.rectTransform.offsetMax = new Vector2(0f, 0f);
        fill.rectTransform.sizeDelta = new Vector2(0f, 0f);

        var percent = UIBuildUtils.CreateText("Percent", realmPanel, font, "0%", 20, TextAnchor.MiddleRight, UIBuildUtils.ColorText);
        percent.rectTransform.anchorMin = new Vector2(0f, 0.08f);
        percent.rectTransform.anchorMax = new Vector2(1f, 0.34f);
        percent.rectTransform.offsetMin = new Vector2(20f, 0f);
        percent.rectTransform.offsetMax = new Vector2(-20f, 0f);

        var expText = UIBuildUtils.CreateText("Exp", realmPanel, font, "0 / 0", 18, TextAnchor.MiddleCenter, new Color(0.3f, 0.3f, 0.3f));
        expText.rectTransform.anchorMin = new Vector2(0f, 0.08f);
        expText.rectTransform.anchorMax = new Vector2(0f, 0.34f);
        expText.rectTransform.pivot = new Vector2(0f, 0.5f);
        expText.rectTransform.offsetMin = new Vector2(20f, 0f);
        expText.rectTransform.offsetMax = new Vector2(360f, 0f);

        realmBar = realmPanel.gameObject.AddComponent<UIRealmBar>();
        realmBar.realmNameText = realmName;
        realmBar.progressFill = fill;
        realmBar.percentText = percent;
        realmBar.expText = expText;
        realmBar.fillMaxWidth = 0f;   // 用 Filled 模式
        fill.type = Image.Type.Filled;
        fill.fillMethod = Image.FillMethod.Horizontal;
        fill.fillAmount = 0f;

        return page.gameObject;
    }

    static GameObject BuildAbilityPage(RectTransform parent, Font font, UIPanelData data,
                                       string label,
                                       out UIActiveSkillBar activeBar, out UIEntryList list, out UIEntryInfo info)
    {
        var page = UIBuildUtils.CreateRect("Page_" + label, parent);
        UIBuildUtils.Stretch(page);

        activeBar = CreateActiveSkillBar(page, font);
        PlacePanel((RectTransform)activeBar.transform, new Vector2(0.20f, 0.50f), new Vector2(0.76f, 1f));

        // 右上：生效中的被动神通
        var passivePanel = CreateScrollList("PassiveList", page, font, UIBuildUtils.ColorPanel,
                                            "生效中的被动神通", out _);
        var passiveRect = (RectTransform)passivePanel.parent.parent;
        PlacePanel(passiveRect, new Vector2(0.78f, 0.52f), new Vector2(1f, 1f));

        // 被动神通只读展示，不联动信息栏
        var passiveList = passiveRect.gameObject.AddComponent<UIEntryList>();
        passiveList.container = passivePanel;
        passiveList.font = font;
        passiveList.emptyHint = "（当前没有生效中的被动神通）";
        passiveList.data = data;
        passiveList.source = ListSource.生效被动;

        // 左下：掌握列表
        var listContent = CreateScrollList("KnownList", page, font, UIBuildUtils.ColorPanel,
                                           "掌握" + label + "列表（可拖动到主动技能装备栏）", out _);
        PlacePanel((RectTransform)listContent.parent.parent, new Vector2(0f, 0f), new Vector2(0.70f, 0.44f));

        var listRect = (RectTransform)listContent.parent.parent;
        info = CreateEntryInfo(page, font, label + "信息说明", false, out var infoRect);
        PlacePanel(infoRect, new Vector2(0.72f, 0f), new Vector2(1f, 0.44f));

        list = listRect.gameObject.AddComponent<UIEntryList>();
        list.container = listContent;
        list.font = font;
        list.infoTarget = info;
        list.emptyHint = "（尚未掌握任何" + label + "）";

        return page.gameObject;
    }

    static GameObject BuildTreasurePage(RectTransform parent, Font font, UIPanelData data,
                                        out UIActiveSkillBar activeBar, out UIEntryList list, out UIEntryInfo info)
    {
        var page = UIBuildUtils.CreateRect("Page_法宝", parent);
        UIBuildUtils.Stretch(page);

        activeBar = CreateActiveSkillBar(page, font);
        PlacePanel((RectTransform)activeBar.transform, new Vector2(0.20f, 0.50f), new Vector2(0.76f, 1f));

        info = CreateEntryInfo(page, font, "法宝展示区", true, out var showRect);
        PlacePanel(showRect, new Vector2(0.78f, 0.52f), new Vector2(1f, 1f));

        var listContent = CreateScrollList("OwnedList", page, font, UIBuildUtils.ColorPanel,
                                           "拥有的法宝", out _);
        var listRect = (RectTransform)listContent.parent.parent;
        PlacePanel(listRect, new Vector2(0f, 0f), new Vector2(1f, 0.44f));

        list = listRect.gameObject.AddComponent<UIEntryList>();
        list.container = listContent;
        list.font = font;
        list.infoTarget = info;
        list.emptyHint = "（尚未拥有任何法宝）";

        return page.gameObject;
    }

    static GameObject BuildArrayPage(RectTransform parent, Font font, UIPanelData data,
                                     out UIActiveSkillBar activeBar, out UIEntryList list, out UIEntryInfo info)
    {
        var page = UIBuildUtils.CreateRect("Page_灵阵", parent);
        UIBuildUtils.Stretch(page);

        activeBar = CreateActiveSkillBar(page, font);
        PlacePanel((RectTransform)activeBar.transform, new Vector2(0.20f, 0.50f), new Vector2(0.76f, 1f));

        var listContent = CreateScrollList("ArrayList", page, font, UIBuildUtils.ColorPanel,
                                           "掌握的灵阵（拖动到主动技能装备）", out _);
        var listRect = (RectTransform)listContent.parent.parent;
        PlacePanel(listRect, new Vector2(0f, 0f), new Vector2(0.66f, 0.44f));

        info = CreateEntryInfo(page, font, "选中灵阵的相关信息介绍", false, out var infoRect);
        PlacePanel(infoRect, new Vector2(0.68f, 0f), new Vector2(1f, 0.44f));

        list = listRect.gameObject.AddComponent<UIEntryList>();
        list.container = listContent;
        list.font = font;
        list.infoTarget = info;
        list.emptyHint = "（尚未掌握任何灵阵）";

        return page.gameObject;
    }

    /// <summary>
    /// 战阵页：上面九宫格站位、左下信息简介、右下已获得的战阵成员（带「上阵/下阵」按钮）。
    /// 布局照概念图「战阵ui」。
    /// </summary>
    static GameObject BuildSpiritFormationPage(RectTransform parent, Font font, UIPanelData data,
                                               out UISpiritFormationBar bar, out UIEntryList list, out UIEntryInfo info)
    {
        var page = UIBuildUtils.CreateRect("Page_战阵", parent);
        UIBuildUtils.Stretch(page);

        // 上：九宫格
        var gridPanel = CreatePanel("FormationGrid", page, font,
                                    "战阵站位（上方＝你前方 · 中间＝你 · 最多同时上阵 "
                                    + SpiritFormationLayout.可上阵数 + " 个）",
                                    UIBuildUtils.ColorPanel);
        PlacePanel(gridPanel, new Vector2(0.20f, 0.50f), new Vector2(0.80f, 1f));

        var inner = UIBuildUtils.CreateRect("GridArea", gridPanel);
        UIBuildUtils.Stretch(inner);
        inner.offsetMin = new Vector2(10f, 10f);
        inner.offsetMax = new Vector2(-10f, -40f);      // 让出标题

        bar = CreateSpiritGrid(inner, font);

        // 左下：信息简介
        info = CreateEntryInfo(page, font, "信息简介", false, out var infoRect);
        PlacePanel(infoRect, new Vector2(0f, 0f), new Vector2(0.28f, 0.46f));

        // 右下：已获得的战阵成员（240 个真灵，必须带滚动条）
        var listContent = CreateScrollList("SpiritList", page, font, UIBuildUtils.ColorPanel,
                                           "已获得的战阵成员（按族排列 · 妖魔在上、人类在下）",
                                           out _, 带滚动条: true);
        var listRect = (RectTransform)listContent.parent.parent;
        PlacePanel(listRect, new Vector2(0.30f, 0f), new Vector2(1f, 0.46f));

        list = listRect.gameObject.AddComponent<UIEntryList>();
        list.container = listContent;
        list.font = font;
        list.infoTarget = info;
        list.emptyHint = "（还没有获得任何战阵真灵）";

        return page.gameObject;
    }

    /// <summary>
    /// 战阵的 3×3 九宫格。行优先 0~8（左上起）。
    /// **上方 = 玩家前方、中间那格 = 玩家自己**（所以只有外围 8 格能上阵），
    /// 同时最多上 <see cref="SpiritFormationLayout.可上阵数"/> 个；
    /// 上满之后剩下的空格由 <see cref="UISpiritSlot"/> 自己画成「已满」。
    /// </summary>
    static UISpiritFormationBar CreateSpiritGrid(RectTransform parent, Font font)
    {
        var root = UIBuildUtils.CreateRect("SpiritFormationBar", parent);
        var bar = root.gameObject.AddComponent<UISpiritFormationBar>();

        const float 边长 = 104f;
        const float 间距 = 14f;

        for (int i = 0; i < SpiritFormationLayout.格子数; i++)
        {
            int 行 = i / 3;
            int 列 = i % 3;

            var slotRt = UIBuildUtils.CreateRect("Slot_" + i, root);
            slotRt.anchorMin = new Vector2(0.5f, 0.5f);
            slotRt.anchorMax = new Vector2(0.5f, 0.5f);
            slotRt.pivot = new Vector2(0.5f, 0.5f);
            slotRt.sizeDelta = new Vector2(边长, 边长);
            // 行 0 画在最上面 = 阵型最前排（离玩家最远）
            slotRt.anchoredPosition = new Vector2((列 - 1) * (边长 + 间距), (1 - 行) * (边长 + 间距));

            var bg = slotRt.gameObject.AddComponent<Image>();
            bg.color = UIBuildUtils.ColorSlot;
            // 【坑】CreateImage 默认 raycastTarget=false，必须显式打开，否则点不动
            bg.raycastTarget = true;

            var btn = slotRt.gameObject.AddComponent<Button>();
            btn.targetGraphic = bg;

            // 左侧竖色条：表示这一格站的是妖魔（紫红）还是人类（青）
            var swatch = UIBuildUtils.CreateImage("Swatch", slotRt, Color.clear);
            swatch.rectTransform.anchorMin = new Vector2(0f, 0f);
            swatch.rectTransform.anchorMax = new Vector2(0f, 1f);
            swatch.rectTransform.pivot = new Vector2(0f, 0.5f);
            swatch.rectTransform.offsetMin = new Vector2(8f, 10f);
            swatch.rectTransform.offsetMax = new Vector2(20f, -10f);
            swatch.gameObject.SetActive(false);

            // 名字（格子中间）
            var label = UIBuildUtils.CreateText("Label", slotRt, font, "", 16,
                                                TextAnchor.MiddleCenter, UIBuildUtils.ColorText);
            label.rectTransform.anchorMin = new Vector2(0f, 0f);
            label.rectTransform.anchorMax = new Vector2(1f, 1f);
            label.rectTransform.offsetMin = new Vector2(24f, 8f);
            label.rectTransform.offsetMax = new Vector2(-8f, -8f);

            // 「已满」小字（只在上阵名额用完后的空格上出现）
            var lockText = UIBuildUtils.CreateText("Lock", slotRt, font, "", 15,
                                                   TextAnchor.MiddleCenter, new Color(0.82f, 0.82f, 0.82f));
            lockText.rectTransform.anchorMin = new Vector2(0f, 0f);
            lockText.rectTransform.anchorMax = new Vector2(1f, 1f);
            lockText.rectTransform.offsetMin = new Vector2(6f, 6f);
            lockText.rectTransform.offsetMax = new Vector2(-6f, -6f);

            // 右上角小叉：下阵
            var clearBtn = UIBuildUtils.CreateButton("Clear", slotRt, font, "×", 18);
            var clearRt = clearBtn.GetComponent<RectTransform>();
            clearRt.anchorMin = new Vector2(1f, 1f);
            clearRt.anchorMax = new Vector2(1f, 1f);
            clearRt.pivot = new Vector2(1f, 1f);
            clearRt.anchoredPosition = new Vector2(-2f, -2f);
            clearRt.sizeDelta = new Vector2(24f, 24f);

            var slot = slotRt.gameObject.AddComponent<UISpiritSlot>();
            slot.background = bg;
            slot.swatch = swatch;
            slot.label = label;
            slot.lockText = lockText;
            slot.button = btn;
            slot.clearButton = clearBtn;
            slot.slot = i;
            bar.slots.Add(slot);
        }

        return bar;
    }

    static GameObject BuildInventoryPage(RectTransform parent, Font font, UIPanelData data,
                                         out UIEntryList list, out UIEntryInfo info)
    {
        var page = UIBuildUtils.CreateRect("Page_背包", parent);
        UIBuildUtils.Stretch(page);

        var grid = CreateScrollList("BagGrid", page, font, UIBuildUtils.ColorPanel, "背包格子", out _);
        PlacePanel((RectTransform)grid.parent.parent, new Vector2(0f, 0f), new Vector2(0.70f, 1f));

        var listRect = (RectTransform)grid.parent.parent;
        info = CreateEntryInfo(page, font, "选中物品", true, out var infoRect);
        PlacePanel(infoRect, new Vector2(0.72f, 0.42f), new Vector2(1f, 1f));

        // 右下：介绍栏（与图标分开，和概念图一致）
        var descPanel = CreatePanel("ItemDesc", page, font, "选中物品介绍", UIBuildUtils.ColorPanelAlt);
        PlacePanel(descPanel, new Vector2(0.72f, 0f), new Vector2(1f, 0.40f));
        var descText = UIBuildUtils.CreateText("Desc", descPanel, font, "", 17, TextAnchor.UpperLeft, UIBuildUtils.ColorText);
        UIBuildUtils.Stretch(descText.rectTransform, 0f);
        descText.rectTransform.offsetMin = new Vector2(12f, 12f);
        descText.rectTransform.offsetMax = new Vector2(-12f, -38f);

        // 复用 info 的描述文本：两条信息栏同时刷新
        info.descriptionText = descText;

        list = listRect.gameObject.AddComponent<UIEntryList>();
        list.container = grid;
        list.font = font;
        list.infoTarget = info;
        list.emptyHint = "（背包是空的）";

        return page.gameObject;
    }

    /// <summary>
    /// 坐骑页（照概念图「坐骑ui」）：
    ///   上 = 坐骑 idle 动画展示（RawImage + RenderTexture 的**真 3D 预览**）
    ///   左下 = 拥有的坐骑（带滚动条）
    ///   右下 = 选中坐骑信息
    /// </summary>
    static GameObject BuildMountPage(RectTransform parent, Font font, UIPanelData data,
                                     out UIMountPage mountPage, out UIEntryList list, out UIEntryInfo info)
    {
        var page = UIBuildUtils.CreateRect("Page_坐骑", parent);
        UIBuildUtils.Stretch(page);

        // ---- 上：idle 动画展示 ----
        var showPanel = CreatePanel("MountShow", page, font, "坐骑 idle 动画展示", UIBuildUtils.ColorPanel);
        PlacePanel(showPanel, new Vector2(0f, 0.50f), new Vector2(1f, 1f));

        var previewRt = UIBuildUtils.CreateRect("Preview", showPanel);
        UIBuildUtils.Stretch(previewRt);
        previewRt.offsetMin = new Vector2(10f, 10f);
        previewRt.offsetMax = new Vector2(-10f, -38f);      // 让出标题

        var raw = previewRt.gameObject.AddComponent<RawImage>();
        raw.color = Color.white;
        raw.raycastTarget = true;                           // 拖动旋转要接射线

        // 提示（右下角小字）
        var tip = UIBuildUtils.CreateText("Tip", showPanel, font, "左键拖动可旋转", 15,
                                          TextAnchor.LowerRight, new Color(0.55f, 0.55f, 0.55f));
        tip.rectTransform.anchorMin = new Vector2(0f, 0f);
        tip.rectTransform.anchorMax = new Vector2(1f, 0f);
        tip.rectTransform.pivot = new Vector2(0.5f, 0f);
        tip.rectTransform.offsetMin = new Vector2(14f, 12f);
        tip.rectTransform.offsetMax = new Vector2(-14f, 34f);

        // ---- 左下：拥有的坐骑（坐骑可能几十个，必须带滚动条）----
        var listContent = CreateScrollList("MountList", page, font, UIBuildUtils.ColorPanel,
                                           "拥有的坐骑", out _, 带滚动条: true);
        var listRect = (RectTransform)listContent.parent.parent;
        PlacePanel(listRect, new Vector2(0f, 0f), new Vector2(0.62f, 0.48f));

        // ---- 右下：选中坐骑信息 ----
        info = CreateEntryInfo(page, font, "选中坐骑信息", true, out var infoRect);
        PlacePanel(infoRect, new Vector2(0.64f, 0f), new Vector2(1f, 0.48f));

        list = listRect.gameObject.AddComponent<UIEntryList>();
        list.container = listContent;
        list.font = font;
        list.infoTarget = info;
        list.emptyHint = "（还没有任何坐骑）";

        mountPage = page.gameObject.AddComponent<UIMountPage>();
        mountPage.预览图 = raw;
        mountPage.列表 = list;
        mountPage.信息栏 = info;
        mountPage.data = data;

        return page.gameObject;
    }
}
