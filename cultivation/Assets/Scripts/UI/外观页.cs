using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// **角色面板「外观」页** —— 布局照用户示意图：
///   左：`当前持有外观列表`（每行「外观名字 + 装备按钮」，带滚动条）
///   右：`模型3D展示`（用 <see cref="外观预览"/> 渲染，**只能左右转**）
///
/// ### 为什么不直接改 CharacterPanelUI
/// 那边是 `enum 背包/境界/神通/…` 的固定枚举 + 编辑器 builder 生成的页面预制结构，
/// 直接改要同时动枚举和 builder。这个组件**自己把页面搭出来**（和对话框 DialogueUI 一个思路），
/// 所以：面板那边只要"把本组件所在的物体当成一个页面打开"就行（一行调用），
/// 以后要接进标签栏也不用重写布局。
///
/// ### 列表规则（对上"当前持有"这四个字）
/// 只列 **<see cref="AppearanceDefinition.已解锁"/> == true** 的外观（没解锁的不该出现在持有列表里），
/// 按表里的顺序；装备中的那行按钮显示「已装备」并置灰，其它显示「装备」。
/// </summary>
[DisallowMultipleComponent]
public class 外观页 : MonoBehaviour
{
    [Header("引用（留空自动找）")]
    [Tooltip("页面根（打开/关闭的就是它）。留空 = 自己这个物体")]
    public GameObject 页面根;

    [Tooltip("列表容器（带 ScrollRect 时把它的 content 拖进来）")]
    public RectTransform 列表容器;

    [Tooltip("3D 展示用的 RawImage")]
    public RawImage 展示画布;

    [Tooltip("把 3D 展示交给它（没挂就自动加在展示画布上）")]
    public 外观预览 预览;

    [Header("外观")]
    public Font 字体;
    public Color 行底色 = new Color(0.86f, 0.86f, 0.86f, 1f);
    public Color 字色 = new Color(0.08f, 0.08f, 0.10f, 1f);
    public Color 装备底色 = new Color(0.95f, 0.78f, 0.35f, 1f);
    public Color 已装备底色 = new Color(0.55f, 0.55f, 0.55f, 1f);
    public int 字号 = 26;

    readonly List<GameObject> 行 = new List<GameObject>();
    AppearanceDatabase 库;
    玩家外观 玩家外观组件;

    void Awake()
    {
        if (页面根 == null) 页面根 = gameObject;
    }

    void OnEnable() => 刷新();

    /// <summary>重建列表 + 刷新预览</summary>
    public void 刷新()
    {
        库 = AppearanceDatabase.取();
        if (玩家外观组件 == null) 玩家外观组件 = FindObjectOfType<玩家外观>();

        清理();
        if (库 == null) { Debug.LogWarning("[外观页] 找不到外观库（跑一下 修仙/外观/一键：生成表 + 导入 + 收集）"); return; }

        for (int i = 0; i < 库.全部.Count; i++)
        {
            var a = 库.全部[i];
            if (a == null || !已拥有(a)) continue;      // 只列"当前持有"的（用户 2026-09-27：靠道具获得，不看解锁标记）
            建行(a);
        }

        // 预览：默认展示当前穿的那件，没有就展示第一件
        if (预览 != null)
        {
            var 展示 = 玩家外观组件 != null && 玩家外观组件.当前外观 != null ? 玩家外观组件.当前外观 : 第一件();
            if (展示 != null) 预览.显示(展示);
        }
    }

    /// <summary>
    /// 这件外观现在算"持有"吗。
    /// 用户 2026-09-27 定的口径：**不靠标记解锁，而是任务发道具、道具使用后获得** ——
    /// 所以权威是 <see cref="玩家外观.已拥有"/>（默认外观永远算有）。
    /// 拿不到 玩家外观 组件时退回外观资产自己的 已解锁（默认拥有 / 标记），至少不会全空。
    /// </summary>
    bool 已拥有(AppearanceDefinition a)
    {
        if (玩家外观组件 == null) 玩家外观组件 = FindObjectOfType<玩家外观>();
        if (玩家外观组件 != null) return 玩家外观组件.已拥有(a);
        return a != null && a.已解锁;
    }

