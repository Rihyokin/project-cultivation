using UnityEngine;

/// <summary>
/// 功法父类。对应 lore/功法父类.txt：所有的功法使用此父类。
/// </summary>
[CreateAssetMenu(fileName = "GongFa_", menuName = "修仙/功法父类", order = 1)]
public class GongFaDefinition : ScriptableObject, IPanelEntry
{
    [Header("lore 字段 · 基本信息")]
    [Tooltip("功法名称")]
    public string 功法名称 = "新功法";

    [Tooltip("功法id，全局唯一")]
    public string 功法id = "";

    [Tooltip("功法品阶")]
    public QualityTier 功法品阶 = QualityTier.凡品;

    [Header("lore 字段 · 修炼条件")]
    [Tooltip("功法修炼门槛：可开始修炼的最低境界序号")]
    public int 修炼门槛 = 0;

    [Tooltip("功法最高可修炼境界：可修炼到的最高境界序号")]
    public int 最高可修炼境界 = 9;

    [Tooltip("功法难度等级：每提升一级境界所需的吐纳经验")]
    [Min(1)]
    public long 难度等级 = 100;

    [Header("lore 字段 · 普攻")]
    [Tooltip("功法提供的普攻方法id。指向具体的普攻实现（技能表/方法名）")]
    public string 普攻方法id = "";

    [Header("lore 字段 · 每次升级提供的增益")]
    [Tooltip("功法每升一级，玩家获得的属性增益。26 项，与玩家基本属性一一对应")]
    public AttributeSet 每级增益 = new AttributeSet();

    [Header("UI 用")]
    public Sprite 图标;
    [TextArea(2, 8)]
    public string 介绍 = "";

    /// <summary>按等级累加的总增益</summary>
    public AttributeSet GetTotalBonus(int level)
    {
        return 每级增益 != null ? 每级增益.ScaledBy(Mathf.Max(0, level)) : new AttributeSet();
    }

    // ---- IPanelEntry ----
    public string DisplayName => string.IsNullOrEmpty(功法名称) ? name : 功法名称;
    public string DisplayDescription => 介绍;
    public QualityTier DisplayTier => 功法品阶;
    public Sprite DisplayIcon => 图标;

    void OnValidate()
    {
        if (string.IsNullOrEmpty(功法id)) 功法id = name;
        if (难度等级 < 1) 难度等级 = 1;
        if (最高可修炼境界 < 修炼门槛) 最高可修炼境界 = 修炼门槛;
    }
}
