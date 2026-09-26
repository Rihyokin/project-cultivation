using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// 存档读写。每个槽位一个 JSON 文件，放在 Application.persistentDataPath/saves 下。
///
/// 用 JsonUtility 而不是自己拼字符串：字段增删时有版本号兜底，
/// 缺的字段会拿到默认值，不会因为旧存档少一个字段就整个读不出来。
/// </summary>
public static class SaveSystem
{
    /// <summary>存档槽位数。UI 上就是一排格子，改这里 UI 会自动跟着变</summary>
    public const int 槽位数 = 5;

    public static string 存档目录 => Path.Combine(Application.persistentDataPath, "saves");

    public static string 槽位路径(int 槽位) => Path.Combine(存档目录, "slot_" + 槽位 + ".json");

    // ---------------------------------------------------------------- 读写

    public static bool 存档(int 槽位, SaveData 数据)
    {
        if (数据 == null) return false;
        if (槽位 < 0 || 槽位 >= 槽位数) { Debug.LogWarning("[SaveSystem] 槽位越界 " + 槽位); return false; }

        try
        {
            Directory.CreateDirectory(存档目录);
            数据.版本 = SaveData.当前版本;
            数据.刷新时间戳();
            File.WriteAllText(槽位路径(槽位), JsonUtility.ToJson(数据, true));
            Debug.Log("[SaveSystem] 已存档槽位 " + 槽位 + "：" + 数据.角色名 + " / " + 数据.境界);
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError("[SaveSystem] 存档失败：" + e.Message);
            return false;
        }
    }

    /// <summary>读槽位。没有存档或读失败都返回 null</summary>
    public static SaveData 读档(int 槽位)
    {
        var p = 槽位路径(槽位);
        if (!File.Exists(p)) return null;

        try
        {
            var 数据 = JsonUtility.FromJson<SaveData>(File.ReadAllText(p));
            if (数据 == null) return null;
            if (数据.版本 > SaveData.当前版本)
                Debug.LogWarning("[SaveSystem] 槽位 " + 槽位 + " 的存档版本 " + 数据.版本 + " 比程序新，可能读不全");
            return 数据;
        }
        catch (Exception e)
        {
            Debug.LogError("[SaveSystem] 读档失败（槽位 " + 槽位 + "）：" + e.Message);
            return null;
        }
    }

    public static bool 有存档(int 槽位)
    {
        var 数据 = 读档(槽位);
        return 数据 != null && !数据.是空的;
    }

    public static bool 删档(int 槽位)
    {
        var p = 槽位路径(槽位);
        if (!File.Exists(p)) return false;
        try { File.Delete(p); return true; }
        catch (Exception e) { Debug.LogError("[SaveSystem] 删档失败：" + e.Message); return false; }
    }

    /// <summary>
    /// 删掉一个槽位，并让后面的存档【依次往前补位】，不留空档。
    ///
    /// 例：槽位 0/1/2 有档，删掉 0 → 原来的 1 变成 0、原来的 2 变成 1，最后空出来。
    /// 补位立刻落盘，所以下次打开面板看到的就是紧凑的一列。
    /// </summary>
    public static bool 删档并补位(int 槽位)
    {
        if (槽位 < 0 || 槽位 >= 槽位数) return false;
        if (!删档(槽位)) return false;

        for (int i = 槽位; i < 槽位数 - 1; i++)
        {
            var 下一个 = 读档(i + 1);
            if (下一个 == null) { 删档(i); continue; }   // 后面本来就空，顺手清掉
            存档(i, 下一个);
        }
        删档(槽位数 - 1);   // 整体前移后，最后一格必定空出来

        Debug.Log("[SaveSystem] 已删除槽位 " + 槽位 + "，后面的存档已依次补位");
        return true;
    }

    /// <summary>读全部槽位，没存档的位置是 null</summary>
    public static SaveData[] 读全部()
    {
        var 全部 = new SaveData[槽位数];
        for (int i = 0; i < 槽位数; i++) 全部[i] = 读档(i);
        return 全部;
    }

    // ---------------------------------------------------------------- 新档

