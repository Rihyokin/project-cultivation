using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 修复 NPC 材质的 shader。
///
/// 问题：部分 NPC 的材质引用了【已删除的 MMO 框架里的自定义 shader】，
/// 于是 shader 变成 Hidden/InternalErrorShader —— 模型渲染不出来，
/// 看着像"没贴图、也没动作"（其实动画在播，只是模型根本不显示）。
///
/// 修法：把这些材质的 shader 换成能用的内置 shader，并保住 _MainTex。
/// 参照物是那些正常的 NPC —— 它们用的是 Legacy Shaders/Self-Illumin/Diffuse。
///
/// 菜单：Cultivation / 修复 NPC 材质 Shader
/// </summary>
public static class FixNpcMaterials
{
    const string 目标Shader名 = "Legacy Shaders/Self-Illumin/Diffuse";

    // 备选：如果目标 shader 也找不到，按顺序试
    static readonly string[] 备选 = {
        "Legacy Shaders/Self-Illumin/Diffuse",
        "Legacy Shaders/Diffuse",
        "Mobile/Diffuse",
        "Standard",
    };

    [MenuItem("Cultivation/检查 NPC 材质 Shader")]
    public static void Check()
    {
        var 坏的 = 找坏材质();
        var sb = new StringBuilder("[FixNpcMaterials] 检查\n");
        sb.Append("  引用失效 shader 的材质：").Append(坏的.Count).Append(" 个\n");
        foreach (var m in 坏的) sb.Append("    ").Append(m.name).Append("  (").Append(AssetDatabase.GetAssetPath(m)).Append(")\n");
        Debug.Log(sb.ToString());
    }

    [MenuItem("Cultivation/修复 NPC 材质 Shader")]
    public static void Fix()
    {
        var 目标 = Shader.Find(目标Shader名);
        if (目标 == null)
        {
            foreach (var n in 备选) { 目标 = Shader.Find(n); if (目标 != null) { Debug.LogWarning("[FixNpcMaterials] 目标 shader 找不到，改用 " + n); break; } }
        }
        if (目标 == null) { Debug.LogError("[FixNpcMaterials] 连备选 shader 都找不到，放弃"); return; }

        var 坏的 = 找坏材质();
        var 报告 = new StringBuilder();
        int 修了 = 0, 没贴图 = 0;

        foreach (var m in 坏的)
        {
            // 先记下原来的贴图（换 shader 会清属性，要重新贴回去）
            Texture 原贴图 = null;
            if (m.HasProperty("_MainTex")) 原贴图 = m.GetTexture("_MainTex");
            if (原贴图 == null)
            {
                // 有些材质的贴图是通过名字找的，试几个常见属性名
                foreach (var 名 in new[] { "_MainTex", "_BaseMap", "_DiffuseTexture", "_Tex" })
                    if (m.HasProperty(名) && m.GetTexture(名) != null) { 原贴图 = m.GetTexture(名); break; }
            }

            m.shader = 目标;

            if (原贴图 != null && m.HasProperty("_MainTex"))
            {
                m.SetTexture("_MainTex", 原贴图);
                // Self-Illumin 的 _Illum 也指同一张，让角色不被阴影压黑
                if (m.HasProperty("_Illum")) m.SetTexture("_Illum", 原贴图);
            }
            else 没贴图++;

            EditorUtility.SetDirty(m);
            修了++;
            报告.Append("  ").Append(m.name)
                .Append("  -> ").Append(目标.name)
                .Append("  贴图=").Append(原贴图 != null ? 原贴图.name : "【没有】").Append('\n');
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[FixNpcMaterials] 修好 " + 修了 + " 个材质（其中 " + 没贴图 + " 个没找到贴图）\n" + 报告);
    }

    static List<Material> 找坏材质()
    {
        var 结果 = new List<Material>();
        foreach (var g in AssetDatabase.FindAssets("t:Material", new[] { "Assets/resources/NPC" }))
        {
            var p = AssetDatabase.GUIDToAssetPath(g);
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (m == null) continue;
            if (m.shader == null || m.shader.name == "Hidden/InternalErrorShader") 结果.Add(m);
        }
        return 结果;
    }

    /// <summary>
    /// 按名字把贴图重新贴回材质。
    ///
    /// 为什么需要这一步：shader 坏掉时 Unity 把材质的 _MainTex 引用一起清空了，
    /// 光修 shader 只能让模型"能渲染"，但会是一片纯色。
    /// 好在贴图文件都还在材质旁边（.dds），按名字配对即可。
    ///
    /// 匹配规则：材质叫 BaiGuNiangNiang_01，就找同目录/上级目录里
    /// 文件名等于或包含它的贴图（dds/png/tga/bmp）。
    /// </summary>
    [MenuItem("Cultivation/重新关联 NPC 贴图")]
    public static void RelinkTextures()
    {
        var 贴图索引 = new Dictionary<string, Texture>();
        foreach (var g in AssetDatabase.FindAssets("t:Texture", new[] { "Assets/resources/NPC" }))
        {
            var p = AssetDatabase.GUIDToAssetPath(g);
            var n = System.IO.Path.GetFileNameWithoutExtension(p);
            if (!贴图索引.ContainsKey(n)) 贴图索引[n] = AssetDatabase.LoadAssetAtPath<Texture>(p);
        }

        int 贴上 = 0, 找不到 = 0;
        var 报告 = new StringBuilder();
        foreach (var g in AssetDatabase.FindAssets("t:Material", new[] { "Assets/resources/NPC" }))
        {
            var 路径 = AssetDatabase.GUIDToAssetPath(g);
            var m = AssetDatabase.LoadAssetAtPath<Material>(路径);
            if (m == null) continue;
            if (m.shader == null || m.shader.name == "Hidden/InternalErrorShader") continue;

            // 已经有贴图就跳过
            if (m.HasProperty("_MainTex") && m.GetTexture("_MainTex") != null) continue;

            var 名 = System.IO.Path.GetFileNameWithoutExtension(路径);
            Texture 找到 = null;

            // 1) 完全同名
            if (贴图索引.TryGetValue(名, out var t1)) 找到 = t1;
            // 2) 贴图名包含材质名
            if (找到 == null)
                foreach (var kv in 贴图索引)
                    if (kv.Key.StartsWith(名, System.StringComparison.OrdinalIgnoreCase)) { 找到 = kv.Value; break; }
            // 3) 材质名包含贴图名
            if (找到 == null)
                foreach (var kv in 贴图索引)
                    if (名.StartsWith(kv.Key, System.StringComparison.OrdinalIgnoreCase)) { 找到 = kv.Value; break; }

            if (找到 == null) { 找不到++; if (找不到 <= 10) 报告.Append("  配不到贴图：").Append(名).Append('\n'); continue; }

            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", 找到);
            if (m.HasProperty("_Illum")) m.SetTexture("_Illum", 找到);
            EditorUtility.SetDirty(m);
            贴上++;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[FixNpcMaterials] 贴图重新关联：贴上 " + 贴上 + " 个，配不到 " + 找不到 + " 个\n" + 报告);
    }
}
