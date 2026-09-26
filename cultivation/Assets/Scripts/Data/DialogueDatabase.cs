using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// **运行时对话库**：把 `Assets/Data/Generated/DialogueDefinition/` 里生成出来的对话资产收集成一份，
/// 放到 <c>Assets/resources/对话/对话库.asset</c> —— 因为生成的资产不在 Resources 下，
/// 运行时只能靠这份库来查（这是项目里既有的做法：运行时资源一律走 Resources）。
///
/// 菜单：**修仙/对话系统/收集对话资产**（`修仙/从配置表生成资产` 跑完会自动跟着跑一次）。
/// </summary>
[CreateAssetMenu(fileName = "对话库", menuName = "修仙/对话库", order = 7)]
public class DialogueDatabase : ScriptableObject
{
    [Tooltip("由「修仙/对话系统/收集对话资产」自动填充，不用手拖")]
    public List<DialogueDefinition> 全部 = new List<DialogueDefinition>();

    static DialogueDatabase 缓存;

    /// <summary>取对话库（Resources/对话/对话库）。没有就返回 null，调用方要判空</summary>
    public static DialogueDatabase 取()
    {
        if (缓存 == null) 缓存 = Resources.Load<DialogueDatabase>("对话/对话库");
        return 缓存;
    }

    /// <summary>换了库（重新收集）之后调一下，免得还拿着旧的</summary>
    public static void 清缓存() => 缓存 = null;

    /// <summary>
    /// 某个 NPC 的某一段里，**现在满足条件的**所有候选，按「优先」从大到小排。
    /// npcId 为空的通用段对任何 NPC 都算候选（这样可以写"所有村民都会说的话"）。
    /// </summary>
    public List<DialogueDefinition> 候选(string npcId, int 分段)
    {
        var 结果 = new List<DialogueDefinition>();
        for (int i = 0; i < 全部.Count; i++)
        {
            var d = 全部[i];
            if (d == null || d.分段 != 分段) continue;
            if (!string.IsNullOrEmpty(d.npcId) && d.npcId != npcId) continue;
            if (!对话条件.满足(d)) continue;
            结果.Add(d);
        }
        结果.Sort((a, b) =>
        {
            int c = b.优先.CompareTo(a.优先);
            if (c != 0) return c;
            // ★ 同等优先时：**NPC 专属压过通用**（通用段是"谁都能说"的兜底，
            //   否则村民自己写的第 2 段会被 对话表里 npcId 空的第 2 段顶掉 —— 实测踩过）
            bool a通用 = string.IsNullOrEmpty(a.npcId);
            bool b通用 = string.IsNullOrEmpty(b.npcId);
            if (a通用 != b通用) return a通用 ? 1 : -1;
            // ★ 再比「具体程度」：**带条件的**（任务专属回答）压过无条件的。
            //   实测踩过：任务回答和默认回答都写 优先=10，平手后按 id 排，"dlg_chengnan_01" 比
            //   "dlg_chengnan_01b" 短就赢了 —— 结果任务回答永远出不来。
            bool a有 = !string.IsNullOrWhiteSpace(a.需要标记);
            bool b有 = !string.IsNullOrWhiteSpace(b.需要标记);
            if (a有 != b有) return a有 ? -1 : 1;
            return string.CompareOrdinal(a.id, b.id);   // 保证顺序稳定
        });
        return 结果;
    }

    /// <summary>取这一段该显示哪条（优先大的赢；都没有就 null）</summary>
    public DialogueDefinition 取段(string npcId, int 分段)
    {
        var c = 候选(npcId, 分段);
        return c.Count > 0 ? c[0] : null;
    }

    /// <summary>这个 NPC 最小的分段号（对话入口），没有就 0</summary>
    public int 最小分段(string npcId)
    {
        int m = 0;
        for (int i = 0; i < 全部.Count; i++)
        {
            var d = 全部[i];
            if (d == null) continue;
            if (!string.IsNullOrEmpty(d.npcId) && d.npcId != npcId) continue;
            if (m == 0 || d.分段 < m) m = d.分段;
        }
        return m;
    }

    /// <summary>这个 NPC 最大的分段号（用来判断"还有没有下一段"）</summary>
    public int 最大分段(string npcId)
    {
        int m = 0;
        for (int i = 0; i < 全部.Count; i++)
        {
            var d = 全部[i];
            if (d == null) continue;
            if (!string.IsNullOrEmpty(d.npcId) && d.npcId != npcId) continue;
            if (d.分段 > m) m = d.分段;
        }
        return m;
    }

    /// <summary>某个 NPC 有没有对话（没写对话的 NPC 就不该弹框）</summary>
    public bool 有对话(string npcId) => 最小分段(npcId) > 0;
}
