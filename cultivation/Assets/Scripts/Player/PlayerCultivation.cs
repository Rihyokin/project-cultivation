using System;
using UnityEngine;

/// <summary>
/// **玩家的修为/境界运行时。** 按《修仙境界 &amp; 功法数值体系设计》实现。
///
/// ## 核心模型
///
/// · **总灵气是角色通用积累，与功法无关** —— 功法只决定「灵气值对应什么境界」的换算比例。
/// · **等效基准灵气 S = 总灵气 ÷ 难度系数 K**，K = 功法难度等级 ÷ 100。
///   拿 S 去对照境界表的累计灵气阈值，就是当前境界。
/// · **杀怪给「修炼次数」**（次数 = 基础 1 次 × 等级差系数），
///   回修炼小屋消耗次数一次性转成灵气。
/// · 单次修炼产出的灵气**只随大境界提升、且不乘 K** ——
///   所以难度越高的功法，同样一次修炼推不动境界，需要更多怪 ✓（这正是设计意图）
///
/// ## 为什么 修炼次数 内部存 float
///
/// 等级差系数有 0.2 / 0.5 / 0.8 这种小数（打低级怪只给 0.2 次），
/// 用 int 存会直接抹成 0。所以内部累积 float，对外给整数值 ——
/// 打 5 只低级怪 = 攒够 1 次 ✓
///
/// 挂在 **Player** 上。
/// </summary>
public class PlayerCultivation : MonoBehaviour
{
    [Header("引用（留空自动找）")]
    [Tooltip("角色面板数据，当前功法从这里的「当前功法」读")]
    public UIPanelData 面板数据;

    [Header("境界表")]
    [Tooltip("90 个境界定义，按等级升序。用菜单「修仙/接线玩家修炼系统」自动填好")]
    public RealmDefinition[] 境界表;

    [Tooltip("全部功法（按 id 查用，存档读档要按 id 还原）。用菜单「修仙/接线玩家修炼系统」自动填")]
    public GongFaDefinition[] 功法表;

    [Header("修为（存档要保存）")]
    [Tooltip("总灵气。与功法无关的通用积累")]
    public long 总灵气;

    [Tooltip("修炼次数的**小数累积**。对外看 修炼次数（整数）")]
    public float 修炼次数累积;

    [Header("杀怪掉落修炼次数")]
    [Tooltip("同级怪物的基础掉落次数")]
    public float 同级基础次数 = 1f;

    [Tooltip("等级差系数（按「怪物等级 − 玩家等级」分段）：≤−5 / −4~−3 / −2~−1 / 0 / +1~+2 / +3~+4 / ≥+5")]
    public float[] 等级差系数 = { 0.2f, 0.5f, 0.8f, 1.0f, 1.5f, 2.0f, 3.0f };

    [Header("调试")]
    public bool 打印修为日志 = true;

    // ============================================================ 派生状态

    /// <summary>当前修炼的功法</summary>
    public GongFaDefinition 当前功法 => 面板数据 != null ? 面板数据.当前功法 : null;

    /// <summary>难度系数 K = 功法难度等级 ÷ 100（以 D=100 为基准 → K=1.0）</summary>
    public float 难度系数
    {
        get
        {
            var g = 当前功法;
            return g != null ? Mathf.Max(0.01f, g.难度等级 / 100f) : 1f;
        }
    }

    /// <summary>等效基准灵气 S = 总灵气 ÷ K。用它去对照境界表</summary>
    public long 等效基准灵气 => (long)(总灵气 / 难度系数);

    /// <summary>
    /// 当前境界（按等效基准灵气 S 查境界表）。
    ///
    /// 【语义】表里的 ``累计灵气(n)`` = **从 1 级升到 n+1 级所需的总灵气**，
    /// 所以「累计 <= S 的最大 n」对应的当前等级是 **n + 1** 而不是 n。
    /// 一开始少加了这一级，导致 S 刚好等于某个累计值时卡在 100% 不升级 ✗
    /// </summary>
    public RealmDefinition 当前境界
    {
        get
        {
            if (境界表 == null || 境界表.Length == 0) return null;
            long S = 等效基准灵气;

            int 推定 = 1;                       // 兜底：等级 1
            for (int i = 0; i < 境界表.Length; i++)
            {
                var d = 境界表[i];
                if (d == null) continue;
                if (d.累计灵气 <= S) 推定 = Mathf.Min(90, d.等级 + 1);
            }
            return 取境界(推定) ?? 境界表[0];
        }
    }

    /// <summary>按等级取境界定义</summary>
    public RealmDefinition 取境界(int 等级)
    {
        if (境界表 == null) return null;
        for (int i = 0; i < 境界表.Length; i++)
            if (境界表[i] != null && 境界表[i].等级 == 等级) return 境界表[i];
        return null;
    }

