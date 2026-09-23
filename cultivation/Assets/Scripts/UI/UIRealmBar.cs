using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 「境界展示」区（角色面板 I 键 →「境界」页 右下那块）。
///
/// **数据源 = <see cref="PlayerCultivation"/>**（新的修炼系统），
/// 拿不到时才退回旧的 <see cref="UIPanelData"/> 经验显示。
///
/// **本组件会自己把面板下半块腾出来**，放一个多行信息区（境界名/等级/进度/修炼次数/
/// 总灵气/突破信息）—— 这样不用改 <c>CharacterPanelBuilder</c>、也不用重建面板。
///
/// **实时更新**：订阅 <see cref="PlayerCultivation.修为变化"/>（修炼、杀怪、转修、升级都会触发），
/// 同时保留每帧 Refresh 兜底。
/// </summary>
public class UIRealmBar : MonoBehaviour
{
    [Header("基础引用（builder 填的）")]
    public Text realmNameText;
    public Image progressFill;
    public Text percentText;
    public Text expText;

    [Header("数据源")]
    public UIPanelData data;

    [Tooltip("玩家修为。留空自动找")]
    public PlayerCultivation cultivation;

    [Header("选项")]
    [Tooltip("进度条满格时的宽度像素。留 0 则用 Filled 模式")]
    public float fillMaxWidth = 0f;

    [Tooltip("运行时自己加的那个多行信息区（自动创建，不用手填）")]
    public Text infoText;

    /// <summary>信息区里的字体（从境界名那行借）</summary>
    Font 字体 => realmNameText != null ? realmNameText.font : null;

    void Start()
    {
        找修为();
        确保信息区();
        订阅();
        Refresh();
    }

    void OnEnable() { 订阅(); }
    void OnDisable() { 退订(); }
    void OnDestroy() { 退订(); }

    void Update()
    {
        // 双保险：订阅是主要实时来源，每帧兜底（开销极小）
        Refresh();
    }

    void 找修为()
    {
        if (cultivation != null) return;
        cultivation = FindObjectOfType<PlayerCultivation>();
    }

    void 订阅()
    {
        找修为();
        if (cultivation == null) return;
        cultivation.修为变化 -= 处理修为变化;        // 防重复订阅
        cultivation.修为变化 += 处理修为变化;
    }

    void 退订()
    {
        if (cultivation != null) cultivation.修为变化 -= 处理修为变化;
    }

    void 处理修为变化(PlayerCultivation _) => Refresh();

    // ============================================================ 自己腾地方 + 加信息区

    /// <summary>把上面几行往上收，下半块腾给多行信息区</summary>
    void 确保信息区()
    {
        if (infoText != null) return;
        if (realmNameText == null) return;

        var 面板 = realmNameText.transform.parent as RectTransform;
        if (面板 == null) return;

        设锚(realmNameText.rectTransform, 0.74f, 1.00f, 20f);
        if (progressFill != null && progressFill.transform.parent != null)
            设锚(progressFill.transform.parent as RectTransform, 0.60f, 0.64f, 20f);
        if (percentText != null) 设锚(percentText.rectTransform, 0.46f, 0.58f, 20f);
        if (expText != null) 设锚(expText.rectTransform, 0.46f, 0.58f, 20f);

        infoText = UIBuildUtils.CreateText("Info", 面板, 字体, "", 17,
            TextAnchor.UpperLeft, UIBuildUtils.ColorText);
        infoText.horizontalOverflow = HorizontalWrapMode.Wrap;
        infoText.verticalOverflow = VerticalWrapMode.Truncate;
        设锚(infoText.rectTransform, 0.02f, 0.44f, 20f);
    }

    static void 设锚(RectTransform rt, float minY, float maxY, float 左右边距)
    {
        if (rt == null) return;
        rt.anchorMin = new Vector2(0f, minY);
        rt.anchorMax = new Vector2(1f, maxY);
        rt.offsetMin = new Vector2(左右边距, 0f);
        rt.offsetMax = new Vector2(-左右边距, 0f);
    }

    // ============================================================ 刷新

    /// <summary>按当前数据刷新显示</summary>
    public void Refresh()
    {
        if (cultivation == null) 找修为();
        确保信息区();

        if (cultivation != null && cultivation.境界表 != null && cultivation.境界表.Length > 0)
            刷新新系统();
        else
            刷新旧系统();
    }

    void 刷新新系统()
    {
        var c = cultivation;
        float ratio = c.进度;

        if (realmNameText != null) realmNameText.text = c.境界名;
        if (percentText != null) percentText.text = (ratio * 100f).ToString("0.#") + "%";
        if (expText != null)
            expText.text = c.已满级 ? "满级" : (c.本级已积累 + " / " + c.升级所需灵气);

        if (infoText != null)
        {
            string 材 = string.IsNullOrEmpty(c.突破材料) ? "（未定名）" : c.突破材料;
            infoText.text =
                "修为等级：" + c.等级 + " / 90"
                + "　（" + c.大境界名 + "）\n"
                + "已积攒修炼次数：" + c.修炼次数
                + "　单次修炼 +" + c.单次修炼灵气.ToString("0.#") + " 灵气\n"
                + "总灵气：" + c.总灵气
                + "　等效基准灵气：" + c.等效基准灵气 + "\n"
                + "当前功法：" + (c.当前功法 != null ? c.当前功法.功法名称 : "无")
                + "　难度系数 K=" + c.难度系数.ToString("0.0") + "\n"
                + (c.已满级
                    ? "已至巅峰，无需再突破"
                    : "下一级突破：" + c.突破类型 + "　需要 " + 材 + " × " + c.材料数量);
        }

        设进度条(ratio);
    }

    void 刷新旧系统()
    {
        if (data == null)
        {
            if (realmNameText != null) realmNameText.text = "（没有修为数据）";
            return;
        }

        long cur = data.当前经验;
        long total = data.GetRequiredExp();
        float ratio = total > 0 ? Mathf.Clamp01((float)cur / total) : 0f;

        if (realmNameText != null) realmNameText.text = data.GetRealmName();
        if (percentText != null) percentText.text = (ratio * 100f).ToString("0.#") + "%";
        if (expText != null) expText.text = cur + " / " + total;
        if (infoText != null) infoText.text = "";

        设进度条(ratio);
    }

    void 设进度条(float ratio)
    {
        if (progressFill == null) return;

        if (fillMaxWidth > 0f)
        {
            var rt = progressFill.rectTransform;
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.offsetMin = new Vector2(0f, rt.offsetMin.y);
            rt.offsetMax = new Vector2(0f, rt.offsetMax.y);
            rt.sizeDelta = new Vector2(fillMaxWidth * ratio, rt.sizeDelta.y);
        }
        else
        {
            progressFill.type = Image.Type.Filled;
            progressFill.fillMethod = Image.FillMethod.Horizontal;
            progressFill.fillOrigin = (int)Image.OriginHorizontal.Left;
            progressFill.fillAmount = ratio;
        }
    }

    // ---- ASCII 别名 ----
    public PlayerCultivation Cultivation => cultivation;
    public void RereadDataSource() { cultivation = null; 找修为(); 订阅(); Refresh(); }
}
