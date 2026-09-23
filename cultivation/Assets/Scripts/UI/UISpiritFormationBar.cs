using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 战阵的九宫格。和 <see cref="UIActiveSkillBar"/> 是同一套交互
/// （先点列表里的「上阵」，再点一个空格子），只是格子是 3×3。
///
/// **九格全部可以上阵**，同时最多上 <see cref="SpiritFormationLayout.可上阵数"/> 个；
/// 名额用完之后，剩下的空格由 <see cref="UISpiritSlot"/> 自己画成「已满」。
/// </summary>
public class UISpiritFormationBar : MonoBehaviour
{
    [Header("引用")]
    [Tooltip("已生成的 9 个格子，顺序 = 格子序号")]
    public List<UISpiritSlot> slots = new List<UISpiritSlot>();

    [Tooltip("数据源。留空则自动往 Canvas 上找")]
    public UIPanelData data;

    [Tooltip("点格子时把内容送到哪个信息栏。留空则不联动")]
    public UIEntryInfo infoTarget;

    void Awake()
    {
        for (int i = 0; i < slots.Count; i++)
        {
            int index = i;
            var slot = slots[i];
            if (slot == null) continue;

            if (slot.button != null) slot.button.onClick.AddListener(() => OnSlotClicked(index));
            if (slot.clearButton != null)
                slot.clearButton.onClick.AddListener(() => { slot.Clear(); Refresh(); });
        }
    }

    void OnEnable()
    {
        var d = ResolveData();
        if (d != null) d.Changed += Refresh;
        Refresh();
    }

    void OnDisable()
    {
        if (data != null) data.Changed -= Refresh;
    }

    public UIPanelData ResolveData()
    {
        if (data != null) return data;

        var canvas = GetComponentInParent<Canvas>(true);
        if (canvas != null)
        {
            data = canvas.GetComponentInParent<UIPanelData>();
            if (data == null) data = canvas.GetComponent<UIPanelData>();
        }
        return data;
    }

    /// <summary>按数据源刷新 9 个格子</summary>
    public void Refresh()
    {
        var d = ResolveData();
        bool pending = d != null && d.待上阵真灵 != null;
        bool 已满 = d != null && d.战阵已满;

        for (int i = 0; i < slots.Count; i++)
        {
            var slot = slots[i];
            if (slot == null) continue;

            slot.slot = i;
            if (d != null) slot.data = d;

            NpcDefinition spirit = null;
            if (d != null && d.战阵站位 != null && i < d.战阵站位.Count) spirit = d.战阵站位[i];
            slot.Bind(spirit, pending, 已满);
        }
    }

    void OnSlotClicked(int index)
    {
        var d = ResolveData();

        if (d != null && d.待上阵真灵 != null)
        {
            if (d.HandleSpiritSlotClicked(index))
            {
                Refresh();
                if (infoTarget != null && index < d.战阵站位.Count)
                    infoTarget.Show(d.战阵站位[index] as IPanelEntry);
            }
            return;
        }

        if (infoTarget == null || d == null) return;
        if (index < 0 || index >= d.战阵站位.Count) { infoTarget.Show(null); return; }
        infoTarget.Show(d.战阵站位[index] as IPanelEntry);
    }
}
