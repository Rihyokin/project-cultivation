using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 给场景里那四个设施装上交互。
///
/// 场景里它们的名字是英文的：
///   cultivation room        → 修炼
///   alchemy furnace         → 炼丹
///   refining furnace        → 炼器
///   deploy formation point  → 布阵
///
/// 做的事：
///   1. 给每个物件加碰撞体（没有的话）—— 右键射线要打得中才有反应
///      · 房子/炉子用 BoxCollider（实体，顺便挡住玩家走进去）
///      · 布阵点用 Trigger 的 BoxCollider（它是地面上的标记，不该挡路）
///   2. 挂 StationInteractable，按名字判定类型
///   3. 给玩家挂 StationInteractor，并把 Assets/Fonts/SimHei.ttf 指派进去
///      （不指派的话中文提示会变成方块）
///
/// 菜单：Cultivation/设施/安装交互
///       Cultivation/设施/检查
///       Cultivation/设施/卸载
/// </summary>
public static class StationSetup
{
    const string 字体路径 = "Assets/Fonts/SimHei.ttf";

    // 物件名 → (类型, 是否用 Trigger)
    static readonly (string 名, StationInteractable.StationKind 类, bool trigger)[] 表 =
    {
        ("cultivation room",       StationInteractable.StationKind.修炼, false),
        ("alchemy furnace",        StationInteractable.StationKind.炼丹, false),
        ("refining furnace",       StationInteractable.StationKind.炼器, false),
        ("deploy formation point", StationInteractable.StationKind.布阵, true),   // 地面标记，别挡路
    };

    [MenuItem("Cultivation/设施/安装交互")]
    public static void Install()
    {
        var 报告 = new StringBuilder();
        int 装好 = 0;

        foreach (var (名, 类, trigger) in 表)
        {
            var go = GameObject.Find(名);
            if (go == null) { 报告.Append("  【找不到】").Append(名).Append('\n'); continue; }

            // 1) 碰撞体
            var 现有 = go.GetComponents<Collider>();
            bool 要加 = true;
            foreach (var c in 现有)
                if (c is BoxCollider || c is CapsuleCollider || c is SphereCollider) { 要加 = false; break; }

            if (要加)
            {
                var 盒 = go.AddComponent<BoxCollider>();
                按包围盒定尺寸(go, 盒);
                盒.isTrigger = trigger;
                报告.Append("  ").Append(名).Append("：加了 BoxCollider（")
                    .Append(盒.isTrigger ? "Trigger" : "实体").Append("）\n");
            }
            else 报告.Append("  ").Append(名).Append("：已有碰撞体\n");

            // 2) 交互组件
            var s = go.GetComponent<StationInteractable>();
            if (s == null) { s = go.AddComponent<StationInteractable>(); 报告.Append("      加了 StationInteractable\n"); }
            s.类型 = 类;
            if (string.IsNullOrEmpty(s.显示名)) s.显示名 = StationInteractable.默认名(类);

            EditorUtility.SetDirty(go);
            装好++;
        }

        // 3) 玩家身上挂交互器
        var 玩家 = Object.FindObjectOfType<PlayerVitals>();
        if (玩家 != null)
        {
            var it = 玩家.GetComponent<StationInteractor>();
            if (it == null) { it = 玩家.gameObject.AddComponent<StationInteractor>(); 报告.Append("  玩家：加了 StationInteractor\n"); }
            else 报告.Append("  玩家：StationInteractor 已有\n");

            var 字体 = AssetDatabase.LoadAssetAtPath<Font>(字体路径);
            if (字体 != null) { it.字体 = 字体; 报告.Append("  玩家：字体已接 ").Append(字体.name).Append('\n'); }
            else 报告.Append("  【警告】没找到 ").Append(字体路径).Append("，中文会是方块\n");

            EditorUtility.SetDirty(it);
        }
        else 报告.Append("  【找不到玩家】场景里没有 PlayerVitals\n");

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveOpenScenes();

        Debug.Log("[StationSetup] 安装完成（" + 装好 + "/4 个设施）\n" + 报告
                  + "\n用法：走近物件 → 出现「右键 XX」提示 → 按右键打开界面");
    }

    [MenuItem("Cultivation/设施/检查")]
    public static void Check()
    {
        var sb = new StringBuilder("[StationSetup] 检查\n");
        foreach (var (名, 类, trigger) in 表)
        {
            var go = GameObject.Find(名);
            if (go == null) { sb.Append("  【找不到】").Append(名).Append('\n'); continue; }

            var s = go.GetComponent<StationInteractable>();
            var cols = go.GetComponents<Collider>();
            sb.Append("  ").Append(名).Append('\n');
            sb.Append("      组件      = ").Append(s != null ? s.类型.ToString() : "【没有】").Append('\n');
            sb.Append("      碰撞体    = ").Append(cols.Length);
            foreach (var c in cols) sb.Append(" [").Append(c.GetType().Name).Append(c.isTrigger ? "/Trigger" : "").Append("]");
            sb.Append('\n');

            float 距离 = s != null ? s.交互距离 : 0f;
            sb.Append("      交互距离  = ").Append(距离.ToString("F1")).Append(" 米\n");
        }

        var 玩家 = Object.FindObjectOfType<PlayerVitals>();
        sb.Append("  玩家 StationInteractor = ");
        if (玩家 == null) sb.Append("【找不到玩家】\n");
        else
        {
            var it = 玩家.GetComponent<StationInteractor>();
            sb.Append(it != null ? "有" : "【没有】");
            if (it != null) sb.Append("，字体=").Append(it.字体 != null ? it.字体.name : "【null，中文会变方块】");
            sb.Append('\n');
        }
        Debug.Log(sb.ToString());
    }

    [MenuItem("Cultivation/设施/卸载")]
    public static void Uninstall()
    {
        foreach (var (名, 类, trigger) in 表)
        {
            var go = GameObject.Find(名);
            if (go == null) continue;
            var s = go.GetComponent<StationInteractable>();
            if (s != null) Object.DestroyImmediate(s);
        }
        var 玩家 = Object.FindObjectOfType<PlayerVitals>();
        if (玩家 != null)
        {
            var it = 玩家.GetComponent<StationInteractor>();
            if (it != null) Object.DestroyImmediate(it);
        }
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log("[StationSetup] 已卸载交互组件（碰撞体保留）");
    }

    /// <summary>
    /// 按渲染体包围盒定盒子尺寸。
    /// 【注意】BoxCollider.size 是本地空间，要除以 lossyScale ——
    /// prefab 根缩放不一定是 1（有的 NPC 是 12），不除会大得离谱。
    /// </summary>
    static void 按包围盒定尺寸(GameObject go, BoxCollider 盒)
    {
        var 渲染器 = go.GetComponentsInChildren<Renderer>(true);
        if (渲染器.Length == 0) { 盒.size = Vector3.one * 2f; 盒.center = Vector3.up; return; }

        var b = 渲染器[0].bounds;
        for (int i = 1; i < 渲染器.Length; i++) b.Encapsulate(渲染器[i].bounds);

        var s = go.transform.lossyScale;
        盒.center = go.transform.InverseTransformPoint(b.center);
        盒.size = new Vector3(
            b.size.x / Mathf.Max(0.0001f, Mathf.Abs(s.x)),
            b.size.y / Mathf.Max(0.0001f, Mathf.Abs(s.y)),
            b.size.z / Mathf.Max(0.0001f, Mathf.Abs(s.z)));
    }
}
