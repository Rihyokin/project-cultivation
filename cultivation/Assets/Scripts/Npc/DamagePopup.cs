using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 受伤飘字：NPC 挨打时在旁边弹出一个伤害数字，**1 秒后自动消失**。
/// **暴击 / 会心的字号更大**，颜色也不同。
///
/// 整块是运行时现建的（WorldSpace Canvas + Text）：
/// 伤害数字只有阿拉伯数字，用 Unity 内置字体 `LegacyRuntime.ttf` 就够，
/// 不需要美术资源，也不需要往场景里摆任何东西。
///
/// 谁负责生成：<see cref="NpcIndicator"/> 订阅 <see cref="NpcInstance.结算完成"/> 后调用
/// <see cref="弹出"/> —— 所以飞剑、普攻、神通全都自动有飘字，不用各自接。
///
/// 手感想全局调整就改 <see cref="默认样式"/> 上的字段。
/// </summary>
public class DamagePopup : MonoBehaviour
{
    /// <summary>可调样式</summary>
    [System.Serializable]
    public class 样式
    {
        [Tooltip("存活多久后消失（秒）")]
        public float 存活时长 = 1f;

        [Tooltip("这段时间内往上飘多高（米）")]
        public float 上浮高度 = 1.1f;

        [Tooltip("横向随机散开的最大距离（米），免得连续几下叠在一起")]
        public float 横向漂移 = 0.5f;

        [Tooltip("刚出现时的缩放，会有个「砰」一下的感觉")]
        public float 起始缩放 = 0.55f;

        [Tooltip("从初始缩放弹到 1 倍所用的时间（秒）")]
        public float 弹出时间 = 0.09f;

        [Tooltip("存活进度超过这个比例后开始淡出（0~1）")]
        public float 淡出起点 = 0.6f;

        [Header("字号：暴击 / 会心稍大")]
        public int 普通字号 = 34;
        public int 暴击字号 = 46;

        [Header("颜色")]
        public Color 普通色 = new Color(1f, 0.96f, 0.86f, 1f);
        public Color 暴击色 = new Color(1f, 0.70f, 0.16f, 1f);
        public Color 会心色 = new Color(0.52f, 0.86f, 1f, 1f);
        public Color 描边色 = new Color(0f, 0f, 0f, 0.9f);

        [Tooltip("Canvas 排序层级，要比 NPC 血条（100）高")]
        public int 排序层级 = 200;

        [Tooltip("世界空间 Canvas 的缩放：0.01 = 100 画布单位等于 1 米")]
        public float 画布缩放 = 0.01f;
    }

    /// <summary>全局默认样式</summary>
    public static 样式 默认样式 = new 样式();

    /// <summary>总开关，想临时全关就设 false</summary>
    public static bool 开关 = true;

    static Font 内置字体;

    Text 文字;
    Camera 主相机;
    样式 风格;
    float 起始时间;
    Vector3 起点;
    Vector3 漂移;

    /// <summary>弹一个伤害数字。暴击 / 会心会放大字号并换色。</summary>
    public static DamagePopup 弹出(Vector3 世界位置, float 伤害, bool 暴击, bool 会心)
    {
        if (!开关) return null;

        var go = new GameObject("DamagePopup", typeof(RectTransform));
        go.transform.position = 世界位置;
        var p = go.AddComponent<DamagePopup>();
        p.初始化(世界位置, 伤害, 暴击, 会心);
        return p;
    }

    void 初始化(Vector3 世界位置, float 伤害, bool 暴击, bool 会心)
    {
        风格 = 默认样式 ?? new 样式();
        起点 = 世界位置;
        起始时间 = Time.time;

        // 随机散开的方向（水平面内）
        float 角度 = Random.Range(0f, Mathf.PI * 2f);
        漂移 = new Vector3(Mathf.Cos(角度), 0f, Mathf.Sin(角度))
             * Random.Range(0.35f, 1f) * 风格.横向漂移;

        // ---- 世界空间 Canvas ----
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 风格.排序层级;

        var rt = GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(240f, 90f);
        rt.localScale = Vector3.one * (风格.画布缩放 * 风格.起始缩放);

        // ---- 数字 ----
        var textGo = new GameObject("Text", typeof(RectTransform));
        textGo.transform.SetParent(transform, false);
        var trt = textGo.GetComponent<RectTransform>();
        trt.anchorMin = Vector2.zero;
        trt.anchorMax = Vector2.one;
        trt.offsetMin = Vector2.zero;
        trt.offsetMax = Vector2.zero;

        文字 = textGo.AddComponent<Text>();
        文字.font = 取内置字体();
        文字.fontSize = (暴击 || 会心) ? 风格.暴击字号 : 风格.普通字号;
        文字.fontStyle = FontStyle.Bold;
        文字.alignment = TextAnchor.MiddleCenter;
        文字.color = 暴击 ? 风格.暴击色 : (会心 ? 风格.会心色 : 风格.普通色);
        文字.raycastTarget = false;
        文字.horizontalOverflow = HorizontalWrapMode.Overflow;
        文字.verticalOverflow = VerticalWrapMode.Overflow;
        文字.text = Mathf.Max(1, Mathf.RoundToInt(伤害)).ToString();

        var 描边 = textGo.AddComponent<Outline>();
        描边.effectColor = 风格.描边色;
        描边.effectDistance = new Vector2(2f, -2f);

        主相机 = Camera.main;
        更新朝向();
    }

    void Update()
    {
        // 用 Time.time（受 timeScale 影响）—— 暂停时数字也跟着定住
        float 已过 = Time.time - 起始时间;
        float p = 风格.存活时长 > 0f ? Mathf.Clamp01(已过 / 风格.存活时长) : 1f;

        // 上浮：先快后慢
        float 上浮 = 1f - (1f - p) * (1f - p);
        transform.position = 起点 + Vector3.up * (风格.上浮高度 * 上浮) + 漂移 * p;

        // 弹出缩放
        float 弹 = 风格.弹出时间 > 0f ? Mathf.Clamp01(已过 / 风格.弹出时间) : 1f;
        float 缩放 = Mathf.Lerp(风格.起始缩放, 1f, 弹);
        var rt = (RectTransform)transform;
        rt.localScale = Vector3.one * (风格.画布缩放 * 缩放);

        // 淡出
        if (文字 != null)
        {
            var c = 文字.color;
            c.a = 1f - Mathf.InverseLerp(风格.淡出起点, 1f, p);
            文字.color = c;
        }

        更新朝向();

        if (p >= 1f) Destroy(gameObject);
    }

    void 更新朝向()
    {
        if (主相机 == null) 主相机 = Camera.main;
        if (主相机 == null) return;
        // 和血条用同一套朝向算法
        transform.rotation = Quaternion.LookRotation(
            transform.position - 主相机.transform.position, 主相机.transform.up);
    }

    static Font 取内置字体()
    {
        if (内置字体 == null)
            内置字体 = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        return 内置字体;
    }
}
