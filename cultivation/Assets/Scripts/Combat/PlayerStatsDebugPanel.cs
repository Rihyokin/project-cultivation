using UnityEngine;

/// <summary>
/// 临时调试面板（IMGUI 实现，不依赖任何 prefab / Canvas）。
///
/// 用途：把玩家的【全部数值】都做成可实时拖动/输入，方便测试。
/// 打开方式：默认 F1 键。改动即刻生效（走 PlayerCombatStats 的调试数值覆盖）。
///
/// 面板关闭时如果还开着「调试覆盖」，数值依然生效；点【恢复常规结算】才会关掉。
/// </summary>
public class PlayerStatsDebugPanel : MonoBehaviour
{
    [Header("开关")]
    [Tooltip("显示/隐藏调试面板的按键")]
    public KeyCode 开关按键 = KeyCode.F1;

    [Tooltip("进入游戏时是否默认显示")]
    public bool 启动时显示 = true;

    [Header("引用")]
    [Tooltip("留空则自动在自身找 PlayerCombatStats")]
    public PlayerCombatStats 玩家战斗属性;

    [Header("外观")]
    public float 面板宽度 = 380f;
    public float 面板高度 = 640f;
    public int 字号 = 13;

    bool 显示;
    Vector2 滚动;
    GUIStyle 标题样式, 行样式;
    string[] 编辑缓存;

    const float 标签宽 = 130f;

    void Awake()
    {
        if (玩家战斗属性 == null) 玩家战斗属性 = GetComponent<PlayerCombatStats>();
        显示 = 启动时显示;
        编辑缓存 = new string[AttributeUtil.Count];
    }

    void Update()
    {
        if (Input.GetKeyDown(开关按键)) 显示 = !显示;
    }

    void EnsureStyles()
    {
        if (标题样式 != null) return;
        标题样式 = new GUIStyle(GUI.skin.label) { fontSize = 字号 + 2, fontStyle = FontStyle.Bold };
        行样式 = new GUIStyle(GUI.skin.label) { fontSize = 字号 };
    }

