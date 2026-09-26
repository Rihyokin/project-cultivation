using UnityEngine;

/// <summary>
/// **空气墙**：挡住人，但游戏里什么都不画。
///
/// 【为什么要有这个组件】用户在 Scene 视图里画的空气墙轮廓不满意自己生成的那一圈，
/// 改成"**给一个能自己摆的方块**"（原话：「你弄个空气墙预制体放在场景中，就是一个 cube，我自己来摆」）。
///
/// 【它做了两件事】
///   1. `Awake()` 里把**自己的 Renderer 关掉** —— 预制体在编辑器里带着一个半透明方块
///      （方便在 Scene 视图里看见、点选、拉大小），但**进游戏就完全不画** ✓
///   2. `OnDrawGizmos()` 在 **Scene 视图**里画一个橙色线框 —— 就算方块被关掉/被别的物件挡住，
///      摆场景时也能一眼看到墙在哪、多大（**Game 视图和游戏里都不会画 gizmo** ✓）
///
/// 【怎么用】把 `Assets/Prefabs/空气墙.prefab` 拖进场景 → **直接改 Transform 的 Scale 拉成一段墙**
/// （默认 Scale = `12 × 10 × 1.2`，就是"长 12m、高 10m、厚 1.2m"）→ 需要几段就 Ctrl+D 复制几段。
/// 想改颜色 / 关掉线框，Inspector 上就有。
///
/// ★ **尺寸一律用 Transform 的 Scale**，不要去改 BoxCollider 的 Size ——
/// 那样"看得见的方块"和"实际挡人的碰撞体"就**对不上**了（渲染的是 1m 的 cube，碰撞体却是 12m）✗。
/// 用 Scale 的话，Cube 的网格和它的 BoxCollider 会**一起缩放**，看到的 = 挡到的 ✓
///
/// 【高度参考】`御风` 悬浮 2.4m、`坐骑` 最高 5.64m —— 墙高给到 **10m 以上**才拦得住飞的；
/// Scale.y=10 时以原点为中心，也就是挡 y −5~5，摆的时候记得把它往上抬一点
/// （比如 y=3 → 实际挡 −2~8 ✓；Inspector 上能看到 `世界高度范围` 的实时值）。
/// </summary>
[RequireComponent(typeof(BoxCollider))]
[DisallowMultipleComponent]
public class AirWall : MonoBehaviour
{
    [Header("Scene 视图线框（只在编辑器里画）")]
    [Tooltip("在 Scene 视图里画橙色线框，方便摆位。Game 视图和游戏里都不画。")]
    public bool 画线框 = true;

    [Tooltip("线框颜色")]
    public Color 线框颜色 = new Color(1f, 0.62f, 0.12f, 1f);

    [Header("运行时")]
    [Tooltip("勾上：进游戏时把自己身上的 Renderer 关掉（空气墙本来就该看不见）。\n" +
             "调试想看墙在哪时可以临时取消勾选。")]
    public bool 运行时隐藏 = true;

    void Awake()
    {
        // 预制体上那个半透明方块只是"编辑器里的样子"，游戏里一律不画
        if (!运行时隐藏) return;
        foreach (var r in GetComponentsInChildren<Renderer>(true))
            if (r != null) r.enabled = false;
    }

    void OnDrawGizmos()
    {
        if (!画线框) return;
        var 盒 = GetComponent<BoxCollider>();
        if (盒 == null) return;

        var 旧色 = Gizmos.color;
        var 旧矩阵 = Gizmos.matrix;
        Gizmos.color = 线框颜色;
        Gizmos.matrix = transform.localToWorldMatrix;   // 跟着物体自己的旋转/缩放走
        Gizmos.DrawWireCube(盒.center, 盒.size);
        Gizmos.matrix = 旧矩阵;
        Gizmos.color = 旧色;
    }

    /// <summary>墙的世界高度范围（摆位时对照"御风 2.4 / 坐骑 5.64"用）</summary>
    public Vector2 世界高度范围
    {
        get
        {
            var 盒 = GetComponent<BoxCollider>();
            if (盒 == null) return new Vector2(transform.position.y, transform.position.y);
            float 半 = 盒.size.y * 0.5f * Mathf.Abs(transform.lossyScale.y);
            float 心 = transform.TransformPoint(盒.center).y;
            return new Vector2(心 - 半, 心 + 半);
        }
    }

    void OnValidate()
    {
        // 顺手保证有个 BoxCollider（预制体上已经有，防呆）
        if (GetComponent<BoxCollider>() == null) gameObject.AddComponent<BoxCollider>();
    }

    // ---- ASCII 别名 ----
    public bool DrawWireframe { get => 画线框; set => 画线框 = value; }
    public bool HideAtRuntime { get => 运行时隐藏; set => 运行时隐藏 = value; }
}