    /// <summary>
    /// 建一份新档并初始化角色数据。
    /// 属性快照这一层等「属性结算系统」做好后会替换掉。
    /// </summary>
    public static SaveData 建新档(string 角色名 = null)
    {
        var 数据 = new SaveData
        {
            角色名 = string.IsNullOrEmpty(角色名) ? "无名散修" : 角色名,
            境界 = "炼气期一层",
            场景名 = "3C_Testbed",
            位置 = Vector3.zero,
            朝向Y = 0f,
            当前气血 = -1f,      // -1 = 用满值
            当前灵气 = -1f,
            功法id = "gongfa_taixu_lianqi",
        };

        // 太虚炼气诀自带的基础属性快照（对应 PlayerDefaultStats 的一组值）
        var 默认 = new (string 键, float 值)[]
        {
            ("攻击", 42f), ("气血", 320f), ("灵力", 160f),
            ("防御", 12f), ("暴击率", 0.05f), ("神识", 12.5f), ("吐纳", 2.4f),
        };
        foreach (var (键, 值) in 默认) 数据.写属性(键, 值);

        return 数据;
    }

    /// <summary>把一份档套到当前场景里的角色身上。属性结算系统做好前，只恢复能恢复的那几项</summary>
    public static void 应用到角色(SaveData 数据)
    {
        if (数据 == null) return;
        当前存档 = 数据;

        var 玩家 = GameObject.Find("Player");
        if (玩家 == null) return;

        var cc = 玩家.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;
        玩家.transform.position = 数据.位置 == Vector3.zero ? 玩家.transform.position : 数据.位置;
        玩家.transform.rotation = Quaternion.Euler(0f, 数据.朝向Y, 0f);
        if (cc != null) cc.enabled = true;

        // 资源
        var 生命 = 玩家.GetComponent<PlayerVitals>();
        if (生命 != null)
        {
            if (数据.当前灵气 >= 0f) 生命.CurrentSpirit = 数据.当前灵气;
            if (数据.当前气血 >= 0f) 生命.CurrentHealth = 数据.当前气血;
        }

        // ---- 修炼系统 ----
        var 修炼 = 玩家.GetComponent<PlayerCultivation>();
        if (修炼 != null)
        {
            修炼.设置总灵气(数据.总灵气);
            修炼.设置修炼次数(数据.修炼次数累积);

            var 面板 = 修炼.面板数据;
            if (面板 != null)
            {
                // 功法按 id 还原（取不到就保持当前）
                var 功法 = 修炼.取功法(数据.功法id);
                if (功法 != null) 面板.当前功法 = 功法;

                // 已学功法（按 id 还原；存档为空时保持默认=全学）
                if (数据.已学功法 != null && 数据.已学功法.Count > 0)
                {
                    var 还原 = new System.Collections.Generic.List<GongFaDefinition>();
                    foreach (var id in 数据.已学功法)
                    {
                        var g = 修炼.取功法(id);
                        if (g != null && !还原.Contains(g)) 还原.Add(g);
                    }
                    if (还原.Count > 0) 面板.已学功法 = 还原;
                }

                // ★ 已获得的能力：**以存档为准**（新档就是空的 —— 用户要求"学了才有"）
                面板.EnsureLists();
                面板.已获得主动神通 = new System.Collections.Generic.List<ActiveDivineAbility>();
                if (数据.已获得主动神通 != null)
                    foreach (var id in 数据.已获得主动神通)
                        foreach (var a in 面板.神通)
                            if (a is ActiveDivineAbility act && act.神通id == id && !面板.已获得主动(act)) 面板.已获得主动神通.Add(act);
                面板.已获得被动神通 = new System.Collections.Generic.List<PassiveDivineAbility>();
                if (数据.已获得被动神通 != null)
                    foreach (var id in 数据.已获得被动神通)
                        foreach (var a in 面板.神通)
                            if (a is PassiveDivineAbility ps && ps.神通id == id && !面板.已获得被动(ps)) 面板.已获得被动神通.Add(ps);

                面板.RaiseChanged();
            }

            // 让普攻组件按新功法重装
            var 装载 = 玩家.GetComponent<PlayerAbilityLoader>();
            if (装载 != null) 装载.Refresh();

            Debug.Log("[存档] 已恢复修为：总灵气 " + 数据.总灵气
                + "｜次数 " + 数据.修炼次数累积.ToString("0.##")
                + "｜境界 " + 修炼.境界名, 玩家);
        }

        // ---- 战阵 ----
        // 「已获得的真灵」= `Assets/Data/Generated/NpcDefinition` 里所有 demon / human
        //（由 DataTableImporter 自动收集），所以按 id 在「已获得列表 + 场上 NPC」里就能找到。
        // 兽宠 / 驯服那套玩法已经取消，不需要再存"已获得"进度。
        var 面板数据 = UnityEngine.Object.FindObjectOfType<UIPanelData>();
        if (面板数据 != null && 数据.战阵站位 != null && 数据.战阵站位.Count > 0)
        {
            面板数据.EnsureLists();
            int 还原数 = 0;
            for (int i = 0; i < 面板数据.战阵站位.Count; i++)
            {
                var id = i < 数据.战阵站位.Count ? 数据.战阵站位[i] : null;
                var 真灵 = 找真灵定义(id, 面板数据);
                面板数据.战阵站位[i] = 真灵;
                if (真灵 != null) 还原数++;
            }
            // 广播出去 → 挂在玩家身上的 SpiritFormationManager 会差量重建场上的真灵
            面板数据.RaiseChanged();
            Debug.Log("[存档] 已恢复战阵：上阵 " + 还原数 + " 个", 玩家);
        }
    }

