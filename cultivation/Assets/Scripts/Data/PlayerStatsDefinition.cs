using UnityEngine;

/// <summary>
/// 玩家基本属性。对应 lore/玩家基本属性.txt。
/// 做成 ScriptableObject，方便配置「初始属性」并作为运行时属性结算的基准。
/// </summary>
[CreateAssetMenu(fileName = "PlayerStats_", menuName = "修仙/玩家基本属性", order = 4)]
public class PlayerStatsDefinition : ScriptableObject
{
    [Header("lore 字段 · 修炼相关")]
    [Tooltip("当前境界")]
    public RealmDefinition 境界;

    [Tooltip("神识")]
    public float 神识 = 0f;

    [Tooltip("灵根。五行可多选组合")]
    public SpiritualRoot 灵根 = SpiritualRoot.None;

    [Tooltip("吐纳速度：每秒获得的吐纳经验")]
    public float 吐纳速度 = 1f;

    [Header("lore 字段 · 战斗属性（26 项）")]
    [Tooltip("基础战斗属性。lore 中的 气血/攻击/防御/灵力/移动速度/暴击/… 全在这里")]
    public AttributeSet 基础属性 = new AttributeSet();
}
