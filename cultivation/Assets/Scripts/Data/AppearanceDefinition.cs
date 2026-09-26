using UnityEngine;

/// <summary>
/// **一种外观** —— 玩家可以持有 / 装备的"整个人"。
///
/// 设计（用户 2026-09-27）：
///   · 外观 = **整套 `Player_Visual`**（模型 + 骨骼 + Animator），不是只换一张网格 ——
///     因为要换的正是「成男村民 ↔ 修仙者」这种完全不同的模型。
///   · 主线没踏入仙途之前用「村中少年（= 成男村民模型）」，主线进宗门之后才换成现在的玩家模型。
///     这个切换**不需要任务系统写任何代码**：任务完成时写的标记 = 这里的 <see cref="解锁标记"/>。
///
/// 表：`Assets/Data/Tables/外观表.csv` → `修仙/从配置表生成资产` → 本类型的资产。
/// 模型用**资源路径**给（`Assets/resources/` 下不带扩展名），因为 CSV 里没法直接引用预制体。
/// </summary>
[CreateAssetMenu(fileName = "外观_", menuName = "修仙/外观", order = 30)]
public class AppearanceDefinition : ScriptableObject
{
    [Header("身份")]
    public string id = "";

    [Tooltip("列表里显示的名字")]
    public string 外观名 = "";

    [Header("模型")]
    [Tooltip("模型所在资源路径（`Assets/resources/` 下、不带扩展名）。\n" +
             "例：`NPC/Human/成男村民/成男村民`、`外观/修仙者`。\n" +
             "⚠️ 只要那底下同目录还有同名 FBX，就必须用 Resources.LoadAll 挑 prefab（项目里踩过这个坑）")]
    public string 模型资源路径 = "";

    [Tooltip("也可以直接填预制体（填了就优先用它，忽略上面的路径）")]
    public GameObject 模型预制体;

    [Header("UI")]
    public Sprite 图标;

    [TextArea(2, 4)]
    public string 介绍 = "";

    [Header("拥有 / 解锁")]
    [Tooltip("勾上 = 开局就拥有并装备（村中少年就是靠它做默认外观）")]
    public bool 默认拥有 = false;

    [Tooltip("拥有它需要具备的标记（分号分隔，全部满足才算解锁）。\n" +
             "任务完成时写的标记填在这里 —— 主线走到「进入宗门」就会自动解锁并换上")]
    public string 解锁标记 = "";

    [Tooltip("解锁后是否立刻换上（默认外观 / 剧情给的外观一般勾上）")]
    public bool 解锁即装备 = true;

    /// <summary>现在解锁了吗（标记齐了 / 或者本来就是默认拥有）</summary>
    public bool 已解锁
    {
        get
        {
            if (默认拥有) return true;
            var 需要 = DialogueDefinition.拆标记(解锁标记);
            if (需要.Length == 0) return false;
            for (int i = 0; i < 需要.Length; i++)
                if (!对话标记.具备(需要[i])) return false;
            return true;
        }
    }

    /// <summary>
    /// 取模型预制体：优先用直接填的那个，否则按 <see cref="模型资源路径"/> 在 Resources 里找。
    ///
    /// **必须用 LoadAll 再挑 prefab**：项目里 `Assets/resources/NPC/...` 下同时放着同名 FBX，
    /// 直接 `Resources.Load&lt;GameObject&gt;(路径)` 会拿到 FBX 而不是 prefab（NpcPrefabs 的注释里有完整教训）。
    /// </summary>
    public GameObject 取模型()
    {
        if (模型预制体 != null) return 模型预制体;
        if (string.IsNullOrEmpty(模型资源路径)) return null;

        var 全部 = Resources.LoadAll<GameObject>(模型资源路径);
        for (int i = 0; i < 全部.Length; i++)
        {
            var go = 全部[i];
            if (go == null) continue;
            if (go.GetComponent<Animator>() != null || go.GetComponentInChildren<Animator>(true) != null) return go;
        }
        for (int i = 0; i < 全部.Length; i++) if (全部[i] != null) return 全部[i];

        Debug.LogWarning("[外观] 资源路径里找不到模型：" + 模型资源路径, this);
        return null;
    }

    /// <summary>列表里显示的名字</summary>
    public string DisplayName => string.IsNullOrEmpty(外观名) ? name : 外观名;

    void OnValidate()
    {
        if (string.IsNullOrEmpty(id)) id = name;
    }
}
