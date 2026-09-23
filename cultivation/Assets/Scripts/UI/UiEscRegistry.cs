using UnityEngine;

/// <summary>
/// 全屏界面的 ESC 协调器。
///
/// ============================================================
/// 要解决的问题
/// ============================================================
/// 现在有三个界面都用 ESC：
///   · 角色面板（I 键开，ESC 关）
///   · 设施界面（右键开，ESC 关）
///   · 暂停菜单（ESC 开，ESC 关）
///
/// 它们各自在自己的 Update 里读 Input.GetKeyDown(Escape)。
/// 而【同一帧里多个脚本的 Update 执行顺序是不保证的】，于是：
///   · 一次 ESC 既关了面板、又弹了暂停菜单
///   · 更糟的是暂停菜单记下的"原来的 timeScale"正好是面板设的 0，
///     退出暂停时恢复成 0 —— 游戏永远卡在暂停
///
/// ============================================================
/// 做法
/// ============================================================
/// 任何界面关闭时都来这里记一笔时间；暂停菜单开之前先问"是不是刚关过"。
/// 用时间戳而不是布尔标记，是因为布尔标记又会变成"谁先跑谁赢"。
/// 时间用 unscaledTime —— 暂停菜单会把 timeScale 设成 0，普通时间会冻住。
/// </summary>
public static class UiEscRegistry
{
    /// <summary>最近一次【任何全屏界面】关闭的时刻</summary>
    public static float LastCloseTime { get; private set; } = -99f;

    /// <summary>窗口内（秒）视为"刚刚才关过"</summary>
    public const float Window = 0.15f;

    /// <summary>界面关闭时调一下</summary>
    public static void NotifyClosed()
    {
        LastCloseTime = Time.unscaledTime;
    }

    /// <summary>是不是刚刚才关过界面（刚关过的话，这一下 ESC 不该再被别处消费）</summary>
    public static bool JustClosed(float window = Window)
    {
        return Time.unscaledTime - LastCloseTime < window;
    }

    /// <summary>现在有没有别的全屏界面开着（除暂停菜单外）</summary>
    public static bool AnyOtherUiOpen()
    {
        // 设施界面：静态标记最快
        if (StationInteractor.有界面打开) return true;

        // 角色面板：查一下实例
        foreach (var p in Object.FindObjectsOfType<CharacterPanelUI>())
            if (p != null && p.IsOpen) return true;

        return false;
    }

    // ---- 中文成员别名（项目约定：类名 ASCII，成员可中文）----
    public static float 最后关闭时间 => LastCloseTime;
    public static void 记录关闭() => NotifyClosed();
    public static bool 刚关闭过(float 窗口 = Window) => JustClosed(窗口);
    public static bool 有别的界面开着() => AnyOtherUiOpen();
}
