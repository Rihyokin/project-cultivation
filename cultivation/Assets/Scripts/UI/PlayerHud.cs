using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 游戏内 HUD。左下角一小块，全部用**方框**：
///
///   ┌────┐ ┌─┐┌─┐┌─┐┌─┐┌─┐┌─┐
///   │功法│ │1││2││3││4││5││6│
///   └────┘ └─┘└─┘└─┘└─┘└─┘└─┘
///   气血 ▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓
///   灵气 ▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓
///
/// **不显示任何名字**：鼠标移到格子上会弹出半透明「信息幕布」，移开就消失
/// （见 <see cref="HudHoverTarget"/> 与 <see cref="显示信息"/>）。
///
/// 由 <c>Assets/Editor/Builders/HudBuilder.cs</c> 生成
/// （菜单：修仙 / 生成游戏界面 HUD）。
///
/// 冷却表现：技能进冷却后
///   · 图标整体变暗
///   · 中央实时显示剩余秒数
///   · 暗部遮罩的 fillAmount = 冷却比例（1 → 0），所以暗部【逐渐消退】
/// </summary>
public class PlayerHud : MonoBehaviour
{
    [System.Serializable]
    public class 技能格
    {
        [Tooltip("整格的方形底图（同时也是鼠标悬停的接收体）")]
        public Image 底;

        [Tooltip("技能图标")]
        public Image 图标;

        [Tooltip("空槽 / 未实现时盖在图标上的灰罩")]
        public Image 灰罩;

        [Tooltip("冷却暗部遮罩。Filled / Vertical(从上往下)，fillAmount = 冷却比例")]
        public Image 冷却遮罩;

        [Tooltip("冷却剩余秒数")]
        public Text 冷却文字;

        [Tooltip("按键提示 1~6")]
        public Text 按键文字;
    }

    [Header("数据源（留空自动找）")]
    public UIPanelData 面板数据;
    public PlayerVitals 生命;
    public ActiveSkillCaster 施放器;

    [Header("功法")]
    public Image 功法图标;

    [Header("主动技能（顺序即槽位 0~5）")]
    public 技能格[] 技能格们 = new 技能格[6];

    [Header("气血 / 灵力")]
    public Image 气血填充;
    public Text 气血文字;
    public Image 灵力填充;
    public Text 灵力文字;

    [Header("修炼次数进度")]
    [Tooltip("修炼数据源。留空自动找")]
    public PlayerCultivation 修为;

    [Tooltip("进度条填充。**fillAmount = 距离下一次修炼次数的进度**")]
    public Image 修炼填充;

    [Tooltip("显示「现存修炼次数 + 距离下一颗还差百分之几」")]
    public Text 修炼文字;

    [Tooltip("进度条追赶速度（次/秒）。1.6 ≈ 一条满格跑 0.63 秒")]
    public float 修炼追赶速度 = 1.6f;

    [Tooltip("跑满一格时停顿多久（秒），让人看清「又攒够一次了」")]
    public float 修炼满格停顿 = 0.18f;

    [Tooltip("修炼进度条颜色（金色，和灰阶 HUD 区分开）")]
    public Color 修炼色 = new Color(0.86f, 0.72f, 0.32f, 1f);

    [Header("信息幕布（鼠标悬停时显示）")]
    public GameObject 信息幕布;
    public Text 幕布名称;
    public Text 幕布品阶;
    public Text 幕布正文;

    [Header("配色")]
    public Color 气血色 = new Color(0.80f, 0.20f, 0.20f, 1f);
    public Color 灵力色 = new Color(0.26f, 0.54f, 0.88f, 1f);
    public Color 图标正常 = Color.white;
    public Color 冷却遮罩色 = new Color(0f, 0f, 0f, 0.66f);

    /// <summary>每格上次绑定的内容，用来避免每帧重设 sprite</summary>
    readonly Object[] 上次内容 = new Object[6];

    static Sprite 白图;

    void Awake()
    {
        解析引用();
        补齐贴图();
        隐藏信息();
    }

    void Update()
    {
        刷新功法();
        刷新技能栏();
        刷新数值条();
        刷新修炼进度();
    }

