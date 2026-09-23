using UnityEngine;

/// <summary>
/// 物品父类。对应 lore/物品父类设定.txt：
/// 所有可以被塞进背包的物品都使用此父类。
/// </summary>
[CreateAssetMenu(fileName = "Item_", menuName = "修仙/物品父类", order = 0)]
public class ItemDefinition : ScriptableObject, IPanelEntry
{
    [Header("lore 字段")]
    [Tooltip("物品名")]
    public string 物品名 = "新物品";

    [Tooltip("物品id，全局唯一")]
    public string 物品id = "";

    [Tooltip("最大堆叠数量。1 表示不可堆叠")]
    [Min(1)]
    public int 最大堆叠数量 = 1;

    [Header("UI 用")]
    [Tooltip("图标，对应「选中物品图片」栏")]
    public Sprite 图标;

    [TextArea(2, 6)]
    [Tooltip("介绍文本，对应「选中物品介绍」栏")]
    public string 介绍 = "";

    [Tooltip("品阶，用于列表着色/排序")]
    public QualityTier 品阶 = QualityTier.凡品;

    // ---- IPanelEntry ----
    public string DisplayName => string.IsNullOrEmpty(物品名) ? name : 物品名;
    public string DisplayDescription => 介绍;
    public QualityTier DisplayTier => 品阶;
    public Sprite DisplayIcon => 图标;

    void OnValidate()
    {
        if (string.IsNullOrEmpty(物品id)) 物品id = name;
        if (最大堆叠数量 < 1) 最大堆叠数量 = 1;
    }
}
