using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 条目信息栏。背包的「选中物品图片 / 选中物品介绍」、神通的「神通信息说明」、
/// 灵阵的「选中灵阵的相关信息介绍」都用它。
///
/// 对被动神通会额外显示一个【启用 / 停用】按钮。
/// </summary>
public class UIEntryInfo : MonoBehaviour
{
    [Header("引用")]
    public Image iconImage;
    public Text nameText;
    public Text tierText;
    public Text descriptionText;

    [Tooltip("主动 / 被动 标签")]
    public Text kindText;

    [Tooltip("启用/停用按钮，只对被动神通出现")]
    public Button actionButton;

    [Tooltip("按钮上的文字")]
    public Text actionLabel;

    [Tooltip("数据源，用于读写被动神通的启用状态")]
    public UIPanelData data;

    [Header("占位")]
    [Tooltip("没有选中内容时显示的提示")]
    public string emptyHint = "（未选中）";

    /// <summary>当前展示的条目</summary>
    public IPanelEntry Current { get; private set; }

    /// <summary>按钮被点击（参数为当前条目）</summary>
    public event Action<IPanelEntry> ActionClicked;

    void Awake()
    {
        if (actionButton != null)
            actionButton.onClick.AddListener(OnActionClicked);
    }

    void OnEnable()
    {
        if (data != null) data.Changed += RefreshActionState;
    }

    void OnDisable()
    {
        if (data != null) data.Changed -= RefreshActionState;
    }

    void OnActionClicked()
    {
        if (Current is PassiveDivineAbility p && data != null)
            data.TogglePassive(p);          // 会触发 Changed，列表跟着刷新

        RefreshActionState();
        ActionClicked?.Invoke(Current);
    }

    /// <summary>展示某个条目；传 null 清空</summary>
    public void Show(IPanelEntry entry)
    {
        Current = entry;

        if (entry == null)
        {
            if (nameText != null) nameText.text = emptyHint;
            if (tierText != null) tierText.text = "";
            if (descriptionText != null) descriptionText.text = "";
            if (kindText != null) kindText.text = "";
            if (iconImage != null)
            {
                iconImage.sprite = null;
                iconImage.color = new Color(0.62f, 0.62f, 0.62f, 1f);
            }
            if (actionButton != null) actionButton.gameObject.SetActive(false);
            return;
        }

        if (nameText != null) nameText.text = entry.DisplayName;
        if (tierText != null) tierText.text = "【" + entry.DisplayTier + "】";
        if (descriptionText != null) descriptionText.text = entry.DisplayDescription;

        // 主动 / 被动 标识
        if (kindText != null)
        {
            kindText.text = UIEntryRow.TagOf(entry);
            kindText.color = UIEntryRow.TagColor(entry);
        }

        if (iconImage != null)
        {
            iconImage.sprite = entry.DisplayIcon;
            // 没有图时用一块品阶色底板占位，避免一片空白
            iconImage.color = entry.DisplayIcon != null
                ? Color.white
                : UIEntryRow.TierColor(entry.DisplayTier);
        }

        RefreshActionState();
    }

    /// <summary>刷新「启用/停用」按钮的显隐与文字</summary>
    public void RefreshActionState()
    {
        if (actionButton == null) return;

        var passive = Current as PassiveDivineAbility;
        bool show = passive != null && data != null;
        actionButton.gameObject.SetActive(show);
        if (!show) return;

        bool enabled = data.IsPassiveEnabled(passive);
        if (actionLabel != null) actionLabel.text = enabled ? "停用" : "启用";
        actionButton.image.color = enabled
            ? new Color(0.85f, 0.45f, 0.35f)      // 已启用 → 点它是停用，用暖色
            : new Color(0.40f, 0.70f, 0.45f);     // 已停用 → 点它是启用，用冷色
    }
}
