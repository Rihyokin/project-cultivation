using UnityEngine;

/// <summary>
/// **玩家外观** —— 换外观 = **只换蒙皮网格和材质**，不换人。
///
/// 实测结论（2026-09-27，用户提醒后确认）：
/// 「成男村民」和玩家两套模型的**骨骼完全一样** ——
/// 节点数都是 81、路径除根名外逐段相同（`…/Armature/BoneRoot/Hip/Pelvis/L_Thigh/…`）、
/// 两个 `SkinnedMeshRenderer` 的 **78 根骨骼同名同序**。所以：
///
///   · **不换 `Player_Visual`**、**不换 Animator**、**不动朝向补偿**、**不做动画状态同步**；
///   · 只把玩家自己那个 `SkinnedMeshRenderer` 的 `sharedMesh` 和 `sharedMaterials` 换成外观的
///     —— 因为骨骼一模一样，新网格挂在同一套骨骼上，动画/朝向/物理全都天然正确 ✓
///
/// 之前那版"整套 Player_Visual 换掉"的复杂度（缓存引用、死亡流程指向、动画桥接）**全都不需要了**。
///
/// 外观怎么来：由 `AppearanceDefinition` 给（模型资源路径 → 预制体 → 取它的 SkinnedMeshRenderer）。
/// 主线挂钩：外观自带 `默认拥有` / `解锁标记`，任务完成写的标记一上就自动换上（任务侧零代码）。
/// </summary>
[DisallowMultipleComponent]
public class 玩家外观 : MonoBehaviour
{
    [Tooltip("玩家模型根（找它的 SkinnedMeshRenderer）。留空自动找名为 Player_Visual 的子物件")]
    public Transform 玩家视觉;

    [Tooltip("【调试/预览用】直接看当前穿的是哪件")]
    public AppearanceDefinition 当前外观;

    [Header("已拥有（靠物品获得，比如「门派便服」）")]
    [Tooltip("已经拿到手的外观。**用户 2026-09-27**：不靠标记解锁，而是任务发道具、道具使用后获得。\n" +
             "以后存进存档就按这个列表的 id 存")]
    public System.Collections.Generic.List<AppearanceDefinition> 已获得
        = new System.Collections.Generic.List<AppearanceDefinition>();

    /// <summary>这件外观现在拥有吗（默认拥有 或 已经拿到手）</summary>
    public bool 已拥有(AppearanceDefinition a)
    {
        if (a == null) return false;
        if (a.默认拥有) return true;
        return 已获得 != null && 已获得.Contains(a);
    }

    /// <summary>
    /// 获得一件外观（已经是就不重复加），默认**立刻换上**。
    /// 由 <see cref="学外观效果"/> 在玩家使用道具时调用。
    /// </summary>
    public bool 获得(AppearanceDefinition 外观, bool 立刻装备 = true)
    {
        if (外观 == null) return false;
        if (已获得 == null) 已获得 = new System.Collections.Generic.List<AppearanceDefinition>();

        bool 新的 = !已拥有(外观);
        if (新的 && !外观.默认拥有) 已获得.Add(外观);
        if (立刻装备) 装备(外观);
        Debug.Log("[外观] 获得「" + 外观.DisplayName + "」" + (新的 ? "" : "（早就有了，不重复给）"), 外观);
        return 新的;
    }

    /// <summary>外观换了（参数是新外观，null = 换回原始网格）</summary>
    public static event System.Action<AppearanceDefinition> 外观变化;

    AppearanceDatabase 库;
    SkinnedMeshRenderer 网格;
    Mesh 原网格;
    Material[] 原材质;

    void Awake()
    {
        if (玩家视觉 == null)
        {
            var t = transform.Find("Player_Visual");
            玩家视觉 = t != null ? t : transform;
        }
        网格 = 玩家视觉 != null ? 玩家视觉.GetComponentInChildren<SkinnedMeshRenderer>(true) : null;
        if (网格 != null)
        {
            原网格 = 网格.sharedMesh;
            原材质 = 网格.sharedMaterials;
        }
        else Debug.LogWarning("[外观] 在 " + (玩家视觉 != null ? 玩家视觉.name : "?") + " 底下找不到 SkinnedMeshRenderer，换外观会没反应", this);
    }

    void Start()
    {
        库 = AppearanceDatabase.取();
        对话标记.变化 += 处理标记变化;
        按规则选一件();
    }

    void OnDestroy() { 对话标记.变化 -= 处理标记变化; }

    void 处理标记变化(string 标记, bool 新增)
    {
        if (!新增) return;
        var 该穿 = 按规则挑();
        if (该穿 != null && 该穿 != 当前外观) 装备(该穿);
    }

    /// <summary>按「默认拥有 + 解锁标记」挑一件：已解锁且“解锁即装备”的最后一件优先（表里越靠后越高级）</summary>
    AppearanceDefinition 按规则挑()
    {
        if (库 == null) return null;
        AppearanceDefinition 缺省 = null, 解锁的 = null;
        for (int i = 0; i < 库.全部.Count; i++)
        {
            var a = 库.全部[i];
            if (a == null) continue;
            if (a.默认拥有 && 缺省 == null) 缺省 = a;
            if (a.已解锁 && a.解锁即装备) 解锁的 = a;
        }
        return 解锁的 != null ? 解锁的 : 缺省;
    }

    void 按规则选一件() => 装备(按规则挑());

    /// <summary>换上某件外观（传 null = 换回原始网格）</summary>
    public bool 装备(AppearanceDefinition 外观)
    {
        if (外观 == null) { 还原(); return false; }
        if (网格 == null) return false;
        if (当前外观 == 外观 && 网格.sharedMesh != 原网格) return true;

        var 源 = 外观.取模型();
        var 源网格 = 源 != null ? 源.GetComponentInChildren<SkinnedMeshRenderer>(true) : null;
        if (源网格 == null || 源网格.sharedMesh == null)
        {
            Debug.LogWarning("[外观] 「" + 外观.DisplayName + "」没有可用的蒙皮网格（模型=" + (源 != null ? 源.name : "取不到") + "）", 外观);
            return false;
        }

        网格.sharedMesh = 源网格.sharedMesh;
        网格.sharedMaterials = 源网格.sharedMaterials;
        当前外观 = 外观;
        Debug.Log("[外观] 换上「" + 外观.DisplayName + "」（网格 " + 源网格.sharedMesh.name + "）", 外观);
        外观变化?.Invoke(外观);
        return true;
    }

    /// <summary>换回原始的玩家网格</summary>
    public void 还原()
    {
        if (网格 != null && 原网格 != null)
        {
            网格.sharedMesh = 原网格;
            网格.sharedMaterials = 原材质;
        }
        当前外观 = null;
        外观变化?.Invoke(null);
    }

    // ---- ASCII 别名 ----
    public AppearanceDefinition CurrentAppearance => 当前外观;
    public bool Equip(AppearanceDefinition a) => 装备(a);
    public void RestoreOriginal() => 还原();
}
