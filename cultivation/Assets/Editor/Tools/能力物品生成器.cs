using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// **把每一门功法 / 主动神通 / 被动神通都生成一件对应的物品**，另外生成一颗「回春丹」。
///
/// 用户要求（2026-09-26）：主角不再是"天生全都会"，要**用物品学**，而且学了不能再学。
/// 所以每个能力一件物品 + 一份「学会/获得」效果资产：
///   · 功法     → `&lt;功法名&gt; 秘籍`      + 学功法效果
///   · 主动神通 → `&lt;神通名&gt; 神通玉简` + 学主动神通效果
///   · 被动神通 → `&lt;神通名&gt; 心得`      + 学被动神通效果
///   · 回春丹   → 1 小时内气血回复 +5%（属性增益效果）
///
/// 菜单：
///   · **修仙/物品/生成能力物品** —— 生成/更新上面这些资产（可反复跑）
///   · **修仙/物品/把能力物品塞进当前场景背包** —— 测着方便：把生成出来的物品都塞进当前打开场景的背包
/// </summary>
public static class 能力物品生成器
{
    public const string 目录 = "Assets/Data/Generated/能力物品";
    const string 能力来源 = "Assets/Data/Generated";

    [MenuItem("修仙/物品/生成能力物品", false, 300)]
    public static void 生成()
    {
        确保目录();
        int 功法数 = 0, 主动数 = 0, 被动数 = 0;

        foreach (var g in 全部<GongFaDefinition>(能力来源))
        {
            var 效果 = 取或建<学功法效果>(目录 + "/效果_学功法_" + 安全(g.功法id));
            效果.功法 = g;
            效果.说明 = "使用后学会功法「" + g.DisplayName + "」。";
            EditorUtility.SetDirty(效果);

            var 物品 = 取或建<ItemDefinition>(目录 + "/物品_功法_" + 安全(g.功法id));
            物品.物品id = "item_gongfa_" + g.功法id;
            物品.物品名 = g.DisplayName + " 秘籍";
            物品.介绍 = "记载着「" + g.DisplayName + "」的入门功法，研读之后即可修习。";
            物品.可使用 = true;
            物品.使用效果 = 效果;
            EditorUtility.SetDirty(物品);
            功法数++;
        }

        foreach (var a in 全部<ActiveDivineAbility>(能力来源))
        {
            var 效果 = 取或建<学主动神通效果>(目录 + "/效果_学主动_" + 安全(a.神通id));
            效果.神通 = a;
            效果.说明 = "使用后获得主动神通「" + a.DisplayName + "」。";
            EditorUtility.SetDirty(效果);

            var 物品 = 取或建<ItemDefinition>(目录 + "/物品_主动_" + 安全(a.神通id));
            物品.物品id = "item_active_" + a.神通id;
            物品.物品名 = a.DisplayName + " 神通玉简";
            物品.介绍 = "刻着「" + a.DisplayName + "」的玉简，神识一触即可领悟。";
            物品.可使用 = true;
            物品.使用效果 = 效果;
            EditorUtility.SetDirty(物品);
            主动数++;
        }

        foreach (var p in 全部<PassiveDivineAbility>(能力来源))
        {
            var 效果 = 取或建<学被动神通效果>(目录 + "/效果_学被动_" + 安全(p.神通id));
            效果.神通 = p;
            效果.说明 = "使用后获得被动神通「" + p.DisplayName + "」。";
            EditorUtility.SetDirty(效果);

            var 物品 = 取或建<ItemDefinition>(目录 + "/物品_被动_" + 安全(p.神通id));
            物品.物品id = "item_passive_" + p.神通id;
            物品.物品名 = p.DisplayName + " 心得";
            物品.介绍 = "前辈留下的修炼心得，读罢常驻其效。";
            物品.可使用 = true;
            物品.使用效果 = 效果;
            EditorUtility.SetDirty(物品);
            被动数++;
        }

        // ---- 回春丹：学习以外的那颗测试丹药 ----
        {
            var 效果 = 取或建<属性增益效果>(目录 + "/效果_回春丹");
            效果.属性 = AttributeType.HealthRegen;
            效果.百分比 = 0.05f;
            效果.固定值 = 0f;
            效果.持续秒 = 3600f;            // 1 小时
            效果.说明 = "1 小时内气血回复 +5%。";
            EditorUtility.SetDirty(效果);

            var 物品 = 取或建<ItemDefinition>(目录 + "/物品_回春丹");
            物品.物品id = "item_huichundan";
            物品.物品名 = "回春丹";
            物品.介绍 = "服用后一炷香…不，一个时辰内气血自行回复得更快（气血回复 +5%，持续 1 小时）。";
            物品.可使用 = true;
            物品.使用效果 = 效果;
            EditorUtility.SetDirty(物品);
        }

        AssetDatabase.SaveAssets();
        Debug.Log("[能力物品] 生成完成：" + 目录 + "\n  功法 " + 功法数 + " 件、主动神通 " + 主动数
            + " 件、被动神通 " + 被动数 + " 件、另有回春丹 1 件");
    }

