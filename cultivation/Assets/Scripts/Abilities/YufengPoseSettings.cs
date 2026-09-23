using UnityEngine;

/// <summary>
/// 【凭虚御风】的姿势参数。给策划在 Inspector 里拖滑块调姿势用。
///
/// 用法：
///   1. Project 里选中 Assets/Animations/凭虚御风/御风姿势参数.asset
///   2. 在 Inspector 里拖这些滑块
///   3. 菜单 修仙 / 生成凭虚御风动画片段 → 重新烘焙 → 进游戏看效果
///
/// 数值都是「在人形肌肉空间里叠加多少」，0 = 保持基准站姿。
/// 躯干前倾 = 前倾总量 × 各部分分配比例，调总量最直观。
/// </summary>
[CreateAssetMenu(fileName = "御风姿势参数", menuName = "修仙/凭虚御风姿势参数", order = 20)]
public class YufengPoseSettings : ScriptableObject
{
    [Header("前进（御风_前进）")]
    [Tooltip("整体前倾力度。越大压得越低")]
    [Range(0f, 3f)] public float 前进_前倾总量 = 0f;

    [Tooltip("前倾在腰部的分配比例")]
    [Range(0f, 1f)] public float 前进_腰部占比 = 0.50f;

    [Tooltip("前倾在胸部的分配比例（想让上半身更扑出去就加大这个）")]
    [Range(0f, 1f)] public float 前进_胸部占比 = 0.50f;

    [Tooltip("前倾在上胸的分配比例")]
    [Range(0f, 1f)] public float 前进_上胸占比 = 0.42f;

    [Tooltip("头颈反向补偿。0 = 头完全跟着躯干前压（推荐）；" +
             "给正值会让头往后仰看前方，给大了就会变成「挺胸抬头」")]
    [Range(-1f, 1f)] public float 前进_头颈补偿 = 0f;

    [Tooltip("前进时整体的上下浮沉幅度")]
    [Range(0f, 0.2f)] public float 前进_浮沉幅度 = 0.022f;

    [Tooltip("肢体轻微摆动的幅度。0 = 完全静止（只有浮动）")]
    [Range(0f, 0.3f)] public float 摆动幅度 = 0.05f;

    [Header("悬停（御风_Idle）")]
    [Tooltip("原地悬停时的前倾力度。几乎站直就是 0.1 左右")]
    [Range(0f, 1f)] public float 悬停_前倾总量 = 0f;

    [Tooltip("悬停时的上下浮沉幅度")]
    [Range(0f, 0.3f)] public float 悬停_浮沉幅度 = 0.038f;

    [Header("升空 / 落地")]
    [Tooltip("升空开始时下蹲蓄力的幅度")]
    [Range(0f, 1f)] public float 升空_蓄力幅度 = 0.18f;

    [Tooltip("落地时屈膝缓冲的幅度")]
    [Range(0f, 1f)] public float 落地_缓冲幅度 = 0.28f;

    /// <summary>取参数资产；不存在就新建一个</summary>
    public static YufengPoseSettings LoadOrCreate()
    {
        const string Dir = "Assets/Animations";
        const string Path = Dir + "/凭虚御风/御风姿势参数.asset";

        var s = UnityEditor.AssetDatabase.LoadAssetAtPath<YufengPoseSettings>(Path);
        if (s != null) return s;

        if (!UnityEditor.AssetDatabase.IsValidFolder(Dir))
            UnityEditor.AssetDatabase.CreateFolder("Assets", "Animations");
        if (!UnityEditor.AssetDatabase.IsValidFolder(Dir + "/凭虚御风"))
            UnityEditor.AssetDatabase.CreateFolder(Dir, "凭虚御风");

        s = CreateInstance<YufengPoseSettings>();
        UnityEditor.AssetDatabase.CreateAsset(s, Path);
        UnityEditor.AssetDatabase.SaveAssets();
        return s;
    }
}
