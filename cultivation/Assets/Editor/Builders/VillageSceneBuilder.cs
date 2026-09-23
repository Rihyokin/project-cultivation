using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 生成【村庄】游戏场景。
///
/// 做法：拿 3C_Testbed 复制一份当基底 —— 这样 Player、相机、CharacterUI、
/// EventSystem 这些一整套游戏必需的东西都现成，不用重建。
/// 然后把测试场专用的东西（木桩、边界墙、Level、Props）删掉，再搭村庄。
///
/// 菜单：Cultivation / Build Village Scene
/// </summary>
public static class VillageSceneBuilder
{
    const string 模板场景 = "Assets/Scenes/3C_Testbed.scene";
    const string 新场景 = "Assets/Scenes/Village.scene";

    // 测试场专用，村庄场景里不要
    static readonly string[] 删掉的根 = { "Level", "Boundary", "Props", "Npc_WoodenStake" };

    [MenuItem("Cultivation/Build Village Scene")]
    public static void Build()
    {
        // 1) 复制模板场景
        if (AssetDatabase.LoadAssetAtPath<Object>(新场景) != null)
        {
            if (!EditorUtility.DisplayDialog("村庄场景已存在",
                    新场景 + " 已存在，要覆盖重建吗？", "覆盖", "取消"))
                return;
            AssetDatabase.DeleteAsset(新场景);
        }

        if (!AssetDatabase.CopyAsset(模板场景, 新场景))
        {
            Debug.LogError("[VillageSceneBuilder] 复制场景失败：" + 模板场景);
            return;
        }

        // 2) 打开新场景
        var scene = EditorSceneManager.OpenScene(新场景, OpenSceneMode.Single);
        Debug.Log("[VillageSceneBuilder] 已创建并打开 " + 新场景);

        // 3) 删掉测试场专用对象
        foreach (var 名 in 删掉的根)
        {
            var go = GameObject.Find(名);
            if (go != null) { Object.DestroyImmediate(go); Debug.Log("[VillageSceneBuilder] 删除 " + 名); }
        }

        // 4) 把玩家挪到村庄的出生点（村口内侧）
        var 玩家 = GameObject.Find("Player");
        if (玩家 != null)
        {
            var cc = 玩家.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            玩家.transform.position = new Vector3(0f, 0.1f, -12f);
            玩家.transform.rotation = Quaternion.identity;
            if (cc != null) cc.enabled = true;
        }

        // 5) 搭村庄
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, 新场景);
        VillageBuilder.Build();          // 摆位
        EditorSceneManager.SaveScene(scene, 新场景);

        // 6) 加进 Build Settings
        var 列表 = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        bool 有 = false;
        foreach (var s in 列表) if (s.path == 新场景) { 有 = true; break; }
        if (!有) 列表.Add(new EditorBuildSettingsScene(新场景, true));
        EditorBuildSettings.scenes = 列表.ToArray();

        var sb = new System.Text.StringBuilder("[VillageSceneBuilder] 完成。Build Settings：\n");
        foreach (var s in EditorBuildSettings.scenes)
            sb.Append("  ").Append(s.enabled ? "[x] " : "[ ] ").Append(s.path).Append('\n');
        Debug.Log(sb.ToString());
    }

    /// <summary>
    /// 贴地对齐要在下一帧跑（刚实例化的对象包围盒还没算好），
    /// 所以单独留一个菜单，和 VillageBuilder 的那步配合。
    /// </summary>
    [MenuItem("Cultivation/Finish Village Scene")]
    public static void Finish()
    {
        VillageBuilder.AlignToGround();
        var scene = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, 新场景);
        Debug.Log("[VillageSceneBuilder] 贴地对齐并保存 " + scene.name);
    }
}
