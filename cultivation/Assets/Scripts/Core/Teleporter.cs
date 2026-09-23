using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// 传送光圈：玩家踏进来 → 弹出「是否传送？」→ 确认后传送。
///
/// 一个组件同时管两种传送：
///   · **场景间**：选项里填了场景名 → `SceneManager.LoadScene`，再在新场景里找落点物体
///   · **场景内**：场景名留空 → 直接把玩家挪到本场景里的落点物体
///
/// 一个光圈可以给**多个选项**（例如镇妖塔的光圈：①进入下一层 ②出塔），
/// 选项可标记「暂未开放」→ 界面上灰掉、点了提示"尚未开放"。
///
/// ★ 前置条件：<see cref="场景"/> 里写的场景必须加进 **Build Settings**，否则 LoadScene 会失败。
/// </summary>
[DisallowMultipleComponent]
public class Teleporter : MonoBehaviour
{
    [System.Serializable]
    public class 传送选项
    {
        [Tooltip("按钮上显示的名字，例如「出塔」")]
        public string 名称 = "传送";
        [Tooltip("目标场景名（不要带路径/扩展名）。**留空 = 本场景内传送**")]
        public string 场景 = "";
        [Tooltip("落点物体名。跨场景时在新场景里找；同场景时在本场景里找")]
        public string 落点 = "SpawnPoint";
        [Tooltip("还没做好的选项勾上 → 界面灰掉、点了只提示「尚未开放」")]
        public bool 暂未开放 = false;
    }

    [Header("选项")]
    public 传送选项[] 选项 = new 传送选项[0];

    [Header("触发")]
    [Tooltip("光圈触发半径（米）。留 0 则用物体上已有的 Trigger 碰撞体")]
    public float 半径 = 1.8f;
    [Tooltip("离开光圈后多久才能再次触发（秒）")]
    public float 冷却 = 0.5f;

    [Header("界面")]
    [Tooltip("提示标题")]
    public string 标题 = "是否传送？";
    [Tooltip("打印日志")]
    public bool 打印日志 = false;

    bool 面板开着;
    bool 在圈内;
    float 冷却到;
    int 上次选项 = -1;
    GameObject 面板;
    readonly List<Button> 按钮s = new List<Button>();
    Text 文本;
    Transform 玩家;

    void Reset()
    {
        name = "Teleport";
        if (选项 == null || 选项.Length == 0)
            选项 = new[] { new 传送选项 { 名称 = "传送", 场景 = "", 落点 = "SpawnPoint" } };
    }

    void Awake()
    {
        // 没有 Trigger 就自己补一个球（用光圈粒子的尺寸估不出来，所以用 半径 参数）
        if (GetComponent<Collider>() == null && 半径 > 0.01f)
        {
            var sc = gameObject.AddComponent<SphereCollider>();
            sc.isTrigger = true;
            sc.radius = 半径;
        }
    }

    void OnTriggerEnter(Collider 其他)
    {
        if (Time.unscaledTime < 冷却到) return;
        var p = 找玩家(其他);
        if (p == null) return;
        玩家 = p;
        在圈内 = true;
        if (!面板开着) 开面板();
    }

    void OnTriggerExit(Collider 其他)
    {
        if (找玩家(其他) == null) return;
        在圈内 = false;
        冷却到 = Time.unscaledTime + 冷却;
        关面板();
    }

    static Transform 找玩家(Collider c)
    {
        if (c == null) return null;
        var v = c.GetComponentInParent<PlayerVitals>();
        if (v != null) return v.transform;
        if (c.transform.root != null && c.transform.root.name == "Player") return c.transform.root;
        return null;
    }

    // ---------------- 面板 ----------------

    void 开面板()
    {
        if (面板 == null) 建面板();
        面板.SetActive(true);
        面板开着 = true;
        刷新文本();
        if (打印日志) Debug.Log("[传送] 「" + name + "」弹出选项（" + 选项.Length + " 个）", this);
    }

    void 关面板()
    {
        if (面板 != null) 面板.SetActive(false);
        面板开着 = false;
    }

