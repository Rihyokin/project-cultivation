using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// NPC 的三大类（外加两个兜底）。
/// 决定运行时自动装配哪套 AI（见 <c>NpcAiBase.确保</c>）。
/// **枚举成员名要和 NPC表.csv 的「类型」列写得一模一样。**
/// </summary>
public enum NpcKind
{
    /// <summary>没填类型：不装 AI，站着不动（旧资产 / 临时对象）</summary>
    未指定 = 0,

    /// <summary>野兽：只有 idle / walk 之类。被打掉好感，好感高会跟着玩家，好感低会躲</summary>
    兽类 = 1,

    /// <summary>妖魔：初始就敌视玩家，进索敌范围就主动上来打</summary>
    妖魔 = 2,

    /// <summary>人类：可对话、能触发剧情。好感掉到阈值以下才翻脸动手</summary>
    人类 = 3,

    /// <summary>中立：村民 / 设施 NPC / 练功木桩。能被选中锁定挨打，但自己不会动</summary>
    中立 = 4,
}

/// <summary>
/// NPC 父类。对应 lore/npc父类设定.txt：
/// 所有的非玩家生物套用此父类（包括怪物/木桩）。
/// </summary>
[CreateAssetMenu(fileName = "Npc_", menuName = "修仙/NPC父类", order = 5)]
public class NpcDefinition : ScriptableObject, IPanelEntry
{
    [Header("lore 字段 · 基本信息")]
    [Tooltip("名字")]
    public string 名字 = "新NPC";

    [Tooltip("id，全局唯一")]
    public string id = "";

    [Tooltip("背包物品（击杀/交易掉落）")]
    public List<ItemStack> 背包物品 = new List<ItemStack>();

    [Tooltip("对主角好感度（**这是初始值**。运行时会掉好感，掉的是实例上的副本，不回写这里）")]
    public float 对主角好感度 = 0f;

    [Tooltip("NPC 类型：决定运行时装配哪套 AI")]
    public NpcKind 类型 = NpcKind.未指定;

    [Header("lore 字段 · 属性")]
    [Tooltip("境界 = 修为等级（1~90）。杀怪掉修炼次数按「怪物等级 − 玩家等级」算系数")]
    public int 境界 = 0;

    [Tooltip("神识")]
    public float 神识 = 0f;

    [Tooltip("战斗属性：气血/攻击/防御/灵力/移动速度/暴击/… 共 26 项")]
    public AttributeSet 属性 = new AttributeSet();

    [Header("行为")]
    [Tooltip("AI 索敌范围（米）。0 = 用该类型自己的默认值")]
    public float 索敌范围 = 0f;

    [Tooltip("勾选后：气血归零的瞬间立刻恢复满血（练功木桩这类沙包用）")]
    public bool 死亡后立即重生 = false;

    [Tooltip("重生延迟（秒）。填 0 表示归零当帧就满血")]
    [Min(0f)]
    public float 重生延迟 = 0f;

    [Header("战阵真灵")]
    [Tooltip("当战阵真灵时用哪个模型 prefab。写相对 Assets/resources 的路径，不带扩展名。\n" +
             "例：NPC/Demon/BaiLuJing_01/BaiLuJing_01\n" +
             "**留空 = 这个 NPC 不能上战阵**。目前只有「妖魔 / 人类」能当真灵。")]
    public string 模型资源路径 = "";

    [Header("UI 用")]
    public Sprite 图标;
    [TextArea(2, 6)]
    public string 介绍 = "";

    // ---- IPanelEntry ----
    public string DisplayName => string.IsNullOrEmpty(名字) ? name : 名字;
    public string DisplayDescription => 介绍;
    public QualityTier DisplayTier => QualityTier.凡品;
    public Sprite DisplayIcon => 图标;

    void OnValidate()
    {
        if (string.IsNullOrEmpty(id)) id = name;
    }
}
