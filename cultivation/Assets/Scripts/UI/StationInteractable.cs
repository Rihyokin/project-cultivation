using UnityEngine;

/// <summary>
/// 可交互物（修炼房屋 / 炼丹炉 / 炼器炉 / 布阵点 / **人类 NPC 对话**）。
///
/// 挂在这些物件上，配合玩家身上的 StationInteractor 使用：
///   玩家靠近 + 按交互键 → 打开对应的界面。
///
/// 这里只负责"我是谁、我多大、玩家要离多近、按哪个键"，具体开界面在 StationInteractor。
///
/// 【2026-09-26 合并】原来建筑用右键、NPC 对话想另做一套 —— 用户要求**合并成一套**：
/// 建筑是「F 炼丹 / F 炼器」、人类 NPC 是「F 对话」，**同时进范围时取"离根节点更近"的那个**。
/// 所以这里加了 <see cref="对话"/> 类型和 <see cref="交互键覆盖"/>。
/// </summary>
[DisallowMultipleComponent]
public class StationInteractable : MonoBehaviour
{
    public enum StationKind
    {
        修炼,       // cultivation room
        炼丹,       // alchemy furnace
        炼器,       // refining furnace
        布阵,       // deploy formation point
        对话,       // ★ 人类 NPC 对话（2026-09-26 合并进来）
    }

    [Header("身份")]
    public StationKind 类型 = StationKind.修炼;

    [Tooltip("界面上显示的名字。留空则按类型自动取（NPC 对话会由 NpcDialogue 填成 NPC 名字）")]
    public string 显示名 = "";

    [Header("交互")]
    [Tooltip("玩家离这么近才能交互（米）。按物件大小调")]
    public float 交互距离 = 4f;

    [Tooltip("只有玩家在这个高度差以内才允许交互，防止隔层楼点到")]
    public float 最大高度差 = 3f;

    [Tooltip("★ 这个物件用哪个键。None = 用玩家 StationInteractor 上的全局键。\n" +
             "人类 NPC 对话固定填 F（用户要求），建筑留 None 走全局")]
    public KeyCode 交互键覆盖 = KeyCode.None;

    [Header("提示")]
    [Tooltip("靠近时是否在头顶显示「F 对话」这种提示")]
    public bool 显示靠近提示 = true;

    [Tooltip("提示相对物件顶部再抬高多少（米）")]
    public float 提示抬高 = 0.6f;

    [Header("界面")]
    [Tooltip("留空则由 StationInteractor 用空白幕布代替（UI 还没设计）")]
    public GameObject 界面预制体;

    /// <summary>界面标题</summary>
    public string 标题 => string.IsNullOrEmpty(显示名) ? 默认名(类型) : 显示名;

    public static string 默认名(StationKind k)
    {
        switch (k)
        {
            case StationKind.修炼: return "修炼";
            case StationKind.炼丹: return "炼丹";
            case StationKind.炼器: return "炼器";
            case StationKind.布阵: return "布阵";
            case StationKind.对话: return "对话";
            default: return "交互";
        }
    }

    /// <summary>这件东西实际用哪个键（覆盖优先，否则用玩家身上的全局键）</summary>
    public KeyCode 取按键(KeyCode 全局键) => 交互键覆盖 == KeyCode.None ? 全局键 : 交互键覆盖;

    /// <summary>把按键翻译成提示里显示的字（F / 右键 / 空格…）</summary>
    public static string 按键名(KeyCode 键)
    {
        switch (键)
        {
            case KeyCode.F: return "F";
            case KeyCode.E: return "E";
            case KeyCode.Q: return "Q";
            case KeyCode.R: return "R";
            case KeyCode.Space: return "空格";
            case KeyCode.Return: return "回车";
            case KeyCode.Mouse0: return "左键";
            case KeyCode.Mouse1: return "右键";
            case KeyCode.Mouse2: return "中键";
            default: return 键.ToString();
        }
    }

    /// <summary>玩家在不在这台的交互范围内</summary>
    public bool 玩家在范围内(float 玩家距离, float 高度差)
    {
        return 玩家距离 <= Mathf.Max(0.1f, 交互距离) && Mathf.Abs(高度差) <= 最大高度差;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.4f, 0.9f, 1f, 0.6f);
        Gizmos.DrawWireSphere(transform.position, 交互距离);
    }

    // ---- ASCII 别名 ----
    public StationKind Kind => 类型;
    public string Title => 标题;
    public KeyCode OverrideKey => 交互键覆盖;
}

