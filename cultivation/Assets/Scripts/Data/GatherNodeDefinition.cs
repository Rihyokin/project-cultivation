using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 采集物父类。对应 lore/采集物父类设定.txt：
/// 所有场景中可破坏的采集物使用此父类。
/// </summary>
[CreateAssetMenu(fileName = "GatherNode_", menuName = "修仙/采集物父类", order = 7)]
public class GatherNodeDefinition : ScriptableObject, IPanelEntry
{
    [Header("lore 字段")]
    [Tooltip("名字")]
    public string 名字 = "新采集物";

    [Tooltip("id，全局唯一")]
    public string id = "";

    [Tooltip("掉落物。概率字段只在这里生效")]
    public List<ItemStack> 掉落物 = new List<ItemStack>();

    [Tooltip("耐久。被打掉多少点后破坏")]
    public float 耐久 = 10f;

    [Tooltip("限制破坏等级：低于该等级的采集能力无法破坏")]
    public int 限制破坏等级 = 0;

    [Tooltip("重生时间（秒）。0 或负数表示不重生")]
    public float 重生时间 = 60f;

    [Tooltip("生成位置（世界坐标）。仅作为摆放参考，运行时以场景实例位置为准")]
    public Vector3 生成位置 = Vector3.zero;

    [Header("UI 用")]
    public Sprite 图标;
    [TextArea(2, 6)]
    public string 介绍 = "";

    [Tooltip("品阶，用于列表着色/排序")]
    public QualityTier 品阶 = QualityTier.凡品;

    // ---- IPanelEntry ----
    public string DisplayName => string.IsNullOrEmpty(名字) ? name : 名字;
    public string DisplayDescription => 介绍;
    public QualityTier DisplayTier => 品阶;
    public Sprite DisplayIcon => 图标;

    void OnValidate()
    {
        if (string.IsNullOrEmpty(id)) id = name;
        if (耐久 < 0f) 耐久 = 0f;
    }
}
