using UnityEngine;

/// <summary>
/// 野兽（animal / 数据里叫 beast）。
///
/// 特点：
///   · 动画很少：通常只有 `Idle` / `Specialidle` / `Walk`（有的只有 `Idle` / `Run`），
///     **没有攻击、没有死亡动画** —— 所以基类的「缺动画自动降级」在这里最关键
///   · **不会攻击玩家**（<see cref="视玩家为敌"/> 永远是 false）
///   · 好感度 &gt; <see cref="亲近好感阈值"/>：玩家靠近会**主动凑过来并跟随**（到合适距离停下）
///   · 好感度 &lt; <see cref="回避好感阈值"/>：玩家靠近会**躲开**
///   · 挨打 → 朝远离玩家的方向**跑一段路**（<see cref="受击逃跑时长"/>）
///   · 死亡没有动画 → 靠 <see cref="NpcAiBase.死亡消散特效路径"/> 的粒子消散
/// </summary>
public class NpcAiAnimal : NpcAiBase
{
    [Header("亲近 / 回避")]
    [Tooltip("好感度 > 这个值：玩家靠近就凑过来跟随")]
    public float 亲近好感阈值 = 60f;

    [Tooltip("好感度 < 这个值：玩家靠近就躲开")]
    public float 回避好感阈值 = 10f;

    [Tooltip("跟随到多近就停下（米）")]
    public float 跟随距离 = 3f;

    [Tooltip("躲开时想拉开到多远（米）")]
    public float 回避距离 = 8f;

    [Header("挨打反应")]
    [Tooltip("挨打后朝反方向跑多久（秒）")]
    public float 受击逃跑时长 = 3f;

    /// <summary>正在逃跑（挨打之后的这段时间）</summary>
    public bool 逃跑中 => Time.time < 逃跑结束时间;

    float 逃跑结束时间 = -999f;

    protected override void 取默认参数()
    {
        base.取默认参数();

        索敌范围 = 8f;          // 动物「注意到玩家」的距离
        脱战范围 = 12f;
        攻击距离 = 0f;          // 不打人
        行走倍率 = 0.45f;
        奔跑倍率 = 1f;
        死亡后销毁延迟 = 4f;
    }

    /// <summary>野兽永远不把玩家当敌人</summary>
    public override bool 视玩家为敌 => false;

    public override void 收到伤害通知(AttackResult 结果)
    {
        base.收到伤害通知(结果);

        if (!结果.命中 || 结果.伤害 <= 0f) return;
        逃跑结束时间 = Time.time + 受击逃跑时长;      // 挨打就跑一段
    }

    protected override void 决策()
    {
        if (玩家生命 == null || 玩家生命.已死亡) { 进入状态(NpcAiState.待机); return; }

        float 好感 = 自己.对主角好感度;

        // 1) 挨打之后的逃跑期：一路往反方向跑，期间不理会好感
        if (逃跑中) { 进入状态(NpcAiState.远离); return; }

        // 2) 好感低：玩家靠近就躲
        if (好感 < 回避好感阈值)
        {
            if (到玩家距离 <= 有效索敌范围) 进入状态(NpcAiState.远离);
            else 进入状态(NpcAiState.待机);
            return;
        }

        // 3) 好感高：主动凑过去，到了合适距离就停
        if (好感 > 亲近好感阈值)
        {
            if (到玩家距离 > 跟随距离) 进入状态(NpcAiState.接近);
            else { 进入状态(NpcAiState.待机); 朝向玩家(); }
            return;
        }

        // 4) 中间地带：各过各的
        进入状态(NpcAiState.待机);
    }

    protected override void 执行远离()
    {
        if (玩家生命 == null) { 进入状态(NpcAiState.待机); return; }

        // 已经拉得够开就不再无脑退（逃跑期除外）
        if (!逃跑中 && 到玩家距离 >= 回避距离) { 进入状态(NpcAiState.待机); return; }

        base.执行远离();
    }

    // ============================================================ 待机花样（啄食 / 刨地）

    /// <summary>
    /// 站桩的时候偶尔插一段「特殊待机」动作（动物基本都是 `Specialidle` = 啄食 / 刨地），
    /// 不然一直播 Idle 看着像块木头。
    ///
    /// 这一层放在 **兽类** 上而不是某个物种上：11 只动物里 10 只都是
    /// `Idle / Specialidle / Walk`，全都能直接用。
    /// 唯一例外是**仙鹤**（源资产里只有 `Xun`），把 <see cref="待机花样动作"/> 留空即可。
    /// </summary>
    [Header("待机花样（啄食 / 刨地）")]
    [Tooltip("待机时偶尔插播的动作。留空 = 不插播")]
    public string 待机花样动作 = "Specialidle";

    [Tooltip("隔多久来一次（秒）")]
    public float 待机花样间隔 = 3.5f;

    [Tooltip("每次插播多久（秒）")]
    public float 待机花样时长 = 1.4f;

    /// <summary>正在插播待机花样</summary>
    public bool 花样中 { get; private set; }

    float 花样结束时间;
    float 下次花样时间;

    protected override void 执行待机()
    {
        if (花样中)
        {
            if (Time.time < 花样结束时间) { 播(待机花样动作, true); return; }   // 保持花样
            花样中 = false;
            下次花样时间 = Time.time + 待机花样间隔;
            播待机动画();
            return;
        }

        base.执行待机();

        if (string.IsNullOrEmpty(待机花样动作)) return;
        if (Time.time < 下次花样时间) return;
        if (!有动作(待机花样动作)) return;

        花样中 = true;
        花样结束时间 = Time.time + 待机花样时长;
        播(待机花样动作, true);
    }

    protected override void 执行接近()
    {
        停止花样();
        base.执行接近();
    }

    void 停止花样()
    {
        if (!花样中) return;
        花样中 = false;
        下次花样时间 = Time.time + 待机花样间隔;   // 走完回来再隔一会儿才来
    }

    // ---- ASCII 别名 ----
    public bool IsFleeing => 逃跑中;
    public bool IsIdling => 花样中;
    public float FriendFavorThreshold => 亲近好感阈值;
    public float AvoidFavorThreshold => 回避好感阈值;
}
