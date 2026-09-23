using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

/// <summary>
/// 找出一个 NPC 的【身躯】渲染体，并据此算包围盒 —— **把武器和飘带/特效排除在外**。
///
/// 为什么不能用「体积最大的那个」（我第一版就是这么写的，结果全错）：
///
/// | 渲染体（白骨娘娘） | 世界包围盒 minY | 体积 |
/// |---|---|---|
/// | `BaiGuNiangNiang_01`（身体） | **0** | 4.76 |
/// | `BaiGuNiangNiang_01_wuqi`（武器） | 1.069 | 0.37 |
/// | `BaiGuNiangNiang_02`（飘带） | **0.945** | **8.63 ← 最大** |
///
/// 体积最大的是**飘带**，它最低点在 0.945 米高 —— 拿它当身躯去贴地，
/// 就会把整个 NPC 往下压 0.945 米，表现就是「陷进地板只露一个头」。
///
/// **正确的识别办法（按命名，用户给的规律）**：
///   1. 先按关键字排掉武器：名字里有 `wuqi` / `weapon` / `武器` 的
///   2. 剩下的里面优先取**名字以 `_01` 结尾、且数字后缀最少**的那个 —— 那就是身躯
///      （`BaiGuNiangNiang_01` ✓、`BLJ_01` ✓ 而不是 `BLJ_01_01`、
///        `BaiXiongJing_01_01` ✓ 而不是 `_01_02`、`Changdaonv_01` ✓ 而不是 `ChangDaoNv_02`）
///   3. 都对不上（`gou` / `muji` 这种只有一个网格的）→ 取体积最大的
///
/// 包围盒取自 mesh 的**绑定姿势本地包围盒**（`sharedMesh.bounds`）再变换到世界，
/// 而不是 `Renderer.bounds` —— 后者是「当前姿势」的动态包围盒，没播动画时不可靠。
/// </summary>
public static class NpcBodyBounds
{
    /// <summary>名字里带这些字样的当武器排掉</summary>
    static readonly string[] 武器关键字 = { "wuqi", "weapon", "武器" };

    static readonly Regex 数字后缀 = new Regex(@"_(\d+)(?=_|$)", RegexOptions.Compiled);

    /// <summary>取身躯包围盒。取不到渲染体时返回 false</summary>
    public static bool 取(GameObject 目标, out Bounds 结果)
    {
        结果 = default;
        if (目标 == null) return false;

        var 身躯 = 取身躯渲染体(目标);
        if (身躯 == null) return false;
        if (!取网格包围盒(身躯, out 结果)) return false;

        // ---- 安全阀 ----
        // 万一「以 _01 结尾」挑中的其实是个小挂件（实测 BaiLuJing_02 挑出来只有 0.69 米高），
        // 就退回「所有非武器渲染体的并集」—— 宁可宽松一点，也不能小到能穿过去。
        if (取非武器并集(目标, out var 并集))
        {
            float 并集高 = 并集.size.y;
            if (并集高 > 0.01f && 结果.size.y < 并集高 * 0.6f)
                结果 = 并集;
        }
        return true;
    }

    /// <summary>所有「非武器」渲染体的并集包围盒</summary>
    public static bool 取非武器并集(GameObject 目标, out Bounds 结果)
    {
        结果 = default;
        if (目标 == null) return false;

        var 全部 = 目标.GetComponentsInChildren<Renderer>(true);
        bool 有 = false;
        foreach (var r in 全部)
        {
            if (r == null || !r.enabled || !有网格(r) || 是武器(r.name)) continue;
            if (!取网格包围盒(r, out var b)) continue;
            if (!有) { 结果 = b; 有 = true; }
            else 结果.Encapsulate(b);
        }
        return 有;
    }

    /// <summary>取「身躯」那个渲染体（可能为 null）</summary>
    public static Renderer 取身躯渲染体(GameObject 目标)
    {
        if (目标 == null) return null;

        var 全部 = 目标.GetComponentsInChildren<Renderer>(true);
        if (全部 == null || 全部.Length == 0) return null;

        // ---- 1) 排掉武器（和没有网格的）----
        var 候选 = new List<Renderer>();
        foreach (var r in 全部)
        {
            if (r == null || !r.enabled) continue;
            if (!有网格(r)) continue;
            if (是武器(r.name)) continue;
            候选.Add(r);
        }
        if (候选.Count == 0) return 最大体积(全部);
        if (候选.Count == 1) return 候选[0];

        // ---- 2) 优先「以 _01 结尾且数字后缀最少」----
        Renderer 最佳 = null;
        int 最佳组数 = int.MaxValue;
        foreach (var r in 候选)
        {
            string 名 = r.name.Trim().ToLowerInvariant();
            if (!名.EndsWith("_01")) continue;

            int 组数 = 数字后缀.Matches(r.name).Count;
            if (组数 < 最佳组数) { 最佳组数 = 组数; 最佳 = r; }
        }
        if (最佳 != null) return 最佳;

        // ---- 3) 兜底：体积最大的 ----
        return 最大体积(候选.ToArray());
    }

