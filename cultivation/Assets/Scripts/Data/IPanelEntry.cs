using UnityEngine;

/// <summary>
/// 所有列表/详情面板通用的一行条目接口。
/// 背包 / 神通 / 法宝 / 灵阵 的列表与信息栏都按这个接口渲染，避免每套写一份 UI 代码。
/// </summary>
public interface IPanelEntry
{
    string DisplayName { get; }
    string DisplayDescription { get; }
    QualityTier DisplayTier { get; }
    Sprite DisplayIcon { get; }
}
