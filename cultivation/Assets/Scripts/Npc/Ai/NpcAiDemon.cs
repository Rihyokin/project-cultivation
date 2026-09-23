using UnityEngine;

/// <summary>
/// 妖魔（demon）。
///
/// 特点：
///   · 初始好感度就低于 -10 → **一进索敌范围就锁定玩家、主动上前打**
///   · 动画齐全（Attack1~3 / Death / Wound / Idle / Walk / Run），
///     个别还有 Deffence / Dodge
///   · 死亡播 Death，然后延迟销毁
///
/// 出手分工（策划约定）：Attack1 走普通攻击，Attack2 / Attack3 走主动神通。
/// </summary>
public class NpcAiDemon : NpcAiCombatant
{
    protected override void 取默认参数()
    {
        base.取默认参数();

        索敌范围 = 14f;
        脱战范围 = 22f;
        攻击距离 = 3.2f;      // 中心距：要盖过双方碰撞体半径，不然会卡在跟前打不着
        攻击间隔倍率 = 1.3f;
        奔跑倍率 = 1f;
        行走倍率 = 0.5f;
        敌对好感阈值 = -10f;
        死亡后销毁延迟 = 4f;
    }
}
