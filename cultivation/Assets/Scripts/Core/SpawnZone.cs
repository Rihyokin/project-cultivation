using UnityEngine;

/// <summary>
/// **刷怪区标记**：只在 Scene 视图画一个圈 + 十字，标出"这片空地是留给刷怪点的"。
///
/// 由来：用户 2026-09-27「可以稍微留出一些空间用来放刷怪区」。
/// `SectWildernessBuilder` 会在 `野外环境/刷怪区/` 下按固定坐标生成几个空物体挂这个组件，
/// 地形那边已经保证**这片区域不长树**（见 builder 的 `可放()` / `空地权()`）。
///
/// 它**不参与运行逻辑**：没有 Update、没有碰撞体、Game 视图里什么都不画，
/// 只是编辑器里的一个提示圈。要取消就把 `野外环境/刷怪区` 整个删掉（或删本脚本引用）。
/// </summary>
[DisallowMultipleComponent]
public class SpawnZone : MonoBehaviour
{
    [Tooltip("区域半径（米）。地形生成时按这个尺寸留白")]
    public float 半径 = 26f;

    [Tooltip("线框颜色")]
    public Color 颜色 = new Color(1f, 0.35f, 0.25f, 1f);

    void OnDrawGizmos()
    {
        var 旧 = Gizmos.color;
        Gizmos.color = 颜色;

        // 贴地画一圈（每段都按地形高度取点，山坡上也不会悬空/埋进地里）
        const int 段 = 48;
        Vector3 上一点 = Vector3.zero;
        for (int i = 0; i <= 段; i++)
        {
            float a = i / (float)段 * Mathf.PI * 2f;
            var w = new Vector3(transform.position.x + Mathf.Cos(a) * 半径, 0f,
                                transform.position.z + Mathf.Sin(a) * 半径);
            w.y = 取地面(w);
            if (i > 0) Gizmos.DrawLine(上一点, w);
            上一点 = w;
        }

        // 中间一个十字，好认中心点
        var c = new Vector3(transform.position.x, 取地面(transform.position), transform.position.z);
        float r = Mathf.Min(4f, 半径 * 0.25f);
        for (int i = 0; i < 4; i++)
        {
            float a = i * Mathf.PI * 0.5f;
            Gizmos.DrawLine(c, c + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r));
        }
        Gizmos.DrawLine(c, c + Vector3.up * 3f);

        Gizmos.color = 旧;
    }

    static float 取地面(Vector3 w)
    {
        if (Physics.Raycast(new Vector3(w.x, w.y + 60f, w.z), Vector3.down, out var h, 200f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            return h.point.y + 0.15f;
        return w.y;
    }

    // ---- ASCII 别名 ----
    public float Radius { get => 半径; set => 半径 = value; }
}
