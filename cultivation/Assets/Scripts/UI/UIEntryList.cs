using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 拖拽上下文。列表行是拖拽源，主动技能格是放置目标，
/// 中间用这个静态量传递「正在拖的是哪个条目」。
/// （点选式装备是主路径，拖拽作为额外便利保留）
/// </summary>
public static class UIDragContext
{
    public static IPanelEntry Entry { get; private set; }
    public static GameObject Ghost { get; private set; }
    public static bool Dragging => Entry != null;

    public static void Begin(IPanelEntry entry, Transform canvasRoot, Font font)
    {
        End();
        Entry = entry;
        if (entry == null || canvasRoot == null) return;

        var go = new GameObject("DragGhost", typeof(RectTransform), typeof(Image));
        go.transform.SetParent(canvasRoot, false);
        go.transform.SetAsLastSibling();

        var img = go.GetComponent<Image>();
        img.color = UIEntryRow.TierColor(entry.DisplayTier);
        img.raycastTarget = false;

        var rt = (RectTransform)go.transform;
        rt.sizeDelta = new Vector2(200f, 40f);

        if (font != null)
        {
            var label = UIBuildUtils.CreateText("Label", rt, font, entry.DisplayName, 16,
                                                TextAnchor.MiddleCenter, new Color(0.1f, 0.1f, 0.1f));
            UIBuildUtils.Stretch(label.rectTransform, 2f);
            label.raycastTarget = false;
        }
        Ghost = go;
    }

    public static void Move(Vector2 screenPosition)
    {
        if (Ghost == null) return;
        Ghost.transform.position = screenPosition;
    }

    public static void End()
    {
        Entry = null;
        if (Ghost != null)
        {
            if (Application.isPlaying) UnityEngine.Object.Destroy(Ghost);
            else UnityEngine.Object.DestroyImmediate(Ghost);
            Ghost = null;
        }
    }
}

