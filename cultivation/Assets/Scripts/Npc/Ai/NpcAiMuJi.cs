using System;
using UnityEngine;

/// <summary>
/// 母鸡（母鸡 / MuJi）。
///
/// 动画只有 `Idle` / `Walk` / `Specialidle` —— 没有 Run、没有攻击、没有死亡动画，
/// 所以完全落在「兽类」那套规则里（见 <see cref="NpcAiAnimal"/>）：
///
///   · 不会攻击玩家
///   · 好感 &gt; 60 → 玩家靠近会凑过来跟随，到 3.5 米停下
///   · 好感 &lt; 10 → 玩家靠近就躲
///   · 挨打 → 朝反方向跑一段，同时好感每次 −20
///   · **啄食**（待机花样）也由兽类基类统一提供，这里不用再写
///   · 死亡 → 没有死亡动画，模型立刻消失、用粒子消散
///
/// 母鸡真正独有的只剩**下蛋**（默认关）。
///
/// 【装配】类名 `NpcAiMuJi` 按 `NpcAi&lt;模型名&gt;` 约定被自动发现，
/// prefab 叫 `MuJi_01` 的 NPC 会自动装上它，不用改任何注册表。
/// </summary>
public class NpcAiMuJi : NpcAiAnimal
{
    [Header("母鸡 · 下蛋（默认关）")]
    [Tooltip("打开才会下蛋。需要同时配好「蛋预制体路径」，或者外部监听 下蛋 事件")]
    public bool 会下蛋 = false;

    [Tooltip("下蛋间隔（秒）")]
    public float 下蛋间隔 = 30f;

    [Tooltip("好感度低于这个值就不下蛋（0 = 一直下）")]
    public float 下蛋所需好感 = 60f;

    [Tooltip("蛋的预制体 Resources 路径。留空则只发事件、不生成东西")]
    public string 蛋预制体路径 = "";

    /// <summary>下了一个蛋（参数：自己、生成的蛋；没配预制体时蛋为 null）</summary>
    public event Action<NpcAiMuJi, GameObject> 下蛋;

    float 下次下蛋时间;

    protected override void 取默认参数()
    {
        base.取默认参数();

        // 母鸡个头小、注意玩家的距离近、跟得也近
        索敌范围 = 6f;
        脱战范围 = 10f;
        跟随距离 = 3.5f;
        回避距离 = 5f;
        行走倍率 = 0.45f;
        奔跑倍率 = 0.8f;          // 没有 Run 动作，基类会自动降级成 Walk

        // 啄食：母鸡比别的动物频率高一点
        待机花样动作 = "Specialidle";
        待机花样间隔 = 3f;
        待机花样时长 = 1.4f;

        // 死亡：用内置的通用消散（粒子炸开 + 各自随机渐隐）。
        // 大小走基类的基准缩放 3，这里只调颜色和数量
        死亡消散特效路径 = "";
        死亡消散颜色 = new Color(1f, 0.96f, 0.86f, 0.95f);   // 偏暖，像散了一地羽毛
        死亡消散粒子数 = 40;
        死亡消散特效存活 = 2f;
        死亡后销毁延迟 = 1.2f;
    }

    protected override void Update()
    {
        base.Update();
        处理下蛋();
    }

    // ============================================================ 下蛋

    void 处理下蛋()
    {
        if (!会下蛋 || 自己 == null || 自己.IsDead) return;
        if (状态 != NpcAiState.待机) return;              // 只在安静站着时下
        if (Time.time < 下次下蛋时间) return;

        下次下蛋时间 = Time.time + Mathf.Max(1f, 下蛋间隔);

        if (自己.对主角好感度 < 下蛋所需好感) return;      // 还不亲近，先不下
        下一个蛋();
    }

    void 下一个蛋()
    {
        GameObject 蛋 = null;

        if (!string.IsNullOrEmpty(蛋预制体路径))
        {
            var prefab = Resources.Load<GameObject>(蛋预制体路径);
            if (prefab != null)
            {
                Vector3 位置 = transform.position + transform.forward * 0.4f + Vector3.up * 0.05f;
                蛋 = Instantiate(prefab, 位置, Quaternion.identity);
                蛋.name = "蛋_" + name;
            }
            else Debug.LogWarning("[AI] " + name + " 找不到蛋预制体：" + 蛋预制体路径, this);
        }

        下蛋?.Invoke(this, 蛋);
        if (打印状态日志) Debug.Log("[AI] " + name + " 下了一个蛋");
    }

    // ---- ASCII 别名 ----
    public bool CanLayEggs => 会下蛋;
    public event Action<NpcAiMuJi, GameObject> LaidEgg { add { 下蛋 += value; } remove { 下蛋 -= value; } }
}
