using System;
using UnityEngine;

/// <summary>
/// 全局的「选中 / 锁定」管理器。挂在 Player 上。
///
/// 语义区分（按策划说明）：
///   · 选中：鼠标左键点 NPC。用于查看信息、执行任务。脚底显示【绿色圆环】。
///   · 锁定：鼠标右键点 NPC。用于攻击。脚底显示【红色圆环】，头顶显示血条。
///
/// 注意：**鼠标直接右键锁定的目标不受好感度限制**（好感度 &gt; 0 也能锁）。
///      「好感度 &lt; 0」只用于战斗中的【自动寻找下一个目标】。
/// </summary>
public class NpcTargeting : MonoBehaviour
{
    [Header("输入")]
    [Tooltip("选中键")]
    public KeyCode 选中键 = KeyCode.Mouse0;

    [Tooltip("锁定键")]
    public KeyCode 锁定键 = KeyCode.Mouse1;

    [Tooltip("射线检测层级")]
    public LayerMask 射线层 = ~0;

    [Tooltip("最远拾取距离")]
    public float 最远距离 = 200f;

    [Header("引用")]
    [Tooltip("用于发射鼠标射线的相机。留空则取 Camera.main")]
    public Camera 射线相机;

    /// <summary>当前选中的 NPC（绿环）</summary>
    public NpcInstance Selected { get; private set; }

    /// <summary>当前锁定的 NPC（红环 + 血条）</summary>
    public NpcInstance Locked { get; private set; }

    /// <summary>选中发生变化</summary>
    public event Action<NpcInstance> SelectionChanged;

    /// <summary>锁定发生变化。第二个参数表示是否由玩家鼠标点击触发</summary>
    public event Action<NpcInstance, bool> LockChanged;

    void Awake()
    {
        if (射线相机 == null) 射线相机 = Camera.main;
    }

    void Update()
    {
        if (射线相机 == null) { 射线相机 = Camera.main; if (射线相机 == null) return; }

        // 有全屏界面（设施界面等）开着时，鼠标不该穿透到场景里。
        // 否则站在炼丹炉前开着界面，右键会连建筑一起点，又开一个新界面。
        if (StationInteractor.有界面打开) return;

        if (Input.GetKeyDown(选中键))
        {
            // 【点空白 = 取消选中】以前只有"点到 NPC 才做事"，点空地什么都不发生，
            // 选中的绿环就撤不掉（只有右键能撤锁定）。现在点空地也走一遍：
            // RaycastNpc() 返回 null → Select(null) → 绿环消失、SelectionChanged 发 null。
            //
            // 点在可点的 UI（技能格子 / 面板按钮）上时不算"世界里的点击" ——
            // HUD 是 ScreenSpaceOverlay，Physics.Raycast 打不到它（见开发注意事项 9.5），
            // 不挡一下的话点技能格子会顺手把选中也取消掉。
            if (!点在界面上()) 点击选中();
        }

        if (Input.GetKeyDown(锁定键))
        {
            // 站在设施（修炼房屋/炼丹炉/炼器炉/布阵点）旁边按右键时，
            // 这一下是"开界面"而不是"锁定敌人" —— 否则想开炼丹炉结果锁了个怪。
            //
            // 【为什么不靠 StationInteractor 设的静态标记】
            // 那依赖两个脚本的 Update 执行顺序，顺序不定就会偶发失效。
            // 这里自己查一遍，和顺序无关，稳定。
            if (附近有设施()) return;

            var npc = RaycastNpc();
            if (npc != null) Lock(npc, byPlayerClick: true);
            else ClearLock();          // 右键点在空白处 → 取消锁定（红环与血条一并消失）
        }
    }

    /// <summary>
    /// 鼠标左键那一下：射到 NPC 就选中它，射到**空白处就取消选中**。
    /// 单独抽出来是为了能直接验证（不用伪造鼠标输入）。
    /// </summary>
    public void 点击选中()
    {
        Select(RaycastNpc());
    }

    /// <summary>鼠标是不是压在可点的 UI 上（HUD 按钮 / 面板）</summary>
    static bool 点在界面上()
    {
        var es = UnityEngine.EventSystems.EventSystem.current;
        return es != null && es.IsPointerOverGameObject();
    }

    /// <summary>玩家身边有没有可交互的设施（有的话右键要留给它）</summary>
    bool 附近有设施()
    {
        foreach (var s in FindObjectsOfType<StationInteractable>())
        {
            if (s == null || !s.isActiveAndEnabled) continue;
            var 差 = s.transform.position - transform.position;
            float 水平 = new Vector2(差.x, 差.z).magnitude;
            if (s.玩家在范围内(水平, 差.y)) return true;
        }
        return false;
    }

    /// <summary>鼠标位置射到的 NPC（没有则 null）</summary>
    public NpcInstance RaycastNpc()
    {
        if (射线相机 == null) return null;
        var ray = 射线相机.ScreenPointToRay(Input.mousePosition);
        if (!Physics.Raycast(ray, out var hit, 最远距离, 射线层)) return null;
        return hit.collider.GetComponentInParent<NpcInstance>();
    }

    /// <summary>选中某个 NPC（绿环）</summary>
    public void Select(NpcInstance npc)
    {
        if (Selected == npc) return;

        if (Selected != null)
        {
            var old = IndicatorOf(Selected);
            if (old != null) old.SetSelected(false);
        }

        Selected = npc;

        if (Selected != null)
        {
            var cur = IndicatorOf(Selected);
            if (cur != null) cur.SetSelected(true);
        }

        SelectionChanged?.Invoke(Selected);
    }

    /// <summary>
    /// 锁定某个 NPC（红环 + 血条）。
    /// 加上 byPlayerClick 语义只是为了区分「玩家手动点的」和「系统自动找的」，
    /// 两者在锁定条件上本来就不同：手动点不看好感度，自动找要求好感度 &lt; 0。
    /// </summary>
    public void Lock(NpcInstance npc, bool byPlayerClick = false)
    {
        if (Locked == npc) return;

        if (Locked != null)
        {
            var old = IndicatorOf(Locked);
            if (old != null) old.SetLocked(false);
        }

        Locked = npc;

        if (Locked != null)
        {
            var cur = IndicatorOf(Locked);
            if (cur != null) cur.SetLocked(true);
        }

        LockChanged?.Invoke(Locked, byPlayerClick);
    }

    /// <summary>解除锁定</summary>
    public void ClearLock()
    {
        Lock(null);
    }

    /// <summary>解除选中</summary>
    public void ClearSelection()
    {
        Select(null);
    }

    static NpcIndicator IndicatorOf(NpcInstance npc)
    {
        if (npc == null) return null;
        var ind = npc.GetComponent<NpcIndicator>();
        if (ind == null) ind = npc.gameObject.AddComponent<NpcIndicator>();
        return ind;
    }

    // ============================================================
    //  ASCII 公开接口（内部中文命名，对外统一 ASCII，见项目约定）
    // ============================================================
    public NpcInstance SelectedNpc => Selected;
    public NpcInstance LockedNpc => Locked;
    public void SelectNpc(NpcInstance npc) => Select(npc);
    public void LockNpc(NpcInstance npc, bool byPlayerClick = true) => Lock(npc, byPlayerClick);
    public NpcInstance RaycastNpcUnderMouse() => RaycastNpc();
}
