using UnityEngine;

/// <summary>
/// **使用后获得一件外观**（用户 2026-09-27 定的流程）。
///
/// 例：主线走到「进入宗门」那一步，任务奖励发一个道具 **门派便服**；
/// 门派便服是可使用物品，使用效果就是本类（指向 `修仙者` 那件外观）——
/// 玩家用了它才拥有并换上那套模型。**不靠标记解锁外观**。
///
/// 和学功法/神通那几个效果是同构的：已经有的不能再吃（不可重复获得），
/// 用完消耗一个道具（由 <see cref="物品使用器"/> 负责扣）。
/// </summary>
[CreateAssetMenu(fileName = "学外观_", menuName = "修仙/物品效果/学外观", order = 15)]
public class 学外观效果 : 物品使用效果
{
    [Tooltip("使用后要获得的外观")]
    public AppearanceDefinition 外观;

    [Tooltip("获得后立刻换上（一般勾上：换上新衣服当然要穿上）")]
    public bool 获得即装备 = true;

    public override bool 能使用(物品使用请求 请求)
        => 外观 != null && 取外观组件(请求) != null && !取外观组件(请求).已拥有(外观);

    public override string 不能用原因(物品使用请求 请求)
    {
        if (外观 == null) return "效果没配外观";
        var 组件 = 取外观组件(请求);
        if (组件 == null) return "玩家身上没有 玩家外观 组件";
        return "已经拥有「" + 外观.DisplayName + "」了";
    }

    public override bool 使用(物品使用请求 请求)
    {
        var 组件 = 取外观组件(请求);
        if (外观 == null || 组件 == null) return false;
        if (组件.已拥有(外观)) return false;          // 不可重复获得

        组件.获得(外观, 获得即装备);
        Debug.Log("[物品] 「" + 请求.物品名 + "」使用后获得外观「" + 外观.DisplayName + "」", 外观);
        return true;      // 消耗掉这个道具
    }

    static 玩家外观 取外观组件(物品使用请求 请求)
    {
        if (请求.玩家 == null) return null;
        return 请求.玩家.GetComponent<玩家外观>();
    }
}
