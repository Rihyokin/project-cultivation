using System;
using UnityEngine;

/// <summary>
/// 一组「物品 + 数量」。背包物品栏、采集物掉落物都用它。
/// </summary>
[Serializable]
public struct ItemStack
{
    [Tooltip("物品定义")]
    public ItemDefinition 物品;

    [Tooltip("数量")]
    [Min(1)]
    public int 数量;

    [Tooltip("掉落概率（0~1）。背包内固定为 1，只有采集掉落会用到")]
    [Range(0f, 1f)]
    public float 概率;

    public bool IsValid => 物品 != null && 数量 > 0;

    public override string ToString()
    {
        return 物品 != null ? 物品.DisplayName + " ×" + 数量 : "（空）";
    }
}