    void 刷新文本()
    {
        if (文本 == null) return;
        string s = 标题;
        for (int i = 0; i < 选项.Length; i++)
            if (选项[i] != null && 选项[i].暂未开放) s += "\n（「" + 选项[i].名称 + "」尚未开放）";
        文本.text = s;
    }

    void 建面板()
    {
        // 借场景里已有 UI 的字体，避免依赖内置字体（团结引擎里名称可能不同）
        Font 字 = null;
        foreach (var t in Resources.FindObjectsOfTypeAll<Text>())
            if (t.font != null && t.gameObject.scene.IsValid()) { 字 = t.font; break; }

        var canvasGo = new GameObject("传送面板", typeof(Canvas), typeof(GraphicRaycaster), typeof(CanvasScaler));
        面板 = canvasGo;
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 500;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        var 底板 = new GameObject("底板", typeof(Image));
        底板.transform.SetParent(canvasGo.transform, false);
        var rt = 底板.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(460f, 120f + 56f * Mathf.Max(1, 选项.Length));
        rt.anchoredPosition = new Vector2(0f, 40f);
        底板.GetComponent<Image>().color = new Color(0.06f, 0.05f, 0.07f, 0.92f);

        var 文go = new GameObject("文本", typeof(Text));
        文go.transform.SetParent(底板.transform, false);
        var 文rt = 文go.GetComponent<RectTransform>();
        文rt.anchorMin = new Vector2(0f, 1f); 文rt.anchorMax = new Vector2(1f, 1f);
        文rt.pivot = new Vector2(0.5f, 1f);
        文rt.sizeDelta = new Vector2(-24f, 96f);
        文rt.anchoredPosition = new Vector2(0f, -14f);
        文本 = 文go.GetComponent<Text>();
        文本.font = 字; 文本.fontSize = 26; 文本.alignment = TextAnchor.UpperCenter;
        文本.color = new Color(0.95f, 0.92f, 0.8f);

        按钮s.Clear();
        for (int i = 0; i < 选项.Length; i++)
        {
            int 序号 = i;                                   // 闭包捕获
            var bgo = new GameObject("按钮_" + 选项[i].名称, typeof(Image), typeof(Button));
            bgo.transform.SetParent(底板.transform, false);
            var brt = bgo.GetComponent<RectTransform>();
            brt.anchorMin = new Vector2(0.5f, 0f); brt.anchorMax = new Vector2(0.5f, 0f);
            brt.pivot = new Vector2(0.5f, 0f);
            brt.sizeDelta = new Vector2(400f, 46f);
            brt.anchoredPosition = new Vector2(0f, 18f + 56f * (选项.Length - 1 - i));
            bool 灰 = 选项[i] == null || 选项[i].暂未开放;
            bgo.GetComponent<Image>().color = 灰 ? new Color(0.22f, 0.2f, 0.22f, 1f) : new Color(0.18f, 0.32f, 0.22f, 1f);
            var bt = bgo.GetComponent<Button>();
            bt.interactable = !灰;
            if (!灰) bt.onClick.AddListener(() => 执行(序号));

            var tgo = new GameObject("字", typeof(Text));
            tgo.transform.SetParent(bgo.transform, false);
            var trt = tgo.GetComponent<RectTransform>();
            trt.anchorMin = Vector3.zero; trt.anchorMax = Vector3.one;
            trt.sizeDelta = Vector2.zero;
            var tt = tgo.GetComponent<Text>();
            tt.font = 字; tt.fontSize = 24; tt.alignment = TextAnchor.MiddleCenter;
            tt.color = 灰 ? new Color(0.55f, 0.53f, 0.55f) : Color.white;
            tt.text = (选项[i] == null ? "?" : 选项[i].名称) + (灰 ? "（未开放）" : "");
            按钮s.Add(bt);
        }
    }

    // ---------------- 执行 ----------------