    /// <summary>当前修为等级（1~90）</summary>
    public int 等级 => 当前境界 != null ? 当前境界.等级 : 1;

    /// <summary>当前境界名，例如「炼气第1层」</summary>
    public string 境界名 => 当前境界 != null ? 当前境界.境界名 : "—";

    /// <summary>九大境界名，例如「炼气」</summary>
    public string 大境界名 => 当前境界 != null ? 当前境界.大境界.ToString() : "—";

    /// <summary>升到下一级还需要多少**基准**灵气（不含 K）</summary>
    public long 升级所需灵气 => 当前境界 != null ? 当前境界.升级所需灵气 : 0;

    /// <summary>本级的起点累计（= 上一级的 累计灵气；等级 1 时为 0）</summary>
    public long 本级起点累计 => 取累计(等级 - 1);

    /// <summary>本级已经积累的基准灵气</summary>
    public long 本级已积累 => Math.Max(0L, 等效基准灵气 - 本级起点累计);

    /// <summary>当前小境界的进度 0~1（满级恒为 1）</summary>
    public float 进度
    {
        get
        {
            var d = 当前境界;
            if (d == null) return 0f;
            if (d.突破类型 == BreakthroughKind.满级) return 1f;
            if (d.升级所需灵气 <= 0) return 1f;
            return Mathf.Clamp01(本级已积累 / (float)d.升级所需灵气);
        }
    }

    /// <summary>单次修炼能拿到的灵气（只随大境界变，**不乘 K**）</summary>
    public float 单次修炼灵气 => 当前境界 != null ? 当前境界.单次修炼灵气 : 2f;

    /// <summary>剩余修炼次数（整数）</summary>
    public int 修炼次数 => Mathf.Max(0, Mathf.FloorToInt(修炼次数累积));

    /// <summary>下一次修炼还差的次数（0~1 的小数进度）</summary>
    public float 次数小数部分 => Mathf.Clamp01(修炼次数累积 - Mathf.Floor(修炼次数累积));

    /// <summary>是不是满级了</summary>
    public bool 已满级 => 当前境界 != null && 当前境界.突破类型 == BreakthroughKind.满级;

    /// <summary>当前这一级要哪种突破</summary>
    public BreakthroughKind 突破类型 => 当前境界 != null ? 当前境界.突破类型 : BreakthroughKind.未指定;

    /// <summary>突破材料 id（留空 = 策划还没定）</summary>
    public string 突破材料 => 当前境界 != null ? 当前境界.突破材料 : "";
    public int 材料数量 => 当前境界 != null ? 当前境界.材料数量 : 1;

    // ---- ASCII 别名 ----
    public long TotalSpirit => 总灵气;
    public int Level => 等级;
    public float DifficultyK => 难度系数;
    public long EffectiveSpirit => 等效基准灵气;
    public int CultivationCharges => 修炼次数;
    public float Progress => 进度;
    public bool IsMaxLevel => 已满级;

    // ============================================================ 事件

    /// <summary>修为有变化（灵气 / 次数 / 等级 / 换功法）</summary>
    public event Action<PlayerCultivation> 修为变化;

    /// <summary>修炼了一次（参数：本次获得的灵气）</summary>
    public event Action<PlayerCultivation, float> 修炼了;

    /// <summary>境界等级变了（参数：旧等级、新等级）</summary>
    public event Action<PlayerCultivation, int, int> 等级变化;

    /// <summary>杀了怪拿到修炼次数（参数：怪物等级、本次次数）</summary>
    public event Action<PlayerCultivation, int, float> 击杀获得次数;

    void Awake()
    {
        if (面板数据 == null) 面板数据 = GetComponent<UIPanelData>();
        if (面板数据 == null) 面板数据 = FindObjectOfType<UIPanelData>();
        确保境界表();
    }

    int 上次等级 = -1;

    void Update()
    {
        int 现 = 等级;
        if (上次等级 < 0) { 上次等级 = 现; return; }
        if (现 != 上次等级)
        {
            var 旧 = 上次等级;
            上次等级 = 现;
            if (打印修为日志)
                Debug.Log("[修炼] 境界变化：" + 旧 + " 级 → " + 现 + " 级（" + 境界名 + "）", this);
            等级变化?.Invoke(this, 旧, 现);
            修为变化?.Invoke(this);
        }
    }

    /// <summary>境界表没接线时，自动从 Resources 兜底找一遍</summary>
    void 确保境界表()
    {
        if (境界表 != null && 境界表.Length > 0) return;
        var 找 = Resources.LoadAll<RealmDefinition>("");
        if (找 != null && 找.Length > 0)
        {
            Array.Sort(找, (a, b) => a.等级.CompareTo(b.等级));
            境界表 = 找;
        }
    }

    // ============================================================ 修炼

