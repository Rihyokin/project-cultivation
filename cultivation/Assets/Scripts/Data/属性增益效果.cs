using UnityEngine;

/// <summary>
/// **通用的「用一段时间属性增益」效果**（回春丹就是它：1 小时内气血回复 +5%）。
///
/// 增益加在 <see cref="玩家临时增益"/> 上，最终由 <c>PlayerCombatStats.Recalculate</c> 汇总进属性，
/// 所以**不会去改任何数据资产**（改资产会把改动写进 .asset，污染配置表）。
/// </summary>
[CreateAssetMenu(fileName = "增益_", menuName = "修仙/物品效果/属性增益", order = 14)]
public class 属性增益效果 : 物品使用效果
{
    [Tooltip("增益哪一项属性")]
    public AttributeType 属性 = AttributeType.HealthRegen;

    [Tooltip("按「加完固定值之后」的量的百分比加成。0.05 = +5%")]
    public float 百分比 = 0.05f;

    [Tooltip("固定值加成（一般不用，留 0）")]
    public float 固定值 = 0f;

    [Tooltip("持续秒数。3600 = 1 小时")]
    public float 持续秒 = 3600f;

    public override bool 使用(物品使用请求 请求)
    {
        if (请求.玩家 == null) return false;

        var 增益 = 请求.玩家.GetComponent<玩家临时增益>();
        if (增益 == null) 增益 = 请求.玩家.AddComponent<玩家临时增益>();

        string 名字 = string.IsNullOrEmpty(请求.物品名) ? name : 请求.物品名;
        增益.加(名字, 属性, 固定值, 百分比, 持续秒);
        Debug.Log("[物品] 「" + 名字 + "」生效：" + 属性 + " +" + (百分比 * 100f).ToString("0.#") + "% 固定+" + 固定值
            + "，持续 " + 持续秒.ToString("0") + " 秒");
        return true;
    }
}
