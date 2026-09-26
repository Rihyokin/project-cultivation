using UnityEngine;

/// <summary>
/// **使用后学会一门功法**。已经学会的→不能用（用户要求："无法重复习得"）。
/// 学会之后如果主角还没有当前功法，会自动把这门设为当前修炼的功法
/// （不然新号学完功法，属性/普攻方法还都是空的）。
/// </summary>
[CreateAssetMenu(fileName = "学功法_", menuName = "修仙/物品效果/学功法", order = 11)]
public class 学功法效果 : 物品使用效果
{
    [Tooltip("使用后要学会的功法")]
    public GongFaDefinition 功法;

    public override bool 能使用(物品使用请求 请求)
        => 功法 != null && 请求.面板 != null && !请求.面板.已学(功法);

    public override string 不能用原因(物品使用请求 请求)
        => 功法 == null ? "效果没配功法" : ("已经学会「" + 功法.DisplayName + "」了");

    public override bool 使用(物品使用请求 请求)
    {
        if (!能使用(请求)) return false;
        if (!请求.面板.学会(功法)) return false;
        if (请求.面板.当前功法 == null) 请求.面板.当前功法 = 功法;
        Debug.Log("[物品] 学会功法「" + 功法.DisplayName + "」", 功法);
        return true;
    }
}
