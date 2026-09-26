using UnityEngine;

/// <summary>
/// **使用物品的唯一入口**。背包页的「使用」按钮、任务奖励、NPC 给予都走这里，
/// 这样"能不能用 / 用完扣不扣 / 扣几个"只有一份逻辑。
///
/// 背包的存储方式沿用项目现状：<c>UIPanelData.物品</c> 是 <c>List&lt;ItemDefinition&gt;</c>，
/// **同一件物品有 N 个就是 N 条**（面板/存档都按这个来），所以数量就是数条数。
/// </summary>
public static class 物品使用器
{
    /// <summary>现在能不能用（没配效果、或已经学会过 → 不能）</summary>
    public static bool 可使用(ItemDefinition 物品, UIPanelData 面板)
    {
        if (物品 == null || 面板 == null) return false;
        if (!物品.可使用 || 物品.使用效果 == null) return false;
        return 物品.使用效果.能使用(取请求(物品, 面板));
    }

    /// <summary>不能用时的原因（面板上飘字用）</summary>
    public static string 不能使用原因(ItemDefinition 物品, UIPanelData 面板)
    {
        if (物品 == null) return "没有物品";
        if (!物品.可使用) return "这件物品不能使用";
        if (物品.使用效果 == null) return "这件物品没配使用效果";
        string 原因 = 物品.使用效果.不能用原因(取请求(物品, 面板));
        return string.IsNullOrEmpty(原因) ? "现在不能用" : 原因;
    }

    /// <summary>用一件：效果生效 → 从背包扣掉一个。返回是否真的用掉了</summary>
    public static bool 使用(ItemDefinition 物品, UIPanelData 面板, GameObject 玩家 = null)
    {
        if (物品 == null || 面板 == null) return false;

        if (!可使用(物品, 面板))
        {
            Debug.Log("[物品] 用不了「" + 物品.DisplayName + "」：" + 不能使用原因(物品, 面板), 物品);
            return false;
        }

        var 请求 = new 物品使用请求 { 面板 = 面板, 玩家 = 玩家 != null ? 玩家 : 取玩家物体(), 物品 = 物品 };
        if (!物品.使用效果.使用(请求)) return false;

        面板.移除物品(物品, 1);
        return true;
    }

    static 物品使用请求 取请求(ItemDefinition 物品, UIPanelData 面板)
        => new 物品使用请求 { 面板 = 面板, 玩家 = 取玩家物体(), 物品 = 物品 };

    static GameObject 缓存玩家;

    /// <summary>场景里的玩家（挂 PlayerVitals 的那个）</summary>
    public static GameObject 取玩家物体()
    {
        if (缓存玩家 != null) return 缓存玩家;
        var v = Object.FindObjectOfType<PlayerVitals>();
        if (v != null) 缓存玩家 = v.gameObject;
        return 缓存玩家;
    }
}
