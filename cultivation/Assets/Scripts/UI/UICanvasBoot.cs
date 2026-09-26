using UnityEngine;

/// <summary>
/// **让 UI 画布在编辑器里默认不显示、运行时自动打开。**
///
/// 【为什么需要它】每个场景一打开，`HudCanvas` / `CharacterUI` / `暂停菜单` 这些
/// ScreenSpaceOverlay 画布就**铺在 Scene 视图和 Game 视图上**，摆场景时非常挡视线
/// （用户 2026-09-26：「每个场景中我在编辑时一开始各种 ui 和 hud 的面板是默认显示的，
/// 很不方便我编辑场景，让他们默认不显示先」）。
///
/// 【做法】把场景里那个 `Canvas` 组件的 **enabled 关掉**（不是关 GameObject！）：
///   · 编辑器里：画布不绘制 → Scene / Game 视图都干净 ✓
///   · 运行时：本组件在 `Awake()` 把它打开 → 玩家看到的东西一点没少 ✓
///
/// 【为什么不是 SetActive(false)】关 GameObject 会让 `GameObject.Find("CharacterUI")` **找不到它**
/// （Find 默认跳过未激活对象），而 `PlayerAbilityLoader` / `MountRider` 都是这么找
/// `UIPanelData` 的 → 直接坏掉 ✗✗。关 Canvas 组件则完全不影响查找与脚本执行。
///
/// 【只开自己这一个】只打开**同一个 GameObject 上**的 Canvas，不动子物体的画布 ——
/// 那些多半是各自面板（背包页/坐骑页…）自己的开关，不能被这里强行打开。
///
/// 【加进来就自动生效】`Reset()` 里顺手把画布关掉：这样任何生成器只要
/// `AddComponent&lt;UICanvasBoot&gt;()`，产出的画布就是"默认不显示"的 ✓
/// （`Reset` 只在编辑器加组件时调用，运行时不会）。
///
/// 想在编辑器里**临时看一眼 UI**：菜单 `修仙/编辑器里显示或隐藏 UI 画布`（只动 scene 的 enabled，不保存即可还原）。
/// </summary>
[DisallowMultipleComponent]
public class UICanvasBoot : MonoBehaviour
{
    [Tooltip("运行时(Awake)是否自动把本物体上的画布打开。一般保持勾选。")]
    public bool 运行时打开 = true;

    void Reset()
    {
        // 加组件的那一刻就把画布关掉 → 生成器产出的 UI 天然是"编辑器里不显示"
        var c = GetComponent<Canvas>();
        if (c != null) c.enabled = false;
    }

    void Awake()
    {
        if (!运行时打开) return;
        var c = GetComponent<Canvas>();
        if (c != null) c.enabled = true;   // 只开自己这一个，别管子物体
    }

    // ---- ASCII 别名 ----
    public bool EnableAtRuntime { get => 运行时打开; set => 运行时打开 = value; }
}