    void 解析引用()
    {
        if (面板数据 == null) 面板数据 = FindObjectOfType<UIPanelData>();
        if (生命 == null) 生命 = FindObjectOfType<PlayerVitals>();
        if (施放器 == null) 施放器 = FindObjectOfType<ActiveSkillCaster>();
        if (修为 == null) 修为 = FindObjectOfType<PlayerCultivation>();
    }

    // ------------------------------------------------------------ 贴图兜底

    /// <summary>
    /// 兜底：Image 在 sprite 为空时【会忽略 fillAmount】，不管 type 是不是 Filled。
    /// 所以进度条 / 遮罩必须有 sprite，缺了就现场生成一张纯白。
    /// </summary>
    void 补齐贴图()
    {
        if (白图 == null) 白图 = 生成纯白();

        if (气血填充 != null && 气血填充.sprite == null) 气血填充.sprite = 白图;
        if (灵力填充 != null && 灵力填充.sprite == null) 灵力填充.sprite = 白图;
        if (修炼填充 != null && 修炼填充.sprite == null) 修炼填充.sprite = 白图;

        foreach (var g in 技能格们)
        {
            if (g == null) continue;
            if (g.底 != null && g.底.sprite == null) g.底.sprite = 白图;
            if (g.灰罩 != null && g.灰罩.sprite == null) g.灰罩.sprite = 白图;
            if (g.冷却遮罩 != null && g.冷却遮罩.sprite == null) g.冷却遮罩.sprite = 白图;
        }
    }

    static Sprite 生成纯白()
    {
        var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
        var px = new Color32[16];
        for (int i = 0; i < px.Length; i++) px[i] = new Color32(255, 255, 255, 255);
        tex.SetPixels32(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 100f);
    }

    // ------------------------------------------------------------ 刷新

    void 刷新功法()
    {
        if (功法图标 == null) return;
        if (面板数据 == null) { 解析引用(); if (面板数据 == null) return; }

        var 功法 = 面板数据.当前功法;
        var sp = 功法 != null ? 功法.DisplayIcon : null;

        if (功法图标.sprite != sp)
        {
            功法图标.sprite = sp;
            功法图标.color = sp != null
                ? 图标正常
                : (功法 != null ? UIEntryRow.TierColor(功法.DisplayTier) : new Color(1f, 1f, 1f, 0f));
        }
    }

    void 刷新技能栏()
    {
        if (技能格们 == null) return;
        if (面板数据 == null) { 解析引用(); if (面板数据 == null) return; }

        for (int i = 0; i < 技能格们.Length; i++)
        {
            var g = 技能格们[i];
            if (g == null) continue;

            var 内容 = (面板数据.主动技能 != null && i < 面板数据.主动技能.Count)
                     ? 面板数据.主动技能[i] : null;

            // 内容变了才动 sprite（每帧设 sprite 会白白触发重建）
            if (上次内容[i] != 内容)
            {
                上次内容[i] = 内容;
                var entry = 内容 as IPanelEntry;
                var sp = entry != null ? entry.DisplayIcon : null;

                if (g.图标 != null)
                {
                    g.图标.sprite = sp;
                    // 没有图标就用品阶色顶着，至少能看出是个什么东西
                    g.图标.color = sp != null
                        ? 图标正常
                        : (entry != null ? UIEntryRow.TierColor(entry.DisplayTier) : new Color(1f, 1f, 1f, 0f));
                }
            }

            // 冷却表现
            float 剩余 = 施放器 != null ? 施放器.冷却剩余秒(i) : 0f;
            float 比例 = 施放器 != null ? 施放器.冷却比例(i) : 0f;
            bool 冷却中 = 剩余 > 0.001f;
            bool 未实现 = 内容 != null && 施放器 != null && !施放器.槽位可用(i);

            if (g.冷却遮罩 != null)
            {
                g.冷却遮罩.gameObject.SetActive(冷却中);
                if (冷却中)
                {
                    g.冷却遮罩.color = 冷却遮罩色;
                    g.冷却遮罩.fillAmount = 比例;   // 1 → 0，暗部随冷却逐渐消退
                }
            }

            if (g.冷却文字 != null)
            {
                g.冷却文字.gameObject.SetActive(冷却中);
                if (冷却中)
                {
                    // ≥1 秒显示整数（经典手感），最后一秒显示一位小数
                    g.冷却文字.text = 剩余 >= 1f
                        ? Mathf.CeilToInt(剩余).ToString()
                        : 剩余.ToString("0.0");
                }
            }

            if (g.灰罩 != null) g.灰罩.gameObject.SetActive(内容 != null && 未实现);
            if (g.按键文字 != null) g.按键文字.text = (i + 1).ToString();
        }
    }

