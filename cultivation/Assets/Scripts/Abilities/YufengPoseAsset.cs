using UnityEngine;

/// <summary>
/// 【凭虚御风】的目标姿势资产。
///
/// 策划在场景里摆好一个 PoseDisplay，用菜单把它当前的姿势【抓取】成这个资产：
/// 之后烘焙就只依赖资产，和场景解耦 —— 删掉 PoseDisplay 也不影响重烘焙。
///
/// 抓取方式：HumanPoseHandler.GetHumanPose 把骨骼层级读成 95 项肌肉值 + 根位移/旋转。
/// 注意 bodyPosition 是【相对角色根节点】的，所以 PoseDisplay 摆在世界哪个高度都不影响。
/// </summary>
[CreateAssetMenu(fileName = "御风姿势", menuName = "修仙/凭虚御风姿势资产", order = 21)]
public class YufengPoseAsset : ScriptableObject
{
    [Tooltip("这份姿势是从哪个对象抓来的（只作记录）")]
    public string 来源对象 = "";

    [Tooltip("抓取时间（只作记录）")]
    public string 抓取时间 = "";

    [Header("姿势数据")]
    public Vector3 bodyPosition = new Vector3(0f, 0.94f, 0f);
    public Quaternion bodyRotation = Quaternion.identity;

    [Tooltip("95 项人形肌肉值，由 HumanPoseHandler 读出")]
    public float[] muscles;

    /// <summary>是否是一份有效数据</summary>
    public bool IsValid => muscles != null && muscles.Length == HumanTrait.MuscleCount;

    /// <summary>转成 HumanPose 给烘焙器用</summary>
    public HumanPose ToHumanPose()
    {
        return new HumanPose
        {
            bodyPosition = bodyPosition,
            bodyRotation = bodyRotation,
            muscles = muscles != null ? (float[])muscles.Clone() : new float[HumanTrait.MuscleCount],
        };
    }

    /// <summary>从一份 HumanPose 写入</summary>
    public void FromHumanPose(HumanPose pose, string sourceName)
    {
        bodyPosition = pose.bodyPosition;
        bodyRotation = pose.bodyRotation;
        muscles = pose.muscles != null ? (float[])pose.muscles.Clone() : new float[HumanTrait.MuscleCount];
        来源对象 = sourceName;
        抓取时间 = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
    }
}
