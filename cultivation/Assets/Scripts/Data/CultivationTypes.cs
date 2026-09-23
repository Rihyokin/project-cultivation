using System;
using UnityEngine;

/// <summary>九大境界（序号 1~9，对应修为等级 1~90 每 10 级一段）</summary>
public enum RealmTier
{
    未指定 = 0,
    炼气 = 1, 筑基 = 2, 金丹 = 3, 元婴 = 4, 化神 = 5,
    炼虚 = 6, 合体 = 7, 大乘 = 8, 登仙 = 9,
}

/// <summary>突破类型</summary>
public enum BreakthroughKind
{
    未指定 = 0,
    小境界突破 = 1,     // 层内 1→2 … 9→10：消耗破境丹 + 满灵气
    大境界突破 = 2,     // 10 层 → 下一境界 1 层：消耗特殊突破材料 + 满灵气
    满级 = 3,          // 90 级封顶
}

/// <summary>
/// 品阶。lore 里功法/神通/灵阵都带「品阶」字段，但未指定具体分档；
/// 这里按修仙题材常见分法给出，后续可在 Inspector 里自由选。
/// </summary>
public enum QualityTier
{
    凡品 = 0,
    黄品 = 1,
    玄品 = 2,
    地品 = 3,
    天品 = 4,
    仙品 = 5,
    神品 = 6,
}

/// <summary>
/// 灵根。五行灵根，可组合（如「水火双灵根」）。lore 里「灵根」是玩家的独立字段。
/// </summary>
[Flags]
public enum SpiritualRoot
{
    None = 0,
    金 = 1 << 0,
    木 = 1 << 1,
    水 = 1 << 2,
    火 = 1 << 3,
    土 = 1 << 4,
    /// <summary>五行俱全</summary>
    混沌 = 金 | 木 | 水 | 火 | 土,
}

/// <summary>
/// 境界定义。UI 的「境界展示」需要：境界名 + 升到下一境界所需经验。
/// lore 未给出具体境界表，这里做成 ScriptableObject 以便你自行填写。
/// </summary>
[CreateAssetMenu(fileName = "Realm_", menuName = "修仙/境界定义", order = 10)]
public class RealmDefinition : ScriptableObject
{
    [Header("基本信息")]
    [Tooltip("境界名，例如「炼气一层」")]
    public string 境界名 = "炼气一层";

    [Header("修为等级体系（1~90）")]
    [Tooltip("修为等级 1~90。等级 = (大境界-1)*10 + 层数")]
    public int 等级 = 1;

    [Tooltip("九大境界")]
    public RealmTier 大境界 = RealmTier.炼气;

    [Tooltip("小境界层数 1~10")]
    public int 层数 = 1;

    [Tooltip("升到下一级所需的**基准**灵气（还要 × 功法难度系数 K）")]
    public long 升级所需灵气 = 100;

    [Tooltip("从 1 级累计到这里的基准灵气总和")]
    public long 累计灵气 = 100;

    [Tooltip("单次修炼获得的灵气（只随大境界变，同境界内固定）")]
    public float 单次修炼灵气 = 2f;

    [Tooltip("这一级是哪种突破")]
    public BreakthroughKind 突破类型 = BreakthroughKind.小境界突破;

    [Tooltip("突破所需材料 id（小境界=破境丹，大境界=特殊材料）。留空=暂未定")]
    public string 突破材料 = "";

    [Tooltip("突破所需材料数量")]
    public int 材料数量 = 1;
    [Tooltip("境界序号，从 0 开始。用于排序与判断高低")]
    public int 序号 = 0;

    [Header("升级")]
    [Tooltip("突破到下一境界所需的总吐纳经验")]
    public long 所需总经验 = 100;

    [Tooltip("突破所需的最低功法难度等级")]
    public int 功法难度要求 = 0;

    [Header("突破奖励")]
    [Tooltip("突破到该境界时一次性获得的属性")]
    public AttributeSet 境界加成 = new AttributeSet();
}
