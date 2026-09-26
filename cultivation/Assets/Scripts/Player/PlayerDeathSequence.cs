using System.Collections;
using UnityEngine;

/// <summary>
/// **玩家死亡 → 消散 → 弹界面 → 重生** 的整条流程。
///
/// 顺序（策划要求）：
///   1. 玩家倒下 → **原地播一段粒子消散**（复用 NPC 那套 <see cref="NpcDissolveEffect"/>）
///   2. 模型藏起来、操作锁住（不能走、不能打、不能飞、不能放技能）
///   3. **过 2 秒**弹死亡界面（半透明，能看见背后的场景）
///   4. 点「重生」→ 传送到**修炼小屋前**、满血复活、模型和操作还回来
///   5. **重置场上所有敌对 NPC 的战斗状态** —— 免得它们还"以为玩家死了"而发呆
///      （不这么做的话，它们要等下一次重新索敌才动，看着就像卡住了）
///
/// 挂在 **Player** 上。引用留空会自动找。
/// </summary>
public class PlayerDeathSequence : MonoBehaviour
{
    [Header("引用（留空自动找）")]
    [Tooltip("玩家气血。留空自动在本体找 PlayerVitals")]
    public PlayerVitals 气血;

    [Tooltip("玩家模型根（死亡时藏起来）。留空自动找名为 Player_Visual 的子物件")]
    public Transform 玩家视觉;

    [Tooltip("重生点。留空会自动找场景里名为 cultivation room 的物件（= 修炼小屋）")]
    public Transform 重生点;

    [Header("死亡消散")]
    public float 消散粒子时长 = 1.8f;
    public Color 消散颜色 = new Color(0.72f, 0.84f, 1f, 0.95f);
    public int 消散粒子数 = 110;
    public float 消散半径 = 0.95f;

    [Header("节奏")]
    [Tooltip("消散开始后多久弹死亡界面（秒）")]
    public float 弹界面延迟 = 2f;

    [Header("重生")]
    [Tooltip("相对重生点的偏移。默认往前 3 米（= 小屋门前），稍微抬高避免陷地")]
    public Vector3 重生偏移 = new Vector3(0f, 0.1f, -3f);

    [Tooltip("重生后这段时间内不再受伤害（避免刚站起来又被秒）")]
    public float 重生保护时间 = 2.5f;

    [Tooltip("重生时把场上敌对 NPC 的战斗状态重置（推荐开）")]
    public bool 重生时重置怪物 = true;

    [Header("界面")]
    [Tooltip("死亡界面。留空会自动建一个")]
    public DeathScreenUI 死亡界面;

    [Header("死亡时要关掉的操作组件（留空自动收集）")]
    public MonoBehaviour[] 死亡时禁用;

    /// <summary>当前是不是处于"死亡→重生"这段流程里</summary>
    public bool 死亡流程中 { get; private set; }

    /// <summary>重生保护剩余时间</summary>
    public float 保护剩余 { get; private set; }

    bool 模型原本可见 = true;
    Vector3 重生位置;
    Quaternion 重生朝向;

    void Awake()
    {
        if (气血 == null) 气血 = GetComponent<PlayerVitals>();
        if (玩家视觉 == null)
        {
            var t = transform.Find("Player_Visual");
            玩家视觉 = t != null ? t : transform;
        }
        if (死亡界面 == null)
        {
            死亡界面 = GetComponentInChildren<DeathScreenUI>(true);
            if (死亡界面 == null) 死亡界面 = gameObject.AddComponent<DeathScreenUI>();
        }
        if (死亡界面 != null) 死亡界面.点了重生 += 执行重生;

        找重生点();
        收集操作组件();
    }

    void OnEnable()  { if (气血 != null) 气血.死亡 += 处理死亡; }
    void OnDisable() { if (气血 != null) 气血.死亡 -= 处理死亡; }

    void 收集操作组件()
    {
        if (死亡时禁用 != null && 死亡时禁用.Length > 0) return;
        // 死亡时该停下来的东西：走 / 打 / 技能 / 飞
        var 名单 = new[] { "PlayerController", "BasicSword01", "ActiveSkillCaster", "YufengFlight" };
        var 收集 = new System.Collections.Generic.List<MonoBehaviour>();
        foreach (var c in GetComponents<MonoBehaviour>())
        {
            if (c == null || c == this) continue;
            foreach (var n in 名单) if (c.GetType().Name == n) { 收集.Add(c); break; }
        }
        死亡时禁用 = 收集.ToArray();
    }

