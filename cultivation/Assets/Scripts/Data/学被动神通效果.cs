using UnityEngine;

/// <summary>**使用后获得一门被动神通**（不可重复习得）。获得即启用（从"已停用"里摘掉）。</summary>
[CreateAssetMenu(fileName = "学被动神通_", menuName = "修仙/物品效果/学被动神通", order = 13)]
public class 学被动神通效果 : 物品使用效果
{
    [Tooltip("使用后要获得的被动神通")]
    public PassiveDivineAbility 神通;

    public override bool 能使用(物品使用请求 请求)
        => 神通 != null && 请求.面板 != null && !请求.面板.已获得被动(神通);

    public override string 不能用原因(物品使用请求 请求)
        => 神通 == null ? "效果没配神通" : ("已经获得「" + 神通.DisplayName + "」了");

    public override bool 使用(物品使用请求 请求)
    {
        if (!能使用(请求)) return false;
        if (!请求.面板.获得被动(神通)) return false;
        Debug.Log("[物品] 获得被动神通「" + 神通.DisplayName + "」", 神通);
        return true;
    }
}