    /// <summary>界面按钮调这个；也可以从别处（剧情/快捷键）直接调</summary>
    public void 执行(int 序号)
    {
        if (序号 < 0 || 序号 >= 选项.Length || 选项[序号] == null) return;
        var o = 选项[序号];
        if (o.暂未开放)
        {
            if (打印日志) Debug.Log("[传送]「" + o.名称 + "」尚未开放", this);
            return;
        }
        if (打印日志) Debug.Log("[传送] 执行「" + o.名称 + "」→ 场景「" + o.场景 + "」落点「" + o.落点 + "」", this);

        关面板();
        if (string.IsNullOrEmpty(o.场景)) 同场景传送(o);
        else 跨场景传送(o);
    }

    void 同场景传送(传送选项 o)
    {
        var 点 = 找落点(o.落点, SceneManager.GetActiveScene());
        if (点 == null) { Debug.LogWarning("[传送] 本场景里找不到落点「" + o.落点 + "」", this); return; }
        放下玩家(点);
    }

    /// <summary>
    /// ★★ 跨场景传送必须交给一个**不随场景销毁的宿主**去做。
    ///
    /// 【为什么不能用本组件自己的协程】`LoadSceneMode.Single` 会销毁旧场景里所有物体 ——
    /// 包括**挂在本光圈上的这个组件**。协程是挂在组件上的，组件没了协程当场中断 ✗，
    /// 于是"加载完成后找落点、把玩家放过去"那段**永远不会执行**。
    /// 症状极具迷惑性：场景确实切过去了、目标物体也确实存在，但玩家留在**新场景里保存的位置**
    /// （实测：从 3C 传到 Sect，玩家落在 Sect 存档点 (-83.6, 14.2, 89.9)，离目标 91m ✗）。
    /// </summary>
    void 跨场景传送(传送选项 o)
    {
        var 宿主物体 = new GameObject("传送宿主");
        Object.DontDestroyOnLoad(宿主物体);
        var 宿主 = 宿主物体.AddComponent<传送宿主>();
        宿主.开始(o.场景, o.落点);
    }

    /// <summary>跨场景传送的宿主：活在 DontDestroyOnLoad 上，加载完新场景后落地，然后自毁</summary>
    public class 传送宿主 : MonoBehaviour
    {
        public void 开始(string 场景, string 落点) { StartCoroutine(跑(场景, 落点)); }

        System.Collections.IEnumerator 跑(string 场景, string 落点)
        {
            yield return null;                                   // 让确认框先收起来
            var op = SceneManager.LoadSceneAsync(场景, LoadSceneMode.Single);
            if (op == null) { Debug.LogError("[传送] 加载场景「" + 场景 + "」失败 —— 它加进 Build Settings 了吗？"); 自毁(); yield break; }
            while (!op.isDone) yield return null;
            yield return null;                                   // 等新场景 Awake/Start
            yield return new WaitForEndOfFrame();

            var 点 = 找落点(落点, SceneManager.GetActiveScene());
            if (点 == null) { Debug.LogWarning("[传送] 新场景「" + 场景 + "」里找不到落点「" + 落点 + "」"); 自毁(); yield break; }
            放下玩家(点);
            自毁();
        }

        void 自毁() { if (this != null && gameObject != null) Destroy(gameObject); }
    }

    static Transform 找落点(string 名, Scene 场景)
    {
        if (string.IsNullOrEmpty(名)) return null;
        foreach (var g in 场景.GetRootGameObjects())
        {
            if (g.name == 名) return g.transform;
            foreach (var t in g.GetComponentsInChildren<Transform>(true))
                if (t.name == 名) return t.transform;
        }
        return null;
    }

    static void 放下玩家(Transform 点)
    {
        var 玩 = Object.FindObjectOfType<PlayerVitals>();
        if (玩 == null) { Debug.LogWarning("[传送] 场景里找不到玩家（PlayerVitals）"); return; }
        var cc = 玩.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;                  // 关掉再挪，免得被"推出"
        玩.transform.position = 点.position;
        玩.transform.rotation = 点.rotation;
        if (cc != null) cc.enabled = true;
        var 骑 = 玩.GetComponent<MountRider>();
        if (骑 != null) 骑.强制下坐骑();                      // 传送时先下坐骑，免得坐骑跟丢
        if (点.name != null) Debug.Log("[传送] 玩家已放到「" + 点.name + "」 " + 点.position.ToString("F2"));
    }
}
