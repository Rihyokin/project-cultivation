using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 战阵九宫格里的一格。
/// 两种用法和主动技能格一致：
///   · 有待上阵的真灵时点它 → 放进去（空格才行，已满 / 被占用会给提示）
///   · 平时点它            → 看这一格上真灵的信息
/// </summary>
public class UISpiritSlot : MonoBehaviour
{
    [Header("引用")]
    public Image background;
    public Image swatch;
    public Text label;

    [Tooltip("「已满」那一行小字，只在上阵名额用完后的空格子上显示")]
    public Text lockText;

    public Button button;

    [Tooltip("清空按钮（右上角的小叉），留空则没有下阵功能")]
    public Button clearButton;

    [Tooltip("格子序号 0~8（行优先，左上起）")]
    public int slot;

    [Tooltip("数据源。留空则自动往 Canvas 上找")]
    public UIPanelData data;

    [Tooltip("有待上阵真灵时，空格的高亮色")]
    public Color 空位待放色 = new Color(0.55f, 0.88f, 0.55f, 1f);

    [Tooltip("上阵名额用完后，空格子的底色")]
    public Color 已满色 = new Color(0.45f, 0.42f, 0.42f, 1f);

    [Tooltip("「玩家自己」那一格的底色（九宫格正中间）")]
    public Color 玩家格色 = new Color(0.32f, 0.38f, 0.46f, 1f);

    /// <summary>这一格现在站着的真灵</summary>
    public NpcDefinition Spirit { get; private set; }

    /// <summary>这一格是不是玩家自己站的（九宫格正中间，不能放真灵）</summary>
    public bool 是玩家格 => slot == SpiritFormationLayout.玩家格;

    public void Bind(NpcDefinition spirit, bool pendingHighlight, bool 已满 = false)
    {
        Spirit = spirit;

        if (是玩家格)
        {
            if (label != null)
            {
                label.text = "玩家";
                label.color = new Color(0.92f, 0.94f, 0.98f);
            }
            if (swatch != null) swatch.gameObject.SetActive(false);
            if (lockText != null) { lockText.gameObject.SetActive(false); lockText.text = ""; }
            if (background != null) background.color = 玩家格色;
            if (button != null) button.interactable = false;      // 中间这格点不动
            if (clearButton != null) clearButton.gameObject.SetActive(false);
            return;
        }

        if (button != null) button.interactable = true;

        if (label != null)
        {
            label.text = spirit != null ? spirit.DisplayName : "";
            label.color = spirit != null
                ? UIEntryRow.SpiritColor(spirit)
                : new Color(0.45f, 0.45f, 0.45f);
        }

        if (swatch != null)
        {
            swatch.gameObject.SetActive(spirit != null);
            swatch.color = spirit != null ? UIEntryRow.SpiritColor(spirit) : Color.clear;
        }

        if (lockText != null)
        {
            // 8 格本来都能上阵，只有「名额用完 + 这格还空着」才显示已满
            bool 显示已满 = spirit == null && 已满;
            lockText.gameObject.SetActive(显示已满);
            lockText.text = 显示已满 ? "已满" : "";
        }

        if (background != null)
        {
            if (spirit == null && 已满) background.color = 已满色;
            else if (spirit == null) background.color = pendingHighlight ? 空位待放色 : UIBuildUtils.ColorSlot;
            else background.color = UIBuildUtils.ColorSlot;
        }

        if (clearButton != null) clearButton.gameObject.SetActive(spirit != null);
    }

    /// <summary>取数据源。优先用接好的引用，没有就顺着 Canvas 往上找</summary>
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

    /// <summary>把这一格的真灵撤下来</summary>
    public void Clear()
    {
        var panel = ResolveData();
        if (panel == null) return;
        panel.UnequipSpirit(Spirit);
    }
}
