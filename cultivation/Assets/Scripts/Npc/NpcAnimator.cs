using UnityEngine;

/// <summary>
/// NPC 动作播放器。挂在使用 Character 控制器的模型上。
///
/// 控制器里的动作是以 int 参数 "Action" 驱动的（对应状态索引），
/// 这个组件负责按【动作名】查到索引再切过去，调用方不用关心顺序。
///
/// 用法：
///   var a = gameObject.GetComponent&lt;NpcAnimator&gt;();
///   a.PlayAction("Attack1");
///   a.PlayAction("Idle", loop: true);
/// </summary>
[RequireComponent(typeof(Animator))]
public class NpcAnimator : MonoBehaviour
{
    [Header("动作名表（顺序要和控制器里的状态一致）")]
    [Tooltip("留空则运行时从 Animator 的状态里自动读")]
    public string[] 动作列表;

    [Header("常用动作（可按需改）")]
    public string 待机动作 = "Idle";
    public string 移动动作 = "Run";

    Animator 动画;
    bool 已就绪;

    void Awake()
    {
        动画 = GetComponent<Animator>();
    }

    void Start()
    {
        if (动画.runtimeAnimatorController == null)
        {
            Debug.LogWarning("[NpcAnimator] " + name + " 的 Animator 没接控制器", this);
            enabled = false;
            return;
        }
        取动作表();
        已就绪 = true;
        PlayAction(待机动作, true);
    }

    void 取动作表()
    {
        if (动作列表 != null && 动作列表.Length > 0) return;

        // 运行时读不到 UnityEditor.Animations，用反射拿状态名（顺序就是索引顺序）
        var ctrl = 动画.runtimeAnimatorController;
        if (ctrl == null) return;
        // RuntimeAnimatorController 上没有 layers，同样要反射
        var 层属性 = ctrl.GetType().GetProperty("layers");
        var 层 = 层属性 != null ? 层属性.GetValue(ctrl) as System.Array : null;
        if (层 == null || 层.Length == 0) return;
        var sm属性 = 层.GetValue(0).GetType().GetProperty("stateMachine");
        var sm = sm属性 != null ? sm属性.GetValue(层.GetValue(0)) : null;
        if (sm == null) return;
        var 状态 = sm.GetType().GetProperty("states");
        if (状态 == null) return;
        var arr = 状态.GetValue(sm) as System.Array;
        if (arr == null) return;

        var 名 = new System.Collections.Generic.List<string>();
        foreach (var e in arr)
        {
            var 名属性 = e.GetType().GetProperty("state");
            var st = 名属性 != null ? 名属性.GetValue(e) : null;
            var n = st != null ? st.GetType().GetProperty("name") : null;
            if (n != null) 名.Add(n.GetValue(st) as string);
        }
        动作列表 = 名.ToArray();
    }

    /// <summary>按动作名切。找不到就保持原样并打警告。</summary>
    public bool PlayAction(string 动作名, bool loop = false)
    {
        if (!已就绪 || 动画 == null || 动画.runtimeAnimatorController == null) return false;
        if (动作列表 == null || 动作列表.Length == 0) 取动作表();
        if (动作列表 == null) return false;

        int 索引 = System.Array.IndexOf(动作列表, 动作名);
        if (索引 < 0)
        {
            Debug.LogWarning("[NpcAnimator] " + name + " 没有动作「" + 动作名 + "」。可用：" + string.Join(", ", 动作列表), this);
            return false;
        }

        动画.SetInteger("Action", 索引);
        动画.SetFloat("Speed", 1f);
        return true;
    }

    /// <summary>按索引切（想按顺序遍历时用）</summary>
    public bool PlayActionByIndex(int 索引)
    {
        if (动作列表 == null || 索引 < 0 || 索引 >= 动作列表.Length) return false;
        动画.SetInteger("Action", 索引);
        return true;
    }

    public string[] AvailableActions
    {
        get { if (动作列表 == null || 动作列表.Length == 0) 取动作表(); return 动作列表; }
    }

    /// <summary>
    /// 按**动作名**取片段长度（秒）。找不到返回 0。
    ///
    /// 【为什么需要它】AI 要算"这一招播完了没"，而
    /// <c>Animator.GetCurrentAnimatorStateInfo(0).length</c> 读的是**当前状态** ——
    /// 刚 SetInteger 切动作的那一两帧里状态还没切过去，读到的会是**上一个状态**的长度。
    /// 白熊精 Attack2（1.9 秒）就这样被当成 1.3 秒（Fight 的长度）在 1.3 秒时收招砍断 ✗
    /// 所以时长要按"我请求播的那个动作"去查，而不是问 Animator 现在在播什么。
    /// </summary>
    public float 取动作长度(string 动作名)
    {
        if (string.IsNullOrEmpty(动作名) || 动画 == null) return 0f;
        var ctrl = 动画.runtimeAnimatorController;
        if (ctrl == null) return 0f;

        foreach (var c in ctrl.animationClips)
            if (c != null && c.name == 动作名) return c.length;
        return 0f;
    }

    public string CurrentAction
    {
        get
        {
            if (动画 == null || 动作列表 == null) return "";
            int i = 动画.GetInteger("Action");
            return i >= 0 && i < 动作列表.Length ? 动作列表[i] : "";
        }
    }

    // ---- ASCII 别名 ----
    public bool Play(string action, bool loop = false) => PlayAction(action, loop);
    public string[] Actions => AvailableActions;
}
