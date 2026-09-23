using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 把暂停菜单装进当前场景。
///
/// 做的事：
///   1. 场景里找 / 建一个【暂停菜单】物件，挂上 PauseMenuUI
///   2. 把 Assets/Fonts/SimHei.ttf 指派到它的「字体」字段
///      （不指派的话中文会显示成方块 —— uGUI 内置字体没有汉字）
///   3. 标脏场景
///
/// 菜单：Cultivation / 安装暂停菜单
///       Cultivation / 卸载暂停菜单
/// </summary>
public static class PauseMenuInstaller
{
    const string 物件名 = "暂停菜单";
    const string 字体路径 = "Assets/Fonts/SimHei.ttf";

    [MenuItem("Cultivation/安装暂停菜单")]
    public static void Install()
    {
        var 旧 = GameObject.Find(物件名);
        if (旧 == null)
        {
            旧 = new GameObject(物件名);
            Undo.RegisterCreatedObjectUndo(旧, "创建暂停菜单");
        }

        var 组件 = 旧.GetComponent<PauseMenuUI>();
        if (组件 == null) 组件 = Undo.AddComponent<PauseMenuUI>(旧);

        var 字体 = AssetDatabase.LoadAssetAtPath<Font>(字体路径);
        if (字体 == null)
        {
            // 退路：Fonts 目录下随便找一个 ttf
            foreach (var g in AssetDatabase.FindAssets("t:Font", new[] { "Assets/Fonts" }))
            {
                字体 = AssetDatabase.LoadAssetAtPath<Font>(AssetDatabase.GUIDToAssetPath(g));
                if (字体 != null) break;
            }
        }
        if (字体 != null) 组件.字体 = 字体;
        else Debug.LogWarning("[PauseMenuInstaller] 没在 Assets/Fonts 找到字体，中文可能显示成方块");

        EditorUtility.SetDirty(组件);
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveOpenScenes();

        Debug.Log("[PauseMenuInstaller] 安装完成\n"
                  + "  物件：" + 物件名 + "（场景根）\n"
                  + "  字体：" + (字体 != null ? 字体.name : "【没找到】") + "\n"
                  + "  按 " + 组件.暂停键 + " 开关\n"
                  + "  场景已保存");
    }

    [MenuItem("Cultivation/卸载暂停菜单")]
    public static void Uninstall()
    {
        var go = GameObject.Find(物件名);
        if (go == null) { Debug.Log("[PauseMenuInstaller] 场景里没有 " + 物件名); return; }
        Undo.DestroyObjectImmediate(go);
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log("[PauseMenuInstaller] 已移除暂停菜单");
    }
}
