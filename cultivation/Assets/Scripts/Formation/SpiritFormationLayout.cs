using UnityEngine;

/// <summary>
/// 战阵的「格子 → 站位」对照表。**UI 和运行时都读这一份**，所以两边永远一致。
///
/// 布局照概念图那个 3×3 九宫格，但**它是以玩家为中心的阵**：
///
/// <code>
///      0   1   2        ← 最上面 = 玩家【正前方】
///      3   4   5        ← 中间那格 4 = **玩家自己**（不能放真灵）
///      6   7   8        ← 最下面 = 玩家【身后】
/// </code>
///
/// 也就是说：**上方 = 前方**（+Z 是玩家朝向），中间 = 玩家，下方 = 背后。
/// 所以真正能上阵的是 **8 格**（<see cref="玩家格"/> 除外），
/// 同时最多上 <see cref="可上阵数"/> 个。
///
/// 站位偏移是**玩家本地坐标**：+Z = 正前方、+X = 右手边。
/// 整体是个半径约 3.8 米的环 —— 相邻格子至少 3 米，本来就挤不到一起，
/// 但真灵追同一个目标时会互相靠拢，那种情况靠
/// <see cref="SpiritFormationManager.真灵最小间距"/> 做软分离。
/// </summary>
public static class SpiritFormationLayout
{
    /// <summary>九宫格一共几个格子</summary>
    public const int 格子数 = 9;

    /// <summary>中间那一格 —— **玩家自己站的地方**，不能上阵</summary>
    public const int 玩家格 = 4;

    /// <summary>同时最多上阵几个</summary>
    public const int 可上阵数 = 5;

    /// <summary>
    /// 每个格子的站位偏移（玩家本地坐标，按格子序号索引）。
    /// 索引 4（玩家格）写的是 Zero —— 那里就是玩家本人，不会生成真灵。
    /// </summary>
    public static readonly Vector3[] 站位偏移 =
    {
        new Vector3(-3.0f, 0f,  3.0f),     // 0 前方左
        new Vector3( 0.0f, 0f,  3.8f),     // 1 正前方
        new Vector3( 3.0f, 0f,  3.0f),     // 2 前方右
        new Vector3(-3.8f, 0f,  0.0f),     // 3 正左
        new Vector3( 0.0f, 0f,  0.0f),     // 4 ★ 玩家自己（不生成真灵）
        new Vector3( 3.8f, 0f,  0.0f),     // 5 正右
        new Vector3(-3.0f, 0f, -3.0f),     // 6 身后左
        new Vector3( 0.0f, 0f, -3.8f),     // 7 正后方
        new Vector3( 3.0f, 0f, -3.0f),     // 8 身后右
    };

    /// <summary>这个序号是不是有效的格子</summary>
    public static bool 格子有效(int 格) => 格 >= 0 && 格 < 格子数;

    /// <summary>这一格能不能放真灵（有效 + 不是玩家自己那格）</summary>
    public static bool 格子可上阵(int 格) => 格子有效(格) && 格 != 玩家格;

    /// <summary>这个格子的站位偏移（无效序号返回 Vector3.zero）</summary>
    public static Vector3 取偏移(int 格)
        => 格子有效(格) && 格 < 站位偏移.Length ? 站位偏移[格] : Vector3.zero;

    /// <summary>玩家本地坐标 → 世界坐标。朝向跟着玩家转，所以真灵始终站在玩家的「阵」里</summary>
    public static Vector3 取世界位置(Transform 玩家, int 格)
    {
        if (玩家 == null) return Vector3.zero;
        return 玩家.position + 玩家.rotation * 取偏移(格);
    }
}
