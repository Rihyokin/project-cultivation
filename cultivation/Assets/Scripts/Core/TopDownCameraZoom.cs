using UnityEngine;

/// <summary>
/// 鼠标滚轮控制 2.5D 摄像机的远近。
///
/// 两段区间：
///   · 软区间 softMin~softMax（默认 0.7~1.3 倍基准距离）—— 正常可停留的范围
///   · 硬极限 hardMin~hardMax（默认 0.6~1.4 倍）—— 滚轮最多能推到这么远/这么近
///
/// 滚进极限区（0.6~0.7 或 1.3~1.4）后松手，摄像机会平滑弹回软区间。
/// 本脚本只改 TopDownCamera.distance，其余跟随逻辑仍由 TopDownCamera 负责。
/// </summary>
public class TopDownCameraZoom : MonoBehaviour
{
    [Header("引用（留空自动取本物体上的 TopDownCamera）")]
    public TopDownCamera cameraRig;

    [Header("滚轮")]
    [Tooltip("滚轮一格改变的距离比例。0.07 约等于 4 格从 1.0 缩到 0.7")]
    public float zoomPerNotch = 0.07f;

    [Tooltip("鼠标滚轮轴上「一格」对应的原始值，Unity 默认 0.1，一般不用改")]
    public float scrollNotchValue = 0.1f;

    [Header("区间（相对基准距离的倍数）")]
    [Tooltip("软区间下限：正常能停住的最小倍数")]
    public float softMin = 0.7f;

    [Tooltip("软区间上限：正常能停住的最大倍数")]
    public float softMax = 1.3f;

    [Tooltip("硬极限下限：滚轮最多能推到这么近")]
    public float hardMin = 0.6f;

    [Tooltip("硬极限上限：滚轮最多能推到这么远")]
    public float hardMax = 1.4f;

    [Header("越界回弹")]
    [Tooltip("回弹陡度。越大回得越快，6 左右比较自然")]
    public float returnSharpness = 6f;

    [Header("基准")]
    [Tooltip("启动时把 TopDownCamera 当前的 distance 记为基准距离（推荐勾选）")]
    public bool captureBaseOnStart = true;

    /// <summary>基准距离（1.0 倍）</summary>
    public float BaseDistance { get { return baseDistance; } }

    /// <summary>当前距离相对基准的倍数</summary>
    public float CurrentRatio { get { return ratio; } }

    float baseDistance = 16f;
    float ratio = 1f;
    bool initialised;

    void Start()
    {
        Initialise();
    }

    void Initialise()
    {
        if (initialised) return;

        if (cameraRig == null) cameraRig = GetComponent<TopDownCamera>();
        if (cameraRig != null && captureBaseOnStart) baseDistance = cameraRig.distance;

        ratio = 1f;
        initialised = true;
    }

    void Update()
    {
        if (!initialised) Initialise();
        if (cameraRig == null) return;

        // ---- 滚轮输入 ----
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) > 0.00001f && scrollNotchValue > 0.00001f)
        {
            float notches = scroll / scrollNotchValue;
            ratio -= notches * zoomPerNotch;          // 向上滚 = 拉近
            ratio = Mathf.Clamp(ratio, hardMin, hardMax);
        }

        // ---- 超出软区间则平滑弹回 ----
        float target = Mathf.Clamp(ratio, softMin, softMax);
        if (!Mathf.Approximately(ratio, target))
        {
            ratio = Mathf.Lerp(ratio, target, 1f - Mathf.Exp(-returnSharpness * Time.deltaTime));
            if (Mathf.Abs(ratio - target) < 0.0005f) ratio = target;
        }

        cameraRig.distance = baseDistance * ratio;
    }
}
