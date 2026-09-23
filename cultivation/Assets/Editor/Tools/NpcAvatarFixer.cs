using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// **给 NPC 的 Animator 补上 Avatar。**
///
/// ## 为什么必须补
///
/// NPC 的动画片段（`BaiLuJing@Attack1.FBX` 里的）是**人形片段**：
/// `clip.isHumanMotion = True`、150+ 条**肌肉曲线**。
/// 人形片段不会直接驱动骨骼 —— 它靠 **Avatar** 把「肌肉空间」的曲线重定向到实际骨架。
///
/// **Avatar 为空时 → 映射不上 → 骨骼一根都不动**，
/// 表现就是「动画完全不播放」，但 `Animator` 的状态机、`Action` 整数、
/// 甚至 `CurrentAction` 读出来全都是**正常的**（我就在这上面误判过一次：只看了状态在切，
/// 没验蒙皮有没有动）。
///
/// 叠加的坑：prefab 上**同时**有个 legacy `Animation` 组件，它也想播这些片段，
/// 但片段没标 Legacy，于是 Unity 一直刷
/// `The AnimationClip 'Attack1' used by the Animation component must be marked as Legacy` ——
/// **这条警告和"动画不播"没关系**，别被它带跑。
///
/// ## Avatar 从哪来
///
/// 从**模型 FBX** 里取（`BaiLuJing_01.FBX` 导入时会生成一个 Avatar 子资产）。
/// 这里靠「SkinnedMeshRenderer 用的那个 mesh 在哪个 FBX 里」反查，
/// 同一个 FBX 里的 Avatar 就是对的。
///
/// 菜单：修仙 / NPC 资产 / …
/// </summary>
public static class NpcAvatarFixer
{
    const string NPC资源目录 = "Assets/resources/NPC";

    [MenuItem("修仙/NPC 资产/检查 Animator 的 Avatar（只读）")]
    public static void 检查Avatar() => 处理(true);

    [MenuItem("修仙/NPC 资产/修复 Animator 的 Avatar")]
    public static void 修复Avatar()
    {
        if (!EditorUtility.DisplayDialog("补 NPC 的 Avatar",
            "会扫描所有 NPC prefab，给 Animator 为空的补上模型 FBX 里的 Avatar。\n\n" +
            "为什么必须补：片段是**人形片段**（肌肉曲线），没有 Avatar 映射不上骨架，\n" +
            "骨骼一根都不动 —— 表现就是「动画完全不播放」。\n\n" +
            "会直接改 prefab 资产。继续？", "开始修复", "取消"))
            return;
        处理(false);
    }

    /// <summary>无确认弹窗的入口</summary>
    public static void 处理(bool 只检查)
    {
        var prefabs = new List<string>(Directory.GetFiles(NPC资源目录, "*.prefab", SearchOption.AllDirectories));
        int 没Animator = 0, 本来就有 = 0, 补上 = 0, 找不到Avatar = 0;
        var 找不到的 = new List<string>();
        var 例子 = new List<string>();

        foreach (var 路径 in prefabs)
        {
            string assetPath = 路径.Replace('\\', '/');

            if (只检查)
            {
                var g = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
                if (g == null) continue;
                var a = g.GetComponent<Animator>();
                if (a == null) 没Animator++;
                else if (a.avatar != null) 本来就有++;
                else
                {
                    补上++;
                    if (例子.Count < 12) 例子.Add(g.name);
                }
                continue;
            }

            var 内容 = PrefabUtility.LoadPrefabContents(assetPath);
            if (内容 == null) continue;
            try
            {
                var an = 内容.GetComponent<Animator>();
                if (an == null) { 没Animator++; continue; }
                if (an.avatar != null) { 本来就有++; continue; }

                var av = 找Avatar(内容, assetPath);
                if (av == null)
                {
                    找不到Avatar++;
                    if (找不到的.Count < 12) 找不到的.Add(内容.name);
                    continue;
                }

                an.avatar = av;
                EditorUtility.SetDirty(an);
                PrefabUtility.SaveAsPrefabAsset(内容, assetPath);
                补上++;
                if (例子.Count < 12) 例子.Add(内容.name + " ← " + av.name);
            }
            finally { PrefabUtility.UnloadPrefabContents(内容); }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        string 头 = 只检查 ? "[Avatar·检查] " : "[Avatar·修复] ";
        if (只检查)
            Debug.Log(头 + "prefab " + prefabs.Count + " 个｜**Avatar 为空（动画不播）** " + 补上
                + "｜本来就有 " + 本来就有 + "｜没 Animator " + 没Animator
                + (例子.Count > 0 ? "\n        前几个: " + string.Join(", ", 例子) : ""));
        else
            Debug.Log(头 + "补上 " + 补上 + " 个｜本来就有 " + 本来就有 + "｜没 Animator " + 没Animator
                + "｜找不到 Avatar " + 找不到Avatar
                + (例子.Count > 0 ? "\n        改了: " + string.Join(", ", 例子) : "")
                + (找不到的.Count > 0 ? "\n        找不到 Avatar: " + string.Join(", ", 找不到的) : ""));
    }

    /// <summary>
    /// 找这个 prefab 该用的 Avatar：
    /// 先看它自己的蒙皮网格来自哪个 FBX，取同一个 FBX 里的 Avatar；
    /// 不行再扫同目录 / 兄弟 _Animation 目录里的 FBX。
    /// </summary>
    static Avatar 找Avatar(GameObject go, string 自身路径)
    {
        // 1) 蒙皮网格所在的 FBX
        foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (smr.sharedMesh == null) continue;
            string p = AssetDatabase.GetAssetPath(smr.sharedMesh);
            var av = 取FBX里的Avatar(p);
            if (av != null) return av;
        }

        // 2) 同目录（含子目录）里任意 FBX
        //    注意：LoadPrefabContents 出来的物件不是资产，GetAssetPath 会返回空 —— 所以路径要传进来
        string 目录 = null;
        if (!string.IsNullOrEmpty(自身路径))
        {
            int i = 自身路径.LastIndexOf('/');
            if (i > 0) 目录 = 自身路径.Substring(0, i);
        }
        if (!string.IsNullOrEmpty(目录) && Directory.Exists(目录))
        {
            foreach (var f in Directory.GetFiles(目录, "*.FBX", SearchOption.AllDirectories))
            {
                var av = 取FBX里的Avatar(f.Replace('\\', '/'));
                if (av != null) return av;
            }
        }
        return null;
    }

    static Avatar 取FBX里的Avatar(string 路径)
    {
        if (string.IsNullOrEmpty(路径)) return null;
        foreach (var o in AssetDatabase.LoadAllAssetsAtPath(路径))
            if (o is Avatar a) return a;
        return null;
    }

    // ---- ASCII 别名 ----
    public static void Run(bool dryRun) => 处理(dryRun);
}
