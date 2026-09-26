using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// **把生成出来的对话资产收集成运行时对话库**。
///
/// 生成的资产在 `Assets/Data/Generated/DialogueDefinition/`，**不在 Resources 下**，
/// 运行时 `Resources.Load` 拿不到；所以要摊平成一份库放到 `Assets/resources/对话/对话库.asset`
/// （运行时读 Resources 是本项目既有做法）。
///
/// 菜单：**修仙/对话系统/收集对话资产**（`修仙/从配置表生成资产` 结束时会自动调一次，一般不用手点）。
/// </summary>
public static class DialogueDatabaseBuilder
{
    public const string 库路径 = "Assets/resources/对话/对话库.asset";

    [MenuItem("修仙/对话系统/收集对话资产", false, 200)]
    public static void 收集() => 收集(true);

    public static void 收集(bool 打日志)
    {
        var 目录 = Path.GetDirectoryName(库路径).Replace('\\', '/');
        if (!AssetDatabase.IsValidFolder(目录))
        {
            // Assets/resources/对话
            var 父 = Path.GetDirectoryName(目录).Replace('\\', '/');
            if (!AssetDatabase.IsValidFolder(父)) AssetDatabase.CreateFolder("Assets", "resources");
            AssetDatabase.CreateFolder(父, Path.GetFileName(目录));
        }

        var 库 = AssetDatabase.LoadAssetAtPath<DialogueDatabase>(库路径);
        bool 新建 = 库 == null;
        if (新建)
        {
            库 = ScriptableObject.CreateInstance<DialogueDatabase>();
            AssetDatabase.CreateAsset(库, 库路径);
        }

        库.全部.Clear();
        var guids = AssetDatabase.FindAssets("t:DialogueDefinition", new[] { "Assets/Data/Generated" });
        var 名单 = new List<string>();
        foreach (var g in guids)
        {
            var p = AssetDatabase.GUIDToAssetPath(g);
            var d = AssetDatabase.LoadAssetAtPath<DialogueDefinition>(p);
            if (d != null) { 库.全部.Add(d); 名单.Add(d.id); }
        }
        名单.Sort();
        库.全部.Sort((a, b) =>
        {
            int c = string.CompareOrdinal(a.npcId ?? "", b.npcId ?? "");
            if (c != 0) return c;
            return a.分段.CompareTo(b.分段);
        });

        EditorUtility.SetDirty(库);
        AssetDatabase.SaveAssets();
        DialogueDatabase.清缓存();

        if (打日志)
            Debug.Log("[对话库] " + (新建 ? "新建" : "更新") + " " + 库路径 + "：收了 " + 库.全部.Count + " 段对话\n  "
                + string.Join(" ", 名单.ToArray()));
    }
}
