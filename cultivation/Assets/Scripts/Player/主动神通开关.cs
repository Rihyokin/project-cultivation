using UnityEngine;

/// <summary>
/// **主动神通释放器的开关** —— 补上 <see cref="PlayerAbilityLoader"/> 没管的那一块。
///
/// 背景（用户 2026-09-27 确认）：主角改成"什么都要学了才有"的新档口径之后，
/// 普攻方法（功法提供）和被动神通都归 `PlayerAbilityLoader` 装卸，
/// **但主动神通的释放器 `ActiveSkillCaster` 一直是开着的** —— 明明什么都没装备，
/// 它仍然活着（虽然大多时候放不出东西，但口径不统一，而且它自己会做锁定/动画/特效那些事）。
///
/// 规则很简单：**主动技能槽里有东西才开，空槽就关**。
/// 挂在 Player 上即可；面板数据留空会自动找。
/// </summary>
[DisallowMultipleComponent]
public class 主动神通开关 : MonoBehaviour
{
    [Tooltip("角色面板数据。留空 = 自动找场景里的 UIPanelData")]
    public UIPanelData 面板;

    [Tooltip("要跟着开关的组件类名关键字（默认只关主动神通的释放器）")]
    public string 组件名关键字 = "ActiveSkillCaster";

    void Awake()
    {
        if (面板 == null) 面板 = FindObjectOfType<UIPanelData>();
        同步();
    }

    void OnEnable()
    {
        if (面板 != null) 面板.Changed += 同步;
    }

    void OnDisable()
    {
        if (面板 != null) 面板.Changed -= 同步;
    }

    void Update()
    {
        if (面板 == null) 面板 = FindObjectOfType<UIPanelData>();
        同步();
    }

    /// <summary>主动技能槽里有没有装备东西（空着就不该能放主动神通）</summary>
    public bool 有装备
    {
        get
        {
            if (面板 == null || 面板.主动技能 == null) return false;
            for (int i = 0; i < 面板.主动技能.Count; i++)
                if (面板.主动技能[i] != null) return true;
            return false;
        }
    }

    void 同步()
    {
        bool 开 = 有装备;
        foreach (var c in GetComponents<MonoBehaviour>())
        {
            if (c == null || c == this) continue;
            if (string.IsNullOrEmpty(组件名关键字)) continue;
            if (c.GetType().Name.Contains(组件名关键字) && c.enabled != 开) c.enabled = 开;
        }
    }

    // ---- ASCII 别名 ----
    public bool HasEquipped => 有装备;
}
