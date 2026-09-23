using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 「主动技能装备（神通法宝灵阵共用）」的 6 格装备栏。
/// 概念图里三个页面都画了同一组六边形排布，所以做成一个可复用组件。
///
/// 格子本身是 <see cref="UIActiveSkillSlot"/> —— **它在自己的文件里**
/// （Unity 只认文件名同名的那一个类是主类，同文件里的其它 MonoBehaviour 会变成 Missing，见那边的注释）。
/// </summary>
public class UIActiveSkillBar : MonoBehaviour
{
    [Header("引用")]
    [Tooltip("已生成的 6 个格子，顺序即槽位顺序")]
    public List<UIActiveSkillSlot> slots = new List<UIActiveSkillSlot>();

    [Tooltip("数据源")]
    public UIPanelData data;

    [Tooltip("点击格子时把内容送到哪个信息栏。留空则不联动")]
    public UIEntryInfo infoTarget;

    void Awake()
    {
        for (int i = 0; i < slots.Count; i++)
        {
            int index = i;
            var slot = slots[i];
            if (slot == null) continue;

            if (slot.button != null)
                slot.button.onClick.AddListener(() => OnSlotClicked(index));

            if (slot.clearButton != null)
                slot.clearButton.onClick.AddListener(() => { slot.Clear(); Refresh(); });
        }
    }

    void OnEnable()
    {
        if (data != null) data.Changed += Refresh;
        Refresh();
    }

    void OnDisable()
    {
        if (data != null) data.Changed -= Refresh;
    }

    void Start() => Refresh();

    /// <summary>按数据源刷新 6 个格子</summary>
    public void Refresh()
    {
        bool pending = data != null && data.待装备神通 != null;

        for (int i = 0; i < slots.Count; i++)
        {
            if (slots[i] == null) continue;
            slots[i].index = i;
            if (data != null) slots[i].data = data;
            else if (slots[i].data == null) slots[i].data = GetComponentInParent<UIPanelData>(true);

            Object content = null;
            if (data != null && data.主动技能 != null && i < data.主动技能.Count)
                content = data.主动技能[i];
            slots[i].Bind(content, pending);
        }
    }

    /// <summary>
    /// 点格子：有待装备的神通就放入，否则单纯选中看信息。
    /// </summary>
    void OnSlotClicked(int index)
    {
        if (data != null && data.待装备神通 != null)
        {
            bool placed = data.HandleSlotClicked(index);   // 被占用时数据层会弹提示
            Refresh();
            if (placed && infoTarget != null)
                infoTarget.Show(data.主动技能[index] as IPanelEntry);
            return;
        }

        if (infoTarget == null) return;
        Object content = null;
        if (data != null && data.主动技能 != null && index < data.主动技能.Count)
            content = data.主动技能[index];
        infoTarget.Show(content as IPanelEntry);
    }
}
