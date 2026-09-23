using UnityEngine;

/// <summary>
/// 被动神通。常驻生效，不占主动技能位，提供 26 项属性增益。
/// 对应 lore/被动神通父类.txt。
/// </summary>
[CreateAssetMenu(fileName = "PassiveAbility_", menuName = "修仙/被动神通父类", order = 3)]
public class PassiveDivineAbility : DivineAbilityDefinition
{
    public override bool IsActive => false;

    [Header("lore 字段 · 被动神通提供的增益")]
    [Tooltip("被动神通常驻提供的 26 项属性增益")]
    public AttributeSet 增益 = new AttributeSet();

    void OnValidate()
    {
        ValidateCommon();
    }
}
