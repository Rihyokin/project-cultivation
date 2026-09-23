using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// **按 <see cref="NpcDefinition.模型资源路径"/> 加载 NPC 预制体。**
///
/// 【坑·已修·很贵的一个】直接 `Resources.Load&lt;GameObject&gt;(路径)` 会
/// **挑到模型文件（.FBX）而不是预制体** —— 只要同一个 Resources 目录下
/// 放着一个和 prefab **同名的 FBX**，`Resources.Load` 就把那个 FBX 给你。
/// 本工程实测 **240 个真灵里有 17 个** 是这种摆法
/// （`YeZhu/YeZhu.FBX` + `YeZhu/YeZhu.prefab`、`TieJiang/TieJiang.FBX` + …）。
///
/// 拿到 FBX 的后果**非常隐蔽，而且不报任何错**：
///
/// | 少了什么 | 表现 |
/// |---|---|
/// | 根节点上**没有 <see cref="NpcInstance"/>** | `定义` 为空 → `移动速度` 读出 **0** → **一步都走不动** |
/// | **没有 AnimatorController** | `有动作("Attack1")` 永远 false → **一招都放不出来** |
///
/// 合起来就是「真灵站在那儿发呆、也不过来也不打」，而且日志一片干净 ✗
///
/// **修法**：用 <see cref="Resources.LoadAll{T}(string)"/> 把这一个路径下的资产**全取出来**，
/// 挑**带 <see cref="NpcInstance"/> 的那个**（FBX 的根节点上没有这个组件）。
/// 实测 240 个 NPC 全部选对。
///
/// 【注意】`Resources.Load("xxx.prefab")` 带扩展名是**不行**的（实测返回 null），
/// 所以只能靠 LoadAll + 组件判定。
/// </summary>
public static class NpcPrefabs
{
    /// <summary>已经警告过的路径（每个路径只吵一次）</summary>
    static readonly HashSet<string> 警告过 = new HashSet<string>();

    /// <summary>
    /// 取「NPC 预制体」。路径下只有一个资产时直接返回；
    /// 有多个（典型 = 同名 FBX + prefab）时返回**带 <see cref="NpcInstance"/> 的那个**。
    /// </summary>
    public static GameObject 加载(string 模型资源路径)
    {
        if (string.IsNullOrEmpty(模型资源路径)) return null;

        var 全部 = Resources.LoadAll<GameObject>(模型资源路径);
        if (全部 == null || 全部.Length == 0) return null;
        if (全部.Length == 1) return 全部[0];

        GameObject 备选 = null;
        foreach (var g in 全部)
        {
            if (g == null) continue;
            var 实例 = g.GetComponent<NpcInstance>();
            if (实例 == null) continue;              // 模型文件（FBX）走这条

            if (实例.定义 != null) return g;          // ★ 最优：带 NpcInstance 且接了定义
            if (备选 == null) 备选 = g;               // 次优：带 NpcInstance 但定义是空的
        }

        if (备选 != null) return 备选;

        // 一个都不带 NpcInstance → 路径配错了。**不再静默**，说清楚哪儿不对
        if (警告过.Add(模型资源路径))
            Debug.LogWarning("[NPC]「" + 模型资源路径 + "」下找到 " + 全部.Length
                             + " 个资产，但没有一个带 NpcInstance —— "
                             + "多半是「模型资源路径」指到了模型文件 / 空目录。这会把 NPC 变成不会动的木头人。");
        return 全部[0];
    }

    /// <summary>取「NPC 预制体」并检查它是不是真的能当 NPC 用（拿不到就返回 null + 警告）</summary>
    public static GameObject 加载并校验(string 模型资源路径, out string 原因)
    {
        原因 = null;
        var pf = 加载(模型资源路径);
        if (pf == null) { 原因 = "加载不到（路径要相对 Assets/resources，不带扩展名）"; return null; }

        if (pf.GetComponent<NpcInstance>() == null)
        {
            原因 = "这个资产上没有 NpcInstance —— 路径很可能指到了模型文件而不是预制体";
            return null;
        }
        return pf;
    }
}
