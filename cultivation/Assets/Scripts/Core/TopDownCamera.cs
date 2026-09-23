using UnityEngine;

/// <summary>
/// 2.5D 俯视角跟随摄像机。
/// 机位角度恒定（yaw / pitch 不变，永不自转），只平滑跟随目标位置。
/// 场景里靠 OnValidate 实时预览：在 Inspector 里拖 yaw / pitch / distance 就能直接看到效果。
/// </summary>
public class TopDownCamera : MonoBehaviour
{
    [Header("跟随目标")]
    [Tooltip("要跟随的角色。留空则自动找场景里名为 Player 的对象")]
    public Transform target;

    [Tooltip("注视点相对目标脚底的高度")]
    public float lookHeight = 1.2f;

    [Header("机位角度（恒定，不随角色旋转）")]
    [Range(5f, 89f)]
    [Tooltip("俯角。越大越接近正俯视，45~60 是常见 2.5D 区间")]
    public float pitch = 50f;

    [Tooltip("水平朝向。45 是经典斜 45 度 2.5D 视角")]
    public float yaw = 45f;

    [Header("距离")]
    [Tooltip("摄像机到注视点的距离。约 16 时角色占屏高 ~15%")]
    public float distance = 16f;

    [Header("跟随平滑")]
    [Tooltip("位置平滑时间（秒）。0 = 硬跟随，0.1~0.2 比较舒服")]
    public float smoothTime = 0.12f;

    [Tooltip("最大跟随速度，0 = 不限制（内部换算成 Infinity）")]
    public float maxFollowSpeed = 0f;

    [Header("投影方式")]
    [Tooltip("勾选=正交投影（更纯粹的 2.5D），取消=透视")]
    public bool orthographic = false;

    [Tooltip("正交模式下的视野高度")]
    public float orthographicSize = 14f;

    [Tooltip("透视模式下的视野角度")]
    [Range(10f, 90f)]
    public float fieldOfView = 40f;

    Camera cam;
    Vector3 followVelocity;

    void Awake()
    {
        cam = GetComponent<Camera>();

        if (target == null)
        {
            GameObject player = GameObject.Find("Player");
            if (player != null) target = player.transform;
        }

        SnapToTarget();
    }

    // 放在 LateUpdate：保证在角色 Update 移动之后才取位置，避免抖动
    void LateUpdate()
    {
        if (target == null) return;

        ApplyProjection();

        Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 lookPoint = target.position + Vector3.up * lookHeight;
        Vector3 desired = lookPoint - rotation * Vector3.forward * distance;

        // 注意：SmoothDamp 内部按 maxSpeed * smoothTime 限制单次位移，
        // 传 0 会把位移钳成 0（摄像机完全不动）。0 必须换算成 Infinity。
        float maxSpeed = maxFollowSpeed > 0f ? maxFollowSpeed : Mathf.Infinity;

        transform.position = smoothTime > 0f
            ? Vector3.SmoothDamp(transform.position, desired, ref followVelocity, smoothTime, maxSpeed)
            : desired;

        transform.rotation = rotation;   // 方向恒定
    }

    void ApplyProjection()
    {
        if (cam == null) return;
        cam.orthographic = orthographic;
        if (orthographic) cam.orthographicSize = orthographicSize;
        else cam.fieldOfView = fieldOfView;
    }

    /// <summary>立刻把摄像机摆到目标位置，不做平滑</summary>
    public void SnapToTarget()
    {
        if (target == null) return;

        Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
        transform.rotation = rotation;
        transform.position = target.position + Vector3.up * lookHeight - rotation * Vector3.forward * distance;
        followVelocity = Vector3.zero;
        ApplyProjection();
    }

    // 编辑期调参时实时预览，不用进 Play
    void OnValidate()
    {
        distance = Mathf.Max(0.1f, distance);
        if (cam == null) cam = GetComponent<Camera>();
        ApplyProjection();                                            // 改 fov / 正交 立刻生效
        if (!Application.isPlaying && target != null) SnapToTarget();  // 改角度 / 距离 立刻挪机位
    }
}
