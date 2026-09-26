using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// **编辑器里临时显示/隐藏当前场景的 UI 画布**。
///
/// 场景里的 `HudCanvas` / `CharacterUI` / `暂停菜单` 平时是**关掉 Canvas 组件**的
/// （见 <see cref="UICanvasBoot"/>），这样摆场景时不被挡；
/// 但偶尔要**调 UI 布局**就得把它们显示出来 —— 用这个菜单来回切。
///
/// ★ 只改 `Canvas.enabled`，**不保存场景**：看一眼之后 Ctrl+Z 或直接不保存即可还原。
/// （保存了也不怕：运行时 `UICanvasBoot.Awake()` 会再打开一次，玩家侧没有影响。）
///
/// 菜单：修仙 / 编辑器里显示或隐藏 UI 画布
/// </summary>
public static class UICanvasEditorToggle
{
    [MenuItem("修仙/编辑器里显示或隐藏 UI 画布")]
    public static void Toggle()
    {
        var sc = EditorSceneManager.GetActiveScene();
        int 开了 = 0, 关了 = 0;

        // 只看**根物体**上的画布：UI 根就是那几个，子画布是面板自己的开关，别乱动
        foreach (var 根 in sc.GetRootGameObjects())
        {
            var c = 根.GetComponent<Canvas>();
            if (c == null) continue;
            c.enabled = !c.enabled;
            EditorUtility.SetDirty(c);
            if (c.enabled) 开了++; else 关了++;
        }

        Debug.Log("[UI画布]「" + sc.name + "」：打开 " + 开了 + " 个、关闭 " + 关了 + " 个"
                  + "（只改了 Canvas.enabled，没保存场景）");
    }
}
