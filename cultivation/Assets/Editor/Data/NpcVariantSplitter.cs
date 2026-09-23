using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// **NPC表：按蒙皮版本拆行 + 数值逐档递推。** 可重复执行。
///
/// 菜单：修仙 / 配置表 / NPC 按蒙皮版本拆分并递推数值
///      （只做数值递推：修仙 / 配置表 / NPC 数值按版本递推）
///
/// ## 背景
///
/// 素材里同一个怪有多个蒙皮版本（`BaiXiongJing_01 ~ 04`、`BingPoGuai_01 ~ 04` ……），
/// 但 NPC表里原来**一个家族只有一行**，4 个 prefab 共用同一份 `NpcDefinition` ——
/// 于是它们等级、属性完全一样，也没法单独调。用户要求「按不同的蒙皮版本完善 NPC表」。
///
/// ## 三步
///
/// 1. **拆分**：多蒙皮家族（prefab > 1 个）里，每个 prefab 一行：
///    - `id` = `<原id>_<NN>`（`NN` 按 prefab 名排序，两位）
///    - `名字` = `<原名><NN>`
///    - `模型资源路径` = 该 prefab 自己的路径
///    - 其它列先照抄 `_01`，随后由第 2 步递推
///    - 顺手删掉旧的「共用定义」资产，并把每个 prefab 的 `NpcInstance.定义` 接成它自己那份
///    - **已经拆过的家族会自动跳过**（表里有 `<原id>_01` 就算拆过）→ 可以反复跑
///
/// 2. **递推**：每个家族的第 N 个版本**高一个大档**（策划定的）：
///    - `境界` = 基础 + 10 × 步长 × (N-1)，**步长自动压缩**保证不越过满级 90
///      （例：基础 81 的 3 个版本 → 81 / 86 / 90；基础 71 的 4 个 → 71/77/84/90）
///    - 属性按**从现有表里拟合出来的曲线**跟着涨（见下面的常量）
///    - 每次都以 `_01` 那行为基准重算 → 也**可反复跑**（手工微调 02 以后会被覆盖，注意）
///
/// 3. **命名**：给每个版本一个**由弱到强**的称呼（用户要求，不许再叫 `01/02/03`）：
///    - `白熊精01` → `白熊精·幼`，最强的那个 → `白熊精·王`
///    - 人类用另一套（`微末 / 寻常 / … / 无双`）—— 人类那列里平民和神仙是混着的，
///      用修士进阶的词会出现「嫦娥·初学」这种别扭名字
///    - **可以反复跑**：换一套称号词重跑就会整表重排；手工起过名的家族一个都不动
///    - `手工家族` 名单里的 4 个家族**连递推都跳过**（她们的境界不是「每版一大档」）
///
/// ## 拟合出来的曲线（来自现有 NPC 表，不是拍的）
///
/// 同一个怪在不同大档上的属性实测是：
///
/// | 属性 | 每大档 |
/// |---|---|
/// | 气血 | ×1.75 |
/// | 攻击 | ×1.6489 |
/// | 防御 | ×1.6225 |
/// | 灵力 | ×1.6998 |
/// | 暴击 | +0.05 | 
/// | 暴击抗性 / 会心抗性 / 真实 / 普攻伤害加成 / 普攻伤害减免 | +0.02 |
/// | 会心 / 真实伤害 / 闪避 / 冷却缩减 | +0.03 |
/// | 会心伤害抗性 / 真实抗性 | +0.01 |
/// | 气血回复 / 灵力回复 | = 气血×0.01 / 灵力×0.02（跟着推） |
/// | 移动速度 / 攻速 / 暴击伤害 / 会心伤害 / 忽视闪避 / 治疗加成 / 各类法术加成减免 | 不变 |
///
/// 用拟合比例（而不是查表）是因为：非大档的等级（86 / 77…）本来就没有现成数值，
/// 而且这样一次性覆盖所有属性。代价是落到整档上时有 **≤0.2% 的取整偏差**
/// （例：境界81 的气血表里是 35186，递推出来是 35184）。
/// </summary>
public static class NpcVariantSplitter
{
    const string 表路径 = "Assets/Data/Tables/NPC表.csv";
    const string 定义目录 = "Assets/Data/Generated/NpcDefinition";
    const string 备份目录 = "D:/project：cultivation";

    const int 列数 = 39;
    const int 满级 = 90;

