using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 配置表导入器：把 Assets/Data/Tables/*.csv 解析成 ScriptableObject 资产。
///
/// 约定：
///   · 第一行是表头，列名直接对应父类的字段名（中文）。
///   · 属性列用「属性名 + 增益/加成」或「属性名」，会自动写进该表的 AttributeSet 字段。
///   · 位置列写 "x|y|z"；列表列写 "id:数量[:概率]|id:数量"。
///   · 布尔列写 是/否；枚举列直接写枚举成员名。
///   · 资产生成到 Assets/Data/Generated/&lt;类型名&gt;/&lt;id&gt;.asset，可重复执行（按 id 覆盖更新）。
///
/// 菜单：修仙 / 从配置表生成资产  （ASCII 别名：Cultivation / Import Data Tables）
/// </summary>
public static class DataTableImporter
{
    const string TableDir = "Assets/Data/Tables";
    const string OutRoot = "Assets/Data/Generated";
    const string ScenePath = "Assets/Scenes/3C_Testbed.scene";

    /// <summary>一张表的定义</summary>
    class TableSpec
    {
        public string Csv;              // 表文件名（不含 .csv）
        public Type Type;               // 目标类型
        public string AttrField;        // 属性列写进哪个 AttributeSet 字段；null 表示本表没有属性列
        public string[] AttrSuffixes;   // 属性列可能带的后缀

        public TableSpec(string csv, Type type, string attrField, params string[] suffixes)
        {
            Csv = csv; Type = type; AttrField = attrField; AttrSuffixes = suffixes;
        }
    }

    static readonly TableSpec[] Specs =
    {
        new TableSpec("物品表",     typeof(ItemDefinition),          null,       null),
        new TableSpec("功法表",     typeof(GongFaDefinition),        "每级增益", "增益"),
        new TableSpec("主动神通表", typeof(ActiveDivineAbility),     null,       null),
        new TableSpec("被动神通表", typeof(PassiveDivineAbility),    "增益",     "增益"),
        new TableSpec("法宝表",     typeof(TreasureDefinition),      null,       null),
        new TableSpec("NPC表",      typeof(NpcDefinition),           "属性",     null),
        new TableSpec("灵阵表",     typeof(SpiritArrayDefinition),   null,       null),
        new TableSpec("采集物表",   typeof(GatherNodeDefinition),    null,       null),
        new TableSpec("境界表",     typeof(RealmDefinition),         null,       null),
        // 坐骑：表头照「被动神通表」来的，属性列同样以「增益」结尾
        new TableSpec("坐骑表",     typeof(MountDefinition),         "增益",     "增益"),
        // 对话：每行一段对话（同一 npcId 的若干「分段」= 该 NPC 的默认对话；回答可跳转分段；带条件列给任务管理器用）
        new TableSpec("对话表",     typeof(DialogueDefinition),      null,       null),
        new TableSpec("任务表",     typeof(QuestDefinition),         null,       null),
        new TableSpec("外观表",     typeof(AppearanceDefinition),    null,       null),
        // 「兽宠表」已删 —— 用户决定不要兽宠系统了（战阵真灵取代了它）
    };

    /// <summary>
    /// 找出「CSV 比生成资产还新」的表 —— 也就是改了配置表但忘了跑导入器。
    /// 游戏读的永远是 <c>Assets/Data/Generated</c> 下的资产，不是 CSV，
    /// 所以这种情况会表现成「明明改了表，游戏里没变化」，非常容易被误判成代码 bug。
    ///
    /// **只读**：不写任何文件。返回空列表 = 全部已同步。
    /// </summary>
    public static List<string> 找出未同步的表()
    {
        var 结果 = new List<string>();

        foreach (var spec in Specs)
        {
            string csv = TableDir + "/" + spec.Csv + ".csv";
            if (!File.Exists(csv)) continue;

            string dir = OutRoot + "/" + spec.Type.Name;
            if (!Directory.Exists(dir)) { 结果.Add(spec.Csv); continue; }

            DateTime csv时间 = File.GetLastWriteTimeUtc(csv);
            DateTime 最新资产 = DateTime.MinValue;
            foreach (var f in Directory.GetFiles(dir, "*.asset"))
            {
                var t = File.GetLastWriteTimeUtc(f);
                if (t > 最新资产) 最新资产 = t;
            }

            // 给 2 秒容差：改完表马上导入时，两者时间戳可能只差几十毫秒
            //（或落在同一个文件系统时间粒度里），不该被判成"未同步"
            if (最新资产 == DateTime.MinValue || csv时间 > 最新资产.AddSeconds(2)) 结果.Add(spec.Csv);
        }

        return 结果;
    }

    /// <summary>本导入器认得的表数量（用来在提示里报个数）</summary>
    public static int 表数量 => Specs.Length;

    [MenuItem("修仙/从配置表生成资产")]
    [MenuItem("Cultivation/Import Data Tables")]
    public static void ImportAll()
    {
        var report = new StringBuilder();
        var itemById = new Dictionary<string, ItemDefinition>();

        // ---- 第一遍：生成 / 更新资产（不含跨表引用）----
        foreach (var spec in Specs)
        {
            string path = TableDir + "/" + spec.Csv + ".csv";
            if (!File.Exists(path)) { report.Append("跳过（文件不存在）: ").Append(path).Append("\n"); continue; }

            var rows = ParseCsv(File.ReadAllText(path, Encoding.UTF8));
            if (rows.Count < 2) { report.Append("跳过（无数据行）: ").Append(spec.Csv).Append("\n"); continue; }

            var header = rows[0];
            string idField = FindIdField(spec.Type);
            int created = 0, updated = 0;

            for (int r = 1; r < rows.Count; r++)
            {
                var row = rows[r];
                if (row.Count == 0 || string.IsNullOrWhiteSpace(Get(row, 0))) continue;

                string id = null;
                foreach (var kv in MapRow(header, row))
                    if (kv.Key == idField) { id = kv.Value; break; }
                if (string.IsNullOrEmpty(id)) id = Get(row, 0);
                if (string.IsNullOrEmpty(id)) continue;

                string assetPath = OutRoot + "/" + spec.Type.Name + "/" + Sanitize(id) + ".asset";
                EnsureFolder(Path.GetDirectoryName(assetPath).Replace('\\', '/'));

                var so = AssetDatabase.LoadAssetAtPath<ScriptableObject>(assetPath);
                bool isNew = so == null;
                if (isNew) { so = ScriptableObject.CreateInstance(spec.Type); AssetDatabase.CreateAsset(so, assetPath); }
                else created += 0;

                ApplyRow(so, spec, header, row, report);

                EditorUtility.SetDirty(so);
                if (isNew) created++; else updated++;

                if (so is ItemDefinition item)
                {
                    string iid = idField != null ? (string)spec.Type.GetField(idField).GetValue(item) : id;
                    if (string.IsNullOrEmpty(iid)) iid = item.物品id;
                    if (!string.IsNullOrEmpty(iid)) itemById[iid] = item;
                    if (!string.IsNullOrEmpty(item.物品id)) itemById[item.物品id] = item;
                }
            }
            report.Append(spec.Csv).Append(": 新建 ").Append(created).Append(" 更新 ").Append(updated).Append("\n");
        }

        AssetDatabase.SaveAssets();

        // 对话资产在 Generated 下、运行时读不到 → 顺手摊平成 Resources/对话/对话库.asset
        try { DialogueDatabaseBuilder.收集(false); }
        catch (System.Exception e) { report.Append("对话库收集失败: ").Append(e.Message).Append("\n"); }

        // 任务阶段同理 → Resources/任务/任务库.asset
        try { QuestDatabaseBuilder.收集(false); }
        catch (System.Exception e) { report.Append("任务库收集失败: ").Append(e.Message).Append("\n"); }

        // ---- 第二遍：解析跨表引用（背包物品 / 掉落物）----
        foreach (var spec in Specs)
        {
            string path = TableDir + "/" + spec.Csv + ".csv";
            if (!File.Exists(path)) continue;
            var rows = ParseCsv(File.ReadAllText(path, Encoding.UTF8));
            if (rows.Count < 2) continue;
            var header = rows[0];
            string idField = FindIdField(spec.Type);

            for (int r = 1; r < rows.Count; r++)
            {
                var row = rows[r];
                if (row.Count == 0 || string.IsNullOrWhiteSpace(Get(row, 0))) continue;
                string id = null;
                foreach (var kv in MapRow(header, row)) if (kv.Key == idField) { id = kv.Value; break; }
                if (string.IsNullOrEmpty(id)) id = Get(row, 0);

                string assetPath = OutRoot + "/" + spec.Type.Name + "/" + Sanitize(id) + ".asset";
                var so = AssetDatabase.LoadAssetAtPath<ScriptableObject>(assetPath);
                if (so == null) continue;

                ResolveItemLists(so, header, row, itemById);
                EditorUtility.SetDirty(so);
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        // ---- 顺手把 UI 数据源指向生成的资产 ----
        RewirePanelData();

        EditorSceneManager_MarkAndSave();
        Debug.Log("[DataTableImporter] 导入完成：\n" + report);
    }

    // ------------------------------------------------------------------ 行应用

    static void ApplyRow(ScriptableObject so, TableSpec spec, List<string> header, List<string> row, StringBuilder report)
    {
        var type = spec.Type;
        AttributeSet attrSet = null;

        if (!string.IsNullOrEmpty(spec.AttrField))
        {
            var f = type.GetField(spec.AttrField, BindingFlags.Public | BindingFlags.Instance);
            if (f != null) attrSet = f.GetValue(so) as AttributeSet;
            if (attrSet == null && f != null) { attrSet = new AttributeSet(); f.SetValue(so, attrSet); }
            if (attrSet != null) attrSet.Clear();
        }

        for (int c = 0; c < header.Count && c < row.Count; c++)
        {
            string col = header[c].Trim();
            string val = row[c].Trim();
            if (string.IsNullOrEmpty(col)) continue;

            // 属性列？
            if (attrSet != null)
            {
                var at = MatchAttribute(col, spec.AttrSuffixes);
                if (at.HasValue)
                {
                    if (!string.IsNullOrEmpty(val)) attrSet[at.Value] = ParseFloat(val);
                    continue;
                }
            }

            // 跨表引用列，第二遍处理
            if (col == "背包物品" || col == "掉落物") continue;

            var field = type.GetField(col, BindingFlags.Public | BindingFlags.Instance);
            if (field == null) continue;   // 多余列（例如纯注释列）直接忽略
            if (!string.IsNullOrEmpty(val)) SetFieldValue(so, field, val);
        }
    }

    static void ResolveItemLists(ScriptableObject so, List<string> header, List<string> row, Dictionary<string, ItemDefinition> itemById)
    {
        var type = so.GetType();
        for (int c = 0; c < header.Count && c < row.Count; c++)
        {
            string col = header[c].Trim();
            if (col != "背包物品" && col != "掉落物") continue;

            var field = type.GetField(col, BindingFlags.Public | BindingFlags.Instance);
            if (field == null) continue;

            var list = new List<ItemStack>();
            string val = row[c].Trim();
            if (!string.IsNullOrEmpty(val))
            {
                foreach (var entry in val.Split('|'))
                {
                    var parts = entry.Split(':');
                    if (parts.Length < 2) continue;
                    string itemId = parts[0].Trim();
                    if (!itemById.TryGetValue(itemId, out var item)) continue;
                    int count = (int)ParseFloat(parts[1]);
                    float chance = parts.Length >= 3 ? ParseFloat(parts[2]) : 1f;
                    list.Add(new ItemStack { 物品 = item, 数量 = Mathf.Max(1, count), 概率 = chance });
                }
            }
            field.SetValue(so, list);
        }
    }

    /// <summary>把列名匹配到属性枚举。会先剥掉后缀（增益/加成），再比对中文显示名</summary>
    static AttributeType? MatchAttribute(string col, string[] suffixes)
    {
        string key = col;
        if (suffixes != null)
            foreach (var s in suffixes)
                if (!string.IsNullOrEmpty(s) && key.EndsWith(s, StringComparison.Ordinal))
                { key = key.Substring(0, key.Length - s.Length); break; }

        for (int i = 0; i < AttributeUtil.Count; i++)
        {
            var t = (AttributeType)i;
            if (AttributeUtil.GetDisplayName(t) == key) return t;
            if (t.ToString() == key) return t;
        }
        return null;
    }

    static void SetFieldValue(object target, FieldInfo field, string raw)
    {
        var ft = field.FieldType;
        try
        {
            if (ft == typeof(string)) field.SetValue(target, raw);
            // 【坑·已修】整型以前也走 ParseFloat（float），而 float 只能精确表示到 16,777,216 ——
            // 境界表的灵气值到 23 亿，用 float 解析会**丢精度**
            // （实测 83733876 → 83733872、2368614941 → 2368614912）。
            // 整型一律先按整型解析，解析不了再退回 float。
            else if (ft == typeof(int))
            {
                if (int.TryParse(raw, out int iv2)) field.SetValue(target, iv2);
                else field.SetValue(target, (int)ParseFloat(raw));
            }
            else if (ft == typeof(long))
            {
                if (long.TryParse(raw, out long lv)) field.SetValue(target, lv);
                else field.SetValue(target, (long)ParseFloat(raw));
            }
            else if (ft == typeof(float)) field.SetValue(target, ParseFloat(raw));
            else if (ft == typeof(double)) field.SetValue(target, (double)ParseFloat(raw));
            else if (ft == typeof(bool)) field.SetValue(target, ParseBool(raw));
            else if (ft == typeof(Vector3)) field.SetValue(target, ParseVector3(raw));
            else if (ft.IsEnum)
            {
                foreach (var name in Enum.GetNames(ft))
                    if (string.Equals(name, raw, StringComparison.OrdinalIgnoreCase))
                    { field.SetValue(target, Enum.Parse(ft, name)); return; }
                // 数字形式的枚举值
                if (int.TryParse(raw, out int iv)) field.SetValue(target, Enum.ToObject(ft, iv));
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("[DataTableImporter] 字段 " + field.Name + " 解析失败（值=" + raw + "）：" + e.Message);
        }
    }

    static float ParseFloat(string s)
    {
        if (string.IsNullOrEmpty(s)) return 0f;
        if (float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float v)) return v;
        if (float.TryParse(s, NumberStyles.Float, CultureInfo.CurrentCulture, out v)) return v;
        return 0f;
    }

    static bool ParseBool(string s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        s = s.Trim();
        if (s == "是" || s == "true" || s == "True" || s == "1" || s == "Y" || s == "y") return true;
        return false;
    }

    static Vector3 ParseVector3(string s)
    {
        var parts = s.Split('|');
        if (parts.Length < 3) return Vector3.zero;
        return new Vector3(ParseFloat(parts[0]), ParseFloat(parts[1]), ParseFloat(parts[2]));
    }

    // ------------------------------------------------------------------ 工具

    /// <summary>找该类型里代表 id 的字符串字段（名字以 id 结尾，或就叫 id）</summary>
    static string FindIdField(Type t)
    {
        foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            if (f.FieldType != typeof(string)) continue;
            if (f.Name == "id" || f.Name.EndsWith("id", StringComparison.OrdinalIgnoreCase)) return f.Name;
        }
        return null;
    }

    static IEnumerable<KeyValuePair<string, string>> MapRow(List<string> header, List<string> row)
    {
        for (int i = 0; i < header.Count && i < row.Count; i++)
            yield return new KeyValuePair<string, string>(header[i].Trim(), row[i].Trim());
    }

    static string Get(List<string> row, int i) => i < row.Count ? row[i] : "";

    static string Sanitize(string s)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
        return s;
    }

    static void EnsureFolder(string folder)
    {
        if (string.IsNullOrEmpty(folder) || AssetDatabase.IsValidFolder(folder)) return;
        var parent = Path.GetDirectoryName(folder).Replace('\\', '/');
        var leaf = Path.GetFileName(folder);
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, leaf);
    }

    /// <summary>极简 CSV 解析：支持双引号包裹与 "" 转义</summary>
    static List<List<string>> ParseCsv(string text)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var cell = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < text.Length; i++)
        {
            char ch = text[i];
            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; }
                    else inQuotes = false;
                }
                else cell.Append(ch);
            }
            else
            {
                if (ch == '"') inQuotes = true;
                else if (ch == ',') { row.Add(cell.ToString()); cell.Clear(); }
                else if (ch == '\n')
                {
                    row.Add(cell.ToString()); cell.Clear();
                    rows.Add(row); row = new List<string>();
                }
                else if (ch == '\r') { /* 忽略 */ }
                else cell.Append(ch);
            }
        }
        if (cell.Length > 0 || row.Count > 0) { row.Add(cell.ToString()); rows.Add(row); }

        // 去掉完全空白的行
        rows.RemoveAll(r => r.TrueForAll(string.IsNullOrWhiteSpace));
        return rows;
    }

    // ------------------------------------------------------------------ 刷新 UI 数据源

    /// <summary>把生成出来的资产灌进 CharacterUI 的 UIPanelData，方便直接在面板里看到</summary>
    public static void RewirePanelData()
    {
        var ui = GameObject.Find("CharacterUI");
        if (ui == null) return;
        var data = ui.GetComponent<UIPanelData>();
        if (data == null) return;

        data.物品 = LoadAll<ItemDefinition>();
        data.神通 = new List<DivineAbilityDefinition>();
        foreach (var a in LoadAll<ActiveDivineAbility>()) data.神通.Add(a);
        foreach (var p in LoadAll<PassiveDivineAbility>()) data.神通.Add(p);
        data.法宝 = LoadAll<TreasureDefinition>();
        data.灵阵 = LoadAll<SpiritArrayDefinition>();

        // ---- 坐骑 ----
        // 和真灵一样**自动收集**：坐骑表里有一行就进列表，不用手工维护白名单。
        // 排序用运行时同一个比较器（门槛由低到高），避免两处各排一套。
        if (data.坐骑 == null) data.坐骑 = new List<MountDefinition>();
        data.坐骑.Clear();
        foreach (var m in LoadAll<MountDefinition>()) if (m != null) data.坐骑.Add(m);
        data.坐骑.Sort(UIPanelData.比坐骑);

        // ---- 战阵真灵 ----
        // 「已获得的真灵」= **所有能当真灵的 NPC**（`模型资源路径` 非空 = 有 prefab 的妖魔 / 人类）。
        //
        // 【已修】以前这里是一份**写死的白名单**（只有三只白鹿精），
        // 结果是「新做完一只怪，它不会出现在战阵真灵里」—— 用户反馈过
        // 「白熊精没有同步到战阵真灵中」✗ 现在改成**自动收集**：
        // 导入配置表 / 重建面板之后，做完的怪自动就能上阵。
        //
        // 排序：**妖魔 → 人类 → 同族相邻 → 族内由弱到强**（见 UIPanelData.比真灵）。
        // 用户在列表里要的是"同一族的挨在一起、人类别和 demon 混着"，
        // 所以这里和运行时 GetSpirits() 用**同一个比较器**，避免两处各排一套。
        if (data.已获得真灵 == null) data.已获得真灵 = new List<NpcDefinition>();
        data.已获得真灵.Clear();
        foreach (var npc in LoadAll<NpcDefinition>())
            if (能当真灵(npc)) data.已获得真灵.Add(npc);
        data.已获得真灵.Sort(UIPanelData.比真灵);
        data.EnsureLists();      // 站位列表补成 9 格

        // ---- 玩家身上的 PlayerCultivation（修炼系统）也要接 ----
        // 【为什么要管它】它和 UIPanelData 一样，装的是**场景里的引用**：
        // 场景被重建 / 玩家组件被重新 AddComponent 之后，面板数据、境界表、功法表
        // 会**全部留空**。表现是修炼小屋界面上「当前境界」显示成「—」、
        // 「修炼一次」没反应 —— 而且不报任何错，很难查。
        // 放在这里 = 跑一次「从配置表生成资产」就能自愈。
        var 玩家对象 = GameObject.Find("Player");
        var 修炼 = 玩家对象 != null ? 玩家对象.GetComponent<PlayerCultivation>() : null;
        if (修炼 == null) 修炼 = UnityEngine.Object.FindObjectOfType<PlayerCultivation>();
        if (修炼 != null)
        {
            修炼.面板数据 = data;

            // ★★★【坑】境界表**必须按 等级 升序**，不能直接用 LoadAll 的结果！
            // LoadAll 内部是按**资产名**排序的（元婴第10层 / 元婴第1层 / 化神第10层 …），
            // 而 PlayerCultivation.查境界() 里有一个 `else break` —— 它假定数组是升序的，
            // 顺序一乱就会提前退出、返回一个离谱的境界。
            // 实测后果：玩家明明是「炼气第1层」，查境界(0) 却返回「元婴第10层」，
            // 于是修炼小屋的「转修后境界预估」显示成 元婴第10层，看起来像转修功能坏了。
            var 境界们 = LoadAll<RealmDefinition>();
            境界们.Sort((a, b) => (a != null ? a.等级 : 0).CompareTo(b != null ? b.等级 : 0));
            修炼.境界表 = 境界们.ToArray();

            // 功法表没有顺序要求，直接给
            修炼.功法表 = LoadAll<GongFaDefinition>().ToArray();

            EditorUtility.SetDirty(修炼);
            Debug.Log("[DataTableImporter] 已接 PlayerCultivation：境界表 "
                      + 修炼.境界表.Length + " 条（已按等级升序，1→" + (修炼.境界表.Length > 0 ? 修炼.境界表[修炼.境界表.Length - 1].等级.ToString() : "?")
                      + "）、功法表 " + 修炼.功法表.Length + " 条、面板数据="
                      + (修炼.面板数据 != null ? 修炼.面板数据.name : "null"));
        }

        if (data.玩家属性 == null)
        {
            var stats = AssetDatabase.LoadAssetAtPath<PlayerStatsDefinition>("Assets/DemoData/PlayerStats_Demo.asset");
            if (stats != null) data.玩家属性 = stats;
        }
        // ★ 用户 2026-09-26：主角不再是"天生带着功法"。当前功法留空，等玩家在背包里
        //   「使用」秘籍类物品学会之后，学功法效果会把它设为当前修炼（见 学功法效果.使用）。
        //   所以这里**不再自动指定**。（`找当前功法()` 保留给调试/其它工具用）

        // 主动技能槽：**全部空着**（原来是"前两个主动神通 + 灵阵 + 法宝"，
        // 那是"天生全会"的旧口径；现在主动神通要靠物品获得、自己装备）
        data.主动技能 = new List<UnityEngine.Object>();
        while (data.主动技能.Count < 6) data.主动技能.Add(null);

        EditorUtility.SetDirty(data);

        // 重灌各列表
        foreach (var list in ui.GetComponentsInChildren<UIEntryList>(true))
        {
            string page = list.transform.parent != null ? list.transform.parent.name : "";
            string panel = list.gameObject.name;
            if (panel == "PassiveList") list.SetEntries(data.GetPassiveAbilities());
            else if (panel == "KnownList") list.SetEntries(page == "Page_灵阵" ? data.GetSpiritArrays() : data.GetAbilities());
            else if (panel == "OwnedList") list.SetEntries(data.GetTreasures());
            else if (panel == "ArrayList") list.SetEntries(data.GetSpiritArrays());
            else if (panel == "BagGrid") list.SetEntries(data.GetItems());
            else if (panel == "MountList") list.SetEntries(data.GetMounts());
            EditorUtility.SetDirty(list);
        }
        foreach (var info in ui.GetComponentsInChildren<UIEntryInfo>(true))
            if (info.gameObject.name == "GongFaShow") { info.Show(data.当前功法); EditorUtility.SetDirty(info); }
        foreach (var bar in ui.GetComponentsInChildren<UIActiveSkillBar>(true)) { bar.Refresh(); EditorUtility.SetDirty(bar); }

        // 境界页：左侧的角色属性列举 + 右下的境界进度条
        foreach (var attr in ui.GetComponentsInChildren<UIAttributeList>(true))
        {
            attr.source = data.玩家属性;
            attr.Rebuild();
            EditorUtility.SetDirty(attr);
        }
        foreach (var realmBar in ui.GetComponentsInChildren<UIRealmBar>(true))
        {
            realmBar.data = data;
            realmBar.Refresh();
            EditorUtility.SetDirty(realmBar);
        }
    }

    /// <summary>
    /// 找出玩家当前修炼的功法：拿玩家身上普攻方法的方法id，去功法表里反查「普攻方法id」相同的功法。
    /// 这样境界页显示的功法与玩家实际能用的普攻永远是一致的，不会因为改名 / 排序而错位。
    /// </summary>
    static GongFaDefinition 找当前功法()
    {
        string 方法id = null;

        var player = GameObject.Find("Player");
        if (player != null)
        {
            var sword = player.GetComponent<BasicSword01>();
            if (sword != null) 方法id = sword.方法id;
        }

        var 全部 = LoadAll<GongFaDefinition>();

        if (!string.IsNullOrEmpty(方法id))
        {
            foreach (var gf in 全部)
                if (gf.普攻方法id == 方法id) return gf;
            Debug.LogWarning("[DataTableImporter] 没有功法提供普攻方法 " + 方法id + "，退回第一款功法");
        }

        return 全部.Count > 0 ? 全部[0] : null;
    }

    /// <summary>
    /// 「能当战阵真灵」的判定：**所有 demon / human 都算，只要它做完了 AI**。
    ///
    /// 实现上就一条：`NpcDefinition.模型资源路径` 非空
    /// （NPC表.csv 里**只有妖魔 / 人类**填了这一列，兽类 / 中立设施没填）。
    ///
    /// > 【用户明确要求】真灵列表要**包括土地公、铁匠这种 NPC** ——
    /// > 所有 demon / human 在完成 AI 之后都应该能进真灵列表供战阵选择。
    /// > 我一度擅自收窄成"只收有专属 AI 脚本的怪"，被明确纠正了 ✗ **别再自行加过滤条件**。
    /// > （按 id 去匹配物种脚本还会误伤：`demon_gou**tou**junshi` 里含 `_gou`，
    /// >   会被认成狗的专属 AI —— 这也是别用脚本判定来过滤的一个理由。）
    /// </summary>
    static bool 能当真灵(NpcDefinition npc)
        => npc != null && !string.IsNullOrEmpty(npc.模型资源路径);

    static List<T> LoadAll<T>() where T : ScriptableObject
    {
        var result = new List<T>();
        string folder = OutRoot + "/" + typeof(T).Name;
        if (!AssetDatabase.IsValidFolder(folder)) return result;
        foreach (var guid in AssetDatabase.FindAssets("t:" + typeof(T).Name, new[] { folder }))
        {
            var a = AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid));
            if (a != null) result.Add(a);
        }
        result.Sort((x, y) => string.Compare(x.name, y.name, StringComparison.Ordinal));
        return result;
    }

    /// <summary>
    /// 导入完把**当前打开的那个场景**存一下（因为 <see cref="RewirePanelData"/> 改的是
    /// 场景里 <see cref="UIPanelData"/> 组件上的引用）。
    ///
    /// ★★★【血的教训 · 2026-09-23】★★★
    /// 这里以前是这一行：
    /// <code>
    /// EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), ScenePath);
    /// </code>
    /// —— **无条件把"当前活动场景"按硬编码路径存成 `3C_Testbed.scene`**。
    /// 只要那一刻开着的是别的场景（比如 `Sect`），**那个场景的内容就会被整个写进
    /// `3C_Testbed.scene`**，把原来的 3C_Testbed 顶掉。
    /// 用户就是这么丢掉 3C_Testbed 的环境的（`Ground` / `Props*` 全没了）。
    ///
    /// **规矩：`SaveScene` 永远不要传 path，让它存回自己的路径。**
    /// 想存别的场景就显式 `OpenScene` 过去，别用"活动场景 + 硬编码路径"这种组合。
    /// </summary>
    static void EditorSceneManager_MarkAndSave()
    {
        var scene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();

        // 未命名场景没有路径，存不了（也不该在这里强行存成某个固定文件）
        if (string.IsNullOrEmpty(scene.path))
        {
            Debug.LogWarning("[DataTableImporter] 当前场景还没保存过（没有路径），跳过存场景。");
            return;
        }

        if (scene.path != ScenePath)
            Debug.Log("[DataTableImporter] 存的是当前打开的场景「" + scene.path
                      + "」，不是 " + ScenePath + "（按下标保存，不会覆盖别的场景）。");

        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
        UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);   // ★ 不传 path = 存回自己
    }
}
