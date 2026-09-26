using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// **跨场景数据接力**（主线需求第 ⑤ 项）。
///
/// 背景（查过项目现状）：切场景走 `Teleporter` → `LoadSceneMode.Single`，**旧场景里所有物体都被销毁** ✗，
/// 只有玩家靠一个 `DontDestroyOnLoad` 宿主跨过去 ✓。于是：
///   · 玩家身上的东西（`玩家外观` 的"已获得外观"、`演出锁`）**能活下来** ✓；
///   · **角色面板 `CharacterUI`（`UIPanelData`：背包 / 已学功法 / 已获得神通 / 主动技能槽 / 停用被动）会没** ✗
///     —— 新场景自带一份全新的、空的。
///
/// 做法：**不把 UI 物件挂 DontDestroyOnLoad**（那样新场景的 UI 绑定会全指向旧物件 ✗），
/// 而是**只把数据接力过去**：
///   · 旧场景销毁前（`OnDestroy`）把数据拍进一个静态快照；
///   · 新场景的角色面板起来时（`Awake`）从快照灌回去。
/// 这样新场景的 UI 引用天然是对的，数据也不丢 ✓。
///
/// 用法：把本组件挂到**角色面板那个物件**（挂着 `UIPanelData` 的，通常叫 `CharacterUI`）上即可。
/// 注意：字段用**引用**（ScriptableObject 资产）直接搬 —— 它们本身就是资产，跨场景完全合法 ✓。
/// </summary>
[DisallowMultipleComponent]
public class 跨场景数据 : MonoBehaviour
{
    [Tooltip("勾上：进游戏/读档时清掉快照（新档不该继承上一局的背包）")]
    public bool 新开局时清空 = false;

    [Tooltip("调试用：每次接力都打一条日志")]
    public bool 打日志 = true;

    void Awake()
    {
        var 面板 = GetComponent<UIPanelData>();
        if (面板 == null) { Debug.LogWarning("[跨场景] 这个物件上没有 UIPanelData，组件没起作用", this); return; }

        if (新开局时清空) { 快照.清空(); 快照.有数据 = false; }

        if (!快照.有数据) { if (打日志) Debug.Log("[跨场景] 没有快照：用本场景的初始数据（新开局/直接运行本场景）"); return; }

        快照.灌回(面板);
        if (打日志) Debug.Log("[跨场景] 已把上一场景的数据接过来：背包 " + 面板.物品.Count + " 件、已学功法 " + 面板.已学功法.Count
            + "、已获得主动神通 " + 面板.已获得主动神通.Count + "、已获得被动 " + 面板.已获得被动神通.Count);
    }

    void OnDestroy()
    {
        var 面板 = GetComponent<UIPanelData>();
        if (面板 == null) return;
        快照.拍下(面板);
        if (打日志) Debug.Log("[跨场景] 已把当前数据拍成快照：背包 " + 面板.物品.Count + " 件、已学功法 " + 面板.已学功法.Count
            + "、已获得主动神通 " + 面板.已获得主动神通.Count + "、已获得被动 " + 面板.已获得被动神通.Count);
    }

    // ================================================================ 静态快照
    //
    // 只搬"玩法数据"，不搬"场景配的展示数据"（比如 神通 全表、已获得真灵全表是导入器按表灌的，
    // 新场景自己会灌一份，搬过去反而会串味 ✗）。

    static class 快照
    {
        public static bool 有数据;

        public static List<ItemDefinition> 物品;
        public static List<GongFaDefinition> 已学功法;
        public static List<ActiveDivineAbility> 已获得主动;
        public static List<PassiveDivineAbility> 已获得被动;
        public static List<PassiveDivineAbility> 已停用被动;
        public static List<UnityEngine.Object> 主动技能;
        public static GongFaDefinition 当前功法;
        public static MountDefinition 当前坐骑;

        public static void 拍下(UIPanelData 面板)
        {
            面板.EnsureLists();
            物品 = new List<ItemDefinition>(面板.物品);
            已学功法 = new List<GongFaDefinition>(面板.已学功法);
            已获得主动 = new List<ActiveDivineAbility>(面板.已获得主动神通);
            已获得被动 = new List<PassiveDivineAbility>(面板.已获得被动神通);
            已停用被动 = new List<PassiveDivineAbility>(面板.已停用被动);
            主动技能 = new List<UnityEngine.Object>(面板.主动技能);
            当前功法 = 面板.当前功法;
            当前坐骑 = 面板.当前坐骑;
            有数据 = true;
        }

        public static void 灌回(UIPanelData 面板)
        {
            面板.EnsureLists();
            面板.物品 = new List<ItemDefinition>(物品);
            面板.已学功法 = new List<GongFaDefinition>(已学功法);
            面板.已获得主动神通 = new List<ActiveDivineAbility>(已获得主动);
            面板.已获得被动神通 = new List<PassiveDivineAbility>(已获得被动);
            面板.已停用被动 = new List<PassiveDivineAbility>(已停用被动);
            面板.主动技能 = new List<UnityEngine.Object>(主动技能);
            面板.当前功法 = 当前功法;
            面板.当前坐骑 = 当前坐骑;
            面板.RaiseChanged();
        }

        public static void 清空()
        {
            物品 = null; 已学功法 = null; 已获得主动 = null; 已获得被动 = null;
            已停用被动 = null; 主动技能 = null; 当前功法 = null; 当前坐骑 = null;
        }
    }

    // ---- ASCII 别名 ----
    public static void ClearSnapshot() { 快照.清空(); 快照.有数据 = false; }
    public static bool HasSnapshot => 快照.有数据;
}
