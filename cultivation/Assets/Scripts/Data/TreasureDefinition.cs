using UnityEngine;

/// <summary>
/// 法宝父类。
///
/// ⚠ lore/法宝父类.txt 目前只有「待开发」三个字，尚无字段定义。
/// 这里先给出「法宝 UI 页」能跑起来所需的最小字段（名称/id/品阶/图标/介绍），
/// 等 lore 补全后再往这里加真正的战斗与养成字段。
/// </summary>
[CreateAssetMenu(fileName = "Treasure_", menuName = "修仙/法宝父类（待开发）", order = 8)]
public class TreasureDefinition : ScriptableObject, IPanelEntry
{
    [Header("占位字段（lore 待补全）")]
    [Tooltip("法宝名称")]
    public string 法宝名称 = "新法宝";

    [Tooltip("法宝id，全局唯一")]
    public string 法宝id = "";

    [Tooltip("法宝品阶")]
    public QualityTier 品阶 = QualityTier.凡品;

    [Header("UI 用")]
    public Sprite 图标;

    [TextArea(2, 8)]
    public string 介绍 = "";

    // ---- IPanelEntry ----
    public string DisplayName => string.IsNullOrEmpty(法宝名称) ? name : 法宝名称;
    public string DisplayDescription => 介绍;
    public QualityTier DisplayTier => 品阶;
    public Sprite DisplayIcon => 图标;

    void OnValidate()
    {
        if (string.IsNullOrEmpty(法宝id)) 法宝id = name;
    }
}
