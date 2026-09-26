using System;
using UnityEngine;

/// <summary>
/// **人类 NPC 的对话模块**：靠近出「F 对话」提示、idle 时只转头看玩家、按 F 开对话框。
///
/// 三条规则（和用户确认过的设计）：
///   ① 提示：复用项目里建筑那一套 <see cref="StationInteractable"/> + <see cref="StationInteractor"/>，
///      **不做第二套**。本脚本只在 Start 时确保 NPC 上挂着一个「类型=对话、交互键=F」的设施。
///   ② 朝向：玩家进入 <see cref="看向距离"/> 才看；此时若只需要转头就能对上（偏角≤<see cref="最大偏角"/>）
///      就**只转脖子/头**、身体不动；超过这个偏角才让身体转（沿用 NpcAiHuman 原来的转身，带阻尼）。
///      离开范围立刻把头恢复原状。
///   ③ 按 F：<see cref="请求对话"/> 事件广播出去（对话框 UI 订阅）。若设施上已经配了「界面预制体」，
///      就交给 StationInteractor 自己开界面，本脚本不再抢 F 键（避免开两次）。
///
/// 头部旋转用**世界 Y 轴偏移 + 还原基准局部旋转**的算法，因此不挑骨骼朝向（模型正面是 +X 也不影响），
/// 身体在转的时候头也不会跟着歪。
/// </summary>
[DisallowMultipleComponent]
public class NpcDialogue : MonoBehaviour
{
    [Header("范围")]
    [Tooltip("玩家进这个距离才会看向玩家")]
    public float 看向距离 = 8f;

    [Tooltip("只要在这个距离内、且偏角不超过「最大偏角」，就只转头不转身")]
    public float 只转头距离 = 4.5f;

    [Tooltip("只转头的最大偏角（度）。超过它就交给身体转")]
    public float 最大偏角 = 70f;

    [Header("骨骼")]
    [Tooltip("模型朝向补偿角。运行时优先取 NpcAiBase.模型朝向补偿（本项目村民=90）；没有 AI 时用这个值")]
    public float 朝向补偿 = 90f;

    [Tooltip("头骨名字关键字，找不到就用备用")]
    public string 头部骨骼关键字 = "Head";

    public string 备用骨骼关键字 = "Neck";

    [Header("互动")]
    [Tooltip("靠近提示的交互距离")]
    public float 交互距离 = 3.5f;

    [Tooltip("按这个键对话（提示里显示的字也按它走）")]
    public KeyCode 交互键 = KeyCode.F;

    /// <summary>按 F 且当前没有对话框时广播。对话框 UI / 剧情系统订阅它。</summary>
    public static event Action<NpcDialogue> 请求对话;

    /// <summary>当前正在对话（用来冻结移动 / 屏蔽别的 NPC 抢 F）</summary>
    public static NpcDialogue 当前对话中;

    public Transform 头部 { get; private set; }

    Transform 头部父;
    Quaternion 头部基准局部;
    bool 已存基准;
    StationInteractable 设施;

    void Start()
    {
        确保设施();
    }

    /// <summary>确保 NPC 身上有「F 对话」设施（建筑那套），没有就补一个</summary>
    public void 确保设施()
    {
        if (设施 == null) 设施 = GetComponent<StationInteractable>();
        if (设施 == null) 设施 = gameObject.AddComponent<StationInteractable>();
        设施.类型 = StationInteractable.StationKind.对话;
        设施.交互键覆盖 = 交互键;
        设施.交互距离 = 交互距离;
        if (string.IsNullOrEmpty(设施.显示名)) 设施.显示名 = gameObject.name;
    }

    /// <summary>本 NPC 的配置表 id（对话表用它找分段）</summary>
    public string 取NpcId()
    {
        var inst = GetComponent<NpcInstance>();
        if (inst != null && inst.定义 != null && !string.IsNullOrEmpty(inst.定义.id)) return inst.定义.id;
        return gameObject.name;
    }

    /// <summary>调试用：最近一帧的决策结果（只读）</summary>
    public string 最近决策 { get; private set; } = "未跑";
    public int 转头帧数 { get; private set; }
    public int 转身帧数 { get; private set; }
    public int 不跑帧数 { get; private set; }

