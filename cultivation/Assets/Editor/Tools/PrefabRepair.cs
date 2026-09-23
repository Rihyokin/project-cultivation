using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 修复读不出来的 NPC prefab。
///
/// 现象：目录里明明有 xxx_01.prefab，但 AssetDatabase.LoadAssetAtPath 返回 null，
/// 于是各种批量工具都会静默跳过它（XiaoLu 就是这样被漏掉的）。
/// 常见原因是导入中途出错、prefab 内部根节点丢了，成了空 prefab。
///
/// 做法：读不出来就【用同目录的 FBX 重建一个 prefab】，
/// 把同目录的材质接上，然后交给 动物 AI/全部装上 继续处理。
///
/// 菜单：Cultivation/动物 AI/修复 XiaoLu
///       Cultivation/动物 AI/检查所有 prefab 是否可读
/// </summary>
public static class PrefabRepair
{
    const string 动物目录 = "Assets/resources/NPC/Animal";

    [MenuItem("Cultivation/动物 AI/检查所有 prefab 是否可读")]
    public static void CheckAll()
    {
        var sb = new StringBuilder("[PrefabRepair] prefab 可读性检查\n");
        int 坏 = 0;

        foreach (var 子 in AssetDatabase.GetSubFolders(动物目录))
        {
            if (System.IO.Path.GetFileName(子).EndsWith("_Animation")) continue;

            foreach (var g in AssetDatabase.FindAssets("t:Prefab", new[] { 子 }))
            {
                var p = AssetDatabase.GUIDToAssetPath(g);
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(p);
                bool 可读 = go != null;
                if (!可读)
                {
                    坏++;
                    sb.Append("  【读不到】").Append(p).Append('\n');
                }
            }
        }
        sb.Append("  不可读的 prefab = ").Append(坏).Append('\n');
        Debug.Log(sb.ToString());
    }

    [MenuItem("Cultivation/动物 AI/修复 XiaoLu")]
    public static void FixXiaoLu()
    {
        const string 目录 = 动物目录 + "/XiaoLu";
        const string prefab路径 = 目录 + "/XiaoLu_01.prefab";
        string fbx路径 = 目录 + "/XiaoLu.FBX";      // 不能是 const —— 下面找不到时会改它

        var 报告 = new StringBuilder("[PrefabRepair] 修 XiaoLu\n");

        // 1) 现状
        var 现有 = AssetDatabase.LoadAssetAtPath<GameObject>(prefab路径);
        if (现有 != null)
        {
            报告.Append("  prefab 本来就能读，不必重建\n");
            报告.Append("    组件：");
            foreach (var c in 现有.GetComponents<Component>())
                if (c != null) 报告.Append(c.GetType().Name).Append(' ');
            报告.Append('\n');
            Debug.Log(报告.ToString());
            return;
        }
        报告.Append("  prefab 读不到（空 prefab 或导入损坏），开始用 FBX 重建\n");

        // 2) 拿 FBX
        var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(fbx路径);
        if (fbx == null)
        {
            // 换个大小写再试
            foreach (var g in AssetDatabase.FindAssets("t:Model", new[] { 目录 }))
            {
                var p = AssetDatabase.GUIDToAssetPath(g);
                if (System.IO.Path.GetFileNameWithoutExtension(p).ToLower().Contains("xiaolu"))
                { fbx = AssetDatabase.LoadAssetAtPath<GameObject>(p); fbx路径 = p; break; }
            }
        }
        if (fbx == null) { 报告.Append("  【找不到 FBX，无法重建】\n"); Debug.LogError(报告.ToString()); return; }
        报告.Append("  用模型：").Append(fbx路径).Append('\n');

        // 3) 实例化 + 挂到新根
        var 实例 = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
        实例.name = "XiaoLu_01";

        // 4) 材质：FBX 自带的话就不用管；同目录有 .mat 也接一下
        foreach (var g in AssetDatabase.FindAssets("t:Material", new[] { 目录, 目录 + "/Materials" }))
        {
            var mp = AssetDatabase.GUIDToAssetPath(g);
            var mat = AssetDatabase.LoadAssetAtPath<Material>(mp);
            if (mat == null) continue;
            foreach (var r in 实例.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                if (mats == null || mats.Length == 0) { r.sharedMaterial = mat; continue; }
                bool 有空的 = false;
                foreach (var m in mats) if (m == null) 有空的 = true;
                if (有空的) r.sharedMaterial = mat;
            }
            报告.Append("  接上材质：").Append(mat.name).Append('\n');
            break;
        }

        // 5) 存成 prefab（覆盖那个坏的）
        var 新prefab = PrefabUtility.SaveAsPrefabAsset(实例, prefab路径);
        Object.DestroyImmediate(实例);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        if (新prefab != null)
        {
            报告.Append("  ✓ 已重建并保存到 ").Append(prefab路径).Append('\n');
            报告.Append("    组件：");
            foreach (var c in 新prefab.GetComponents<Component>())
                if (c != null) 报告.Append(c.GetType().Name).Append(' ');
            报告.Append("\n  接着跑：Cultivation/动物 AI/全部装上\n");
        }
        else 报告.Append("  【保存失败】\n");

        Debug.Log(报告.ToString());
    }
}
