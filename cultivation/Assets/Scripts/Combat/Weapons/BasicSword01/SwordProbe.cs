using System.Reflection;
using System.Text;
using UnityEngine;

/// <summary>
/// 飞剑朝向的运行时探针。直接量真实飞行/悬浮中的剑对象，输出坐标，不靠看图。
///
/// 放在 Scripts 下（而不是 Editor 下）是因为要在【运行时的真实剑对象】上量 ——
/// 编辑器里实例化预制体量出来的结果，和实际代码路径未必一致。
///
/// 调用：exec_runtime_script 里 return SwordProbe.Measure();
/// </summary>
public static class SwordProbe
{
    const BindingFlags BF = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    public static string Measure()
    {
        var sb = new StringBuilder();

        var player = GameObject.Find("Player");
        if (player == null) return "找不到 Player";

        var swordType = FindType("BasicSword01");
        if (swordType == null) return "找不到 BasicSword01";
        var sword = player.GetComponent(swordType);
        if (sword == null) return "Player 上没有 BasicSword01";

        // ---- 1) 组件的朝向相关字段真实值 ----
        sb.Append("=== 组件字段 ===\n");
        foreach (var f in swordType.GetFields(BF))
        {
            if (f.FieldType == typeof(Vector3) && (f.Name.Contains("朝向") || f.Name.Contains("旋转") || f.Name.Contains("偏移")))
                sb.Append("  ").Append(f.Name).Append(" = ").Append(((Vector3)f.GetValue(sword)).ToString("F2")).Append("\n");
        }

        // ---- 2) 剑体根节点 ----
        Transform root = null;
        foreach (var f in swordType.GetFields(BF))
            if (f.FieldType == typeof(Transform)) { root = f.GetValue(sword) as Transform; break; }

        if (root == null) { sb.Append("剑体根节点 = NULL（还没生成）\n"); return sb.ToString(); }

        sb.Append("\n=== 剑体根节点 ").Append(root.name).Append(" ===\n");
        sb.Append("  世界位置 = ").Append(root.position.ToString("F3")).Append("\n");
        sb.Append("  世界旋转 = ").Append(root.rotation.eulerAngles.ToString("F1")).Append("\n");

        // ---- 3) 网格节点 ----
        Transform meshNode = null;
        MeshFilter mf = null;
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            var f = t.GetComponent<MeshFilter>();
            if (f != null && f.sharedMesh != null) { meshNode = t; mf = f; break; }
        }
        if (meshNode == null) { sb.Append("  网格节点 = NULL\n"); return sb.ToString(); }

        sb.Append("\n=== 网格节点 ").Append(meshNode.name).Append(" ===\n");
        sb.Append("  localRotation = ").Append(meshNode.localRotation.eulerAngles.ToString("F1")).Append("\n");
        sb.Append("  worldRotation = ").Append(meshNode.rotation.eulerAngles.ToString("F1")).Append("\n");

        // ---- 4) 剑尖方向 ----
        // 【不要读 Mesh.vertices】—— base_sword 的网格 isReadable=false，运行时读会抛异常、
        // 拿到 NaN，然后算出个 (0,0,0) 还被判成"接近下"，得出完全错误的结论（踩过）。
        // 改成纯旋转推导：编辑器标定已确认剑尖在网格本地 Z=0 端、剑柄在 Z=0.998 端，
        // 所以【剑尖方向 = 网格节点本地 -Z】。
        // 剑尖在【网格本地空间】的指向。
        //
        // 一开始我按「尖在 Z=0 端」直接取 -Z，是错的 —— 这把剑的剑身是弯的，
        // 「剑尖位于哪一端」和「从柄指向尖的方向」不是同一个方向，两者差了 45°。
        // 反推：策划肉眼确认 补偿=225 时剑尖竖直朝下，代入
        //     R_root(90°X) × R_comp(225°X) × axis = (0,-1,0)
        //   → R_x(315) × axis = (0,-1,0)
        //   → axis = (0, -0.7071, -0.7071)
        // 所以真实轴是「-Z 再向 -Y 偏 45°」。
        Vector3 tipLocalAxis = new Vector3(0f, -0.70710678f, -0.70710678f);
        var dir = meshNode.rotation * tipLocalAxis;

        sb.Append("\n=== 实测（由旋转推导，不读网格顶点）===\n");
        sb.Append("  剑尖本地轴 = (0,-0.707,-0.707)（由「补偿225=竖直朝下」反推，剑身是弯的）\n");
        sb.Append("  【剑尖世界方向】 = ").Append(dir.ToString("F3")).Append("\n");

        // 候选补偿扫描。
        // 步长必须是细的：只扫 0/90/180/270 会漏掉 45° 这种偏差 ——
        // 实测中「斜向上 45 度」就是这么被漏掉的（四个整直角全都对不上，看不出该填多少）。
        // 现在按 15° 扫一整圈，并直接标出最接近正下方的那个值。
        sb.Append("\n=== 候选补偿扫描（15° 步长，根节点旋转不变）===\n");
        var rootRot = root.rotation;
        float bestA = 0f, bestDot = float.MinValue;
        for (int a = 0; a < 360; a += 15)
        {
            var v = rootRot * (Quaternion.Euler(a, 0f, 0f) * tipLocalAxis);
            float dot = Vector3.Dot(v, Vector3.down);
            if (dot > bestDot) { bestDot = dot; bestA = a; }
            if (dot > 0.7f)   // 只打印接近朝下的，避免刷屏
                sb.Append("  (").Append(a).Append(",0,0) -> ").Append(v.ToString("F3"))
                  .Append("  与下方夹角 ").Append(Vector3.Angle(v, Vector3.down).ToString("F1")).Append("°\n");
        }
        sb.Append("  => 最接近正下方的是 (").Append(bestA).Append(",0,0)，夹角 ")
          .Append(Vector3.Angle(rootRot * (Quaternion.Euler(bestA, 0f, 0f) * tipLocalAxis), Vector3.down).ToString("F1")).Append("°\n");

        // 换算成易读的说法
        float down = Vector3.Angle(dir, Vector3.down);
        float up = Vector3.Angle(dir, Vector3.up);
        float fwd = Vector3.Angle(dir, Vector3.forward);
        float back = Vector3.Angle(dir, Vector3.back);
        float right = Vector3.Angle(dir, Vector3.right);
        float left = Vector3.Angle(dir, Vector3.left);

        sb.Append("  与各轴夹角： 下=").Append(down.ToString("F1")).Append("°  上=").Append(up.ToString("F1"))
          .Append("°  前=").Append(fwd.ToString("F1")).Append("°  后=").Append(back.ToString("F1"))
          .Append("°  右=").Append(right.ToString("F1")).Append("°  左=").Append(left.ToString("F1")).Append("°\n");

        string best = "下";
        float bd = down;
        if (up < bd) { best = "上"; bd = up; }
        if (fwd < bd) { best = "前"; bd = fwd; }
        if (back < bd) { best = "后"; bd = back; }
        if (right < bd) { best = "右"; bd = right; }
        if (left < bd) { best = "左"; bd = left; }
        sb.Append("  => 剑尖最接近【").Append(best).Append("】，偏差 ").Append(bd.ToString("F1")).Append("°\n");

        return sb.ToString();
    }

    static System.Type FindType(string n)
    {
        foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies())
        {
            var t = a.GetType(n);
            if (t != null) return t;
            foreach (var t2 in a.GetTypes()) if (t2.Name == n) return t2;
        }
        return null;
    }
}
