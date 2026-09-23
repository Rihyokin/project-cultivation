using UnityEngine;

/// <summary>
/// 人类（human）。
///
/// 特点：
///   · 平时好感度在 10 以上 —— **不主动打玩家**，可以对话、触发剧情
///   · 好感被玩家削减到 10 以下 → 翻脸，把玩家当锁定目标开始打
///   · 动画配置和妖魔类似，但**部分 Attack 是施法动作**：把那几条的
///     <see cref="NpcAttackConfig.是施法"/> 勾上、再填 <see cref="NpcAttackConfig.子弹特效路径"/>，
///     就会走飞行道具（子弹资产导入后接上即可）
/// </summary>
public class NpcAiHuman : NpcAiCombatant
{
    [Header("人类专属")]
    [Tooltip("勾上：好感被拉回阈值以上就收手。\n" +
             "去掉勾：一旦翻脸就一直记仇，直到玩家跑出脱战范围")]
    public bool 好感回升会停手 = true;

    [Tooltip("可对话（后续接对话 / 剧情用）")]
    public bool 可对话 = true;

    /// <summary>玩家现在能不能跟它说话</summary>
    public bool 现在可对话
        => 可对话 && !视玩家为敌 && !已交战 && 玩家生命 != null && !玩家生命.已死亡;

    protected override void 取默认参数()
    {
        base.取默认参数();

        索敌范围 = 12f;
        脱战范围 = 20f;
        攻击距离 = 3.4f;      // 中心距，同上
        攻击间隔倍率 = 1.4f;
        奔跑倍率 = 0.95f;
        行走倍率 = 0.5f;

        // 策划原话：「好感度被玩家削减到 10 以下时开始攻击」
        // → 10 及以上都还算友好，所以阈值取 9（判定用的是「≤ 阈值」）
        敌对好感阈值 = 9f;

        死亡后销毁延迟 = 5f;
    }

    protected override void 决策()
    {
        // 记仇模式：翻脸之后不再看好感，只按脱战范围收手
        if (!好感回升会停手 && 已交战)
        {
            if (玩家已脱战()) { 已交战 = false; 进入状态(NpcAiState.待机); return; }
            if (玩家生命 == null || 玩家生命.已死亡) { 进入状态(NpcAiState.待机); return; }

            if (可出手()) { 进入攻击(); return; }
            if (到玩家距离 <= 攻击距离) { 进入状态(NpcAiState.待机); 朝向玩家(); return; }
            进入状态(NpcAiState.接近);
            return;
        }

        base.决策();
    }

    protected override void 执行待机()
    {
        base.执行待机();

        // 友好状态下，玩家走近会转过来看着（为后续对话做铺垫）
        if (!视玩家为敌 && 玩家生命 != null && 到玩家距离 <= 有效索敌范围)
            朝向玩家();
    }

    // ---- ASCII 别名 ----
    public bool CanTalk => 现在可对话;
    public bool HoldsGrudge => !好感回升会停手;
}
