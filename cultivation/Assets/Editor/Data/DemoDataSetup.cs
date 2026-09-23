using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 一键初始化：建境界与玩家属性资产 → 生成角色面板 UI → 从配置表导入其余资产并接线。
///
/// 说明：物品/功法/主动神通/被动神通/法宝/NPC/灵阵/采集物 都改由
/// Assets/Data/Tables/*.csv 驱动，本文件只负责配置表覆盖不到的部分（境界、玩家属性）。
///
/// 菜单：修仙 / 一键初始化项目数据  （ASCII 别名：Cultivation / Setup Project Data）
/// </summary>
public static class DemoDataSetup
{
    const string DemoFolder = "Assets/DemoData";
    const string ScenePath = "Assets/Scenes/3C_Testbed.scene";

    [MenuItem("修仙/一键初始化项目数据")]
    [MenuItem("Cultivation/Setup Project Data")]
    public static void Setup()
    {
        if (!AssetDatabase.IsValidFolder(DemoFolder))
            AssetDatabase.CreateFolder("Assets", "DemoData");

        CreateRealms();
        var stats = CreatePlayerStats();

        // 1) 搭 UI 外壳
        CharacterPanelBuilder.Build(true);

        // 2) 先把玩家属性接上，再导入配置表。
        //    顺序很关键：RewirePanelData 里 UIAttributeList 是按「当时的 data.玩家属性」灌的，
        //    如果那时还是 null，属性列表就会一直空着。
        var canvasGo = GameObject.Find("CharacterUI");
        UIPanelData data = null;
        if (canvasGo != null)
        {
            data = canvasGo.GetComponent<UIPanelData>();
            if (data != null)
            {
                data.玩家属性 = stats;
                EditorUtility.SetDirty(data);
            }
        }

        // 3) 从配置表导入其余资产，并在玩家属性已就位的前提下刷新面板
        DataTableImporter.ImportAll();

        // 4) 再兜一次，保证与 ImportAll 内部的刷新顺序无关
        DataTableImporter.RewirePanelData();
        if (data != null) EditorUtility.SetDirty(data);

        var scene = EditorSceneManager.GetActiveScene();
        // ★ 不传 path！见 DataTableImporter.EditorSceneManager_MarkAndSave 上的注释：
        //   以前这里写 SaveScene(scene, ScenePath)，会把"当前打开的场景"存成硬编码的
        //   3C_Testbed.scene —— 开着 Sect 跑一次就把 3C_Testbed 顶掉了。
        if (!string.IsNullOrEmpty(scene.path))
        {
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);      // 存回自己的路径
        }
        Debug.Log("[DemoDataSetup] 项目数据初始化完成。");
    }

    // ------------------------------------------------------------------

    static void CreateRealms()
    {
        var realm1 = MakeOrLoad<RealmDefinition>("Realm_RealmQi1");
        realm1.境界名 = "炼气一层"; realm1.序号 = 0; realm1.所需总经验 = 100;
        EditorUtility.SetDirty(realm1);

        var realm2 = MakeOrLoad<RealmDefinition>("Realm_RealmQi2");
        realm2.境界名 = "炼气二层"; realm2.序号 = 1; realm2.所需总经验 = 300;
        EditorUtility.SetDirty(realm2);
    }

    static PlayerStatsDefinition CreatePlayerStats()
    {
        var ps = MakeOrLoad<PlayerStatsDefinition>("PlayerStats_Demo");
        ps.境界 = MakeOrLoad<RealmDefinition>("Realm_RealmQi1");

        // 默认数值统一在 PlayerDefaultStats 里维护，调试面板的一键重置也用同一份
        ps.神识 = PlayerDefaultStats.默认神识;
        ps.灵根 = PlayerDefaultStats.默认灵根;
        ps.吐纳速度 = PlayerDefaultStats.默认吐纳速度;

        var def = PlayerDefaultStats.Create();
        for (int i = 0; i < AttributeUtil.Count; i++)
            ps.基础属性[(AttributeType)i] = def[(AttributeType)i];

        EditorUtility.SetDirty(ps);
        return ps;
    }

    static void Set(AttributeSet set, AttributeType type, float value)
    {
        if (set == null) return;
        set[type] = value;
    }

    static T MakeOrLoad<T>(string assetName) where T : ScriptableObject
    {
        string path = DemoFolder + "/" + assetName + ".asset";
        var existing = AssetDatabase.LoadAssetAtPath<T>(path);
        if (existing != null) return existing;
        var so = ScriptableObject.CreateInstance<T>();
        AssetDatabase.CreateAsset(so, path);
        return so;
    }
}
