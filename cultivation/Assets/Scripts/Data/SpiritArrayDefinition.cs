using UnityEngine;

/// <summary>灵阵的目标筛选方式</summary>
public enum ArrayTargetFilter
{
    敌方 = 0,
    友方 = 1,
    全体 = 2,
    仅自身 = 3,
    条件筛选 = 4,
}

/// <summary>灵阵的触发条件</summary>
public enum ArrayTriggerCondition
{
    手动触发 = 0,
    进入范围即触发 = 1,
    定时触发 = 2,
    受击触发 = 3,
    血量阈值触发 = 4,
}

/// <summary>
/// 灵阵父类。对应 lore/灵阵父类.txt。
/// </summary>
[CreateAssetMenu(fileName = "SpiritArray_", menuName = "修仙/灵阵父类", order = 6)]
public class SpiritArrayDefinition : ScriptableObject, IPanelEntry
{
    [Header("lore 字段 · 基本信息")]
    [Tooltip("灵阵名称")]
    public string 灵阵名称 = "新灵阵";

    [Tooltip("灵阵id，全局唯一")]
    public string 灵阵id = "";

    [Tooltip("灵阵品阶")]
    public QualityTier 灵阵品阶 = QualityTier.凡品;

    [Header("lore 字段 · 生效参数")]
    [Tooltip("布置时间（秒）")]
    public float 布置时间 = 0f;

    [Tooltip("持续时间（秒）。0 或负数视为永久")]
    public float 持续时间 = 10f;

    [Tooltip("有效范围（半径，米）")]
    public float 有效范围 = 5f;

    [Tooltip("阵眼数量")]
    public int 阵眼数量 = 1;

    [Tooltip("触发条件")]
    public ArrayTriggerCondition 触发条件 = ArrayTriggerCondition.手动触发;

    [Tooltip("目标筛选")]
    public ArrayTargetFilter 目标筛选 = ArrayTargetFilter.敌方;

    [Tooltip("阵体耐久")]
    public float 阵体耐久 = 100f;

    [Header("lore 字段 · 消耗")]
    [Tooltip("布置消耗灵气")]
    public float 布置消耗灵气 = 0f;

    [Tooltip("维持消耗灵气（每秒）")]
    public float 维持消耗灵气 = 0f;

    [Header("lore 字段 · 规则")]
    [Tooltip("是否可叠加")]
    public bool 是否可叠加 = false;

    [Tooltip("是否可移动")]
    public bool 是否可移动 = false;

    [Tooltip("判定优先级，数值越大越优先")]
    public int 判定优先级 = 0;

    [Tooltip("是否可驱散")]
    public bool 是否可驱散 = true;

    [Tooltip("最大生效目标数量")]
    public int 最大生效目标数量 = 1;

    [Tooltip("布置冷却时间（秒）")]
    public float 布置冷却时间 = 0f;

    [Header("UI 用")]
    public Sprite 图标;
    [TextArea(2, 8)]
    public string 介绍 = "";

    // ---- IPanelEntry ----
    public string DisplayName => string.IsNullOrEmpty(灵阵名称) ? name : 灵阵名称;
    public string DisplayDescription => 介绍;
    public QualityTier DisplayTier => 灵阵品阶;
    public Sprite DisplayIcon => 图标;

    void OnValidate()
    {
        if (string.IsNullOrEmpty(灵阵id)) 灵阵id = name;
        if (阵眼数量 < 1) 阵眼数量 = 1;
        if (最大生效目标数量 < 1) 最大生效目标数量 = 1;
        if (有效范围 < 0.1f) 有效范围 = 0.1f;
    }
}