    // 拟合曲线
    const float 气血比 = 1.75f, 攻击比 = 1.6489f, 防御比 = 1.6225f, 灵力比 = 1.6998f;

    /// <summary>每大档线性增加的百分比属性：(列号, 每档步长)</summary>
    static readonly (int 列, float 步长)[] 线性 =
    {
        (16, 0.05f),   // 暴击
        (17, 0.02f),   // 暴击抗性
        (19, 0.04f),   // 会心
        (20, 0.02f),   // 会心抗性
        (22, 0.02f),   // 真实
        (23, 0.01f),   // 真实抗性
        (24, 0.03f),   // 真实伤害
        (25, 0.03f),   // 闪避
        (27, 0.03f),   // 冷却缩减
        (32, 0.02f),   // 普攻伤害加成
        (33, 0.02f),   // 普攻伤害减免
    };

    [MenuItem("修仙/配置表/NPC 按蒙皮版本拆分并递推数值")]
    [MenuItem("Cultivation/Tables/Split NPC By Model Variant")]
    public static void 拆分并递推()
    {
        拆分();
        递推();
        命名();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    [MenuItem("修仙/配置表/NPC 数值按版本递推")]
    public static void 只递推()
    {
        递推();
        AssetDatabase.SaveAssets();
        Debug.Log("[NPC版本] 数值递推完成");
    }

    [MenuItem("修仙/配置表/NPC 称号按版本命名")]
    [MenuItem("Cultivation/Tables/Name NPC Variants")]
    public static void 只命名()
    {
        命名();
        AssetDatabase.SaveAssets();
        Debug.Log("[NPC版本] 命名完成");
    }

    // ============================================================ 手工家族

    /// <summary>
    /// **手工配好的家族 —— 递推 / 命名都不许碰。**
    ///
    /// 这几个家族是逐个蒙皮版本**手工分档起名**的（白鹿精·幼 / 赤蟒精 / 鬼气将·帅 / 冥妖·一…），
    /// 境界也不遵守「每版一大档」（白鹿精是 1 / 4 / 10，赤蟒精是 21 / 51 / 71）。
    ///
    /// 【坑·已修】以前这里是靠「**名字以 NN 结尾**」这个**隐式**条件把她们排除掉的 ——
    /// 而 <see cref="命名"/> 把名字改成「白熊精·壮」之后这个条件就失效了，
    /// 再跑一次递推就会**把白鹿精的 1/4/10 冲成 1/11/21**、把赤蟒精的 21/51/71 冲成 21/41/61 ✗
    /// 所以改成这份**显式名单**。（名字里没有数字，所以命名步也不会动她们）
    /// </summary>
    static readonly string[] 手工家族 =
    {
        "demon_bailujing", "demon_chimangjin", "demon_guiqijiang", "demon_mingyao",
    };

    static bool 是手工家族(string 基) => System.Array.IndexOf(手工家族, 基) >= 0;

    // ============================================================ 拆分

    public static void 拆分()
    {
        var 家族 = 扫prefab家族();
        var 已拆 = 读表();
        if (已拆 == null) return;

        // 哪些家族还没拆过（表里没有 <id>_01 这行）
        var 待拆 = new Dictionary<string, List<(string 名, string 路径)>>();
        foreach (var kv in 家族)
        {
            if (kv.Value.Count <= 1) continue;
            bool 拆过 = false;
            foreach (var L in 已拆)
            {
                var 列 = L.Split(',');
                if (列.Length > 0 && 列[0].Trim() == kv.Key + "_01") { 拆过 = true; break; }
            }
            if (!拆过) 待拆[kv.Key] = kv.Value;
        }

        if (待拆.Count == 0) { Debug.Log("[NPC版本] 没有需要拆分的新家族（都已拆过）"); return; }

        Backup();
        var prefab到新id = new Dictionary<string, string>();
        var 输出 = new List<string>();
        int 新增 = 0;

        for (int i = 0; i < 已拆.Count; i++)
        {
            var L = 已拆[i];
            if (i == 0 || string.IsNullOrWhiteSpace(L)) { 输出.Add(L); continue; }
            var 列 = L.Split(',');
            if (列.Length != 列数) { 输出.Add(L); continue; }

            var id = 列[0].Trim();
            if (!待拆.ContainsKey(id)) { 输出.Add(L); continue; }

            var 列表 = 待拆[id];
            列表.Sort((a, b) => string.Compare(a.名, b.名, System.StringComparison.Ordinal));
            for (int k = 0; k < 列表.Count; k++)
            {
                var 新列 = (string[])列.Clone();
                新列[0] = id + "_" + (k + 1).ToString("00");
                新列[1] = 列[1] + (k + 1).ToString("00");
                新列[38] = 列表[k].路径;
                输出.Add(string.Join(",", 新列));
                prefab到新id[列表[k].名] = 新列[0];
                新增++;
            }
        }

        Write(输出);
        AssetDatabase.ImportAsset(表路径);

        // 删掉旧的共用定义 + 把 prefab 接成自己那份
        int 删 = 0;
        foreach (var id in 待拆.Keys)
        {
            var p = 定义目录 + "/" + id + ".asset";
            if (AssetDatabase.LoadAssetAtPath<NpcDefinition>(p) != null) { AssetDatabase.DeleteAsset(p); 删++; }
        }
        DataTableImporter.ImportAll();
        int 接 = 接线(prefab到新id);
        Debug.Log("[NPC版本] 拆分完成：家族 " + 待拆.Count + " 个 → 新增 " + 新增 + " 行；删旧定义 " + 删
                  + " 份；prefab 接线 " + 接 + " 个。表里现在 " + 输出.Count + " 行");
    }

    // ============================================================ 数值递推

    public static void 递推()
    {
        var 行 = 读表();
        if (行 == null) return;
        Backup();

        // 变体行 = id 形如 `<基>_NN` 且**不是手工家族**。
        //
        // 【坑·已修】以前这里还要求「名字也以同样的 NN 结尾」（靠它认拆分行、
        // 顺带排除手工家族）。命名步上线后名字变成了「白熊精·壮」，
        // 这个条件就永远不成立、递推会**整表跳过** ✗
        // 现在改成：**只看 id**，手工家族用显式名单排除（见 手工家族）。
        var 家族 = new Dictionary<string, List<(int 行号, int 版本)>>();
        for (int i = 1; i < 行.Count; i++)
        {
            var L = 行[i];
            if (string.IsNullOrWhiteSpace(L)) continue;
            var 列 = L.Split(',');
            if (列.Length != 列数) continue;

            var id = 列[0].Trim();
            if (!是变体id(id, out var 基)) continue;
            if (是手工家族(基)) continue;

            int 版本 = int.Parse(id.Substring(id.Length - 2));
            if (!家族.ContainsKey(基)) 家族[基] = new List<(int, int)>();
            家族[基].Add((i, 版本));
        }

        int 改 = 0;
        foreach (var kv in 家族)
        {
            var 列表 = new List<(int 行号, int 版本)>(kv.Value);
            列表.Sort((a, b) => a.版本.CompareTo(b.版本));
            if (列表.Count <= 1) continue;

            var 基列 = 行[列表[0].行号].Split(',');
            if (基列.Length != 列数) continue;
            float 基础境界;
            if (!float.TryParse(基列[2], out 基础境界)) continue;

            // 每版一档；高境界家族压缩步长，保证不越过满级
            float 步长 = Mathf.Min(1f, (满级 - 基础境界) / 10f / (列表.Count - 1));

            for (int i = 1; i < 列表.Count; i++)
            {
                float d = 步长 * i;
                var 列 = 行[列表[i].行号].Split(',');
                列[2] = Mathf.RoundToInt(基础境界 + 10f * d).ToString();

                float 取(int 列号) { float v; return float.TryParse(基列[列号], out v) ? v : 0f; }
                列[11] = Mathf.RoundToInt(取(11) * Mathf.Pow(气血比, d)).ToString();
                列[12] = Mathf.RoundToInt(取(12) * Mathf.Pow(攻击比, d)).ToString();
                列[13] = Mathf.RoundToInt(取(13) * Mathf.Pow(防御比, d)).ToString();
                列[14] = Mathf.RoundToInt(取(14) * Mathf.Pow(灵力比, d)).ToString();

                foreach (var x in 线性)
                    列[x.列] = (取(x.列) + x.步长 * d).ToString("0.####");

                // 回复类是按气血/灵力推的（实测 气血回复 = 气血×0.01、灵力回复 = 灵力×0.02）
                列[29] = (float.Parse(列[11]) * 0.01f).ToString("0.##");
                列[30] = (float.Parse(列[14]) * 0.02f).ToString("0.##");

                行[列表[i].行号] = string.Join(",", 列);
                改++;
            }
        }

        Write(行);
        AssetDatabase.ImportAsset(表路径);
        DataTableImporter.ImportAll();
        Debug.Log("[NPC版本] 数值递推完成：家族 " + 家族.Count + " 个，改了 " + 改 + " 行");
    }

    // ============================================================ 命名

    /// <summary>妖魔的称号，**顺序就是由弱到强**（用户要求：编号越低越弱、越高越强）</summary>
    static readonly string[] 妖魔称号 = { "幼", "少", "壮", "猛", "凶", "悍", "霸", "王" };

    /// <summary>
    /// 人类的称号，同样由弱到强。
    ///
    /// 【为什么不是「初学 / 小成 / 宗师」那套】人类这一列里**平民和神仙是混着的** ——
    /// 既有 老头 / 李村医 / 山城土匪，也有 嫦娥 / 哪吒 / 杨戬。
    /// 用「修士进阶」的词会出现「嫦娥·初学」「老头·大家」这种一眼别扭的名字 ✗
    /// 所以改用**气势档次**的词：对凡人和神仙都读得通。
    /// </summary>
    static readonly string[] 人类称号 = { "微末", "寻常", "出众", "拔萃", "超群", "绝顶", "盖世", "无双" };

    /// <summary>称号前的分隔符 —— 和手工起好的「白鹿精·幼 / 鬼气将·帅」保持一致</summary>
    const char 称号点 = '·';

    /// <summary>
    /// **素材名是英文、表里没翻译的，在这里补中文名**（译名用户已确认）。
    ///
    /// 只对**名字本身还是英文**的那几个家族用；键是名字里的「家族名」那一段，
    /// 命中后连单个 NPC（`npc_xiaoyaozi` 这种不成家族的）也一起改。
    /// 改完就再也匹配不上（键是英文），所以**反复跑不会出问题**。
    /// </summary>
    static readonly Dictionary<string, string> 名字修正 = new Dictionary<string, string>
    {
        { "LieQuan",   "猎犬" },     // demon_liequan_01/02/03 —— 犬形妖魔
        { "XiaoYaoZi", "逍遥子" },   // npc_xiaoyaozi + human_xiaoyaozi_01/02 —— 同一个人物
    };

    /// <summary>把名字里的英文家族名换成中文（不命中就原样返回）</summary>
    static string 修正名字(string 名)
    {
        if (string.IsNullOrEmpty(名)) return 名;
        return 名字修正.TryGetValue(名, out var 译) ? 译 : 名;
    }

    /// <summary>
    /// **给拆分出来的版本起个「由弱到强」的称呼**，把 `白熊精01` 换成 `白熊精·幼`。
    ///
    /// 规则：
    ///   · **只改「本步骤管得着」的行** —— 拆分脚本留下的 `<原名>NN`，或者上一次已经叫
    ///     `<原名>·<梯子上的称号>` 的。手工起过名的（白鹿精·幼 / 鬼气将·帅 / 冥妖·一 /
    ///     火狼 / 土地公…）一个都不动。
    ///   · 所以这一步**可以反复跑**：换一套称号词重跑一遍就能整表重排（见 名字可重排）。
    ///   · 同一个家族内按**境界升序**排名，再把 8 个称号**均匀铺开**：
    ///     最弱的拿第一个（幼 / 微末），最强的拿最后一个（王 / 无双）。
    ///     这样 2 个版本是「幼 / 王」，4 个版本是「幼 / 壮 / 悍 / 王」，8 个版本刚好全用上。
    ///   · 家族名从名字本身拆（`白熊精01` → `白熊精`），再过一遍 <see cref="名字修正"/>
    ///     （`LieQuan` → `猎犬`、`XiaoYaoZi` → `逍遥子` —— 素材名是英文的补中文）
    /// </summary>
    public static void 命名()
    {
        var 行 = 读表();
        if (行 == null) return;
        Backup();       // 改表之前先备份到工程外（工程没有 git）

        // ---- 1) 按 id 前缀把变体行聚成家族 ----
        var 家族 = new Dictionary<string, List<int>>();
        for (int i = 1; i < 行.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(行[i])) continue;
            var 列 = 行[i].Split(',');
            if (列.Length != 列数) continue;

            var id = 列[0].Trim();
            if (!是变体id(id, out var 基)) continue;
            if (是手工家族(基)) continue;

            if (!家族.ContainsKey(基)) 家族[基] = new List<int>();
            家族[基].Add(i);
        }

        // ---- 2) 家族内按境界排名，铺开称号 ----
        int 改 = 0;
        foreach (var kv in 家族)
        {
            var 列表 = new List<int>(kv.Value);
            列表.Sort((x, y) =>
            {
                int r = 取境界(行[x]).CompareTo(取境界(行[y]));
                if (r != 0) return r;
                return string.Compare(取id(行[x]), 取id(行[y]), System.StringComparison.Ordinal);
            });
            if (列表.Count <= 1) continue;      // 单行家族不叫"版本"，不动

            for (int k = 0; k < 列表.Count; k++)
            {
                var 列 = 行[列表[k]].Split(',');
                if (!名字可重排(列[1], out var 族名)) continue;   // 手工起的名 → 一个都不动
                if (string.IsNullOrEmpty(族名)) continue;
                族名 = 修正名字(族名);                            // 英文名在这里补中文

                string 称号 = 取称号(列[5].Trim(), k, 列表.Count);
                if (string.IsNullOrEmpty(称号)) continue;

                string 新名 = 族名 + 称号点 + 称号;
                if (列[1] == 新名) continue;

                列[1] = 新名;
                行[列表[k]] = string.Join(",", 列);
                改++;
            }
        }

        // ---- 3) 不成家族的单个 NPC 也要能翻译（`npc_xiaoyaozi` 的名字就是光秃秃的 `XiaoYaoZi`）----
        for (int i = 1; i < 行.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(行[i])) continue;
            var 列 = 行[i].Split(',');
            if (列.Length != 列数) continue;

            string 新名 = 修正名字(列[1].Trim());
            if (新名 == 列[1]) continue;

            列[1] = 新名;
            行[i] = string.Join(",", 列);
            改++;
        }