/// <summary>
/// 列表中的一行：[品阶色块] [名字] [主动/被动] [启用/停用按钮]
/// 点名字看信息；点右侧按钮做启用/停用或装备。
/// </summary>
public class UIEntryRow : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    public Button button;
    public Image swatch;
    public Text label;
    public Text tagText;

    [Tooltip("右侧的启用/停用按钮")]
    public Button actionButton;
    public Text actionLabel;

    [Tooltip("拖拽时影子挂到哪个 Canvas 下。留空则自动取最近的 Canvas")]
    public Transform dragLayer;

    public Font font;

    [Tooltip("数据源，用于判断启用/装备状态。留空会自动从所属列表 / Canvas 上找")]
    public UIPanelData data;

    /// <summary>
    /// 取数据源。行可能是在 list.data 还没赋值时创建的，
    /// 所以不能只信创建时的快照，这里按需回退到列表 / Canvas。
    /// </summary>
    public UIPanelData Data
    {
        get
        {
            if (data != null) return data;
            if (Owner != null && Owner.data != null) { data = Owner.data; return data; }

            var canvas = GetComponentInParent<Canvas>(true);
            if (canvas != null)
            {
                data = canvas.GetComponentInParent<UIPanelData>();
                if (data == null) data = canvas.GetComponent<UIPanelData>();
            }
            return data;
        }
    }

    public IPanelEntry Entry { get; private set; }
    public UIEntryList Owner { get; set; }

    Action<IPanelEntry> clickHandler;
    Action<IPanelEntry> actionHandler;

    /// <summary>绑定数据与回调</summary>
    public void Bind(IPanelEntry entry, Action<IPanelEntry> onClick, Action<IPanelEntry> onAction = null)
    {
        Entry = entry;
        clickHandler = onClick;
        actionHandler = onAction;

        if (label != null) label.text = entry != null ? entry.DisplayName : "";
        if (swatch != null) swatch.color = SwatchColor(entry);
        if (tagText != null)
        {
            tagText.text = TagOf(entry);
            tagText.color = TagColor(entry);
        }

        if (button != null)
        {
            button.onClick.RemoveAllListeners();
            if (onClick != null && entry != null)
                button.onClick.AddListener(() => clickHandler(entry));
        }

        if (actionButton != null)
        {
            actionButton.onClick.RemoveAllListeners();
            // 有「启用/停用」「上阵/下阵」这类右侧按钮的条目
            bool usable = entry is DivineAbilityDefinition || entry is NpcDefinition;
            actionButton.gameObject.SetActive(usable);
            if (usable)
            {
                if (actionLabel != null) actionLabel.text = ActionText(entry);
                if (actionButton.image != null) actionButton.image.color = ActionColor(entry);
                if (onAction != null)
                    actionButton.onClick.AddListener(() => actionHandler(entry));
            }
        }
    }

    /// <summary>按钮文字：被动是启用/停用，主动是启用/卸下，真灵是上阵/下阵</summary>
    public string ActionText(IPanelEntry entry)
    {
        var d = Data;

        if (entry is PassiveDivineAbility p)
            return (d == null || d.IsPassiveEnabled(p)) ? "停用" : "启用";

        if (entry is ActiveDivineAbility a)
            return (d != null && d.IsEquipped(a)) ? "卸下" : "启用";

        if (entry is NpcDefinition npc)
            return (d != null && d.IsSpiritOnField(npc)) ? "下阵" : "上阵";

        return "";
    }

    Color ActionColor(IPanelEntry entry)
    {
        var d = Data;

        if (entry is PassiveDivineAbility p)
            return (d == null || d.IsPassiveEnabled(p))
                ? new Color(0.85f, 0.45f, 0.35f)      // 已启用 → 点它是停用
                : new Color(0.40f, 0.70f, 0.45f);     // 已停用 → 点它是启用

        if (entry is ActiveDivineAbility a)
            return (d != null && d.IsEquipped(a))
                ? new Color(0.55f, 0.55f, 0.60f)      // 已装备 → 点它是卸下
                : new Color(0.40f, 0.62f, 0.85f);     // 未装备 → 点它是启用

        if (entry is NpcDefinition npc)
            return (d != null && d.IsSpiritOnField(npc))
                ? new Color(0.55f, 0.55f, 0.60f)      // 已在阵上 → 点它是下阵
                : new Color(0.35f, 0.70f, 0.70f);     // 没上阵 → 点它是上阵

        return new Color(0.6f, 0.6f, 0.6f);
    }

    // ---------------------------------------------------------------- 拖拽

    public void OnBeginDrag(PointerEventData e)
    {
        if (Entry == null) return;
        var layer = dragLayer != null ? dragLayer : FindCanvas();
        UIDragContext.Begin(Entry, layer, font);
        UIDragContext.Move(e.position);
    }

    public void OnDrag(PointerEventData e) => UIDragContext.Move(e.position);
    public void OnEndDrag(PointerEventData e) => UIDragContext.End();

    Transform FindCanvas()
    {
        var c = GetComponentInParent<Canvas>();
        if (c == null) return null;
        return c.rootCanvas != null ? c.rootCanvas.transform : c.transform;
    }

    // ---------------------------------------------------------------- 静态工具

    /// <summary>条目的类型标签：主动 / 被动 / 法宝 / 灵阵 / 物品 …</summary>
    public static string TagOf(IPanelEntry entry)
    {
        switch (entry)
        {
            case ActiveDivineAbility _:   return "主动";
            case PassiveDivineAbility _:  return "被动";
            case TreasureDefinition _:    return "法宝";
            case SpiritArrayDefinition _: return "灵阵";
            case GongFaDefinition _:      return "功法";
            case ItemDefinition _:        return "物品";
            case NpcDefinition n:         return "境界 " + n.境界;   // 战阵真灵列表里显示境界
            case MountDefinition m:       return "门槛 " + m.修炼门槛; // 坐骑列表里显示可乘骑门槛
            default:                      return "";
        }
    }

    /// <summary>标签颜色：主动偏暖、被动偏冷，一眼能分开</summary>
    public static Color TagColor(IPanelEntry entry)
    {
        switch (entry)
        {
            case ActiveDivineAbility _:   return new Color(0.95f, 0.55f, 0.25f);
            case PassiveDivineAbility _:  return new Color(0.35f, 0.70f, 0.95f);
            case TreasureDefinition _:    return new Color(0.80f, 0.65f, 0.25f);
            case SpiritArrayDefinition _: return new Color(0.55f, 0.80f, 0.45f);
            case NpcDefinition _:         return new Color(0.35f, 0.72f, 0.72f);
            case MountDefinition _:       return new Color(0.72f, 0.60f, 0.85f);
            default:                      return new Color(0.45f, 0.45f, 0.45f);
        }
    }

    /// <summary>左侧小色块的颜色。真灵按「妖魔 / 人类」分色，比按品阶有意义</summary>
    public static Color SwatchColor(IPanelEntry entry)
    {
        if (entry is NpcDefinition n) return SpiritColor(n);
        return TierColor(entry != null ? entry.DisplayTier : QualityTier.凡品);
    }

    /// <summary>战阵真灵的颜色：妖魔偏紫红、人类偏青</summary>
    public static Color SpiritColor(NpcDefinition npc)
    {
        if (npc == null) return new Color(0.65f, 0.65f, 0.65f);
        switch (npc.类型)
        {
            case NpcKind.妖魔: return new Color(0.82f, 0.45f, 0.62f);
            case NpcKind.人类: return new Color(0.42f, 0.72f, 0.82f);
            case NpcKind.兽类: return new Color(0.72f, 0.68f, 0.40f);
            default:           return new Color(0.65f, 0.65f, 0.65f);
        }
    }

    /// <summary>品阶对应的小色块颜色</summary>
    public static Color TierColor(QualityTier tier)
    {
        switch (tier)
        {
            case QualityTier.凡品: return new Color(0.65f, 0.65f, 0.65f);
            case QualityTier.黄品: return new Color(0.85f, 0.78f, 0.45f);
            case QualityTier.玄品: return new Color(0.45f, 0.75f, 0.85f);
            case QualityTier.地品: return new Color(0.55f, 0.85f, 0.50f);
            case QualityTier.天品: return new Color(0.85f, 0.55f, 0.85f);
            case QualityTier.仙品: return new Color(0.95f, 0.60f, 0.35f);
            case QualityTier.神品: return new Color(0.95f, 0.35f, 0.35f);
            default: return Color.gray;
        }
    }
}