    /// <summary>
    /// **消耗 1 次修炼，把次数转成灵气。** 返回本次获得的灵气（0 = 没次数了）
    /// </summary>
    public float 修炼一次()
    {
        if (修炼次数 <= 0)
        {
            if (打印修为日志) Debug.Log("[修炼] 没有修炼次数了", this);
            return 0f;
        }

        float 得 = 单次修炼灵气;
        修炼次数累积 = Mathf.Max(0f, 修炼次数累积 - 1f);
        总灵气 += (long)得;

        if (打印修为日志)
            Debug.Log("[修炼] 修炼一次：+" + 得.ToString("0.#") + " 灵气（共 " + 总灵气
                + "｜等效 " + 等效基准灵气 + "｜剩 " + 修炼次数 + " 次）", this);

        修炼了?.Invoke(this, 得);
        修为变化?.Invoke(this);
        return 得;
    }

    /// <summary>一次性把次数全用掉（「闭关」按钮）。返回获得的总灵气</summary>
    public long 闭关全部修炼()
    {
        long 共 = 0;
        while (修炼次数 > 0)
        {
            float 得 = 单次修炼灵气;
            修炼次数累积 = Mathf.Max(0f, 修炼次数累积 - 1f);
            总灵气 += (long)得;
            共 += (long)得;
        }
        if (共 > 0)
        {
            if (打印修为日志) Debug.Log("[修炼] 闭关：+" + 共 + " 灵气（共 " + 总灵气 + "）", this);
            修为变化?.Invoke(this);
        }
        return 共;
    }

    // ============================================================ 杀怪 → 修炼次数

    /// <summary>
    /// **杀掉一只怪 → 掉落修炼次数。**
    /// 单只 = 基础 1 次 × 等级差系数（等级差 = 怪物等级 − 玩家等级）。
    /// 用「修炼次数累积」做小数累积，打 5 只低级怪（各 0.2）能攒够 1 次。
    /// </summary>
    public float 记录击杀(int 怪物等级)
    {
        int 差 = 怪物等级 - 等级;
        float 次 = 同级基础次数 * 取等级差系数(差);
        修炼次数累积 += 次;

        if (打印修为日志)
            Debug.Log("[修炼] 击杀 " + 怪物等级 + " 级怪（差 " + (差 >= 0 ? "+" : "") + 差
                + "）→ +" + 次.ToString("0.##") + " 次（现有 " + 修炼次数 + " 次）", this);

        击杀获得次数?.Invoke(this, 怪物等级, 次);
        修为变化?.Invoke(this);
        return 次;
    }

    /// <summary>等级差系数（按设计文档的分段）</summary>
    public float 取等级差系数(int 等级差)
    {
        int i;
        if (等级差 <= -5) i = 0;
        else if (等级差 <= -3) i = 1;
        else if (等级差 <= -1) i = 2;
        else if (等级差 == 0) i = 3;
        else if (等级差 <= 2) i = 4;
        else if (等级差 <= 4) i = 5;
        else i = 6;

        if (等级差系数 == null || 等级差系数.Length <= i) return 1f;
        return 等级差系数[i];
    }

    // ============================================================ 功法转换

    /// <summary>
    /// **转修功法。** 返回是否转成功。
    ///
    /// · **低难度 → 高难度**（K 变大）：总灵气先扣 **5% 损耗**，再用新 K 换算境界。
    /// · **高难度 → 低难度**（K 变小）：**境界不会提升**，灵气最多填满当前小境界
    ///   （也就是"差一步突破"），超出部分逸散。
    /// </summary>
    public bool 转修功法(GongFaDefinition 新功法)
    {
        if (新功法 == null || 面板数据 == null) return false;
        if (新功法 == 面板数据.当前功法) return false;

        var 旧功法 = 面板数据.当前功法;
        float 旧K = 旧功法 != null ? Mathf.Max(0.01f, 旧功法.难度等级 / 100f) : 1f;
        float 新K = Mathf.Max(0.01f, 新功法.难度等级 / 100f);

        int 旧等级 = 等级;
        long 旧灵气 = 总灵气;

        if (新K > 旧K)
        {
            // 低 → 高：扣 5% 损耗，境界按新 K 重算（总灵气减少，等效基准跟着降）
            总灵气 = (long)(总灵气 * 0.95);
            if (打印修为日志)
                Debug.Log("[修炼] 转修（低→高）：" + (旧功法 != null ? 旧功法.功法名称 : "无")
                    + " → " + 新功法.功法名称 + "｜K " + 旧K.ToString("0.0") + " → " + 新K.ToString("0.0")
                    + "｜总灵气 " + 旧灵气 + " → " + 总灵气 + "（扣 5% 损耗）", this);
        }
        else
        {
            // 高 → 低：境界不许涨。把等效基准灵气压到「当前等级的累计上限」
            long 上限S = 取累计(旧等级);
            总灵气 = Math.Min(总灵气, (long)(上限S * 新K));
            if (打印修为日志)
                Debug.Log("[修炼] 转修（高→低）：K " + 旧K.ToString("0.0") + " → " + 新K.ToString("0.0")
                    + "｜境界不提升（封顶 " + 旧等级 + " 级）｜总灵气 " + 旧灵气 + " → " + 总灵气, this);
        }

        面板数据.当前功法 = 新功法;
        面板数据.RaiseChanged();

        // 让 PlayerAbilityLoader 把普攻方法也跟着换掉
        var 装载 = GetComponent<PlayerAbilityLoader>();
        if (装载 != null) 装载.Refresh();

        上次等级 = 等级;      // 别把转换本身当成一次"升级"
        修为变化?.Invoke(this);
        return true;
    }

