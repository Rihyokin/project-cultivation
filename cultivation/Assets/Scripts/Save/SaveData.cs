using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 一份存档的数据。
///
/// 现在只存最核心的一层（身份 + 位置 + 资源 + 已选功法/神通），
/// 因为完整的属性结算系统还没做。等那个做出来后再往这里加字段，
/// 有 版本 号兜底，旧存档可以按版本做迁移。
/// </summary>
[Serializable]
public class SaveData
{
    public const int 当前版本 = 3;   // 2：加了修炼系统（总灵气/修炼次数/已学功法）；3：加了战阵站位

    [Header("身份")]
    public int 版本 = 当前版本;
    public string 角色名 = "无名散修";
    public string 境界 = "炼气期一层";
    public string 最后存档时间 = "";

    [Header("进度")]
    public string 场景名 = "3C_Testbed";
    public Vector3 位置 = Vector3.zero;
    public float 朝向Y = 0f;

    [Header("资源")]
    public float 当前气血 = -1f;      // -1 表示用满值
    public float 当前灵气 = -1f;

    [Header("配置")]
    public string 功法id = "";
    public List<string> 已装备神通 = new List<string>();
    public List<string> 已启用被动 = new List<string>();

    [Header("属性快照（属性结算系统做好前的临时方案）")]
    [Header("修炼系统")]
    [Tooltip("总灵气（与功法无关的通用积累）")]
    public long 总灵气 = 0;

    [Tooltip("修炼次数的小数累积（杀怪获得）")]
    public float 修炼次数累积 = 0f;

    [Tooltip("境界等级 1~90。可由 总灵气 ÷ 难度系数 重算，这里存一份方便读档后立刻显示")]
    public int 境界等级 = 1;

    [Tooltip("已经学会的功法 id。转修功法只能在这几门里选")]
    public List<string> 已学功法 = new List<string>();

    [Header("战阵")]
    [Tooltip("战阵站位：固定 9 个格子的真灵 id，空位写空字符串。读档时按 id 还原")]
    public List<string> 战阵站位 = new List<string>();

    public List<string> 属性键 = new List<string>();
    public List<float> 属性值 = new List<float>();

    /// <summary>空槽 = 从没用过。读不出来或角色名为空都算空</summary>
    public bool 是空的 => string.IsNullOrEmpty(角色名) || string.IsNullOrEmpty(最后存档时间);

    public void 刷新时间戳()
    {
        最后存档时间 = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
    }

    public void 写属性(string 键, float 值)
    {
        int i = 属性键.IndexOf(键);
        if (i >= 0) 属性值[i] = 值;
        else { 属性键.Add(键); 属性值.Add(值); }
    }

    public float 读属性(string 键, float 默认值 = 0f)
    {
        int i = 属性键.IndexOf(键);
        return i >= 0 && i < 属性值.Count ? 属性值[i] : 默认值;
    }
}