    void 刷新数值条()
    {
        if (生命 == null) { 解析引用(); if (生命 == null) return; }

        float 血上限 = 生命.气血上限, 灵上限 = 生命.灵气上限;
        float 血比 = 血上限 > 0f ? Mathf.Clamp01(生命.当前气血 / 血上限) : 0f;
        float 灵比 = 灵上限 > 0f ? Mathf.Clamp01(生命.当前灵气 / 灵上限) : 0f;

        if (气血填充 != null) { 气血填充.color = 气血色; 气血填充.fillAmount = 血比; }
        if (灵力填充 != null) { 灵力填充.color = 灵力色; 灵力填充.fillAmount = 灵比; }

        if (气血文字 != null)
            气血文字.text = Mathf.CeilToInt(生命.当前气血) + " / " + Mathf.CeilToInt(血上限);
        if (灵力文字 != null)
            灵力文字.text = Mathf.CeilToInt(生命.当前灵气) + " / " + Mathf.CeilToInt(灵上限);
    }

    // ------------------------------------------------------------ 修炼次数进度

    /// <summary>进度条显示用的「累积修炼次数」，会**平滑追赶**真实值（见 <see cref="刷新修炼进度"/>）</summary>
    float 显示修炼累积 = -1f;

    /// <summary>跑满一格后的停顿计时</summary>
    float 修炼停顿;

    /// <summary>
    /// **修炼次数进度条。**
    ///
    /// 真实值是 `PlayerCultivation.修炼次数累积`（float：整数部分 = 现存次数、小数部分 = 进度）。
    /// 这里不直接显示它，而是让一个**显示值平滑追赶**它 —— 这样一次杀怪涨 3.5 次时，
    /// 进度条会**自己跑满 3 遍、第 4 遍停在一半**（用户要的表现），而不是"啪"一下跳过去：
    ///
    /// ```
    /// 0 → 100%（+1 次，停 0.18 秒）→ 0 → 100%（+1 次，停）→ 0 → 100%（+1 次，停）→ 0 → 50%
    /// ```
    ///
    /// · 每跑满一格，左边那个「现存修炼次数」数字就跟着 +1，所以数字和条是同步的
    /// · **次数被消耗**（闭关 / 修炼一次）时显示值**立刻落下**，不做倒放动画
    /// · 读档 / 首帧直接对齐，不会从 0 跑一遍
    /// </summary>
    void 刷新修炼进度()
    {
        if (修炼填充 == null && 修炼文字 == null) return;
        if (修为 == null) { 解析引用(); if (修为 == null) return; }

        float 真 = Mathf.Max(0f, 修为.修炼次数累积);
        float dt = Time.deltaTime;

        if (显示修炼累积 < 0f) 显示修炼累积 = 真;                       // 首帧 / 读档：直接对齐
        else if (真 < 显示修炼累积) { 显示修炼累积 = 真; 修炼停顿 = 0f; }  // 消耗次数 → 立刻落下
        else if (修炼停顿 > 0f) 修炼停顿 -= dt;                          // 满格停顿中
        else
        {
            float 步 = Mathf.Max(0.01f, 修炼追赶速度) * dt;
            float 下一格 = Mathf.Floor(显示修炼累积) + 1f;

            if (显示修炼累积 < 下一格 && 显示修炼累积 + 步 >= 下一格)
            {
                显示修炼累积 = 下一格;              // 正好跑满 → 停一下再继续
                修炼停顿 = 修炼满格停顿;
            }
            else 显示修炼累积 = Mathf.Min(真, 显示修炼累积 + 步);
        }

        int 次 = Mathf.FloorToInt(显示修炼累积 + 0.0001f);
        float 条 = 修炼停顿 > 0f ? 1f : Mathf.Clamp01(显示修炼累积 - 次);

        if (修炼填充 != null) { 修炼填充.color = 修炼色; 修炼填充.fillAmount = 条; }
        if (修炼文字 != null)
            修炼文字.text = "修炼次数 " + 次 + "　下一颗 " + Mathf.RoundToInt(条 * 100f) + "%";
    }