    /// <summary>转修之后境界会变成什么（给 UI 的「转修后境界预估」用），不真的改</summary>
    public string 预估转修后境界(GongFaDefinition 新功法)
    {
        if (新功法 == null) return "—";
        float 新K = Mathf.Max(0.01f, 新功法.难度等级 / 100f);
        var 旧功法 = 当前功法;
        float 旧K = 旧功法 != null ? Mathf.Max(0.01f, 旧功法.难度等级 / 100f) : 1f;

        long 预估总 = 总灵气;
        if (新K > 旧K) 预估总 = (long)(总灵气 * 0.95);                       // 低→高：扣 5%
        else 预估总 = Math.Min(总灵气, (long)(取累计(等级) * 新K));            // 高→低：封顶

        long S = (long)(预估总 / 新K);
        var d = 查境界(S);
        return d != null ? d.境界名 : "—";
    }

    // ============================================================ 查表工具

    /// <summary>
    /// 按「等效基准灵气 S」查境界：**累计灵气 ≤ S 的里面，等级最高的那一个**。
    ///
    /// 【为什么不能依赖数组顺序】以前这里写的是「遇到更大的等级就 `else break`」，
    /// 隐含要求 <see cref="境界表"/> 按等级升序。但表是脚本/工具填的，
    /// 一旦顺序乱了（实测：按资产名字母序 → 元婴第10层排在第 0 位），
    /// 就会**提前退出**、把「炼气第1层」的玩家判成「元婴第10层」，
    /// 而且不报任何错 —— 修炼小屋的「转修后境界预估」就是这么错的。
    ///
    /// 现在改成**全表扫一遍、只看「累计灵气 ≤ S 且等级最高」**，与顺序无关。
    /// 表只有 90 条，这点开销可以忽略。
    /// </summary>
    public RealmDefinition 查境界(long S)
    {
        if (境界表 == null || 境界表.Length == 0) return null;
        RealmDefinition 最佳 = null;
        for (int i = 0; i < 境界表.Length; i++)
        {
            var d = 境界表[i];
            if (d == null) continue;
            if (d.累计灵气 > S) continue;                       // 还没到这一级
            if (最佳 == null || d.等级 > 最佳.等级) 最佳 = d;     // 取等级最高的
        }
        return 最佳 ?? 境界表[0];                                // S 连 1 级都不够 → 兜底第 0 个
    }

    /// <summary>按功法 id 找功法（读档用）</summary>
    public GongFaDefinition 取功法(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        if (面板数据 != null && 面板数据.当前功法 != null && 面板数据.当前功法.功法id == id) return 面板数据.当前功法;
        if (功法表 != null)
            foreach (var g in 功法表)
                if (g != null && g.功法id == id) return g;
        return null;
    }

    /// <summary>某个等级的「累计灵气」；等级 ≤ 0 返回 0</summary>
    public long 取累计(int 等级)
    {
        if (等级 <= 0) return 0;
        if (境界表 == null) return 0;
        for (int i = 0; i < 境界表.Length; i++)
            if (境界表[i] != null && 境界表[i].等级 == 等级) return 境界表[i].累计灵气;
        return 0;
    }

    /// <summary>直接设总灵气（读档 / 调试用）</summary>
    public void 设置总灵气(long 值)
    {
        总灵气 = Math.Max(0, 值);
        上次等级 = 等级;
        修为变化?.Invoke(this);
    }

    /// <summary>直接设修炼次数（读档 / 调试用）</summary>
    public void 设置修炼次数(float 值)
    {
        修炼次数累积 = Math.Max(0f, 值);
        修为变化?.Invoke(this);
    }

    // ---- ASCII 别名 ----
    public float GainKillCharge(int monsterLevel) => 记录击杀(monsterLevel);
    public float CultivateOnce() => 修炼一次();
    public bool SwitchGongFa(GongFaDefinition next) => 转修功法(next);
    public string PreviewRealmAfterSwitch(GongFaDefinition next) => 预估转修后境界(next);
}