        if (改 == 0) { Debug.Log("[NPC版本] 命名：没有需要改名的行（可能已经跑过）"); return; }

        Write(行);
        AssetDatabase.ImportAsset(表路径);
        DataTableImporter.ImportAll();
        Debug.Log("[NPC版本] 命名完成：改了 " + 改 + " 行（家族 " + 家族.Count + " 个）");
    }

    /// <summary>家族里第 <paramref name="第几个"/> / 共 <paramref name="共几个"/> 个版本该叫什么</summary>
    static string 取称号(string 类型, int 第几个, int 共几个)
    {
        var 梯 = 类型 == "人类" ? 人类称号 : 妖魔称号;
        if (共几个 <= 1) return 梯[0];

        // 均匀铺开：第一个 → 梯[0]，最后一个 → 梯[末]
        int idx = Mathf.RoundToInt(第几个 * (梯.Length - 1) / (float)(共几个 - 1));
        return 梯[Mathf.Clamp(idx, 0, 梯.Length - 1)];
    }

    // ============================================================ 工具

    /// <summary>id 是不是 `<基>_NN` 这种变体写法</summary>
    static bool 是变体id(string id, out string 基)
    {
        基 = null;
        if (string.IsNullOrEmpty(id) || id.Length < 4) return false;
        if (id[id.Length - 3] != '_') return false;
        if (!char.IsDigit(id[id.Length - 2]) || !char.IsDigit(id[id.Length - 1])) return false;

        基 = id.Substring(0, id.Length - 3);
        return true;
    }

    static string 取id(string 行文本)
    {
        var 列 = 行文本.Split(',');
        return 列.Length > 0 ? 列[0] : "";
    }

    static int 取境界(string 行文本)
    {
        var 列 = 行文本.Split(',');
        int v;
        return 列.Length > 2 && int.TryParse(列[2], out v) ? v : 0;
    }

    /// <summary>
    /// 这个名字**归本步骤管**吗；是的话顺带把家族名拆出来。
    ///
    /// 两种情况都算：
    ///   · `<原名>NN`（拆分脚本留下的）→ 家族名 = 去掉结尾数字
    ///   · `<原名>·<梯子上已有的称号>`（本步骤上一次跑出来的）→ 家族名 = 点号前面那截
    ///
    /// 第二种是为了**能换一套称号词重跑**（比如把人类的「初学」换成「微末」）。
    /// 手工起的名字（冥妖·一 / 鬼气将·帅 / 火狼 / 土地公）两种都不匹配，所以永远不动；
    /// 另外手工家族在收集阶段就被 <see cref="手工家族"/> 排除了，不会被重排。
    /// </summary>
    static bool 名字可重排(string 名, out string 族名)
    {
        族名 = null;
        if (string.IsNullOrEmpty(名)) return false;

        if (char.IsDigit(名[名.Length - 1])) { 族名 = 去变体后缀(名); return true; }

        int 点 = 名.LastIndexOf(称号点);
        if (点 <= 0 || 点 == 名.Length - 1) return false;

        string 尾 = 名.Substring(点 + 1);
        if (System.Array.IndexOf(妖魔称号, 尾) < 0 && System.Array.IndexOf(人类称号, 尾) < 0) return false;

        族名 = 名.Substring(0, 点);
        return true;
    }

    /// <summary>`白熊精01` → `白熊精`。**不以数字结尾就原样返回**（手工起的名字）</summary>
    static string 去变体后缀(string 名)
    {
        if (string.IsNullOrEmpty(名)) return "";
        int i = 名.Length;
        while (i > 0 && char.IsDigit(名[i - 1])) i--;
        return i == 名.Length ? 名 : 名.Substring(0, i);
    }

    // ============================================================ 工具（原有）

    /// <summary>按「定义 id」把 demon / human 的 prefab 分组</summary>
    static Dictionary<string, List<(string 名, string 路径)>> 扫prefab家族()
    {
        var 家族 = new Dictionary<string, List<(string, string)>>();
        foreach (var g in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/resources/NPC" }))
        {
            var p = AssetDatabase.GUIDToAssetPath(g);
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(p);
            if (go == null) continue;
            var inst = go.GetComponent<NpcInstance>();
            if (inst == null || inst.定义 == null) continue;
            if (inst.定义.类型 != NpcKind.妖魔 && inst.定义.类型 != NpcKind.人类) continue;

            var res = p.Substring("Assets/resources/".Length);
            res = res.Substring(0, res.Length - ".prefab".Length);
            var id = inst.定义.id;
            if (!家族.ContainsKey(id)) 家族[id] = new List<(string, string)>();
            家族[id].Add((Path.GetFileNameWithoutExtension(p), res));
        }
        return 家族;
    }

    /// <summary>把每个 prefab 的 NpcInstance.定义 接成它自己那一份</summary>
    static int 接线(Dictionary<string, string> prefab到新id)
    {
        int 接 = 0;
        foreach (var g in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/resources/NPC" }))
        {
            var p = AssetDatabase.GUIDToAssetPath(g);
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(p);
            if (go == null) continue;
            var inst = go.GetComponent<NpcInstance>();
            if (inst == null) continue;

            var 名 = Path.GetFileNameWithoutExtension(p);
            string 目标id = prefab到新id.ContainsKey(名) ? prefab到新id[名] : (inst.定义 != null ? inst.定义.id : null);
            if (string.IsNullOrEmpty(目标id)) continue;

            var 新定义 = AssetDatabase.LoadAssetAtPath<NpcDefinition>(定义目录 + "/" + 目标id + ".asset");
            if (新定义 == null) { Debug.LogWarning("[NPC版本] " + 名 + " 找不到定义 " + 目标id); continue; }
            if (inst.定义 == 新定义) continue;
            inst.定义 = 新定义;
            EditorUtility.SetDirty(inst);
            接++;
        }
        return 接;
    }

    static List<string> 读表()
    {
        if (!File.Exists(表路径)) { Debug.LogError("[NPC版本] 找不到 " + 表路径); return null; }
        var 字节 = File.ReadAllBytes(表路径);
        bool 有BOM = 字节.Length >= 3 && 字节[0] == 0xEF && 字节[1] == 0xBB && 字节[2] == 0xBF;
        var 文本 = new UTF8Encoding(false).GetString(字节, 有BOM ? 3 : 0, 字节.Length - (有BOM ? 3 : 0));
        表有BOM = 有BOM;
        return new List<string>(文本.Split('\n'));
    }

    static bool 表有BOM = true;

    static void Write(List<string> 行)
    {
        File.WriteAllText(表路径, string.Join("\n", 行), new UTF8Encoding(表有BOM));
    }

    /// <summary>改表之前先备份到工程外（没有 git，改坏了能捞回来）</summary>
    static void Backup()
    {
        try
        {
            var 名 = "_backup_NPC表_" + System.DateTime.Now.ToString("MMdd_HHmmss") + ".csv.bak";
            File.WriteAllBytes(Path.Combine(备份目录, 名), File.ReadAllBytes(表路径));
            Debug.Log("[NPC版本] 已备份 " + 名);
        }
        catch (System.Exception e) { Debug.LogWarning("[NPC版本] 备份失败（继续）：" + e.Message); }
    }
}