    /// <summary>按 id 找战阵真灵的定义：先看「已获得真灵」，再在场上 NPC 里找</summary>
    static NpcDefinition 找真灵定义(string id, UIPanelData 面板)
    {
        if (string.IsNullOrEmpty(id)) return null;

        if (面板 != null && 面板.已获得真灵 != null)
            foreach (var s in 面板.已获得真灵)
                if (s != null && s.id == id) return s;

        foreach (var n in UnityEngine.Object.FindObjectsOfType<NpcInstance>())
            if (n != null && n.定义 != null && n.定义.id == id) return n.定义;

        Debug.LogWarning("[存档] 找不到战阵真灵「" + id + "」的定义，这一格留空"
                         + "（它可能不在「已获得真灵」列表里）");
        return null;
    }

    /// <summary>从角色身上抓一份当前状态写进存档数据（存档时调用）</summary>
    public static void 从角色采集(SaveData 数据)
    {
        if (数据 == null) return;
        var 玩家 = GameObject.Find("Player");
        if (玩家 == null) return;

        数据.位置 = 玩家.transform.position;
        数据.朝向Y = 玩家.transform.eulerAngles.y;

        var 生命 = 玩家.GetComponent<PlayerVitals>();
        if (生命 != null)
        {
            数据.当前气血 = 生命.CurrentHealth;
            数据.当前灵气 = 生命.CurrentSpirit;
        }

        // ---- 修炼系统（总灵气 / 修炼次数 / 境界等级 / 功法 / 已学功法）----
        var 修炼 = 玩家.GetComponent<PlayerCultivation>();
        if (修炼 != null)
        {
            数据.总灵气 = 修炼.总灵气;
            数据.修炼次数累积 = 修炼.修炼次数累积;
            数据.境界等级 = 修炼.等级;
            数据.境界 = 修炼.境界名;                 // 顺手把那个旧字符串字段也更新掉

            var 面板 = 修炼.面板数据;
            if (面板 != null)
            {
                if (面板.当前功法 != null) 数据.功法id = 面板.当前功法.功法id;
                数据.已学功法.Clear();
                if (面板.已学功法 != null)
                    foreach (var g in 面板.已学功法)
                        if (g != null) 数据.已学功法.Add(g.功法id);

                // ★ 已获得的能力（主动 / 被动神通）
                数据.已获得主动神通.Clear();
                if (面板.已获得主动神通 != null)
                    foreach (var a in 面板.已获得主动神通)
                        if (a != null) 数据.已获得主动神通.Add(a.神通id);
                数据.已获得被动神通.Clear();
                if (面板.已获得被动神通 != null)
                    foreach (var a in 面板.已获得被动神通)
                        if (a != null) 数据.已获得被动神通.Add(a.神通id);
            }
        }

        // ---- 战阵站位（9 格，空位记空字符串）----
        数据.战阵站位.Clear();
        var 战阵面板 = UnityEngine.Object.FindObjectOfType<UIPanelData>();
        if (战阵面板 != null)
        {
            战阵面板.EnsureLists();
            for (int i = 0; i < SpiritFormationLayout.格子数; i++)
            {
                var d = i < 战阵面板.战阵站位.Count ? 战阵面板.战阵站位[i] : null;
                数据.战阵站位.Add(d != null ? d.id : "");
            }
        }
    }

    /// <summary>当前正在玩的这份档（菜单里选完带进游戏场景）</summary>
    public static SaveData 当前存档;
    public static int 当前槽位 = -1;
}
