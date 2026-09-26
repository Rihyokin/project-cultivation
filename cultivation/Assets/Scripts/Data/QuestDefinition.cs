using UnityEngine;

/// <summary>任务分类（用户 2026-09-26：主线 / 支线 / 悬赏）</summary>
public enum 任务类型 { 主线, 支线, 悬赏 }

/// <summary>子任务的完成条件</summary>
public enum 任务条件
{
    无,        // 接了就立刻算完成（用来串"过场"阶段）
    提交物品,  // 背包里有够数量的物品 → 交了就算完成（会扣掉）
    击杀,      // 目标npcId 的那个 NPC 死掉（留空 = 任意 NPC 死一个）
    到达,      // 玩家走到 坐标 半径 以内
    对话,      // 和 NPC 说完某一段对话（对话id 留空 = 这段里任意一段）
    等待秒数   // ★ 主线用：进入这一阶段后**等 N 秒**自动完成（"停留2s""等候60s"都用它）
}

/// <summary>阶段开始时要调度的 NPC 动作</summary>
public enum 任务动作
{
    无,
    播动画,     // 动作参数 = 状态名（如 swagger / Attack1 / Yufeng_Idle）
    攻击玩家,   // 好感度压到敌对 → 自动锁定玩家开打
    销毁,       // 直接销毁目标 NPC
    走向,       // ★ 走到「坐标」（带行走动画；速度见 动作速度）
    飞到,       // ★ 御风飞过去：升空 → 前进（同时位移）→ 到点悬停（前进状态名见 动作参数）
    处决,       // ★ 直接把目标 NPC 打死（三幕大师兄一刀劈野猪：配合 闪白）
    镜头看目标, // ★ 镜头平滑推向目标 NPC（时长见 镜头时长）
    镜头回玩家, // ★ 镜头平滑回到玩家身上
    生成NPC     // ★ 生成一个 NPC：**动作参数 = 预制体资源路径**（如 `NPC/Demon/YeZhu/YeZhu`），位置 = `坐标`
}

/// <summary>
/// **任务的一个子任务（阶段）** —— 一行 = 一个阶段，对应 `Assets/Data/Tables/任务表.csv`。
///
/// 设计（对应用户的要求）：
///   · 同一个 <see cref="任务id"/> 下的阶段按 <see cref="阶段"/> **从小到大顺序推进**，
///     当前阶段完成才会把下一个阶段设为"当前"（"只有当子任务完成时，才会触发下一个"）。
///   · 完成条件有四种：提交材料 / 击杀目标 / 到达某地 / 和 NPC 完成某段对话。
///   · 任务状态用**标记**（<see cref="接取加标记"/> / <see cref="完成加标记"/>）和对话系统打通 ——
///     对话表里的「需要标记 / 排除标记」就能让任务出现新的回答（复用对话系统那套条件）。
///   · 阶段开始时可以调度 NPC：<see cref="动作"/>（播动画 / 攻击玩家 / 销毁）+ 目标 id + 参数。
///   · 完成阶段可以发物品奖励：<see cref="奖励物品"/>，写法同「背包物品」列（`物品id:数量|物品id:数量`）。
/// </summary>
[CreateAssetMenu(fileName = "任务阶段_", menuName = "修仙/任务/阶段", order = 20)]
public class QuestDefinition : ScriptableObject
{
    [Header("归属")]
    public string id = "";              // 这一行的唯一 id
    public string 任务id = "";          // 同一串阶段共用一个任务id
    public string 任务名 = "";
    public 任务类型 类型 = 任务类型.支线;

    [Header("阶段")]
    [Min(1)] public int 阶段 = 1;
    public string 阶段名 = "";

    [TextArea(2, 6)]
    [Tooltip("任务面板里显示的说明")]
    public string 说明 = "";

    [Header("完成条件")]
    public 任务条件 条件 = 任务条件.无;

    [Tooltip("条件=提交物品 时：要交的物品 id")]
    public string 物品id = "";

    [Tooltip("条件=提交物品 时：要交几个")]
    [Min(1)] public int 数量 = 1;

    [Tooltip("条件=击杀 时：目标 NPC 的 NpcDefinition.id。**留空 = 任意 NPC 死一个**")]
    public string 目标npcId = "";

    [Header("条件=到达")]
    public float 坐标X = 0f;
    public float 坐标Y = 0f;
    public float 坐标Z = 0f;
    [Min(0.5f)] public float 到达半径 = 3f;

    [Tooltip("条件=对话 时：要说完的那一段对话 id（留空 = 这个 NPC 的任意一段）")]
    public string 对话id = "";

    [Tooltip("条件=等待秒数 时：进这一阶段后等几秒算完成")]
    [Min(0f)] public float 等待秒 = 3f;

    [Tooltip("动作=走向/飞到 时的移动速度（米/秒）")]
    [Min(0.1f)] public float 动作速度 = 3.5f;

    [Tooltip("动作=镜头看目标/镜头回玩家 的过渡时长（秒）")]
    [Min(0.05f)] public float 镜头时长 = 1.2f;

    [Tooltip("动作=镜头看目标 时，镜头停在这个高度偏移上（米）")]
    public float 镜头高度 = 1.6f;

    [Header("条件（复用对话系统的标记机制）")]
    [Tooltip("需要全部具备这些标记，这个阶段才会被算作可接/可推进。分号分隔")]
    public string 需要标记 = "";

    [Tooltip("有其中任意一个标记就跳过这个阶段。分号分隔")]
    public string 排除标记 = "";

    [Header("任务状态标记（和对话系统打通的关键）")]
    [Tooltip("阶段被设为当前时加上的标记。分号分隔。对话表里「需要标记」写它，就能出现任务专属回答")]
    public string 接取加标记 = "";

    [Tooltip("阶段完成时加上的标记。分号分隔")]
    public string 完成加标记 = "";

    [Header("奖励 / 调度")]
    [Tooltip("完成这个阶段发的物品，写法同「背包物品」列：物品id:数量|物品id:数量")]
    public string 奖励物品 = "";

    [Tooltip("这个阶段**开始时**对 NPC 做什么")]
    public 任务动作 动作 = 任务动作.无;

    [Tooltip("动作的目标 NPC：NpcDefinition.id")]
    public string 动作目标npcId = "";

    [Tooltip("动作参数：播动画时填动作名（如 swagger / angry_01 / walk）")]
    public string 动作参数 = "";

    [Header("接取")]
    [Tooltip("勾上 = 进游戏就自动接这个任务（测试用最方便）")]
    public bool 自动接取 = false;

    [Tooltip("需要先完成这个任务id，才能接（留空 = 没有前置）")]
    public string 前置任务id = "";

    public Vector3 坐标 => new Vector3(坐标X, 坐标Y, 坐标Z);

    void OnValidate()
    {
        if (string.IsNullOrEmpty(id)) id = name;
        if (阶段 < 1) 阶段 = 1;
        if (数量 < 1) 数量 = 1;
    }
}