    void LateUpdate()
    {
        var 玩家 = 取玩家();
        if (玩家 == null) { 最近决策 = "没有玩家引用"; 不跑帧数++; 恢复头(); return; }

        // 战斗中 / 敌对，就不看也不给对话
        // 注：这里**不**看 NpcInstance.IsDead —— 运行时刚实例化的 NPC 气血还没初始化，
        // IsDead 会是 true，会把「看向玩家」整个掐掉。真死了的 NPC 通常已被 AI/收回流程接管。
        var 人类 = GetComponent<NpcAiHuman>();
        bool 可看 = 人类 == null || 人类.现在可对话;
        float 距离 = Vector3.Distance(transform.position, 玩家.position);

        if (!可看 || 距离 > 看向距离)
        {
            最近决策 = !可看 ? "不可看" : ("超范围 " + 距离.ToString("F1"));
            不跑帧数++;
            恢复头();
            置身体转向(true);
            return;
        }

        // 目标方向（水平面上）
        Vector3 方向 = 玩家.position - transform.position;
        方向.y = 0f;
        if (方向.sqrMagnitude < 0.0001f) { 恢复头(); return; }

        // 需要转多少度才算面向玩家 —— **完全复用项目自己的朝向公式**
        // （NpcAiBase.转向 就是 `LookRotation(方向) * Euler(0, 模型朝向补偿, 0)`），
        // 这样头和身体的"正面"认定必然一致，不用去猜模型正面是 +X 还是 +Z。
        float 补偿 = 朝向补偿;
        var ai = GetComponent<NpcAiBase>();
        if (ai != null) 补偿 = ai.模型朝向补偿;
        Quaternion 面向玩家 = Quaternion.LookRotation(方向.normalized, Vector3.up) * Quaternion.Euler(0f, 补偿, 0f);
        float 偏角 = Mathf.DeltaAngle(transform.rotation.eulerAngles.y, 面向玩家.eulerAngles.y);

        bool 只转头 = 距离 <= 只转头距离 && Mathf.Abs(偏角) <= 最大偏角;
        置身体转向(!只转头);

        if (只转头) { 最近决策 = "只转头 偏角" + 偏角.ToString("F1") + " 距" + 距离.ToString("F1"); 转头帧数++; 转(方向, 偏角); }
        else { 最近决策 = "转身 偏角" + 偏角.ToString("F1") + " 距" + 距离.ToString("F1"); 转身帧数++; 恢复头(); }
    }

    /// <summary>把 AI 的整体转身按需打开/关掉（挡在 NpcAiBase.转向 源头，所有调用路径都拦得住）</summary>
    void 置身体转向(bool 开)
    {
        var ai = GetComponent<NpcAiBase>();
        if (ai != null) ai.禁止身体转向 = !开;
    }

    void 转(Vector3 水平方向, float 偏角)
    {
        if (头部 == null) 找头();
        if (头部 == null) return;

        // 头只转 Y 轴：基准（父的世界旋转 × 初始局部）再绕世界 Y 偏一点
        Quaternion 基准世界 = 头部父.rotation * 头部基准局部;
        Quaternion 目标世界 = Quaternion.AngleAxis(偏角, Vector3.up) * 基准世界;
        头部.localRotation = Quaternion.Inverse(头部父.rotation) * 目标世界;
    }

    void 恢复头()
    {
        if (头部 != null && 已存基准) 头部.localRotation = 头部基准局部;
    }

    void 找头()
    {
        Transform 备用 = null;
        foreach (var t in GetComponentsInChildren<Transform>(true))
        {
            string n = t.name;
            if (头部 == null && n.IndexOf(头部骨骼关键字, StringComparison.OrdinalIgnoreCase) >= 0) 头部 = t;
            else if (备用 == null && n.IndexOf(备用骨骼关键字, StringComparison.OrdinalIgnoreCase) >= 0) 备用 = t;
        }
        // 有头骨优先；否则退到脖子
        if (头部 == null || 头部 == transform) 头部 = 备用;
        if (头部 == null || 头部 == transform) { 头部 = null; return; }
        头部父 = 头部.parent != null ? 头部.parent : transform;
        头部基准局部 = 头部.localRotation;
        已存基准 = true;
    }

    void Update()
    {
        if (设施 != null && 设施.界面预制体 != null) return;   // 交给 StationInteractor 开
        if (当前对话中 != null && 当前对话中 != this) return;
        if (Input.GetKeyDown(交互键) && 是最近的可对话目标()) 打开对话();
    }

    /// <summary>和 StationInteractor 同一条规则：谁根节点更近就归谁</summary>
    bool 是最近的可对话目标()
    {
        var 玩家 = 取玩家();
        if (玩家 == null) return false;
        float 我 = Vector3.Distance(transform.position, 玩家.position);
        if (我 > 交互距离) return false;
        foreach (var 他 in FindObjectsOfType<NpcDialogue>())
        {
            if (他 == this) continue;
            var 他人类 = 他.GetComponent<NpcAiHuman>();
            if (他人类 != null && !他人类.现在可对话) continue;
            if (Vector3.Distance(他.transform.position, 玩家.position) < 我) return false;
        }
        return true;
    }

    // 玩家引用缓存（PlayerVitals 挂在玩家身上，场景里只有一个）
    static Transform 缓存玩家;

    static Transform 取玩家()
    {
        if (缓存玩家 == null)
        {
            var v = FindObjectOfType<PlayerVitals>();
            if (v != null) 缓存玩家 = v.transform;
        }
        return 缓存玩家;
    }

    /// <summary>真正打开对话：广播给 UI；没人接就只记一条日志（方便逐步接）</summary>
    public void 打开对话()
    {
        if (请求对话 == null) { Debug.Log("[对话] 还没有对话框 UI 订阅，暂时只记一条：" + gameObject.name); return; }
        请求对话(this);
    }

    void OnDisable() { 置身体转向(true); 恢复头(); }
}