    void 找重生点()
    {
        if (重生点 != null) return;

        // 首选：修炼小屋（场景里叫 cultivation room）
        var 屋 = GameObject.Find("cultivation room");
        if (屋 == null)
            foreach (var g in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
                if (g.name.ToLowerInvariant().Contains("cultivation")) { 屋 = g; break; }
        if (屋 == null) 屋 = GameObject.Find("SpawnPoint");   // 退而求其次

        重生点 = 屋 != null ? 屋.transform : null;
        if (重生点 == null)
            Debug.LogWarning("[死亡流程] 场景里既没有 cultivation room 也没有 SpawnPoint，" +
                             "会原地复活。手动指定「重生点」即可", this);
    }

    // ============================================================ 死亡

    void 处理死亡(PlayerVitals 谁)
    {
        if (死亡流程中) return;
        StartCoroutine(死亡流程());
    }

    IEnumerator 死亡流程()
    {
        死亡流程中 = true;

        // 记下出生位置（没有重生点就原地起）
        重生位置 = 重生点 != null ? 重生点.position + 重生偏移 : transform.position;
        重生朝向 = 重生点 != null
            ? Quaternion.LookRotation((重生点.position - 重生位置).normalized, Vector3.up)
            : transform.rotation;
        // 小屋朝向为 0 时，让玩家面朝小屋
        if (重生点 != null && (重生点.position - 重生位置).sqrMagnitude < 0.01f) 重生朝向 = transform.rotation;

        // 1) 原地消散
        NpcDissolveEffect.播放(transform.position + Vector3.up * 0.9f, 消散半径, 消散颜色,
                                消散粒子数, 消散粒子时长);

        // 2) 藏模型 + 锁操作
        if (玩家视觉 != null)
        {
            var rs = 玩家视觉.GetComponentsInChildren<Renderer>(true);
            模型原本可见 = rs.Length > 0 && rs[0].enabled;
            foreach (var r in rs) r.enabled = false;
        }
        foreach (var c in 死亡时禁用) if (c != null) c.enabled = false;

        // 3) 等一会儿再弹界面——让消散先演完，不至于"啪一下"就糊个 UI 上来
        yield return new WaitForSeconds(Mathf.Max(0f, 弹界面延迟));

        if (死亡界面 != null) 死亡界面.显示();
    }

    // ============================================================ 重生

    /// <summary>点了「重生」按钮走这里。外部也可以直接调</summary>
    public void 执行重生()
    {
        if (!死亡流程中) return;

        if (死亡界面 != null) 死亡界面.隐藏();

        // 1) 传送
        var cc = GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;                 // 传送时先关掉，免得 CharacterController 把位置拽回去
        transform.position = 重生位置;
        transform.rotation = 重生朝向;
        if (cc != null) cc.enabled = true;

        // 2) 满血复活
        if (气血 != null) 气血.复活();

        // 3) 还回模型和操作
        if (玩家视觉 != null)
            foreach (var r in 玩家视觉.GetComponentsInChildren<Renderer>(true)) r.enabled = 模型原本可见;
        foreach (var c in 死亡时禁用) if (c != null) c.enabled = true;

        // ★ 修「重生后自己长出一把剑」：上面这行会把死亡时关掉的组件**全部**开回来，
        //   但技能类组件（普攻方法 / 被动神通）的唯一权威是 PlayerAbilityLoader
        //   —— 它按「当前功法 + 已获得被动」装卸。新档主角什么都没学，
        //   普攻方法组件本来就该是关的；全开会让它实例化出剑的模型（用户实测报的）。
        //   所以恢复之后立刻让装载器按规则重新对一遍。
        var 能力装载 = GetComponent<PlayerAbilityLoader>();
        if (能力装载 != null) 能力装载.Refresh();

        // 4) 把怪的战斗状态重置 —— 免得它们还"以为玩家死了"而发呆
        if (重生时重置怪物) 重置全场敌人();

        保护剩余 = 重生保护时间;
        if (气血 != null) 气血.无敌 = 保护剩余 > 0f;
        死亡流程中 = false;
    }

    /// <summary>
    /// 清掉所有敌对 NPC 的战斗锁定，让它们重新索敌。
    /// 不做的话它们会停在"待机"，要等下一次重新索敌冷却才动，看着像卡住。
    /// </summary>
    void 重置全场敌人()
    {
        int n = 0;
        foreach (var ai in UnityEngine.Object.FindObjectsOfType<NpcAiBase>())
        {
            var 会打的 = ai as NpcAiCombatant;
            if (会打的 == null) continue;
            会打的.重置战斗状态();
            n++;
        }
        if (n > 0) Debug.Log("[死亡流程] 重生：重置了 " + n + " 个敌对 NPC 的战斗状态");
    }

    void Update()
    {
        if (保护剩余 > 0f)
        {
            保护剩余 -= Time.deltaTime;
            if (保护剩余 <= 0f) { 保护剩余 = 0f; if (气血 != null) 气血.无敌 = false; }
        }
    }

    /// <summary>重生保护期内免疫伤害（由 PlayerVitals 那边判断，或者外部问这个）</summary>
    public bool 处于保护中 => 保护剩余 > 0f;

    // ---- ASCII 别名 ----
    public bool InDeathSequence => 死亡流程中;
    public bool IsProtected => 处于保护中;
    public void Respawn() => 执行重生();
}
