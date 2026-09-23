using UnityEngine;

/// <summary>
/// 主动技能栏的快捷键施放器。挂在 Player 上。
///
/// 技能栏有 6 格（神通 / 法宝 / 灵阵 共用，见 <see cref="UIPanelData.主动技能"/>），
/// 第 N 格 → 快捷键 N（默认主键盘 1~6，可同时支持小键盘）。
///
/// 一次施放的顺序：
///   冷却 → 结算方式是否实现 → 是否需要锁定 → 灵力够不够
///   → 扣灵力、进冷却 → 生成特效 + 结算伤害
///
/// 任何一步不过都只给玩家一句提示，**不会静默失败、也不会白扣灵力**。
///
/// 界面开着时默认不接收快捷键（跟 NpcTargeting 的做法一致：
/// 全屏界面开着时不该往场景里灌操作）。想改就把 <see cref="界面开着时禁止施放"/> 关掉。
/// </summary>
public class ActiveSkillCaster : MonoBehaviour
{
    [Header("按键（下标 = 技能栏槽位 0~5）")]
    public KeyCode[] 快捷键 =
    {
        KeyCode.Alpha1, KeyCode.Alpha2, KeyCode.Alpha3,
        KeyCode.Alpha4, KeyCode.Alpha5, KeyCode.Alpha6,
    };

    [Tooltip("是否同时接受小键盘 1~6")]
    public bool 同时支持小键盘 = true;

    [Header("输入闸门")]
    [Tooltip("角色面板 / 设施界面开着时是否禁止施放")]
    public bool 界面开着时禁止施放 = true;

    [Header("引用（留空自动找）")]
    public UIPanelData 面板数据;
    public PlayerVitals 生命;
    public PlayerCombatStats 战斗属性;
    public NpcTargeting 目标管理器;

    [Header("特效")]
    [Tooltip("一次性技能（持续时长 = 0）的特效播多久后销毁")]
    public float 一次性特效存活 = 3f;

    [Tooltip("特效在 scale = 1 时的视觉外缘半径（米）。施放时按【范围 ÷ 这个值】缩放特效的 X/Z，\n" +
             "让火焰大小跟着范围走。换了特效资源就要重新标定。\n" +
             "Effect_13_DangerClose 实测 49.1 米 / Effect_13_Explosion 实测 9.9 米")]
    public float 特效基准半径 = 49.1f;

    [Header("施法动作")]
    [Tooltip("玩家动画控制器。留空自动在本体找")]
    public PlayerAnimationController 动画;

    [Tooltip("生成特效时额外施加的欧拉角。\n" +
             "Effect_13_DangerClose 是【倒着做的】：原样播放时粒子全部在地面以下（实测世界 Y = −100~0）\n" +
             "—— 看起来是「从地底往上冒」。绕 X 转 180° 后粒子变成 Y = 0~100，才是「从天而降砸向地面」。\n" +
             "换特效资源时这个值也要跟着改。")]
    public Vector3 特效旋转 = new Vector3(180f, 0f, 0f);

    [Tooltip("范围伤害的检测层级")]
    public LayerMask 敌人层 = ~0;

    [Header("调试")]
    public bool 打印施法日志 = true;

    static readonly KeyCode[] 小键盘键 =
    {
        KeyCode.Keypad1, KeyCode.Keypad2, KeyCode.Keypad3,
        KeyCode.Keypad4, KeyCode.Keypad5, KeyCode.Keypad6,
    };

    readonly float[] 冷却剩余 = new float[6];

    void Awake() => 解析引用();

    void 解析引用()
    {
        if (面板数据 == null) 面板数据 = FindObjectOfType<UIPanelData>();
        if (生命 == null) 生命 = GetComponent<PlayerVitals>();
        if (战斗属性 == null) 战斗属性 = GetComponent<PlayerCombatStats>();
        if (目标管理器 == null) 目标管理器 = GetComponent<NpcTargeting>();
    }

    void Update()
    {
        // 冷却一直走（暂停时 deltaTime 为 0，自然不会推进）
        for (int i = 0; i < 冷却剩余.Length; i++)
            if (冷却剩余[i] > 0f) 冷却剩余[i] -= Time.deltaTime;

        if (!接收输入()) return;

        int 上限 = Mathf.Min(6, 快捷键 != null ? 快捷键.Length : 0);
        for (int i = 0; i < 上限; i++)
        {
            bool 按下 = Input.GetKeyDown(快捷键[i]);
            if (!按下 && 同时支持小键盘 && i < 小键盘键.Length)
                按下 = Input.GetKeyDown(小键盘键[i]);

            if (按下) { 尝试施放(i); return; }   // 一帧只放一个
        }
    }

    /// <summary>现在该不该接收快捷键</summary>
    public bool 接收输入()
    {
        if (Time.timeScale <= 0f) return false;                          // 暂停菜单开着
        if (界面开着时禁止施放 && UiEscRegistry.AnyOtherUiOpen()) return false;
        return true;
    }

