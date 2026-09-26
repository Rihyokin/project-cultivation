using UnityEngine;

/// <summary>
/// **一段对话**（对应 `Assets/Data/Tables/对话表.csv` 的一行，由 `修仙/从配置表生成资产` 生成）。
///
/// 设计要点（都是为了让"以后加内容"尽量只改表）：
///   · **一个 NPC 的默认对话 = 同一 <see cref="npcId"/> 下若干 <see cref="分段"/>**，
///     按分段从小到大播；每个分段最多挂 3 个回答，回答可以跳到别的分段（<see cref="跳转1"/>…）。
///   · **条件**（<see cref="需要标记"/> / <see cref="排除标记"/> / <see cref="优先"/>）是
///     给**任务管理器**留的口子：接了任务、状态变了，只要往"标记集合"里加/删标记，
///     同一个分段的候选行就会换成另一条 —— 不用改代码、不用碰预制体。
///   · 立绘只存**资源名**（`Assets/resources/立绘/` 下不带扩展名），美术补图即可生效。
///   · 情绪特效只存**名字 + 强度**（见 <c>DialogueUI.播放情绪</c>），
///     以后要加"冒泡泡""发怒"之类，只加一种名字 + 一段表现，表格不用改结构。
/// </summary>
[CreateAssetMenu(fileName = "对话_", menuName = "修仙/对话", order = 6)]
public class DialogueDefinition : ScriptableObject
{
    [Header("归属")]
    [Tooltip("本条的唯一 id")]
    public string id = "";

    [Tooltip("挂在哪个 NPC 上（写 NpcDefinition.id）")]
    public string npcId = "";

    [Tooltip("分段序号。同一个 NPC 的默认对话按它从小到大走；回答的「跳转」也指到这里")]
    public int 分段 = 0;

    [Header("内容")]
    [Tooltip("说话人显示名。留空 = 用 NPC 自己的名字")]
    public string 说话人 = "";

    [TextArea(2, 6)]
    [Tooltip("这一段的正文")]
    public string 文本 = "";

    [Tooltip("立绘资源名：Assets/resources/立绘/ 下的名字，不带扩展名。留空 = 不显示立绘")]
    public string 立绘 = "";

    [Tooltip("玩家立绘资源名。留空 = 用 DialogueUI 上的默认玩家立绘")]
    public string 玩家立绘 = "";

    [Header("回答选项（最多 3 个；留空即没有这个选项）")]
    public string 回答1 = "";
    public string 回答2 = "";
    public string 回答3 = "";

    [Tooltip("选了对应回答后跳到哪个「分段」。0 = 结束对话")]
    public int 跳转1 = 0;
    public int 跳转2 = 0;
    public int 跳转3 = 0;

    [Header("条件（任务管理器用；分号分隔多个标记）")]
    [Tooltip("需要**全部**具备这些标记，这一段才会被选中。留空 = 无条件")]
    public string 需要标记 = "";

    [Tooltip("只要具备其中**任意一个**标记，这一段就被跳过")]
    public string 排除标记 = "";

    [Tooltip("同分段里多条都满足条件时，优先选「优先」大的")]
    public int 优先 = 0;

    [Header("表现")]
    [Tooltip("这一段的情绪特效名：留空/无 = 不动。可用值见 DialogueUI.播放情绪（震动、冒泡、发怒…）")]
    public string 情绪 = "";

    [Tooltip("情绪强度倍率")]
    public float 情绪强度 = 1f;

    // ============================================================ 便捷读取（运行时不碰表结构）

    /// <summary>有几个回答选项</summary>
    public int 回答数
    {
        get
        {
            int n = 0;
            if (!string.IsNullOrWhiteSpace(回答1)) n = 1;
            if (!string.IsNullOrWhiteSpace(回答2)) n = 2;
            if (!string.IsNullOrWhiteSpace(回答3)) n = 3;
            return n;
        }
    }

    public string 取回答(int 序号)
        => 序号 == 1 ? 回答1 : 序号 == 2 ? 回答2 : 序号 == 3 ? 回答3 : "";

    public int 取跳转(int 序号)
        => 序号 == 1 ? 跳转1 : 序号 == 2 ? 跳转2 : 序号 == 3 ? 跳转3 : 0;

    public string 取说话人(string 兜底)
        => string.IsNullOrEmpty(说话人) ? 兜底 : 说话人;

    /// <summary>把「a;b;c」拆成数组（空串返回空数组）</summary>
    public static string[] 拆标记(string 串)
    {
        if (string.IsNullOrWhiteSpace(串)) return new string[0];
        var 段 = 串.Split(new[] { ';', '；', ',', '，' }, System.StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < 段.Length; i++) 段[i] = 段[i].Trim();
        return 段;
    }

    void OnValidate()
    {
        if (string.IsNullOrEmpty(id)) id = name;
    }
}
