using UnityEngine;

/// <summary>
/// **地面探测**公共工具：从某个世界位置向下打射线，取「脚下的地面高度」。
///
/// 【为什么要抽成一个工具】坐骑（<see cref="MountRider"/>）和御风（<see cref="YufengFlight"/>）
/// 都要「维持一个固定的**对地**高度」，两边的判据必须完全一样 ——
/// 否则同一块地形上一个贴着地、一个悬在半空，调一边就把另一边带歪。
///
/// 【探不到地面怎么办】本工具只回答「探没探到」，**怎么兜底由调用方决定**：
/// 坐骑和御风都是「没探到就沿用上一次探到的高度」，也就是**原地维持一个固定高度**
/// （断崖 / 空洞上方不会一路掉下去，也不会突然弹起来）。
///
/// 【为什么起点要抬 0.15m】射线起点落在**自己的胶囊内部**时，Unity 不会让射线命中这个凸体，
/// 所以抬高一点点就够避免「打到自己」；再叠一层 <paramref name="自己"/> 过滤兜底。
/// </summary>
public static class GroundProbe
{
    /// <summary>射线起点相对传入位置抬高多少（米）。见类注释</summary>
    public const float 起点抬高 = 0.15f;

    /// <summary>命中缓冲。每帧最多调一两次，静态复用避免每帧分配</summary>
    static readonly RaycastHit[] 缓冲 = new RaycastHit[16];

    /// <summary>
    /// 从 <paramref name="世界位置"/> 向下探地面（起点抬高 <see cref="起点抬高"/>）。
    ///
    /// 命中返回 true 并给出地面的世界 Y；探不到返回 false（此时 <paramref name="高度"/> 无意义）。
    ///
    /// 会被跳过的碰撞体：
    ///   · <paramref name="自己"/> 及其子物体（角色 / 坐骑自己不算地面）；
    ///   · 挂在 <see cref="CharacterController"/> 或 <see cref="NpcInstance"/> 上的
    ///     —— 御风飞过 NPC 头顶时，那条射线会先打到 NPC 的脑袋，
    ///     不过滤的话角色会**莫名其妙被顶高 1.7 米** ✗
    /// </summary>
    public static bool 向下取地面(Vector3 世界位置, float 深度, Transform 自己, out float 高度)
    {
        高度 = 0f;
        if (深度 <= 0.0001f) return false;

        var 起点 = 世界位置 + Vector3.up * 起点抬高;
        int n = Physics.RaycastNonAlloc(起点, Vector3.down, 缓冲, 深度, ~0, QueryTriggerInteraction.Ignore);
        if (n <= 0) return false;

        float 最近 = float.MaxValue;
        bool 找到 = false;

        for (int i = 0; i < n; i++)
        {
            var h = 缓冲[i];
            if (h.collider == null) continue;

            var t = h.collider.transform;
            if (自己 != null && (t == 自己 || t.IsChildOf(自己))) continue;
            if (t.GetComponentInParent<CharacterController>() != null) continue;   // 别人（或自己）的角色胶囊
            if (t.GetComponentInParent<NpcInstance>() != null) continue;           // NPC 的身体

            if (h.distance >= 最近) continue;
            最近 = h.distance;
            高度 = h.point.y;
            找到 = true;
        }

        return 找到;
    }

    // ---- ASCII 别名 ----
    public static bool TryGetGroundY(Vector3 worldPos, float depth, Transform self, out float groundY)
        => 向下取地面(worldPos, depth, self, out groundY);
}
