using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// **运行时外观库**：把生成出来的外观资产收集成一份放到
/// `Assets/resources/外观/外观库.asset`（生成的资产不在 Resources 下，运行时读不到）。
/// 和对话库 / 任务库同一套做法。
/// </summary>
[CreateAssetMenu(fileName = "外观库", menuName = "修仙/外观库", order = 31)]
public class AppearanceDatabase : ScriptableObject
{
    [Tooltip("由「修仙/外观/收集外观资产」自动填充")]
    public List<AppearanceDefinition> 全部 = new List<AppearanceDefinition>();

    static AppearanceDatabase 缓存;

    public static AppearanceDatabase 取()
    {
        if (缓存 == null) 缓存 = Resources.Load<AppearanceDatabase>("外观/外观库");
        return 缓存;
    }

    public static void 清缓存() => 缓存 = null;

    public AppearanceDefinition 取(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        for (int i = 0; i < 全部.Count; i++)
            if (全部[i] != null && 全部[i].id == id) return 全部[i];
        return null;
    }

    /// <summary>开局该穿哪件（默认拥有里的第一件）</summary>
    public AppearanceDefinition 取默认()
    {
        for (int i = 0; i < 全部.Count; i++)
            if (全部[i] != null && 全部[i].默认拥有) return 全部[i];
        return null;
    }
}
