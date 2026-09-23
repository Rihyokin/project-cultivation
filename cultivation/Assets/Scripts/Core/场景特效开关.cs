using UnityEngine;

/// <summary>
/// 【场景特效开关表】把「建筑透明（OcclusionTransparency）」与「玩家描边（OcclusionOutline）」
/// 这两个**场景私有**的特效，抽成一张"哪个场景生效"的表。
///
/// 为什么需要它：这两个组件本来是"哪个场景要就挂在哪个场景"，但通用装配同步（§36）
/// 会重建 Player / Main Camera → 场景私有的组件会被一起冲掉
/// （实测：Sect 的玩家描边就是这么丢的 ✗）。
///
/// 有了表之后：**组件可以跟着通用装配铺到所有场景**，在 Awake 里查表决定自己关不关 —— 
/// 从"靠场景里有没有这个组件"变成"靠表里有没有这一行"，一处改、三处一致 ✓
///
/// 表资产放 `Assets/resources/场景特效开关.asset`（没建资产时用下面的内置默认）。
/// </summary>
[CreateAssetMenu(fileName = "场景特效开关", menuName = "修仙/场景特效开关表")]
public class 场景特效开关 : ScriptableObject
{
    [System.Serializable]
    public class 条目
    {
        [Tooltip("场景名（不带路径/扩展名），例如 Sect")]
        public string 场景 = "";
        [Tooltip("建筑透明：相机与玩家连线上的建筑变透明")]
        public bool 建筑透明 = false;
        [Tooltip("玩家描边：被挡住时给玩家描边")]
        public bool 玩家描边 = false;
    }

    [Tooltip("每个游玩场景一行")]
    public 条目[] 表 = new 条目[0];

    /// <summary>取表：优先 Resources 里的资产，没有就用内置默认</summary>
    public static 场景特效开关 取表()
    {
        var 资产 = Resources.Load<场景特效开关>("场景特效开关");
        if (资产 != null) return 资产;

        var 默 = CreateInstance<场景特效开关>();
        默.表 = new[]
        {
            new 条目 { 场景 = "Demon-Suppressing Tower", 建筑透明 = true,  玩家描边 = false },
            new 条目 { 场景 = "Sect",                     建筑透明 = false, 玩家描边 = true  },
            new 条目 { 场景 = "3C_Testbed",               建筑透明 = false, 玩家描边 = false },
        };
        return 默;
    }

    /// <summary>某个场景里，这个特效该不该生效。表里没有这一行 → 不生效。</summary>
    public static bool 生效(string 场景名, bool 问的是建筑透明)
    {
        var 表 = 取表();
        if (表 == null || 表.表 == null) return false;
        foreach (var e in 表.表)
            if (e != null && e.场景 == 场景名)
                return 问的是建筑透明 ? e.建筑透明 : e.玩家描边;
        return false;
    }

    /// <summary>给组件在 Awake 里用：不在名单里就把自己关掉，并说明原因</summary>
    public static bool 自己该生效(bool 问的是建筑透明, Object 谁)
    {
        string 场景 = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        bool 该 = 生效(场景, 问的是建筑透明);
        if (!该) Debug.Log("[场景特效开关] 「" + (问的是建筑透明 ? "建筑透明" : "玩家描边")
            + "」在场景「" + 场景 + "」不生效（查 场景特效开关表）→ 关闭组件", 谁);
        return 该;
    }
}