    static bool 是武器(string 名)
    {
        if (string.IsNullOrEmpty(名)) return false;
        string 小写 = 名.ToLowerInvariant();
        foreach (var k in 武器关键字)
            if (小写.Contains(k)) return true;
        return false;
    }

    static bool 有网格(Renderer r)
    {
        if (r is SkinnedMeshRenderer s) return s.sharedMesh != null;
        var mf = r.GetComponent<MeshFilter>();
        return mf != null && mf.sharedMesh != null;
    }

    static Renderer 最大体积(Renderer[] 们)
    {
        Renderer 最佳 = null; float 最大 = -1f;
        foreach (var r in 们)
        {
            if (r == null || !有网格(r)) continue;
            var s = r.bounds.size;
            float v = s.x * s.y * s.z;
            if (v > 最大) { 最大 = v; 最佳 = r; }
        }
        return 最佳;
    }

    /// <summary>
    /// 渲染体的世界包围盒。**用 mesh 的绑定姿势本地包围盒算**，
    /// 不用 <c>Renderer.bounds</c>（那是当前姿势的动态值，没动画时不可靠）。
    /// </summary>
    public static bool 取网格包围盒(Renderer r, out Bounds 世界)
    {
        世界 = default;
        if (r == null) return false;

        Bounds 本地;
        if (r is SkinnedMeshRenderer smr && smr.sharedMesh != null) 本地 = smr.sharedMesh.bounds;
        else
        {
            var mf = r.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) { 世界 = r.bounds; return true; }
            本地 = mf.sharedMesh.bounds;
        }

        var t = r.transform;
        Vector3 min = Vector3.positiveInfinity, max = Vector3.negativeInfinity;
        for (int i = 0; i < 8; i++)
        {
            var 角 = new Vector3(
                (i & 1) == 0 ? 本地.min.x : 本地.max.x,
                (i & 2) == 0 ? 本地.min.y : 本地.max.y,
                (i & 4) == 0 ? 本地.min.z : 本地.max.z);
            var w = t.TransformPoint(角);
            min = Vector3.Min(min, w);
            max = Vector3.Max(max, w);
        }
        世界.SetMinMax(min, max);
        return true;
    }

    /// <summary>身躯最低点的世界 Y。取不到就用碰撞体兜底</summary>
    public static float 取最低点(GameObject 目标)
    {
        if (取(目标, out var b)) return b.min.y;

        var col = 目标 != null ? 目标.GetComponent<Collider>() : null;
        return col != null ? col.bounds.min.y : (目标 != null ? 目标.transform.position.y : 0f);
    }

    /// <summary>
    /// 给一个碰撞体算出「贴合身躯」的胶囊参数（本地空间）。
    /// 返回 false 表示算不出来（没有渲染体）。
    /// </summary>
    public static bool 算胶囊(GameObject 目标, out Vector3 中心, out float 半径, out float 高度)
    {
        中心 = Vector3.zero; 半径 = 0.5f; 高度 = 1.8f;
        if (!取(目标, out var 世界)) return false;

        var t = 目标.transform;
        // 世界包围盒 → 本地：8 个角逆变换再合并，兼容任意旋转/缩放
        Vector3 min = Vector3.positiveInfinity, max = Vector3.negativeInfinity;
        for (int i = 0; i < 8; i++)
        {
            var 角 = new Vector3(
                (i & 1) == 0 ? 世界.min.x : 世界.max.x,
                (i & 2) == 0 ? 世界.min.y : 世界.max.y,
                (i & 4) == 0 ? 世界.min.z : 世界.max.z);
            var 本地 = t.InverseTransformPoint(角);
            min = Vector3.Min(min, 本地);
            max = Vector3.Max(max, 本地);
        }

        Vector3 尺寸 = max - min;
        float 水平半径 = Mathf.Max(尺寸.x, 尺寸.z) * 0.5f;
        float 竖直半径 = 尺寸.y * 0.5f;

        // 轴对齐胶囊：半径不能超过半高，否则 Unity 会把它夹成一个球
        半径 = Mathf.Max(0.05f, Mathf.Min(水平半径, 竖直半径));
        高度 = Mathf.Max(半径 * 2f, 尺寸.y);
        中心 = (min + max) * 0.5f;
        return true;
    }

    // ---- ASCII 别名 ----
    public static Renderer GetBodyRenderer(GameObject go) => 取身躯渲染体(go);
    public static float GetLowestY(GameObject go) => 取最低点(go);
    public static bool TryGetBodyBounds(GameObject go, out Bounds b) => 取(go, out b);
}
