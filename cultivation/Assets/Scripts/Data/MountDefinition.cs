using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 坐骑。对应 <c>Assets/Data/Tables/坐骑表.csv</c>。
///
/// **表头是照着「被动神通表」来的**（用户要求「表头基本一致」）：
///   · 前几列换成坐骑自己的 —— `坐骑id` / `坐骑名称` / `坐骑品阶` / `修炼门槛`
///     / `模型资源路径` / `待机动作` / `介绍`
///   · 后面 15 列**逐字照抄被动神通表**的属性增益列（`气血增益` … `被动法术伤害减免增益`）
///
/// 列名 = 字段名（导入器按名字直接写字段），属性列由
/// <c>DataTableImporter.TableSpec</c> 的 `AttrField = "增益"` + 后缀 `增益` 自动识别。
/// </summary>
[CreateAssetMenu(fileName = "Mount_", menuName = "修仙/坐骑父类", order = 6)]
public class MountDefinition : ScriptableObject, IPanelEntry
{
    [Header("lore 字段 · 基本信息")]
    [Tooltip("坐骑名称")]
    public string 坐骑名称 = "新坐骑";

    [Tooltip("坐骑id，全局唯一")]
    public string 坐骑id = "";

    [Tooltip("坐骑品阶")]
    public QualityTier 坐骑品阶 = QualityTier.凡品;

    [Tooltip("可乘骑的最低境界序号（1~90）")]
    public int 修炼门槛 = 1;

    [Header("模型")]
    [Tooltip("坐骑 prefab 路径，写相对 Assets/resources 的路径、不带扩展名。\n" +
             "例：NPC/Mount/LangQi_01/LangQi_01")]
    public string 模型资源路径 = "";

    [Tooltip("待机动作名（列表里选中它时，预览区循环播这个动作）。\n" +
             "要和控制器里的**动作名/状态名**一致")]
    public string 待机动作 = "Idle";

    [Header("lore 字段 · 坐骑提供的增益")]
    [Tooltip("骑乘时提供的 27 项属性增益")]
    public AttributeSet 增益 = new AttributeSet();

    [Header("UI 用")]
    public Sprite 图标;
    [TextArea(2, 8)]
    public string 介绍 = "";

    // ---- IPanelEntry ----
    public string DisplayName => string.IsNullOrEmpty(坐骑名称) ? name : 坐骑名称;
    public string DisplayDescription => 介绍;
    public QualityTier DisplayTier => 坐骑品阶;
    public Sprite DisplayIcon => 图标;

    void OnValidate()
    {
        if (string.IsNullOrEmpty(坐骑id)) 坐骑id = name;
    }

    /// <summary>取坐骑预制体（走 <see cref="MountPrefabs.加载"/>，别用 Resources.Load）</summary>
    public GameObject 加载模型() => MountPrefabs.加载(模型资源路径);
}

/// <summary>
/// **按 <see cref="MountDefinition.模型资源路径"/> 加载坐骑预制体。**
///
/// 和 <see cref="NpcPrefabs"/> 是同一个坑的两种解法：`Resources.Load` 会挑到
/// **和 prefab 同名的 .FBX**（坐骑目录里 `LangQi_01/LangQi_01.FBX` 和
/// `LangQi_01.prefab` 就是这种摆法）。
///
/// 但坐骑**不能**用 `NpcInstance` 来判断 —— 坐骑 prefab 上没有这个组件。
/// 实测：**FBX 的根节点上没有 `Animator`**（三个新导的坐骑 FBX 都是 `Animator: 无`），
/// 而组装好的坐骑 prefab 一定有 `Animator` + `NpcAnimator`，所以：
///
///   1. 优先挑带 <see cref="NpcAnimator"/> 的（组装好的坐骑）
///   2. 其次挑带 `Animator` 的
///   3. 都没有才退回第一个（并警告）
/// </summary>
public static class MountPrefabs
{
    static readonly HashSet<string> 警告过 = new HashSet<string>();

    public static GameObject 加载(string 模型资源路径)
    {
        if (string.IsNullOrEmpty(模型资源路径)) return null;

        var 全部 = Resources.LoadAll<GameObject>(模型资源路径);
        if (全部 == null || 全部.Length == 0) return null;
        if (全部.Length == 1) return 全部[0];

        GameObject 带动画 = null, 带Animator = null;
        foreach (var g in 全部)
        {
            if (g == null) continue;
            if (带动画 == null && g.GetComponent<NpcAnimator>() != null) 带动画 = g;
            if (带Animator == null && g.GetComponent<Animator>() != null) 带Animator = g;
        }
        if (带动画 != null) return 带动画;
        if (带Animator != null) return 带Animator;

        if (警告过.Add(模型资源路径))
            Debug.LogWarning("[坐骑]「" + 模型资源路径 + "」下找到 " + 全部.Length
                             + " 个资产，但都没有 Animator —— 多半是路径指到了模型文件（.FBX）而不是 prefab。");
        return 全部[0];
    }
}
