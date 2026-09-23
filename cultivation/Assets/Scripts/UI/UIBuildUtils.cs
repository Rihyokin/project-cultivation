using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 构建 uGUI 界面的小工具集。编辑器生成脚本与运行时动态行都用它，保证风格一致。
/// 注意：项目没有导入 TMP Essential Resources，所以这里统一用 uGUI 传统 Text + 项目内中文字体。
/// </summary>
public static class UIBuildUtils
{
    // 面板配色（概念图是灰阶稿，这里沿用同一套灰阶，方便你之后换皮）
    public static readonly Color ColorDim        = new Color(0f, 0f, 0f, 0.55f);
    public static readonly Color ColorSidebar    = new Color(0.82f, 0.82f, 0.82f, 1f);
    public static readonly Color ColorTabNormal  = new Color(0.55f, 0.55f, 0.55f, 1f);
    public static readonly Color ColorTabActive  = new Color(0.34f, 0.34f, 0.34f, 1f);
    public static readonly Color ColorPanel      = new Color(0.72f, 0.72f, 0.72f, 1f);
    public static readonly Color ColorPanelAlt   = new Color(0.58f, 0.58f, 0.58f, 1f);
    public static readonly Color ColorSlot       = new Color(0.85f, 0.85f, 0.85f, 1f);
    public static readonly Color ColorText       = new Color(0.12f, 0.12f, 0.12f, 1f);
    public static readonly Color ColorTextOnDark = new Color(0.92f, 0.92f, 0.92f, 1f);
    public static readonly Color ColorAccent     = new Color(0.78f, 0.52f, 0.33f, 1f); // 概念图里的橙色强调

