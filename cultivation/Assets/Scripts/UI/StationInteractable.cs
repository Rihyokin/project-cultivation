using UnityEngine;

/// <summary>
/// 可交互设施（修炼房屋 / 炼丹炉 / 炼器炉 / 布阵点）。
///
/// 挂在这四个物件上，配合玩家身上的 StationInteractor 使用：
///   玩家靠近 + 右键 → 打开对应的界面。
///
/// 这里只负责"我是谁、我多大、玩家要离多近"，具体开界面在 StationInteractor。
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
    }

    [Header("身份")]
    public StationKind 类型 = StationKind.修炼;

    [Tooltip("界面上显示的名字。留空则按类型自动取")]
    public string 显示名 = "";

    [Header("交互")]
    [Tooltip("玩家离这么近才能交互（米）。按物件大小调")]
    public float 交互距离 = 4f;

    [Tooltip("只有玩家在这个高度差以内才允许交互，防止隔层楼点到")]
    public float 最大高度差 = 3f;

    [Header("提示")]
    [Tooltip("靠近时是否在物件头顶显示「右键打开XX」的提示")]
    public bool 显示靠近提示 = true;

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
            default: return "交互";
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
}
