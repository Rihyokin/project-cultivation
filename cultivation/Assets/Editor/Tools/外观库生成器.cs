using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// **外观表 → 资产 → 外观库** 一条龙。
///
/// 菜单：
///   · **修仙/外观/生成外观表** —— 按固定字段数组写出 `Assets/Data/Tables/外观表.csv`（带字段数断言，
///     手写逗号数吃过亏）。已存在就不覆盖，避免吃掉你手改的内容。
///   · **修仙/外观/收集外观资产** —— 把 `Assets/Data/Generated/AppearanceDefinition/` 收到
///     `Assets/resources/外观/外观库.asset`；顺手清掉早期手建在 `resources/外观/` 下的重复资产
///     （同一个 id 两份资产会让 `取(id)` 命中哪份变得随机）。
///   · **修仙/外观/一键：生成表 + 导入 + 收集**
///
/// 加新外观的完整流程见 `外观系统说明.md`。
/// </summary>
public static class 外观库生成器
{
    public const string 表路径 = "Assets/Data/Tables/外观表.csv";
    public const string 库路径 = "Assets/resources/外观/外观库.asset";
    const string 生成目录 = "Assets/Data/Generated/AppearanceDefinition";
    const string 手建目录 = "Assets/resources/外观";

    static readonly string[] 表头 = { "id", "外观名", "模型资源路径", "介绍", "默认拥有", "解锁标记", "解锁即装备" };

    static readonly string[][] 默认行 =
    {
        new[] { "app_cunmin", "村中少年", "NPC/Human/成男村民/成男村民", "还没踏入仙途时的样子。", "1", "", "1" },
        new[] { "app_player", "修仙者", "外观/修仙者", "正式踏入仙途之后的样子。", "0", "q_入宗门_完成", "1" },
    };

    [MenuItem("修仙/外观/生成外观表", false, 500)]
    public static void 生成表()
    {
        if (System.IO.File.Exists(表路径)) { Debug.Log("[外观] 表已存在，没覆盖：" + 表路径); return; }
        int 错 = 0;
        for (int i = 0; i < 默认行.Length; i++)
            if (默认行[i].Length != 表头.Length) { 错++; Debug.LogError("[外观] 第" + (i + 2) + "行字段数=" + 默认行[i].Length + " 期望=" + 表头.Length); }
        if (错 > 0) { Debug.LogError("[外观] 字段数不对，已中止写表"); return; }

        var 文 = new System.Text.StringBuilder();
        文.Append(string.Join(",", 表头)).Append("\n");
        foreach (var r in 默认行) 文.Append(string.Join(",", r)).Append("\n");
        System.IO.File.WriteAllText(表路径, 文.ToString(), new System.Text.UTF8Encoding(false));
        AssetDatabase.Refresh();
        Debug.Log("[外观] 已写出 " + 表路径 + "（" + 默认行.Length + " 行 × " + 表头.Length + " 列）");
    }

    [MenuItem("修仙/外观/收集外观资产", false, 501)]
    public static void 收集() => 收集(true);

    public static void 收集(bool 打日志)
    {
        var 目录 = Path.GetDirectoryName(库路径).Replace('\\', '/');
        if (!AssetDatabase.IsValidFolder(目录))
        {
            var 父 = Path.GetDirectoryName(目录).Replace('\\', '/');
            if (!AssetDatabase.IsValidFolder(父)) AssetDatabase.CreateFolder("Assets", "resources");
            AssetDatabase.CreateFolder(父, Path.GetFileName(目录));
        }

        var 库 = AssetDatabase.LoadAssetAtPath<AppearanceDatabase>(库路径);
        if (库 == null)
        {
            库 = ScriptableObject.CreateInstance<AppearanceDatabase>();
            AssetDatabase.CreateAsset(库, 库路径);
        }

        // 先收集"生成出来的"（表驱动的那份）
        var 名单 = new List<AppearanceDefinition>();
        var 已收id = new HashSet<string>();
        foreach (var g in AssetDatabase.FindAssets("t:AppearanceDefinition", new[] { "Assets/Data/Generated" }))
        {
            var a = AssetDatabase.LoadAssetAtPath<AppearanceDefinition>(AssetDatabase.GUIDToAssetPath(g));
            if (a != null && 已收id.Add(a.id)) 名单.Add(a);
        }
        // 生成目录为空（还没导入表）时，退回收集 resources 下那份，保证功能不断
        if (名单.Count == 0)
            foreach (var g in AssetDatabase.FindAssets("t:AppearanceDefinition", new[] { 手建目录 }))
            {
                var a = AssetDatabase.LoadAssetAtPath<AppearanceDefinition>(AssetDatabase.GUIDToAssetPath(g));
                if (a != null && 已收id.Add(a.id)) 名单.Add(a);
            }

        // 清掉手建目录里与生成资产**同 id** 的重复资产
        int 清 = 0;
        foreach (var g in AssetDatabase.FindAssets("t:AppearanceDefinition", new[] { 手建目录 }))
        {
            var p = AssetDatabase.GUIDToAssetPath(g);
            var a = AssetDatabase.LoadAssetAtPath<AppearanceDefinition>(p);
            if (a == null) continue;
            bool 生成里有 = false;
            foreach (var x in 名单) if (x != null && x.id == a.id && AssetDatabase.GetAssetPath(x) != p) { 生成里有 = true; break; }
            if (生成里有 && AssetDatabase.DeleteAsset(p)) 清++;
        }

        库.全部.Clear();
        库.全部.AddRange(名单);
        EditorUtility.SetDirty(库);
        AssetDatabase.SaveAssets();
        AppearanceDatabase.清缓存();

        if (打日志)
        {
            string 名 = "";
            foreach (var a in 库.全部) 名 += a.DisplayName + "(" + a.id + ") ";
            Debug.Log("[外观库] 收到 " + 库.全部.Count + " 件，清了重复资产 " + 清 + " 个：" + 名
                + "\n  默认外观=" + (库.取默认() != null ? 库.取默认().DisplayName : "**没有！会开局没外观**"));
        }
    }

    [MenuItem("修仙/外观/一键：生成表 + 导入 + 收集", false, 502)]
    public static void 一键()
    {
        生成表();
        EditorApplication.ExecuteMenuItem("修仙/从配置表生成资产");
        收集();
    }
}