/// <summary>列表展示的数据来源</summary>
public enum ListSource
{
    无 = 0,
    物品,
    神通,
    法宝,
    灵阵,
    生效被动,
    战阵成员,
    坐骑,
}

/// <summary>
/// 通用条目列表。背包 / 神通 / 法宝 / 灵阵 四个页面共用同一套渲染逻辑。
/// </summary>
public class UIEntryList : MonoBehaviour
{
    [Header("引用")]
    [Tooltip("行容器，需挂 VerticalLayoutGroup")]
    public RectTransform container;

    [Tooltip("中文字体")]
    public Font font;

    [Tooltip("选中后刷新哪个信息栏。留空则只高亮不联动")]
    public UIEntryInfo infoTarget;

    [Tooltip("数据源。填了之后列表会自动跟随数据变化刷新")]
    public UIPanelData data;

    [Tooltip("本列表展示的是哪一份数据")]
    public ListSource source = ListSource.无;

    [Header("外观")]
    public float rowHeight = 34f;
    public int fontSize = 18;

    [Tooltip("列表为空时显示的提示")]
    public string emptyHint = "（暂无内容）";

    readonly List<GameObject> spawned = new List<GameObject>();
    IList<IPanelEntry> lastEntries;

    public IPanelEntry Selected { get; private set; }
    public event Action<IPanelEntry> SelectionChanged;

    void Start() => RebuildFromSource();

    void OnEnable()
    {
        var d = ResolveData();
        if (d != null) d.Changed += RebuildFromSource;

        // 关键：必须在这里生成行，不能只靠 Start。
        // 神通/法宝/灵阵/背包 这些页面启动时是未激活的，Start 不会执行，
        // 行就不会被创建（尤其现在行是 DontSave、不再保存进场景了）。
        RebuildFromSource();
    }

