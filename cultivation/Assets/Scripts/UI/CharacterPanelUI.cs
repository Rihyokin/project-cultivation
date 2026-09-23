using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 角色面板的页签，顺序与概念图左侧一致：
/// **背包 / 境界 / 神通 / 法宝 / 灵阵 / 战阵 / 坐骑**。
/// </summary>
public enum CharacterTab
{
    背包 = 0,
    境界 = 1,
    神通 = 2,
    法宝 = 3,
    灵阵 = 4,
    战阵 = 5,
    坐骑 = 6,
}

/// <summary>一个页签按钮与它对应的页面。</summary>
[Serializable]
public class TabBinding
{
    [Tooltip("页签按钮")]
    public Button button;

    [Tooltip("该页签对应的整页根节点")]
    public GameObject page;

    [Tooltip("按钮底板，用于切换选中态颜色")]
    public Image background;
}

/// <summary>
/// 角色面板主控。I 键唤出/收起，默认落在「境界」页；Esc 关闭。
/// 挂在与 Canvas 同级的 UI 根节点上。
/// </summary>
public class CharacterPanelUI : MonoBehaviour
{
    [Header("面板")]
    [Tooltip("整个面板的根节点。它会随开关显隐")]
    public GameObject panelRoot;

    [Tooltip("打开面板时暂停游戏（Time.timeScale = 0），方便玩家从容操作")]
    public bool pauseGameWhenOpen = true;

    [Tooltip("唤出/收起按键")]
    public KeyCode toggleKey = KeyCode.I;

    [Tooltip("关闭按键")]
    public KeyCode closeKey = KeyCode.Escape;

    [Tooltip("打开时是否显示鼠标（2.5D 游戏需要）")]
    public bool showCursorWhenOpen = true;

    [Tooltip("启动时是否处于关闭状态")]
    public bool startClosed = true;

    [Header("页签（顺序需与左侧一致）")]
    public List<TabBinding> tabs = new List<TabBinding>();

    [Header("默认页")]
    public CharacterTab defaultTab = CharacterTab.境界;

    /// <summary>当前是否展开</summary>
    public bool IsOpen { get; private set; }

    /// <summary>当前页签</summary>
    public CharacterTab CurrentTab { get; private set; }

    /// <summary>页签切换事件（参数为新页签）</summary>
    public event Action<CharacterTab> TabChanged;

    // 暂停前的时间缩放，关闭时原样恢复
    float cachedTimeScale = 1f;
    bool timeScaleCached;

    void Awake()
    {
        for (int i = 0; i < tabs.Count; i++)
        {
            int index = i;   // 闭包捕获
            if (tabs[i].button != null)
                tabs[i].button.onClick.AddListener(() => ShowTab((CharacterTab)index));
        }
    }

    void Start()
    {
        SetOpen(!startClosed, true);
    }

    void Update()
    {
        if (Input.GetKeyDown(toggleKey))
            SetOpen(!IsOpen);

        if (IsOpen && Input.GetKeyDown(closeKey))
            SetOpen(false);
    }

    /// <summary>开关面板</summary>
    public void SetOpen(bool open, bool snap = false)
    {
        IsOpen = open;

        if (panelRoot != null) panelRoot.SetActive(open);

        if (open)
        {
            ShowTab(defaultTab);
            if (showCursorWhenOpen)
            {
                Cursor.visible = true;
                Cursor.lockState = CursorLockMode.None;
            }
            if (pauseGameWhenOpen && !timeScaleCached)
            {
                cachedTimeScale = Time.timeScale;
                timeScaleCached = true;
                Time.timeScale = 0f;
            }
        }
        else
        {
            RestoreTimeScale();
            UiEscRegistry.记录关闭();     // 告诉别人"我刚关过"，这一下 ESC 别再被暂停菜单消费
        }
    }

    /// <summary>恢复暂停前的时间缩放。关闭面板、组件被禁用或销毁时都会调用</summary>
    public void RestoreTimeScale()
    {
        if (!timeScaleCached) return;
        Time.timeScale = cachedTimeScale;
        timeScaleCached = false;
    }

    void OnDisable()
    {
        RestoreTimeScale();
    }

    void OnDestroy()
    {
        RestoreTimeScale();
    }

    /// <summary>切换到指定页签</summary>
    public void ShowTab(CharacterTab tab)
    {
        CurrentTab = tab;
        int active = (int)tab;

        for (int i = 0; i < tabs.Count; i++)
        {
            var b = tabs[i];
            if (b.page != null) b.page.SetActive(i == active);
            if (b.background != null)
                b.background.color = (i == active) ? UIBuildUtils.ColorTabActive : UIBuildUtils.ColorTabNormal;
        }

        TabChanged?.Invoke(tab);
    }

    /// <summary>按索引切换（给 UnityEvent 用）</summary>
    public void ShowTabByIndex(int index)
    {
        ShowTab((CharacterTab)Mathf.Clamp(index, 0, tabs.Count - 1));
    }
}