    /// <summary>按下技能栏第 N 格（0~5）。返回是否真的放出去了。</summary>
    public bool 尝试施放(int 槽位)
    {
        if (面板数据 == null) 解析引用();
        if (面板数据 == null || 面板数据.主动技能 == null) return false;
        if (槽位 < 0 || 槽位 >= 面板数据.主动技能.Count) return false;

        var 内容 = 面板数据.主动技能[槽位];
        if (内容 == null) return false;                        // 空槽：静默

        // ---- 冷却 ----
        if (槽位 < 冷却剩余.Length && 冷却剩余[槽位] > 0f)
        {
            提示("「" + 取名(内容) + "」冷却中，还需 " + 冷却剩余[槽位].ToString("0.0") + " 秒");
            return false;
        }

        // ---- 目前只有主动神通实现了；法宝 / 灵阵还是占位资产 ----
        var 神通 = 内容 as ActiveDivineAbility;
        if (神通 == null)
        {
            提示("「" + 取名(内容) + "」还没做（目前只实现了主动神通）");
            return false;
        }

        if (神通.结算方式 == ActiveSkillKind.未实现)
        {
            提示("「" + 神通.神通名称 + "」还没做");
            return false;
        }

        // ---- 需要锁定 ----
        var 锁定 = 目标管理器 != null ? 目标管理器.LockedNpc : null;
        if (神通.需要锁定目标 && (锁定 == null || 锁定.IsDead))
        {
            提示("「" + 神通.神通名称 + "」需要先右键锁定一个敌人");
            return false;
        }

        // ---- 灵力 ----
        if (神通.消耗灵力 > 0f)
        {
            if (生命 == null) 解析引用();
            if (生命 == null) return false;

            if (!生命.够灵气(神通.消耗灵力))
            {
                提示("灵力不足：「" + 神通.神通名称 + "」需要 " + 神通.消耗灵力.ToString("0.#")
                     + "，当前 " + 生命.当前灵气.ToString("0.#"));
                return false;
            }
            生命.扣灵气(神通.消耗灵力);
        }

        if (槽位 < 冷却剩余.Length) 冷却剩余[槽位] = 神通.冷却时间;

        施放(神通, 锁定);
        return true;
    }

    void 施放(ActiveDivineAbility 神通, NpcInstance 锁定)
    {
        // 【施法动作】表里「施法动作」列配了才播（例：焚天炎术 → 技能动作2）
        // 片段放在 Assets/resources/技能动作/ 下，所以能 Resources.Load。
        // 注意是"播动作"而不是"等它播完" —— 出手/结算的时机仍由表的「首次造成伤害时间」控制，
        // 这样动画和伤害对齐是策划可调的，不会被动画长度绑架。
        播施法动作(神通);
        if (战斗属性 == null) 解析引用();

        // 以锁定的敌人为中心（不需要锁定的技能则以自己为中心）。取施放瞬间的位置。
        Vector3 中心 = (神通.需要锁定目标 && 锁定 != null) ? 锁定.transform.position : transform.position;

        // 特效。就算没配特效也要结算伤害，所以结算体永远都建。
        GameObject 载体 = null;
        if (!string.IsNullOrEmpty(神通.特效资源路径))
        {
            var prefab = Resources.Load<GameObject>(神通.特效资源路径);
            if (prefab != null) 载体 = Instantiate(prefab, 中心, Quaternion.Euler(特效旋转));
            else Debug.LogWarning("[ActiveSkillCaster] 找不到特效资源：" + 神通.特效资源路径
                                  + "（路径要相对 Assets/resources，且不带扩展名）");
        }
        if (载体 == null) 载体 = new GameObject();
        载体.name = "SkillFx_" + 神通.神通id;

        // 特效大小跟着【范围】走：只缩放 X/Z，Y 保持原样。
        //
        // 必须同时把粒子系统改成 Local 空间：World 空间下 transform 缩放只改变粒子的
        // **散布位置**、不改变**单个粒子的尺寸**，结果是火焰被"摊开"而不是"变大"，
        // 大小根本跟不住范围（实测：scale 1→3 时散布 6.2→18.9 米，但粒径恒为 9.94）。
        // 换成 Local 之后缩放才是等比的（外缘半径/scale ≈ 10.5，基本是常数）。
        // 特效本身是静止的，Local / World 在观感上没有区别。
        if (特效基准半径 > 0f && 神通.范围 > 0f)
        {
            float 缩放 = Mathf.Max(0.05f, 神通.范围 / 特效基准半径);
            载体.transform.localScale = new Vector3(缩放, 1f, 缩放);

            foreach (var ps in 载体.GetComponentsInChildren<ParticleSystem>(true))
            {
                if (ps.main.simulationSpace == ParticleSystemSimulationSpace.World)
                {
                    var m = ps.main;
                    m.simulationSpace = ParticleSystemSimulationSpace.Local;
                }
            }
        }

        var runner = 载体.AddComponent<AreaSkillRunner>();
        runner.初始化(神通, 中心, 战斗属性, 敌人层, 锁定, 一次性特效存活);

        if (打印施法日志)
            Debug.Log("[ActiveSkillCaster] 施放「" + 神通.神通名称 + "」中心=" + 中心
                      + " 范围=" + 神通.范围 + " 倍率=" + 神通.伤害倍率
                      + " 属性=" + 神通.伤害属性
                      + " → 命中 " + runner.结算次数 + " 次，合计 " + runner.累计伤害.ToString("0.##"));
    }