    void OnDisable()
    {
        if (data != null) data.Changed -= RebuildFromSource;
    }

    /// <summary>按 source 从数据源重新取一遍并重建列表，尽量保留原来的选中项</summary>
    public void RebuildFromSource()
    {
        if (data == null)
        {
            if (lastEntries != null) SetEntries(lastEntries);
            return;
        }

        var keep = Selected;
        var entries = Fetch();
        if (entries == null) return;

        SetEntries(entries);
        if (keep != null && entries.Contains(keep)) Select(keep);
    }

    List<IPanelEntry> Fetch()
    {
        switch (source)
        {
            case ListSource.物品:     return data.GetItems();
            case ListSource.神通:     return data.GetAbilities();
            case ListSource.法宝:     return data.GetTreasures();
            case ListSource.灵阵:     return data.GetSpiritArrays();
            case ListSource.生效被动: return data.GetPassiveAbilities();
            case ListSource.战阵成员: return data.GetSpirits();
            case ListSource.坐骑:     return data.GetMounts();
            default:                  return null;
        }
    }

    /// <summary>重建整个列表</summary>
    public void SetEntries(IList<IPanelEntry> entries)
    {
        lastEntries = entries;
        if (container == null || font == null) return;

        // 关键：必须清掉容器里【所有】子物体，不能只清 spawned 记录的。
        // spawned 是运行时字段、不会被序列化，而编辑期生成的行是真实存在场景里的，
        // 进游戏后 spawned 是空的 —— 只清 spawned 会让新旧两批行叠在一起（重复加载）。
        int childCount = container.childCount;
        for (int i = childCount - 1; i >= 0; i--)
        {
            var go = container.GetChild(i).gameObject;
            go.transform.SetParent(null, false);   // 立刻移出容器，避免这一帧内重复
            if (Application.isPlaying) Destroy(go); else DestroyImmediate(go);
        }
        spawned.Clear();

        if (entries == null || entries.Count == 0)
        {
            var hint = UIBuildUtils.CreateText("Empty", container, font, emptyHint, fontSize,
                                               TextAnchor.MiddleCenter, new Color(0.4f, 0.4f, 0.4f));
            var le = hint.gameObject.AddComponent<LayoutElement>();
            le.minHeight = rowHeight;
            le.preferredHeight = rowHeight;
            hint.gameObject.hideFlags = HideFlags.DontSave;   // 只存在于当前会话，不写进场景
            spawned.Add(hint.gameObject);
            Select(null);
            return;
        }

        foreach (var e in entries) AddRow(e);
        Select(entries[0]);
    }