    AppearanceDefinition 第一件()
    {
        for (int i = 0; i < 库.全部.Count; i++)
            if (库.全部[i] != null && 已拥有(库.全部[i])) return 库.全部[i];
        return null;
    }

    void 清理()
    {
        for (int i = 0; i < 行.Count; i++) if (行[i] != null) Destroy(行[i]);
        行.Clear();
    }

    void 建行(AppearanceDefinition 外观)
    {
        var 父 = 列表容器 != null ? 列表容器 : (RectTransform)transform;
        bool 已装备 = 玩家外观组件 != null && 玩家外观组件.当前外观 == 外观;

        var go = new GameObject("行_" + 外观.id, typeof(RectTransform));
        go.transform.SetParent(父, false);
        var rt = (RectTransform)go.transform;
        rt.sizeDelta = new Vector2(0f, 56f);
        var le = go.AddComponent<LayoutElement>();
        le.preferredHeight = 56f;

        var 底 = new GameObject("底", typeof(RectTransform), typeof(Image));
        底.transform.SetParent(go.transform, false);
        var 底图 = 底.GetComponent<Image>();
        底图.color = 行底色;
        拉满((RectTransform)底.transform);

        // 名字（左）
        var 名 = 新字("名字", go.transform, 外观.DisplayName, TextAnchor.MiddleLeft);
        var 名rt = (RectTransform)名.transform;
        名rt.anchorMin = new Vector2(0f, 0f); 名rt.anchorMax = new Vector2(1f, 1f);
        名rt.offsetMin = new Vector2(16f, 0f); 名rt.offsetMax = new Vector2(-120f, 0f);

        // 装备按钮（右）
        var 按钮 = new GameObject("装备", typeof(RectTransform), typeof(Image), typeof(Button));
        按钮.transform.SetParent(go.transform, false);
        var 按钮rt = (RectTransform)按钮.transform;
        按钮rt.anchorMin = new Vector2(1f, 0.5f); 按钮rt.anchorMax = new Vector2(1f, 0.5f);
        按钮rt.pivot = new Vector2(1f, 0.5f);
        按钮rt.anchoredPosition = new Vector2(-10f, 0f);
        按钮rt.sizeDelta = new Vector2(100f, 40f);
        var 按钮图 = 按钮.GetComponent<Image>();
        按钮图.color = 已装备 ? 已装备底色 : 装备底色;
        var btn = 按钮.GetComponent<Button>();
        btn.interactable = !已装备;
        btn.onClick.AddListener(() =>
        {
            if (玩家外观组件 == null) 玩家外观组件 = FindObjectOfType<玩家外观>();
            if (玩家外观组件 != null && 玩家外观组件.装备(外观))
            {
                if (预览 != null) 预览.显示(外观);
                刷新();          // 按钮文字/颜色跟着变
            }
        });

        var 字 = 新字("字", 按钮.transform, 已装备 ? "已装备" : "装备", TextAnchor.MiddleCenter);
        字.color = 字色;
        拉满((RectTransform)字.transform);

        行.Add(go);
    }

    Text 新字(string 名, Transform 父, string 内容, TextAnchor 对齐)
    {
        var go = new GameObject(名, typeof(RectTransform), typeof(Text));
        go.transform.SetParent(父, false);
        var t = go.GetComponent<Text>();
        t.font = 字体 != null ? 字体 : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        t.fontSize = 字号;
        t.color = 字色;
        t.alignment = 对齐;
        t.raycastTarget = false;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.text = 内容;
        return t;
    }

    static void 拉满(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
    }

    // ---- ASCII 别名 ----
    public void Refresh() => 刷新();
}