    static string 取名(Object o)
    {
        var e = o as IPanelEntry;
        if (e != null && !string.IsNullOrEmpty(e.DisplayName)) return e.DisplayName;
        return o != null ? o.name : "?";
    }

    void 提示(string msg)
    {
        if (打印施法日志) Debug.Log("[ActiveSkillCaster] " + msg);
        if (面板数据 != null) 面板数据.ShowHint(msg);
    }

    /// <summary>某格的剩余冷却（秒）</summary>
    public float 冷却剩余秒(int 槽位)
        => (槽位 >= 0 && 槽位 < 冷却剩余.Length) ? Mathf.Max(0f, 冷却剩余[槽位]) : 0f;

    /// <summary>某格装的是什么（没装返回 null）</summary>
    public Object 槽位内容(int 槽位)
        => (面板数据 != null && 面板数据.主动技能 != null
            && 槽位 >= 0 && 槽位 < 面板数据.主动技能.Count)
           ? 面板数据.主动技能[槽位] : null;

    /// <summary>某格的冷却总时长（秒）。空槽 / 没配冷却返回 0</summary>
    public float 冷却总秒(int 槽位)
    {
        var 神通 = 槽位内容(槽位) as ActiveDivineAbility;
        return 神通 != null ? Mathf.Max(0f, 神通.冷却时间) : 0f;
    }

    /// <summary>某格是否正在冷却</summary>
    public bool 冷却中(int 槽位) => 冷却剩余秒(槽位) > 0f;

    /// <summary>
    /// 某格的冷却进度 0~1：**1 = 刚进冷却，0 = 冷却完毕**。
    /// 给 HUD 的暗部遮罩用 —— fillAmount 直接吃这个值，暗部就会随冷却逐渐消退。
    /// </summary>
    public float 冷却比例(int 槽位)
    {
        float 总 = 冷却总秒(槽位);
        if (总 <= 0f) return 0f;
        return Mathf.Clamp01(冷却剩余秒(槽位) / 总);
    }

    /// <summary>某格装的是不是「已实现、能主动施放」的东西（决定 HUD 图标亮不亮）</summary>
    public bool 槽位可用(int 槽位)
    {
        var 神通 = 槽位内容(槽位) as ActiveDivineAbility;
        return 神通 != null && 神通.结算方式 != ActiveSkillKind.未实现;
    }

    // ---- ASCII 别名 ----
    public bool TryCast(int slot) => 尝试施放(slot);
    public bool AcceptsInput() => 接收输入();
    public float CooldownLeft(int slot) => 冷却剩余秒(slot);
    public Object SlotContent(int slot) => 槽位内容(slot);
    public float CooldownTotal(int slot) => 冷却总秒(slot);
    public bool OnCooldown(int slot) => 冷却中(slot);
    public float CooldownRatio(int slot) => 冷却比例(slot);
    public bool SlotUsable(int slot) => 槽位可用(slot);

    // ============================================================ 施法动作

    /// <summary>
    /// 播这个神通配的施法动作。表里没配就什么都不做。
    /// 片段名对应 `Assets/resources/技能动作/&lt;名字&gt;.anim`。
    /// </summary>
    void 播施法动作(ActiveDivineAbility 神通)
    {
        if (神通 == null || string.IsNullOrEmpty(神通.施法动作)) return;

        if (动画 == null) 动画 = GetComponent<PlayerAnimationController>();
        if (动画 == null) 动画 = GetComponentInChildren<PlayerAnimationController>();
        if (动画 == null)
        {
            Debug.LogWarning("[主动神通] 找不到 PlayerAnimationController，播不了施法动作「" + 神通.施法动作 + "」", this);
            return;
        }

        var 片段 = Resources.Load<AnimationClip>("技能动作/" + 神通.施法动作);
        if (片段 == null)
        {
            Debug.LogWarning("[主动神通] 找不到施法动作 Assets/resources/技能动作/" + 神通.施法动作 + ".anim", this);
            return;
        }

        // 施法动作**不受攻速影响**（攻速只管普攻），固定原速
        if (动画.播动作(片段, 1f) && 打印施法日志)
            Debug.Log("[主动神通] 播施法动作「" + 神通.施法动作 + "」（" + 片段.length.ToString("0.##") + "s）", this);
    }
}
