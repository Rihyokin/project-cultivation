using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

/// <summary>
/// 按【当前功法】与【启用中的被动神通】动态装卸玩家身上的能力组件。
///
/// 规则（按策划说明）：
///   · 太虚炼气诀 → basic_sword_01 → 装 BasicSword01；换功法就卸掉旧的、装上新的
///   · 凭虚御风启用 → 装 YufengFlight + YufengVfx；停用就卸掉，重新启用再装上
///
/// 组件按【类名】配置，运行时用反射找类型 —— 这样加新功法/新神通只要在表里加一行，
/// 不用改代码。（Unity 不能序列化 System.Type，所以只能存字符串。）
/// </summary>
public class PlayerAbilityLoader : MonoBehaviour
{
    [Serializable]
    public class 普攻方法绑定
    {
        [Tooltip("功法表里「普攻方法id」的值")]
        public string 方法id = "";
        [Tooltip("提供这个普攻的组件类名")]
        public string 组件类名 = "";
    }

    [Serializable]
    public class 被动神通绑定
    {
        [Tooltip("被动神通表里的 神通id")]
        public string 神通id = "";
        [Tooltip("启用这个被动时要挂上的组件类名，可以多个")]
        public string[] 组件类名 = new string[0];
    }

    [Header("数据源")]
    [Tooltip("角色面板数据（功法 + 被动神通启用状态都从这里读）。留空则自动找 CharacterUI 上的")]
    public UIPanelData 面板数据;

    [Header("配置表")]
    public List<普攻方法绑定> 普攻方法表 = new List<普攻方法绑定>();
    public List<被动神通绑定> 被动神通表 = new List<被动神通绑定>();

    [Header("调试")]
    [Tooltip("装卸组件时打日志")]
    public bool 打印装卸日志 = true;

    // 当前由本组件负责装载出来的能力组件（只记录本组件装的，不碰手工挂的）
    readonly Dictionary<string, Component> 已装载 = new Dictionary<string, Component>();

    void Awake()
    {
        if (面板数据 == null)
        {
            var ui = GameObject.Find("CharacterUI");
            if (ui != null) 面板数据 = ui.GetComponent<UIPanelData>();
        }
        if (面板数据 == null) 面板数据 = FindObjectOfType<UIPanelData>();
    }

    void OnEnable()
    {
        if (面板数据 != null) 面板数据.Changed += Refresh;
        Refresh();
    }

    void OnDisable()
    {
        if (面板数据 != null) 面板数据.Changed -= Refresh;
    }

    /// <summary>重算需要哪些能力组件，装缺的、卸多的</summary>
    public void Refresh()
    {
        if (面板数据 == null) return;

        var 需要 = new HashSet<string>();

        // ---- 功法提供的普攻方法 ----
        var 功法 = 面板数据.当前功法;
        if (功法 != null && !string.IsNullOrEmpty(功法.普攻方法id))
        {
            foreach (var b in 普攻方法表)
                if (b != null && b.方法id == 功法.普攻方法id && !string.IsNullOrEmpty(b.组件类名))
                    需要.Add(b.组件类名);
        }

        // ---- 启用中的被动神通 ----
        if (面板数据.神通 != null)
        {
            foreach (var a in 面板数据.神通)
            {
                var 被动 = a as PassiveDivineAbility;
                if (被动 == null || !面板数据.IsPassiveEnabled(被动)) continue;

                foreach (var b in 被动神通表)
                {
                    if (b == null || b.神通id != 被动.神通id) continue;
                    foreach (var n in b.组件类名)
                        if (!string.IsNullOrEmpty(n)) 需要.Add(n);
                }
            }
        }

        // ---- 卸载不再需要的 ----
        var 待卸载 = new List<string>();
        foreach (var kv in 已装载)
            if (!需要.Contains(kv.Key)) 待卸载.Add(kv.Key);

        // 分两遍卸：组件之间可能有 RequireComponent 依赖，被依赖的要后卸。
        // 第一遍卸得掉的先卸，卸不掉的留到第二遍 —— 这样不用手工维护装卸顺序。
        var 剩余 = new List<string>(待卸载);
        for (int pass = 0; pass < 2 && 剩余.Count > 0; pass++)
        {
            var 下一轮 = new List<string>();
            foreach (var name in 剩余)
            {
                var c = 已装载[name];
                if (c == null) { 已装载.Remove(name); continue; }

                // 御风组件卸载前先落地，免得角色卡在半空
                var yf = c as YufengFlight;
                if (yf != null) yf.强制落地();

                if (TryDestroy(c))
                {
                    已装载.Remove(name);
                    if (打印装卸日志) Debug.Log("[PlayerAbilityLoader] 卸载 " + name, this);
                }
                else 下一轮.Add(name);
            }
            剩余 = 下一轮;
        }
        foreach (var name in 剩余)
            Debug.LogWarning("[PlayerAbilityLoader] 卸不掉 " + name
                             + "：可能有别的组件用 RequireComponent 依赖它，检查一下依赖声明", this);

        // ---- 装载还缺的 ----
        foreach (var name in 需要)
        {
            if (已装载.ContainsKey(name)) continue;

            var type = FindType(name);
            if (type == null) { Debug.LogWarning("[PlayerAbilityLoader] 找不到组件类型 " + name, this); continue; }
            if (GetComponent(type) != null) continue;      // 已经手工挂着了，不重复装

            var c = gameObject.AddComponent(type);
            已装载[name] = c;
            if (打印装卸日志) Debug.Log("[PlayerAbilityLoader] 装载 " + name, this);
        }
    }

    /// <summary>尝试销毁组件。有别的组件 RequireComponent 依赖它时 Unity 会拒绝，这里返回 false。</summary>
    static bool TryDestroy(Component c)
    {
        try
        {
            if (Application.isPlaying) Destroy(c); else DestroyImmediate(c);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    static Type FindType(string className)
    {
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            var t = asm.GetType(className);
            if (t != null) return t;
            // 也允许只写短名
            foreach (var t2 in asm.GetTypes())
                if (t2.Name == className) return t2;
        }
        return null;
    }

    /// <summary>当前已装载的能力组件类名（调试用）</summary>
    public List<string> 已装载列表
    {
        get
        {
            var l = new List<string>();
            foreach (var kv in 已装载) if (kv.Value != null) l.Add(kv.Key);
            return l;
        }
    }

    // ---- ASCII 别名 ----
    public void Reload() => Refresh();
    public List<string> LoadedTypes => 已装载列表;
}
