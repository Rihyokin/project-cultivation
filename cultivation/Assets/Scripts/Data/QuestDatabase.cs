using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// **运行时任务库**：把 `Assets/Data/Generated/QuestDefinition/` 里生成出来的任务阶段收集成一份，
/// 放到 <c>Assets/resources/任务/任务库.asset</c>（生成资产不在 Resources 下，运行时读不到）。
/// 顺带把 <see cref="ItemDefinition"/> 也收进来，任务奖励就能按物品 id 找了。
///
/// 菜单：**修仙/任务/收集任务资产**（`修仙/从配置表生成资产` 跑完会自动跟着跑一次）。
/// </summary>
[CreateAssetMenu(fileName = "任务库", menuName = "修仙/任务库", order = 21)]
public class QuestDatabase : ScriptableObject
{
    [Tooltip("所有任务阶段，由「修仙/任务/收集任务资产」自动填充")]
    public List<QuestDefinition> 全部 = new List<QuestDefinition>();

    [Tooltip("物品库（任务奖励按 id 查找用）")]
    public List<ItemDefinition> 物品库 = new List<ItemDefinition>();

    static QuestDatabase 缓存;

    public static QuestDatabase 取()
    {
        if (缓存 == null) 缓存 = Resources.Load<QuestDatabase>("任务/任务库");
        return 缓存;
    }

    public static void 清缓存() => 缓存 = null;

    /// <summary>某个任务id 的全部阶段，按阶段号升序</summary>
    public List<QuestDefinition> 取任务(string 任务id)
    {
        var 出 = new List<QuestDefinition>();
        if (string.IsNullOrEmpty(任务id)) return 出;
        for (int i = 0; i < 全部.Count; i++)
        {
            var q = 全部[i];
            if (q != null && q.任务id == 任务id) 出.Add(q);
        }
        出.Sort((a, b) => a.阶段.CompareTo(b.阶段));
        return 出;
    }

    /// <summary>某个任务的第几阶段（没有就 null）</summary>
    public QuestDefinition 取阶段(string 任务id, int 阶段)
    {
        var 全 = 取任务(任务id);
        for (int i = 0; i < 全.Count; i++) if (全[i].阶段 == 阶段) return 全[i];
        return null;
    }

    /// <summary>库里的所有任务id（按第一次出现的顺序去重）</summary>
    public List<string> 全部任务id()
    {
        var 出 = new List<string>();
        for (int i = 0; i < 全部.Count; i++)
        {
            var q = 全部[i];
            if (q == null || string.IsNullOrEmpty(q.任务id)) continue;
            if (!出.Contains(q.任务id)) 出.Add(q.任务id);
        }
        return 出;
    }

    /// <summary>任务名（取第一阶段上写的那个）</summary>
    public string 取任务名(string 任务id)
    {
        var q = 取阶段(任务id, 1);
        return q != null ? q.任务名 : 任务id;
    }

    public 任务类型 取任务类型(string 任务id)
    {
        var q = 取阶段(任务id, 1);
        return q != null ? q.类型 : 任务类型.支线;
    }

    public ItemDefinition 找物品(string 物品id)
    {
        if (string.IsNullOrEmpty(物品id)) return null;
        for (int i = 0; i < 物品库.Count; i++)
            if (物品库[i] != null && 物品库[i].物品id == 物品id) return 物品库[i];
        return null;
    }

    /// <summary>解析奖励串：`物品id:数量|物品id:数量`（数量省略 = 1）。找不到的物品会打日志跳过</summary>
    public List<ItemStack> 解析奖励(string 串)
    {
        var 出 = new List<ItemStack>();
        if (string.IsNullOrWhiteSpace(串)) return 出;

        var 段 = 串.Split('|');
        for (int i = 0; i < 段.Length; i++)
        {
            var s = 段[i].Trim();
            if (s.Length == 0) continue;
            int 数量 = 1;
            var 冒号 = s.Split(':');
            string 物品id = 冒号[0].Trim();
            if (冒号.Length > 1) int.TryParse(冒号[1].Trim(), out 数量);
            if (数量 < 1) 数量 = 1;

            var 物品 = 找物品(物品id);
            if (物品 == null) { Debug.LogWarning("[任务] 奖励里找不到物品 id：" + 物品id); continue; }
            出.Add(new ItemStack { 物品 = 物品, 数量 = 数量, 概率 = 1f });
        }
        return 出;
    }
}