    /// <summary>创建一个空的 RectTransform 节点</summary>
    public static RectTransform CreateRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.localScale = Vector3.one;
        return rt;
    }

    /// <summary>创建纯色底板</summary>
    public static Image CreateImage(string name, Transform parent, Color color)
    {
        var rt = CreateRect(name, parent);
        var img = rt.gameObject.AddComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
        return img;
    }

    /// <summary>创建文本</summary>
    public static Text CreateText(string name, Transform parent, Font font, string content,
                                  int fontSize, TextAnchor anchor, Color color)
    {
        var rt = CreateRect(name, parent);
        var t = rt.gameObject.AddComponent<Text>();
        t.font = font;
        t.fontSize = fontSize;
        t.text = content;
        t.alignment = anchor;
        t.color = color;
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        t.supportRichText = false;
        return t;
    }

    /// <summary>创建一个带底色的按钮（返回 Button，文字在子节点 "Label"）</summary>
    public static Button CreateButton(string name, Transform parent, Font font, string label, int fontSize = 22)
    {
        var rt = CreateRect(name, parent);
        var img = rt.gameObject.AddComponent<Image>();
        img.color = ColorTabNormal;
        var btn = rt.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;
        var colors = btn.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(0.9f, 0.9f, 0.9f, 1f);
        colors.pressedColor = new Color(0.75f, 0.75f, 0.75f, 1f);
        btn.colors = colors;

        var text = CreateText("Label", rt, font, label, fontSize, TextAnchor.MiddleCenter, ColorText);
        Stretch(text.rectTransform);
        return btn;
    }

    /// <summary>铺满父节点</summary>
    public static void Stretch(RectTransform rt, float padding = 0f)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(padding, padding);
        rt.offsetMax = new Vector2(-padding, -padding);
    }

    /// <summary>锚定到父节点某个位置，并指定尺寸。anchor 用 0~1 归一化坐标</summary>
    public static void Place(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
    {
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.offsetMin = offsetMin;
        rt.offsetMax = offsetMax;
    }

    /// <summary>带内边距的竖排/横排容器</summary>
    public static VerticalLayoutGroup AddVerticalLayout(RectTransform rt, float spacing, RectOffset padding)
    {
        var v = rt.gameObject.AddComponent<VerticalLayoutGroup>();
        v.spacing = spacing;
        v.padding = padding;
        v.childForceExpandWidth = true;
        v.childForceExpandHeight = false;
        v.childControlWidth = true;
        v.childControlHeight = true;
        return v;
    }

    /// <summary>
    /// 给一个已有 <see cref="ScrollRect"/> 的面板配一根**右侧竖向滚动条**，并把视口右边让开。
    ///
    /// 【坑 1】滚动条必须挂在 **ScrollRect 自己那个节点下**（ugui 的 ScrollRectEditor
    /// 对 AutoHide 系列模式会校验这一点），所以这里强制用 <c>scroll.transform</c> 当父节点。
    /// 【坑 2】`UIBuildUtils.CreateImage` 默认 <c>raycastTarget = false</c> ——
    /// 滑块和图轨都必须显式打开，否则**看得见但拖不动**（和九宫格格子那次是同一个坑）。
    /// 【坑 3】视口右边界要自己往里收「滚动条宽 + 间距」，因为这里用的是
    /// <see cref="ScrollRect.ScrollbarVisibility.AutoHide"/>（不做 ExpandViewport），
    /// 视口宽度是固定的、由构建时算好。
    /// </summary>
    /// <param name="scroll">目标滚动区</param>
    /// <param name="宽">滚动条宽度（像素）</param>
    /// <param name="顶部留白">顶部让出多少（和视口一样，通常等于标题高度）</param>
    /// <param name="间距">滚动条和视口之间的空隙（像素）</param>
    public static Scrollbar AddVerticalScrollbar(ScrollRect scroll, float 宽 = 18f,
                                                  float 顶部留白 = 34f, float 间距 = 6f)
    {
        if (scroll == null) return null;

        // ---- 图轨 ----
        var 轨道 = CreateRect("Scrollbar", scroll.transform);
        Place(轨道, new Vector2(1f, 0f), new Vector2(1f, 1f),
              new Vector2(-宽, 0f), new Vector2(0f, -顶部留白));

        var 轨道图 = 轨道.gameObject.AddComponent<Image>();
        轨道图.color = new Color(0f, 0f, 0f, 0.18f);
        轨道图.raycastTarget = true;      // 点图轨可以翻页

        // ---- 滑动区 + 滑块 ----
        var 滑动区 = CreateRect("Sliding Area", 轨道);
        Stretch(滑动区, 2f);

        var 滑块 = CreateImage("Handle", 滑动区, new Color(0.78f, 0.52f, 0.33f, 0.95f));
        滑块.raycastTarget = true;        // ★ 少了这句就拖不动
        Stretch(滑块.rectTransform);

        // ---- Scrollbar 组件 ----
        var bar = 轨道.gameObject.AddComponent<Scrollbar>();
        bar.direction = Scrollbar.Direction.BottomToTop;
        bar.handleRect = 滑块.rectTransform;
        bar.targetGraphic = 滑块;

        var colors = bar.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1f, 0.82f, 0.62f, 1f);
        colors.pressedColor = new Color(0.92f, 0.72f, 0.5f, 1f);
        bar.colors = colors;

        // ---- 接线 ----
        scroll.verticalScrollbar = bar;
        scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
        scroll.verticalScrollbarSpacing = 间距;

        // 视口右边收进来
        if (scroll.viewport != null)
            scroll.viewport.offsetMax = new Vector2(scroll.viewport.offsetMax.x - (宽 + 间距),
                                                    scroll.viewport.offsetMax.y);

        return bar;
    }

    /// <summary>给容器加一个 1 像素描边，便于看清分区</summary>
    public static Outline AddOutline(RectTransform rt, Color color)
    {
        var o = rt.gameObject.AddComponent<Outline>();
        o.effectColor = color;
        o.effectDistance = new Vector2(1f, -1f);
        return o;
    }
}
