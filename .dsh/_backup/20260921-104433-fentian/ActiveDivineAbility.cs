using UnityEngine;

/// <summary>
/// 主动神通。需要手动释放，占用主动技能装备位。
/// 对应 lore/主动神通父类.txt。
/// </summary>
[CreateAssetMenu(fileName = "ActiveAbility_", menuName = "修仙/主动神通父类", order = 2)]
public class ActiveDivineAbility : DivineAbilityDefinition
{
    public override bool IsActive => true;

    void OnValidate()
    {
        ValidateCommon();
    }
}
