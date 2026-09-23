using System;
using UnityEngine;

/// <summary>
/// 被动神通【凭虚御风】的运行时表现。
///
/// 规则：
///   · 【凭虚御风】处于启用状态时，**按一下 Shift 起飞、再按一下落地**（切换式，不是按住）；
///     起飞后 Shift 原本的「跑步」仍然是被它顶掉的（和以前一样）；
///   · 每秒消耗 0.1 灵气，灵气耗尽会自动落地（并把开关也关掉，不会灵气一回来就自己又飞起来）；
///   · 先播【升空】动画，升空完成后进入【凭虚御风idle】悬浮；
///   · 悬浮期间移动会以比跑步更快的速度飞行，动画切成【凭虚御风前进】；
///   · 再按一下 Shift 播【落地】动画，落地后恢复正常行走/跑步。
///
/// 本组件只负责「状态机 + 灵气消耗 + 高度」，移动速度由 PlayerController 读取，
/// 动画参数由 PlayerAnimationController 读取，互不耦合。
/// </summary>
public class YufengFlight : MonoBehaviour
{
    public enum FlightState
    {
        地面 = 0,
        升空 = 1,
        御风 = 2,
        落地 = 3,
    }

    [Header("神通来源")]
    [Tooltip("被动神通定义。填了就直接用它，否则按下面的 id 从面板数据里找")]
    public PassiveDivineAbility 御风神通;

    [Tooltip("凭虚御风在被动神通表里的 id")]
    public string 神通id = "ability_pingxu_yufeng";

    [Tooltip("数据源，用于判断该被动是否处于启用状态。留空则视为始终可用")]
    public UIPanelData 面板数据;

    [Header("操作")]
    [Tooltip("**切换御风的按键**：按一下起飞、再按一下落地。\n\n" +
             "以前是「按住 Shift 才飞」—— 飞久了要一直按着很累，所以改成切换式。\n" +
             "只在**地面 / 御风悬浮**这两个稳定状态里响应；升空 / 落地过渡中按了不理会。")]
    public KeyCode 切换键 = KeyCode.LeftShift;

    [Tooltip("第二个切换键（键盘左右 Shift 都认）。填 None 表示不要")]
    public KeyCode 切换键2 = KeyCode.RightShift;

    [Header("引用")]
    public PlayerVitals 灵气;
    public PlayerController 移动;

    [Header("消耗")]
    [Tooltip("御风期间每秒消耗的灵气")]
    public float 每秒消耗灵气 = 0.1f;

    [Header("飞行")]
    [Tooltip("御风时的移动速度（要比跑步快）")]
    public float 飞行速度 = 9f;

    [Tooltip("悬浮高度（米）")]
    public float 飞行高度 = 2.4f;

    [Tooltip("升空动画时长（秒），到时间才进入悬浮")]
    public float 升空时长 = 0.85f;

    [Tooltip("落地动画时长（秒）")]
    public float 落地时长 = 0.70f;