    void AddRow(IPanelEntry entry)
    {
        var rowRt = UIBuildUtils.CreateRect("Row", container);
        var le = rowRt.gameObject.AddComponent<LayoutElement>();
        le.minHeight = rowHeight;
        le.preferredHeight = rowHeight;

        var bg = rowRt.gameObject.AddComponent<Image>();
        bg.color = new Color(0.80f, 0.80f, 0.80f, 1f);

        var btn = rowRt.gameObject.AddComponent<Button>();
        btn.targetGraphic = bg;

        // 品阶色块
        var swatch = UIBuildUtils.CreateImage("Swatch", rowRt, Color.gray);
        swatch.rectTransform.anchorMin = new Vector2(0f, 0.15f);
        swatch.rectTransform.anchorMax = new Vector2(0f, 0.85f);
        swatch.rectTransform.pivot = new Vector2(0f, 0.5f);
        swatch.rectTransform.offsetMin = new Vector2(4f, 0f);
        swatch.rectTransform.offsetMax = new Vector2(4f + 26f, 0f);

        // 名称（右侧给「主动/被动」标签和按钮让出位置）
        var label = UIBuildUtils.CreateText("Label", rowRt, font,
            entry != null ? entry.DisplayName : "", fontSize, TextAnchor.MiddleLeft, UIBuildUtils.ColorText);
        label.rectTransform.anchorMin = new Vector2(0f, 0f);
        label.rectTransform.anchorMax = new Vector2(1f, 1f);
        label.rectTransform.offsetMin = new Vector2(38f, 0f);
        label.rectTransform.offsetMax = new Vector2(-176f, 0f);

        // 主动 / 被动 标签
        var tag = UIBuildUtils.CreateText("Tag", rowRt, font, "", Mathf.Max(12, fontSize - 3),
                                          TextAnchor.MiddleRight, new Color(0.45f, 0.45f, 0.45f));
        tag.rectTransform.anchorMin = new Vector2(1f, 0f);
        tag.rectTransform.anchorMax = new Vector2(1f, 1f);
        tag.rectTransform.pivot = new Vector2(1f, 0.5f);
        tag.rectTransform.offsetMin = new Vector2(-172f, 0f);
        tag.rectTransform.offsetMax = new Vector2(-98f, 0f);

        // 右侧：启用 / 停用 按钮
        var actBtn = UIBuildUtils.CreateButton("Action", rowRt, font, "启用", Mathf.Max(12, fontSize - 3));
        var actRt = actBtn.GetComponent<RectTransform>();
        actRt.anchorMin = new Vector2(1f, 0.5f);
        actRt.anchorMax = new Vector2(1f, 0.5f);
        actRt.pivot = new Vector2(1f, 0.5f);
        actRt.anchoredPosition = new Vector2(-4f, 0f);
        actRt.sizeDelta = new Vector2(88f, rowHeight - 8f);
        var actLabel = actBtn.GetComponentInChildren<Text>();

        var row = rowRt.gameObject.AddComponent<UIEntryRow>();
        row.button = btn;
        row.swatch = swatch;
        row.label = label;
        row.tagText = tag;
        row.actionButton = actBtn;
        row.actionLabel = actLabel;
        row.font = font;
        row.data = data;
        row.Owner = this;
        row.Bind(entry, OnRowClicked, OnRowAction);

        // 生成出来的行【不写进场景】。
        // 否则编辑期这批行会被序列化保存，进游戏后又生成一批，两批叠在一起就成了「重复加载」，
        // 而且旧那批的组件引用已经失效，按钮点了没反应。
        rowRt.gameObject.hideFlags = HideFlags.DontSave;

        spawned.Add(rowRt.gameObject);
    }

    void OnRowClicked(IPanelEntry entry) => Select(entry);

    /// <summary>取数据源，没接就从 Canvas 上找</summary>
    public UIPanelData ResolveData()
    {
        if (data != null) return data;

        var canvas = GetComponentInParent<Canvas>(true);
        if (canvas != null)
        {
            data = canvas.GetComponentInParent<UIPanelData>();
            if (data == null) data = canvas.GetComponent<UIPanelData>();
        }
        return data;
    }

    /// <summary>右侧按钮：被动切启停；主动装备 / 卸下</summary>
    void OnRowAction(IPanelEntry entry)
    {
        var panel = ResolveData();
        if (panel == null) return;

        // ---- 被动神通：直接启停，不需要槽位 ----
        if (entry is PassiveDivineAbility passive)
        {
            panel.TogglePassive(passive);      // 内部 RaiseChanged → 列表自动刷新
            return;
        }

        // ---- 主动神通：装备 / 卸下 ----
        if (entry is ActiveDivineAbility active)
        {
            if (panel.IsEquipped(active))
            {
                panel.Unequip(active);
                panel.ShowHint("已卸下「" + active.DisplayName + "」");
                return;
            }

            if (!panel.HasEmptySlot())
            {
                panel.ShowHint("主动技能装备栏已经满了，先卸下一个再装备");
                return;
            }

            panel.BeginPendingEquip(active);
            panel.ShowHint("「" + active.DisplayName + "」待装备：请点一下空的主动技能装备栏");
            return;
        }

        // ---- 战阵真灵：上阵 / 下阵 ----
        if (entry is NpcDefinition spirit)
        {
            panel.ToggleSpirit(spirit);
            return;
        }
    }

    /// <summary>选中某条目并刷新信息栏</summary>
    public void Select(IPanelEntry entry)
    {
        Selected = entry;
        if (infoTarget != null) infoTarget.Show(entry);
        SelectionChanged?.Invoke(entry);
    }
}
