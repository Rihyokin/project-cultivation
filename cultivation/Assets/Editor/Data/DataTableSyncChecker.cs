using UnityEditor;
using UnityEngine;

/// <summary>
/// 配置表同步检查。
///
/// **游戏读的是 <c>Assets/Data/Generated</c> 下的资产，不是 CSV。**
/// 改了 `Assets/Data/Tables/*.csv` 之后必须跑一次
/// 菜单「修仙 / 从配置表生成资产」才会生效 —— 忘了跑的话，
/// 表现是「明明改了表但游戏里没变化」，特别容易被误判成代码 bug。
/// （练功木桩的 `对主角好感度` 就这么坑过一次：CSV 改成 -1 了，
///   资产还是 0，于是「血条不显示」「技能打不到」全冒出来了。）
///
/// 所以这里在**每次进 Play Mode 之前**检查一遍，只管报警、不改任何数据。
/// 也可以在菜单里手动跑：修仙 / 检查配置表是否已同步
/// </summary>
[InitializeOnLoad]
public static class DataTableSyncChecker
{
    static DataTableSyncChecker()
    {
        EditorApplication.playModeStateChanged += 状态变化;
    }

    static void 状态变化(PlayModeStateChange 状态)
    {
        // 进 Play 之前那一下最有用：马上要用数据测了
        if (状态 == PlayModeStateChange.ExitingEditMode) 检查(false);
    }

    [MenuItem("修仙/检查配置表是否已同步")]
    [MenuItem("Cultivation/Check Data Tables")]
    public static void 检查菜单() => 检查(true);

    static void 检查(bool 没问题也报一句)
    {
        var 未同步 = DataTableImporter.找出未同步的表();

        if (未同步.Count > 0)
        {
            Debug.LogWarning("[配置表] 这些表比生成资产新，**游戏里读到的还是旧数据**："
                + string.Join("、", 未同步)
                + "\n         → 先跑菜单「修仙 / 从配置表生成资产」，再进 Play Mode。");
        }
        else if (没问题也报一句)
        {
            Debug.Log("[配置表] " + DataTableImporter.表数量 + " 张表都已同步 ✔");
        }
    }
}