    // ------------------------------------------------------------ 信息幕布

    /// <summary>显示某格的信息。槽位 -1 = 功法图标。</summary>
    public void 显示信息(int 槽位)
    {
        if (信息幕布 == null) return;
        if (面板数据 == null) 解析引用();

        string 名, 品阶, 正文;

        if (槽位 < 0)
        {
            var 功法 = 面板数据 != null ? 面板数据.当前功法 : null;
            if (功法 == null) { 隐藏信息(); return; }

            名 = 功法.DisplayName;
            品阶 = 功法.功法品阶.ToString();
            正文 = "当前修炼功法\n"
                 + "每级所需经验：" + 功法.难度等级 + "\n"
                 + "可修炼境界：" + 功法.修炼门槛 + " ~ " + 功法.最高可修炼境界 + "\n"
                 + "普攻方法：" + (string.IsNullOrEmpty(功法.普攻方法id) ? "—" : 功法.普攻方法id);
            if (!string.IsNullOrEmpty(功法.介绍)) 正文 += "\n\n" + 功法.介绍;
        }
        else
        {
            var 内容 = 施放器 != null ? 施放器.槽位内容(槽位) : null;
            if (内容 == null) { 隐藏信息(); return; }

            var e = 内容 as IPanelEntry;
            名 = e != null ? e.DisplayName : 内容.name;
            品阶 = e != null ? e.DisplayTier.ToString() : "";
            正文 = "";

            var 神通 = 内容 as ActiveDivineAbility;
            var 灵阵 = 内容 as SpiritArrayDefinition;
            var 法宝 = 内容 as TreasureDefinition;

            if (神通 != null)
            {
                正文 = "主动神通 · " + 神通.伤害属性 + " · ";
                if (神通.结算方式 == ActiveSkillKind.未实现)
                {
                    正文 += "【尚未实现】\n";
                }
                else
                {
                    正文 += 神通.结算方式 + "\n"
                         + "消耗灵力：" + 神通.消耗灵力 + "\n"
                         + "冷却：" + 神通.冷却时间 + " 秒\n"
                         + "范围：" + 神通.范围 + " 米\n"
                         + "伤害倍率：" + 神通.伤害倍率 + "\n"
                         + "首次伤害：" + 神通.首次造成伤害时间 + " 秒\n";
                    正文 += 神通.持续时长 > 0f
                         ? "持续 " + 神通.持续时长 + " 秒（每 " + 神通.伤害间隔 + " 秒结算一次）\n"
                         : "一次性结算\n";
                    if (神通.需要锁定目标) 正文 += "需要先锁定目标\n";
                }
            }
            else if (灵阵 != null)
            {
                正文 = "灵阵【尚未实现】\n"
                     + "有效范围：" + 灵阵.有效范围 + " 米\n"
                     + "持续时间：" + 灵阵.持续时间 + " 秒\n"
                     + "目标筛选：" + 灵阵.目标筛选 + "\n"
                     + "布置消耗灵气：" + 灵阵.布置消耗灵气 + "\n";
            }
            else if (法宝 != null)
            {
                正文 = "法宝【尚未实现】\n";
            }
            else
            {
                正文 = "【尚未实现】\n";
            }

            if (e != null && !string.IsNullOrEmpty(e.DisplayDescription))
                正文 += "\n" + e.DisplayDescription;
        }

        if (幕布名称 != null) 幕布名称.text = 名;
        if (幕布品阶 != null) 幕布品阶.text = 品阶;
        if (幕布正文 != null) 幕布正文.text = 正文;
        信息幕布.SetActive(true);
    }

    /// <summary>鼠标移开时收起幕布</summary>
    public void 隐藏信息()
    {
        if (信息幕布 != null) 信息幕布.SetActive(false);
    }

    // ---- ASCII 别名 ----
    public void RefreshAll() { 刷新功法(); 刷新技能栏(); 刷新数值条(); 刷新修炼进度(); }
    public void ShowInfo(int slot) => 显示信息(slot);
    public void HideInfo() => 隐藏信息();
}