    [MenuItem("修仙/物品/清空主角默认能力（新档口径）", false, 302)]
    public static void 清空默认能力()
    {
        UIPanelData 面板 = null;
        foreach (var p in Object.FindObjectsOfType<UIPanelData>()) { 面板 = p; break; }
        if (面板 == null) { Debug.LogWarning("[能力物品] 当前场景里找不到 UIPanelData"); return; }

        面板.EnsureLists();
        int 功法 = 面板.已学功法 != null ? 面板.已学功法.Count : 0;
        int 主动 = 面板.已获得主动神通 != null ? 面板.已获得主动神通.Count : 0;
        int 被动 = 面板.已获得被动神通 != null ? 面板.已获得被动神通.Count : 0;

        面板.已学功法 = new List<GongFaDefinition>();
        面板.已获得主动神通 = new List<ActiveDivineAbility>();
        面板.已获得被动神通 = new List<PassiveDivineAbility>();
        面板.当前功法 = null;
        面板.待装备神通 = null;
        for (int i = 0; i < 面板.主动技能.Count; i++) 面板.主动技能[i] = null;
        面板.已停用被动 = new List<PassiveDivineAbility>();
        面板.RaiseChanged();

        EditorUtility.SetDirty(面板);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(面板.gameObject.scene);
        Debug.Log("[能力物品] 已清空当前场景里的默认能力：功法 " + 功法 + "、主动神通 " + 主动 + "、被动神通 " + 被动
            + "。现在主角是「什么都不会」的新档状态（记得保存场景）");
    }

    [MenuItem("修仙/物品/把能力物品塞进当前场景背包", false, 301)]
    public static void 塞进当前场景背包()
    {
        生成();      // 保证物品是最新的

        UIPanelData 面板 = null;
        foreach (var p in Object.FindObjectsOfType<UIPanelData>()) { 面板 = p; break; }
        if (面板 == null) { Debug.LogWarning("[能力物品] 当前场景里找不到 UIPanelData（角色面板数据）"); return; }

        int n = 0;
        foreach (var g in 全部<ItemDefinition>(目录))
        {
            面板.给物品(g, 1);
            n++;
        }
        EditorUtility.SetDirty(面板);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(面板.gameObject.scene);
        Debug.Log("[能力物品] 往当前场景背包里塞了 " + n + " 件（记得保存场景）");
    }

    // ------------------------------------------------------------------

    static void 确保目录()
    {
        if (AssetDatabase.IsValidFolder(目录)) return;
        var 父 = Path.GetDirectoryName(目录).Replace('\\', '/');
        if (!AssetDatabase.IsValidFolder(父)) AssetDatabase.CreateFolder("Assets/Data", "Generated");
        AssetDatabase.CreateFolder(父, "能力物品");
    }

    static List<T> 全部<T>(string 根) where T : Object
    {
        var 出 = new List<T>();
        foreach (var g in AssetDatabase.FindAssets("t:" + typeof(T).Name, new[] { 根 }))
        {
            var a = AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(g));
            if (a != null) 出.Add(a);
        }
        return 出;
    }

    static T 取或建<T>(string 路径) where T : ScriptableObject
    {
        var a = AssetDatabase.LoadAssetAtPath<T>(路径 + ".asset");
        if (a != null) return a;
        a = ScriptableObject.CreateInstance<T>();
        AssetDatabase.CreateAsset(a, 路径 + ".asset");
        return a;
    }

    static string 安全(string s)
        => string.IsNullOrEmpty(s) ? "无名" : s.Replace('/', '_').Replace('\\', '_');
}
