using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 「角色属性列举」列表。把 <see cref="AttributeSet"/> 里的 27 项属性渲染成「名称 —— 数值」两列。
/// </summary>
public class UIAttributeList : MonoBehaviour
{
    [Header("引用")]
    [Tooltip("行容器，需挂 VerticalLayoutGroup")]
    public RectTransform container;

    [Tooltip("中文字体")]
    public Font font;

    [Tooltip("数据源")]
    public PlayerStatsDefinition source;

    [Header("显示选项")]
    [Tooltip("勾选后隐藏数值为 0 的属性，列表更干净")]
    public bool hideZero = true;

    [Tooltip("行高")]
    public float rowHeight = 26f;

    [Tooltip("字号")]
    public int fontSize = 18;

    readonly List<GameObject> spawned = new List<GameObject>();

    void Start()
    {
        Rebuild();
    }

    /// <summary>按当前数据重建列表</summary>
    public void Rebuild()
    {
        if (container == null || font == null) return;

        // 编辑态必须用 DestroyImmediate，否则会报 "Destroy may not be called from edit mode"
        foreach (var go in spawned)
        {
            if (go == null) continue;
            if (Application.isPlaying) Destroy(go); else DestroyImmediate(go);
        }
        spawned.Clear();

        // 境界 / 神识 / 灵根 / 吐纳速度 是 lore 里独立于 26 项战斗属性的字段，先列出来
        if (source != null)
        {
            AddRow("境界", source.境界 != null ? source.境界.境界名 : "未设定");
            AddRow("神识", source.神识.ToString("0.#"));
            AddRow("灵根", FormatRoot(source.灵根));
            AddRow("吐纳速度", source.吐纳速度.ToString("0.#") + " /秒");
        }

        var attrs = source != null ? source.基础属性 : null;

        for (int i = 0; i < AttributeUtil.Count; i++)
        {
            var type = (AttributeType)i;
            float value = attrs != null ? attrs[type] : 0f;
            if (hideZero && Mathf.Approximately(value, 0f)) continue;
            AddRow(AttributeUtil.GetDisplayName(type), AttributeUtil.Format(type, value));
        }
    }

    void AddRow(string label, string value)
    {
        var row = UIBuildUtils.CreateRect("Row_" + label, container);
        var le = row.gameObject.AddComponent<LayoutElement>();
        le.minHeight = rowHeight;
        le.preferredHeight = rowHeight;

        var nameText = UIBuildUtils.CreateText("Name", row, font, label, fontSize, TextAnchor.MiddleLeft, UIBuildUtils.ColorText);
        nameText.rectTransform.anchorMin = new Vector2(0f, 0f);
        nameText.rectTransform.anchorMax = new Vector2(0.6f, 1f);
        nameText.rectTransform.offsetMin = new Vector2(4f, 0f);
        nameText.rectTransform.offsetMax = Vector2.zero;

        var valueText = UIBuildUtils.CreateText("Value", row, font, value, fontSize, TextAnchor.MiddleRight, UIBuildUtils.ColorText);
        valueText.rectTransform.anchorMin = new Vector2(0.6f, 0f);
        valueText.rectTransform.anchorMax = new Vector2(1f, 1f);
        valueText.rectTransform.offsetMin = Vector2.zero;
        valueText.rectTransform.offsetMax = new Vector2(-4f, 0f);

        spawned.Add(row.gameObject);
    }

    static string FormatRoot(SpiritualRoot root)
    {
        if (root == SpiritualRoot.None) return "无";
        if (root == SpiritualRoot.混沌) return "五行混沌灵根";

        var sb = new System.Text.StringBuilder();
        if ((root & SpiritualRoot.金) != 0) sb.Append('金');
        if ((root & SpiritualRoot.木) != 0) sb.Append('木');
        if ((root & SpiritualRoot.水) != 0) sb.Append('水');
        if ((root & SpiritualRoot.火) != 0) sb.Append('火');
        if ((root & SpiritualRoot.土) != 0) sb.Append('土');
        int count = sb.Length;
        sb.Append(count == 1 ? "单灵根" : count == 2 ? "双灵根" : count + "灵根");
        return sb.ToString();
    }
}