    [Tooltip("升空/落地的高度过渡曲线")]
    public AnimationCurve 高度曲线 = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("调试")]
    public bool 打印状态切换 = false;

    [Tooltip("勾上就无视 Shift 一直保持御风，方便调试 / 以后做剧情飞行")]
    public bool 强制御风 = false;

    /// <summary>当前状态</summary>
    public FlightState 状态 { get; private set; } = FlightState.地面;

    /// <summary>是否已经在御风悬浮中（可以正常飞行移动）</summary>
    public bool 御风中 => 状态 == FlightState.御风;

    /// <summary>
    /// **玩家「想飞」的开关**（按一下开、再按一下关）。
    /// 灵气耗尽 / 神通被停用 / 被强制落地时会自动关掉，不会"灵气一回来就自己又飞起来"。
    /// </summary>
    public bool 想飞 { get; private set; }

    /// <summary>是否处于整个御风流程里（升空/悬浮/落地都算）</summary>
    public bool 御风流程中 => 状态 != FlightState.地面;

    /// <summary>当前相对地面的高度偏移，PlayerController 用它控制升降</summary>
    public float 高度偏移 { get; private set; }

    /// <summary>起飞时记录的地面高度</summary>
    public float 地面高度 { get; private set; }

    /// <summary>状态变化事件（参数为新状态）</summary>
    public event Action<FlightState> 状态变化;

    /// <summary>
    /// 解析数据源。
    /// 必须按需解析：面板数据挂在 CharacterUI 上，而重建角色面板会把 CharacterUI 整个销毁重建，
    /// 手工在 Inspector 里接的引用届时会失效变成空。
    /// </summary>
    UIPanelData 解析面板数据()
    {
        if (面板数据 != null) return 面板数据;

        var ui = GameObject.Find("CharacterUI");
        if (ui != null) 面板数据 = ui.GetComponent<UIPanelData>();
        if (面板数据 == null) 面板数据 = FindObjectOfType<UIPanelData>();
        return 面板数据;
    }

    /// <summary>神通是否处于启用状态</summary>
    public bool 神通已启用
    {
        get
        {
            var d = 解析面板数据();
            if (d == null) return true;            // 实在找不到数据源就不拦着，先保证功能可用

            var ability = 取神通();
            if (ability == null) return true;      // 表里没有这条也不拦着
            return d.IsPassiveEnabled(ability);
        }
    }

    PassiveDivineAbility 取神通()
    {
        if (御风神通 != null) return 御风神通;

        var d = 解析面板数据();
        if (d == null) return null;
        if (d.神通 == null) return null;

        foreach (var a in d.神通)
            if (a is PassiveDivineAbility p && p.神通id == 神通id) { 御风神通 = p; return p; }
        return null;
    }

    /// <summary>每秒消耗：优先用神通表里的「维持消耗灵力」，没配就用面板上的值</summary>
    public float 实际每秒消耗
    {
        get
        {
            var a = 取神通();
            return a != null && a.维持消耗灵力 > 0f ? a.维持消耗灵力 : 每秒消耗灵气;
        }
    }

    void Awake()
    {
        if (灵气 == null) 灵气 = GetComponent<PlayerVitals>();
        if (移动 == null) 移动 = GetComponent<PlayerController>();
    }

    void Update()
    {
        if (移动 == null) return;

        读取切换输入();

        bool wantFly = (想飞 || 强制御风) && 神通已启用 && (灵气 == null || 灵气.有灵气);

        switch (状态)
        {
            case FlightState.地面:
                if (wantFly) 进入升空();
                else if (想飞 && !神通已启用) 想飞 = false;   // 神通被停用 → 开关也清掉
                break;

            case FlightState.升空:
                高度偏移 = 飞行高度 * 高度曲线.Evaluate(1f - Mathf.Clamp01(升空剩余 / Mathf.Max(0.01f, 升空时长)));
                升空剩余 -= Time.deltaTime;
                if (升空剩余 <= 0f) { 高度偏移 = 飞行高度; 切换到(FlightState.御风); }
                break;

            case FlightState.御风:
                高度偏移 = 飞行高度;
                // 持续消耗灵气；耗尽自动落地
                if (灵气 != null)
                {
                    float cost = 实际每秒消耗 * Time.deltaTime;
                    if (cost > 0f)
                    {
                        灵气.扣灵气直到零(cost);
                        if (!灵气.有灵气) { 想飞 = false; 进入落地(); break; }   // ★ 开关一起关掉
                    }
                }
                // 开关关了 / 神通被停用 → 落地
                if (!wantFly) { 想飞 = false; 进入落地(); }
                break;

            case FlightState.落地:
                高度偏移 = 飞行高度 * (1f - 高度曲线.Evaluate(1f - Mathf.Clamp01(落地剩余 / Mathf.Max(0.01f, 落地时长))));
                落地剩余 -= Time.deltaTime;
                if (落地剩余 <= 0f) { 高度偏移 = 0f; 切换到(FlightState.地面); }
                break;
        }
    }

    /// <summary>
    /// 读「切换御风」的按键：**按一下开、再按一下关**（以前是按住）。
    ///
    /// 只在**地面 / 御风悬浮**这两个稳定状态里响应 —— 升空 / 落地过渡中按了不理会，
    /// 免得刚起飞就被自己取消掉。
    /// 想「开」的时候要求神通已启用、而且还有灵气；不然这一下就白按（保持关闭），
    /// 这样不会出现「按了关，灵气回满又自己飞起来」。
    /// </summary>
    void 读取切换输入()
    {
        if (强制御风) { 想飞 = true; return; }

        bool 按下 = Input.GetKeyDown(切换键)
                 || (切换键2 != KeyCode.None && Input.GetKeyDown(切换键2));
        if (!按下) return;

        切换();
    }

    /// <summary>
    /// **切换飞行**（按一下起飞、再按一下落地）。
    /// 抽成公开方法：既给按键用，也方便以后接 UI 按钮 / 剧情脚本 / 自动化测试。
    /// </summary>
    public void 切换()
    {
        if (强制御风) { 想飞 = true; return; }

        // 升空 / 落地过渡中不响应 —— 免得刚起飞就被自己取消掉
        if (状态 == FlightState.升空 || 状态 == FlightState.落地) return;

        if (想飞) { 想飞 = false; }
        else
        {
            if (!神通已启用 || (灵气 != null && !灵气.有灵气)) return;   // 起飞条件不满足，这一下不算
            想飞 = true;
        }

        if (打印状态切换) Debug.Log("[凭虚御风] 切换 → " + (想飞 ? "起飞" : "落地"), this);
    }

    float 升空剩余;
    float 落地剩余;

    void 进入升空()
    {
        地面高度 = transform.position.y;
        升空剩余 = Mathf.Max(0.01f, 升空时长);
        高度偏移 = 0f;
        切换到(FlightState.升空);
    }

    void 进入落地()
    {
        落地剩余 = Mathf.Max(0.01f, 落地时长);
        切换到(FlightState.落地);
    }

    void 切换到(FlightState s)
    {
        if (状态 == s) return;
        状态 = s;
        if (打印状态切换) Debug.Log("[凭虚御风] -> " + s, this);
        状态变化?.Invoke(s);
    }

    /// <summary>强制落地（例如面板打开、角色死亡时）。**连「想飞」的开关一起关掉** ——
    /// 否则关了面板 / 复活之后又自己飞起来。</summary>
    public void 强制落地()
    {
        想飞 = false;
        if (状态 == FlightState.地面) return;
        高度偏移 = 0f;
        切换到(FlightState.地面);
    }

    // ---- ASCII 别名 ----
    public FlightState State => 状态;
    public bool IsFlying => 御风中;
    public bool InFlightFlow => 御风流程中;
    public float HeightOffset => 高度偏移;
    public float GroundY => 地面高度;
    public bool AbilityEnabled => 神通已启用;
    public float SpiritCostPerSecond => 实际每秒消耗;
    public float FlySpeed { get => 飞行速度; set => 飞行速度 = value; }
    public float FlyHeight { get => 飞行高度; set => 飞行高度 = value; }
    public bool ForceFly { get => 强制御风; set => 强制御风 = value; }
    /// <summary>切换键按下的「想飞」开关（按一下开、再按一下关）</summary>
    public bool WantFly => 想飞;
    public void ForceLand() => 强制落地();
    public void ToggleFlight() => 切换();
}
