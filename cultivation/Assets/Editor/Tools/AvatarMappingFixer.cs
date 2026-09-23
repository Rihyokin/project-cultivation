using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 修 avatar 的人形骨骼映射。
///
/// 背景：better_player_test 这个模型的脖子骨叫 NeckTwist01 / NeckTwist02，
/// Unity 自动映射时【没把 Neck 映射上】，导致：
///   · 动画里推到 "Neck Nod Down-Up" 这块肌肉时没有任何骨骼响应；
///   · 在 Animation 窗口里拖脖子滑块，脖子纹丝不动。
///
/// 这里把 Neck 显式指到 NeckTwist01，重建一个 avatar 覆盖原来的。
///
/// 菜单：修仙 / 修复玩家模型的人形映射   （ASCII：Cultivation / Fix Player Avatar Mapping）
/// </summary>
public static class AvatarMappingFixer
{
    const string ModelPath = "Assets/TripoModels/better_player_test/better_player_test.fbx";
    const string AvatarPath = "Assets/TripoModels/better_player_test/better_player_testAvatar.asset";

    [MenuItem("修仙/修复玩家模型的人形映射")]
    [MenuItem("Cultivation/Fix Player Avatar Mapping")]
    public static void Fix()
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (model == null) { Debug.LogError("[AvatarMappingFixer] 找不到模型 " + ModelPath); return; }

        // 找出模型里名字像脖子的骨骼
        string neckBone = null;
        foreach (var t in model.GetComponentsInChildren<Transform>(true))
        {
            string n = t.name.ToLower();
            if (n.Contains("neck")) { neckBone = t.name; break; }
        }
        if (neckBone == null) { Debug.LogError("[AvatarMappingFixer] 模型里找不到脖子骨"); return; }

        // 取现有 avatar 作底子。
        // 注意：这个 avatar 是 FBX 的子资产（avatarSetup=CreateFromThisModel），
        // 不是独立的 .asset，所以要用 LoadAllAssetsAtPath 找，不能按路径 LoadAssetAtPath。
        Avatar old = null;
        foreach (var o in AssetDatabase.LoadAllAssetsAtPath(ModelPath))
            if (o is Avatar a && a.isHuman) { old = a; break; }

        if (old == null) { Debug.LogError("[AvatarMappingFixer] 读不到原 avatar"); return; }
        var desc = old.humanDescription;
        Debug.Log("[AvatarMappingFixer] 原 avatar 人形骨骼数=" + desc.human.Length + "  脖子骨=" + neckBone);

        // 补上 / 修正 Neck
        var list = new List<HumanBone>(desc.human);
        bool found = false;
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].humanName == "Neck")
            {
                list[i] = new HumanBone { humanName = "Neck", boneName = neckBone, limit = list[i].limit };
                found = true;
                break;
            }
        }
        if (!found)
        {
            list.Add(new HumanBone
            {
                humanName = "Neck",
                boneName = neckBone,
                limit = new HumanLimit { useDefaultValues = true },
            });
        }
        desc.human = list.ToArray();
        // 注意：skeleton 数组【必须原样保留】。
        // 之前这里用 BuildSkeleton 重建过，结果中立姿势变了，
        // 同一套肌肉值摆出来的姿势全错（手臂变成前平举）。

        var rebuilt = AvatarBuilder.BuildHumanAvatar(model, desc);
        if (rebuilt == null || !rebuilt.isValid)
        {
            Debug.LogError("[AvatarMappingFixer] 重建 avatar 失败");
            return;
        }
        rebuilt.name = "better_player_testAvatar";

        // 覆盖原 avatar 资产
        var existing = AssetDatabase.LoadAssetAtPath<Avatar>(AvatarPath);
        if (existing != null) AssetDatabase.DeleteAsset(AvatarPath);
        AssetDatabase.CreateAsset(rebuilt, AvatarPath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("[AvatarMappingFixer] 已把 Neck 映射到 " + neckBone + "，重建 avatar 完成");
    }

    static HumanDescription HumanDescriptionWithNeck(GameObject model, string neckBone)
    {
        var desc = new HumanDescription
        {
            human = new HumanBone[0],
            skeleton = BuildSkeleton(model),
            upperArmTwist = 0.5f,
            lowerArmTwist = 0.5f,
            upperLegTwist = 0.5f,
            lowerLegTwist = 0.5f,
            armStretch = 0.05f,
            legStretch = 0.05f,
            feetSpacing = 0f,
            hasTranslationDoF = false,
        };
        return desc;
    }

    static SkeletonBone[] BuildSkeleton(GameObject model)
    {
        var bones = new List<SkeletonBone>();
        foreach (var t in model.GetComponentsInChildren<Transform>(true))
        {
            bones.Add(new SkeletonBone
            {
                name = t.name,
                position = t.localPosition,
                rotation = t.localRotation,
                scale = t.localScale,
            });
        }
        return bones.ToArray();
    }
}
