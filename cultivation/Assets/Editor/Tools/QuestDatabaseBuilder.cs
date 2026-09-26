using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// **把生成出来的任务阶段 + 物品收集成运行时任务库**（`Assets/resources/任务/任务库.asset`）。
/// 和对话库一个思路：生成的资产在 `Assets/Data/Generated/` 下，不在 Resources 里，运行时读不到。
///
/// 菜单：**修仙/任务/收集任务资产**（`修仙/从配置表生成资产` 结束时会自动调一次）。
/// </summary>
public static class QuestDatabaseBuilder
{
    public const string 库路径 = "Assets/resources/任务/任务库.asset";

    [MenuItem("修仙/任务/收集任务资产", false, 400)]
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

        var 库 = AssetDatabase.LoadAssetAtPath<QuestDatabase>(库路径);
        bool 新建 = 库 == null;
        if (新建)
        {
            库 = ScriptableObject.CreateInstance<QuestDatabase>();
            AssetDatabase.CreateAsset(库, 库路径);
        }

        库.全部.Clear();
        foreach (var g in AssetDatabase.FindAssets("t:QuestDefinition", new[] { "Assets/Data/Generated" }))
        {
            var q = AssetDatabase.LoadAssetAtPath<QuestDefinition>(AssetDatabase.GUIDToAssetPath(g));
            if (q != null) 库.全部.Add(q);
        }
        库.全部.Sort((a, b) =>
        {
            int c = string.CompareOrdinal(a.任务id ?? "", b.任务id ?? "");
            return c != 0 ? c : a.阶段.CompareTo(b.阶段);
        });

        // 物品库：物品表生成的 + 能力物品（任务奖励按 id 查找用）
        库.物品库.Clear();
        var 已加 = new HashSet<ItemDefinition>();
        foreach (var 根 in new[] { "Assets/Data/Generated", "Assets/resources" })
            foreach (var g in AssetDatabase.FindAssets("t:ItemDefinition", new[] { 根 }))
            {
                var it = AssetDatabase.LoadAssetAtPath<ItemDefinition>(AssetDatabase.GUIDToAssetPath(g));
                if (it != null && 已加.Add(it)) 库.物品库.Add(it);
            }

        EditorUtility.SetDirty(库);
        AssetDatabase.SaveAssets();
        QuestDatabase.清缓存();

        if (打日志)
            Debug.Log("[任务库] " + (新建 ? "新建" : "更新") + " " + 库路径 + "：收了 " + 库.全部.Count
                + " 个阶段、物品库 " + 库.物品库.Count + " 件\n  任务：" + string.Join("、", 库.全部任务id().ToArray()));
    }
}
