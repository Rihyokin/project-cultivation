using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// **编辑器里临时显示/隐藏当前场景的空气墙**。
///
/// 空气墙在场景里是**关掉 Renderer** 存盘的（一段墙 15m 宽 × 60 多米高，平时摆村子非常挡事），
/// 所以 Scene 视图里默认什么都看不到（连橙色线框也不画，见 <see cref="AirWall.OnDrawGizmos"/>）。
/// 要**挪墙 / 核对位置**的时候用这个菜单把它们一起显示出来 ——
/// 跟隔壁「修仙 / 编辑器里显示或隐藏 UI 画布」是**同一个套路**，快捷键习惯也一样。
///
/// ★ 只改 `Renderer.enabled`，**不保存场景**：调完再按一次关回去，或者干脆不保存即可还原。
/// （保存了也不怕：运行时 `AirWall.Awake()` 会把渲染器再关掉，玩家侧没有任何影响。）
///
/// 另外：隐藏状态下**从 Hierarchy 里点中某一段**，`OnDrawGizmosSelected()` 照样会画线框 ——
/// 只想对某一段的时候不用开这个总开关。
///
/// 菜单：修仙 / 编辑器里显示或隐藏 空气墙
/// </summary>
public static class AirWallEditorToggle
{
    const string 菜单路径 = "修仙/编辑器里显示或隐藏 空气墙";

    [MenuItem(菜单路径)]
    public static void Toggle()
    {
        var sc = EditorSceneManager.GetActiveScene();
        var 墙 = 找当前场景的墙();

        if (墙.Count == 0)
        {
            Debug.Log("[空气墙]「" + sc.name + "」里没有空气墙，无需切换。");
            return;
        }

        // 以**第一段**现在的状态为准决定这次是"全体显示"还是"全体隐藏"，
        // 这样混着半开半关时按一下就统一了（不会越按越乱）
        bool 要显示 = 墙[0].当前隐藏;

        foreach (var w in 墙)
        {
            Undo.RecordObject(w, 要显示 ? "显示空气墙" : "隐藏空气墙");
            w.设置渲染开关(要显示);
            EditorUtility.SetDirty(w);
        }

        SceneView.RepaintAll();
        Debug.Log("[空气墙]「" + sc.name + "」：" + 墙.Count + " 段 → "
                  + (要显示 ? "**显示**（调完记得再按一次关掉）" : "**隐藏**（日常编辑状态）")
                  + "（只改了 Renderer.enabled，没保存场景）");
    }

    /// <summary>当前场景里所有空气墙（含根节点下的、任意层级的）</summary>
    static List<AirWall> 找当前场景的墙()
    {
        var 结果 = new List<AirWall>();
        var sc = EditorSceneManager.GetActiveScene();
        foreach (var 根 in sc.GetRootGameObjects())
            结果.AddRange(根.GetComponentsInChildren<AirWall>(true));
        return 结果;
    }

    // 场景里没有空气墙时把菜单灰掉，免得按了没反应还以为坏了
    [MenuItem(菜单路径, true)]
    static bool 验证() => 找当前场景的墙().Count > 0;
}
