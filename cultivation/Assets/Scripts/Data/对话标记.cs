using System;
using System.Collections.Generic;

/// <summary>
/// **对话 / 任务共用的「状态标记」集合** —— 任务管理器将来只需要调这里，
/// 对话表里的 <c>需要标记</c> / <c>排除标记</c> 列就会自动换掉候选对话。
///
/// 用法（任务管理器侧，将来）：
/// <code>
/// 对话标记.添加("任务_采药_已接");
/// 对话标记.移除("任务_采药_已接");
/// </code>
/// 对话系统侧只读：<see cref="具备"/>。<see cref="变化"/> 事件可以给"标记变了要刷新当前对话"用。
/// </summary>
public static class 对话标记
{
    static readonly HashSet<string> 集合 = new HashSet<string>();

    /// <summary>标记增删时触发（标记名, 是否新增）。任务管理器 / 对话 UI 都可以订</summary>
    public static event Action<string, bool> 变化;

    public static bool 具备(string 标记)
        => !string.IsNullOrEmpty(标记) && 集合.Contains(标记);

    public static void 添加(string 标记)
    {
        if (string.IsNullOrEmpty(标记)) return;
        if (!集合.Add(标记)) return;
        var h = 变化; if (h != null) h(标记, true);
    }

    public static void 移除(string 标记)
    {
        if (string.IsNullOrEmpty(标记)) return;
        if (!集合.Remove(标记)) return;
        var h = 变化; if (h != null) h(标记, false);
    }

    /// <summary>一次加多个（分号/逗号分隔，和表格里的写法一致）</summary>
    public static void 添加一批(string 标记串)
    {
        var 段 = DialogueDefinition.拆标记(标记串);
        for (int i = 0; i < 段.Length; i++) 添加(段[i]);
    }

    public static void 清空() => 集合.Clear();

    public static string[] 全部标记()
    {
        var a = new string[集合.Count];
        集合.CopyTo(a);
        return a;
    }
}

/// <summary>对话表条件列的判定（留给任务管理器的接口，本体不在这里）</summary>
public static class 对话条件
{
    /// <summary>这一条对话现在满不满足条件（需要标记全中 + 排除标记一个都没中）</summary>
    public static bool 满足(DialogueDefinition d)
    {
        if (d == null) return false;

        var 需要 = DialogueDefinition.拆标记(d.需要标记);
        for (int i = 0; i < 需要.Length; i++)
            if (!对话标记.具备(需要[i])) return false;

        var 排除 = DialogueDefinition.拆标记(d.排除标记);
        for (int i = 0; i < 排除.Length; i++)
            if (对话标记.具备(排除[i])) return false;

        return true;
    }
}
