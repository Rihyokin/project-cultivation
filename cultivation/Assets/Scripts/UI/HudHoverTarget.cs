using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// 挂在 HUD 的格子上，把鼠标进入 / 离开转给 <see cref="PlayerHud"/>。
///
/// 注意：事件要能触发，需要满足三件事
///   · HUD 的 Canvas 上有 <c>GraphicRaycaster</c>
///   · 场景里有 <c>EventSystem</c>（3C_Testbed 里已有）
///   · 格子里至少有一个 <c>raycastTarget = true</c> 的 Image（用的是「底」）
///
/// 这个组件挂在格子根节点上就行 —— uGUI 会从命中的图沿父级往上找处理器。
/// </summary>
public class HudHoverTarget : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [Tooltip("目标 HUD，留空自动往父级找")]
    public PlayerHud hud;

    [Tooltip("0~5 = 主动技能槽位；-1 = 功法图标")]
    public int 槽位 = -1;

    void Awake()
    {
        if (hud == null) hud = GetComponentInParent<PlayerHud>();
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (hud != null) hud.显示信息(槽位);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (hud != null) hud.隐藏信息();
    }
}