    void OnGUI()
    {
        if (!显示 || 玩家战斗属性 == null) return;
        EnsureStyles();

        // 用屏幕高度，而不是只用 面板高度 这个字段。
        // 字段是序列化的，改默认值不会影响场景里已有的组件，所以加上
        // 「召唤 NPC」之后面板总内容超过了 640，召唤按钮正好被挤出窗口
        // 下边缘 —— 表现就是"看不到召唤按钮"。
        float 高 = Mathf.Max(面板高度, Screen.height - 24f);
        var rect = new Rect(12f, 12f, 面板宽度, 高);
        GUILayout.BeginArea(rect, GUI.skin.box);

        GUILayout.Label("玩家数值调试  (" + 开关按键 + " 开关)", 标题样式);
        GUILayout.Space(4f);

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("从当前值同步")) { 玩家战斗属性.同步调试数值(); 清空缓存(); }
        if (GUILayout.Button("恢复常规结算")) { 玩家战斗属性.关闭调试数值(); 清空缓存(); }
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("载入初始默认值")) { 载入默认(); }
        if (GUILayout.Button("全部清零")) { 全部清零(); }
        GUILayout.EndHorizontal();

        GUILayout.Space(4f);

        // 这里原来是一个 Toggle，直接改 使用调试数值 这个 bool 字段。
        // 问题是：那个字段是裸的，改它【不会触发 Recalculate()】，
        // 所以勾上以后 当前属性 还是旧的，要等别的事件才刷新 —— 表现就是"勾了没反应"。
        // 换成按钮，走 UseDebugStats 属性（内部会 Recalculate），再补一次重算兜底。
        bool 已在用调试值 = 玩家战斗属性.使用调试数值;
        var 原色 = GUI.backgroundColor;
        GUI.backgroundColor = 已在用调试值 ? new Color(1f, 0.75f, 0.4f) : new Color(0.55f, 1f, 0.6f);
        if (GUILayout.Button(已在用调试值 ? "✔ 修改已应用（点此重新应用）" : "应用修改到角色", GUILayout.Height(28f)))
        {
            玩家战斗属性.UseDebugStats = true;     // 会 Recalculate
            // 【不要在这里调 同步调试数值()】—— 它会把 调试数值 覆盖成「基础属性+功法」，
            // 于是刚改的数值（比如攻速 2）会立刻被顶回默认值（1）。那个按钮单独留着。
            玩家战斗属性.Recalculate();            // 双保险
            清空缓存();
            Debug.Log("[调试面板] 已把修改后的数值应用到角色身上");
        }
        GUI.backgroundColor = 原色;

        GUILayout.Label(已在用调试值
            ? "当前：使用调试数值（覆盖 基础属性 + 功法）"
            : "当前：使用常规结算", 行样式);

        // 召唤区放在「应用修改到角色」正下方 —— 之前放在属性列表底下，
        // 面板内容超高，按钮被挤出窗口下边缘，压根看不见。
        画召唤区();
        GUILayout.Space(4f);

        // ---- 独立字段 ----
        GUILayout.Label("—— 修炼相关 ——", 行样式);
        GUILayout.Label("境界：" + (玩家战斗属性.玩家属性 != null && 玩家战斗属性.玩家属性.境界 != null
                    ? 玩家战斗属性.玩家属性.境界.境界名 : "未设定") + "（暂不可调试）", 行样式);
        玩家战斗属性.调试神识 = 行("神识", 玩家战斗属性.调试神识);
        玩家战斗属性.调试吐纳速度 = 行("吐纳速度", 玩家战斗属性.调试吐纳速度);
        同步独立字段();

        GUILayout.Space(6f);
        GUILayout.Label("—— 26 项战斗属性 ——", 行样式);

        // 属性列表占「窗口高 - 500」：上面约 230、下面的召唤区约 230、底部一行约 20。
        // 加 160 的下限，免得窗口被拖得很矮时这个滚动区变成负数、整个布局崩掉。
        滚动 = GUILayout.BeginScrollView(滚动, GUILayout.Height(Mathf.Max(160f, rect.height - 520f)));
        for (int i = 0; i < AttributeUtil.Count; i++)
        {
            var t = (AttributeType)i;
            玩家战斗属性.调试数值[t] = 行(AttributeUtil.GetDisplayName(t), 玩家战斗属性.调试数值[t]);
        }
        GUILayout.EndScrollView();

        GUILayout.Space(4f);
        GUILayout.Label("当前生效值：" + 玩家战斗属性.当前属性.ToReadableString(), 行样式);

        GUILayout.EndArea();
        if (玩家战斗属性.使用调试数值) 玩家战斗属性.Recalculate();
    }

    /// <summary>第一次打开面板时，把当前实际值灌进调试数值，避免从 0 开始</summary>
    void 同步独立字段()
    {
        if (玩家战斗属性.调试神识 == 0f && 玩家战斗属性.调试吐纳速度 == 0f && !独立字段已同步)
        {
            玩家战斗属性.调试神识 = PlayerDefaultStats.默认神识;
            玩家战斗属性.调试吐纳速度 = PlayerDefaultStats.默认吐纳速度;
            独立字段已同步 = true;
        }
    }
    bool 独立字段已同步;

    /// <summary>一行：标签 + 输入框，返回解析后的值</summary>
    float 行(string label, float value)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, 行样式, GUILayout.Width(标签宽));
        string cached = 编辑缓存[(int)FindIndex(label)];
        if (cached == null || !float.TryParse(cached, out float parsed) || !Mathf.Approximately(parsed, value))
            编辑缓存[(int)FindIndex(label)] = value.ToString("0.####");
        string text = GUILayout.TextField(编辑缓存[(int)FindIndex(label)], GUILayout.Width(90f));
        if (text != 编辑缓存[(int)FindIndex(label)])
        {
            编辑缓存[(int)FindIndex(label)] = text;
            if (float.TryParse(text, out float v)) value = v;
        }
        if (AttributeUtil.IsPercent((AttributeType)FindIndex(label)))
            GUILayout.Label("(" + (value * 100f).ToString("0.#") + "%)", 行样式);
        GUILayout.EndHorizontal();
        return value;
    }

    static int FindIndex(string displayName)
    {
        for (int i = 0; i < AttributeUtil.Count; i++)
            if (AttributeUtil.GetDisplayName((AttributeType)i) == displayName) return i;
        return 0;
    }

    void 清空缓存()
    {
        if (编辑缓存 == null) return;
        for (int i = 0; i < 编辑缓存.Length; i++) 编辑缓存[i] = null;
    }

    void 载入默认()
    {
        var d = PlayerDefaultStats.Create();
        for (int i = 0; i < AttributeUtil.Count; i++)
            玩家战斗属性.调试数值[(AttributeType)i] = d[(AttributeType)i];
        玩家战斗属性.调试神识 = PlayerDefaultStats.默认神识;
        玩家战斗属性.调试吐纳速度 = PlayerDefaultStats.默认吐纳速度;
        玩家战斗属性.使用调试数值 = true;
        清空缓存();
    }

    void 全部清零()
    {
        玩家战斗属性.调试数值.Clear();
        玩家战斗属性.调试神识 = 0f;
        玩家战斗属性.调试吐纳速度 = 0f;
        玩家战斗属性.使用调试数值 = true;
        清空缓存();
    }

    // ---- ASCII 别名 ----
    public bool Visible { get => 显示; set => 显示 = value; }
    public void Toggle() => 显示 = !显示;
    // ================================================================
    // NPC 召唤（调试用）
    //
    // 列表直接从 Resources 加载 —— resources/NPC 本身就是 Resources 目录，
    // 所以 Resources.LoadAll<GameObject>("NPC") 能一次拿到全部 261 个 NPC prefab，
    // 不用在 Inspector 里接任何引用。
    // ================================================================

    GameObject[] 全部NPC;
    string[] 全部NPC名;
    Vector2 召唤滚动;
    int 选中NPC = -1;

    void 加载NPC列表()
    {
        if (全部NPC != null) return;
        var 全部 = Resources.LoadAll<GameObject>("NPC");
        var 列表 = new System.Collections.Generic.List<GameObject>();
        foreach (var g in 全部)
        {
            if (g == null) continue;
            // 只留我们组装好的 prefab：必须有 Animator 且接了控制器。
            // 原始 FBX 也躺在 Resources 里（跟 prefab 同名或差个 _01），
            // 不过滤的话列表会成对重复，而且召唤出来的 FBX 不会动。
            var an = g.GetComponent<Animator>();
            if (an == null || an.runtimeAnimatorController == null) continue;
            if (g.GetComponentInChildren<SkinnedMeshRenderer>(true) == null) continue;
            列表.Add(g);
        }
        列表.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
        全部NPC = 列表.ToArray();
        全部NPC名 = new string[全部NPC.Length];
        for (int i = 0; i < 全部NPC.Length; i++) 全部NPC名[i] = 全部NPC[i].name;
    }

    void 画召唤区()
    {
        加载NPC列表();
        GUILayout.Space(6f);
        GUILayout.Label("—— 召唤 NPC ——", 行样式);
        GUILayout.Label("共 " + 全部NPC.Length + " 个，当前选中：" +
            (选中NPC >= 0 ? 全部NPC名[选中NPC] : "（未选）"), 行样式);

        召唤滚动 = GUILayout.BeginScrollView(召唤滚动, GUILayout.Height(110f));
        for (int i = 0; i < 全部NPC名.Length; i++)
        {
            var 原 = GUI.backgroundColor;
            if (i == 选中NPC) GUI.backgroundColor = new Color(1f, 0.85f, 0.45f);
            if (GUILayout.Button(全部NPC名[i]))
            {
                选中NPC = i;
                Debug.Log("[调试面板] 选中 NPC：" + 全部NPC名[i]);
            }
            GUI.backgroundColor = 原;
        }
        GUILayout.EndScrollView();

        GUILayout.Space(4f);
        var 旧色 = GUI.backgroundColor;
        GUI.backgroundColor = 选中NPC >= 0 ? new Color(0.55f, 1f, 0.6f) : new Color(0.6f, 0.6f, 0.6f);
        GUI.enabled = 选中NPC >= 0;
        if (GUILayout.Button("召唤选中 NPC", GUILayout.Height(30f)))
        {
            var prefab = 全部NPC[选中NPC];
            // 召在玩家正前方 2 米
            var 玩家 = 玩家战斗属性 != null ? 玩家战斗属性.transform : null;
            Vector3 位 = 玩家 != null ? 玩家.position + 玩家.forward * 2f : Vector3.zero;
            var go = Instantiate(prefab, 位, Quaternion.identity);
            go.name = prefab.name + "_召唤";
            Debug.Log("[调试面板] 已召唤 " + prefab.name + " 到 " + 位);
        }
        GUI.enabled = true;
        GUI.backgroundColor = 旧色;
    }
}
