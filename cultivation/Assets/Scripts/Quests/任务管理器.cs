using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// **任务管理器**：管三类任务（主线/支线/悬赏）的进度。
///
/// 一个任务 = 同一 <see cref="QuestDefinition.任务id"/> 下的 N 个**阶段**，按 <c>阶段</c> 升序推进；
/// 当前阶段完成才会把下一阶段设为当前（用户要求："只有当子任务完成时，才会触发下一个"）。
///
/// 完成条件四种，全部由这里统一检查：
///   · <b>提交物品</b>：背包里有够数量 → 交掉（扣物品）算完成
///   · <b>击杀</b>：目标 NPC 死掉（留空 = 任意 NPC 死一个；靠 <c>NpcInstance.Died</c> 事件 + 轮询兜底）
///   · <b>到达</b>：玩家进半径（每 0.25 秒查一次）
///   · <b>对话</b>：对话系统说完某一段时调 <see cref="通知对话"/>
///
/// 阶段开始时可以**调度 NPC**（播动画 / 攻击玩家 / 销毁），完成时**发物品奖励**，
/// 并且把 <see cref="QuestDefinition.接取加标记"/> / <see cref="QuestDefinition.完成加标记"/>
/// 写进 <see cref="对话标记"/> —— 对话表靠这两个标记就能长出新回答（和对话系统打通）。
/// </summary>
[DisallowMultipleComponent]
public class 任务管理器 : MonoBehaviour
{
    public static 任务管理器 实例 { get; private set; }

    /// <summary>某个阶段被设为当前（接取 / 推进到它）</summary>
    public static event Action<QuestDefinition> 阶段开始;
    /// <summary>某个阶段完成</summary>
    public static event Action<QuestDefinition> 阶段完成;
    /// <summary>整条任务（所有阶段）完成</summary>
    public static event Action<string> 任务全部完成;

    [Tooltip("背包数据。留空自动找场景里的 UIPanelData")]
    public UIPanelData 面板;

    [Tooltip("任务库。留空自动 Resources/任务/任务库")]
    public QuestDatabase 库;

    [Tooltip("检查「提交物品 / 到达」的间隔（秒）")]
    public float 检查间隔 = 0.25f;

    readonly Dictionary<string, int> 当前阶段 = new Dictionary<string, int>();

    [Tooltip("演出用的等待条件靠它计时：任务id → 这一阶段是什么时候开始的")]
    readonly Dictionary<string, float> 阶段开始时间 = new Dictionary<string, float>();
    readonly HashSet<string> 已完成任务 = new HashSet<string>();
    readonly List<NpcInstance> 已订阅 = new List<NpcInstance>();
    float 计时;

    public QuestDatabase 取库() => 库 != null ? 库 : (库 = QuestDatabase.取());

    void Awake() { 实例 = this; }

    void Start()
    {
        if (面板 == null) 面板 = FindObjectOfType<UIPanelData>();
        取库();
        订阅死亡();
        自动接取();
    }

    // ================================================================ 查询

    public bool 进行中(string 任务id) => !string.IsNullOrEmpty(任务id) && 当前阶段.ContainsKey(任务id);
    public bool 已完成(string 任务id) => !string.IsNullOrEmpty(任务id) && 已完成任务.Contains(任务id);
    public int 取当前阶段号(string 任务id)
    {
        int v;
        return 当前阶段.TryGetValue(任务id, out v) ? v : 0;
    }

    /// <summary>当前阶段的定义（没有就 null）</summary>
    public QuestDefinition 取当前阶段(string 任务id)
    {
        var db = 取库();
        int 段 = 取当前阶段号(任务id);
        return db != null && 段 > 0 ? db.取阶段(任务id, 段) : null;
    }

    /// <summary>面板要显示的：进行中的阶段列表</summary>
    public List<QuestDefinition> 进行中的阶段()
    {
        var 出 = new List<QuestDefinition>();
        foreach (var kv in 当前阶段)
        {
            var q = 取当前阶段(kv.Key);
            if (q != null) 出.Add(q);
        }
        return 出;
    }

    // ================================================================ 接取 / 推进

    /// <summary>接一个任务（从第 1 阶段开始）</summary>
    public bool 接取(string 任务id)
    {
        var db = 取库();
        if (db == null || string.IsNullOrEmpty(任务id)) return false;
        if (进行中(任务id) || 已完成(任务id)) return false;

        var 首 = db.取阶段(任务id, 1);
        if (首 == null) { Debug.LogWarning("[任务] 库里没有任务 " + 任务id + " 的第 1 阶段"); return false; }
        if (!string.IsNullOrEmpty(首.前置任务id) && !已完成(首.前置任务id))
        {
            Debug.Log("[任务] " + 首.任务名 + " 需要先完成前置任务 " + 首.前置任务id);
            return false;
        }

        进入阶段(任务id, 首);
        Debug.Log("[任务] 接取「" + 首.任务名 + "」第 " + 首.阶段 + " 阶段：" + 首.阶段名, 首);
        return true;
    }

    void 进入阶段(string 任务id, QuestDefinition 阶段)
    {
        当前阶段[任务id] = 阶段.阶段;
        阶段开始时间[任务id] = Time.time;      // ★ 「等待秒数」条件从这一刻开始计时
        对话标记.添加一批(阶段.接取加标记);
        执行动作(阶段);
        阶段开始?.Invoke(阶段);
    }

    /// <summary>完成当前阶段 → 加标记 → 发奖 → 进下一阶段（没有下一阶段就整条完成）</summary>
    public bool 完成当前阶段(string 任务id)
    {
        var db = 取库();
        var 阶段 = 取当前阶段(任务id);
        if (db == null || 阶段 == null) return false;

        // 提交物品：真的把东西扣掉
        if (阶段.条件 == 任务条件.提交物品 && 面板 != null)
        {
            var 物品 = db.找物品(阶段.物品id);
            if (物品 == null || !面板.移除物品(物品, 阶段.数量))
            {
                Debug.Log("[任务] 交不了：" + 阶段.物品id + " ×" + 阶段.数量 + " 不够", 阶段);
                return false;
            }
        }

        对话标记.添加一批(阶段.完成加标记);
        发奖励(阶段);
        阶段完成?.Invoke(阶段);
        Debug.Log("[任务] 完成「" + 阶段.任务名 + "」第 " + 阶段.阶段 + " 阶段：" + 阶段.阶段名, 阶段);

        var 下一 = db.取阶段(任务id, 阶段.阶段 + 1);
        if (下一 != null) 进入阶段(任务id, 下一);
        else
        {
            当前阶段.Remove(任务id);
            已完成任务.Add(任务id);
            Debug.Log("[任务] 「" + 阶段.任务名 + "」全部完成 ✔", 阶段);
            任务全部完成?.Invoke(任务id);
        }
        return true;
    }

    void 发奖励(QuestDefinition 阶段)
    {
        if (面板 == null || string.IsNullOrEmpty(阶段.奖励物品)) return;
        var db = 取库();
        if (db == null) return;
        foreach (var s in db.解析奖励(阶段.奖励物品))
        {
            if (s.物品 == null) continue;
            面板.给物品(s.物品, Mathf.Max(1, s.数量));
            Debug.Log("[任务] 奖励 " + s.物品.DisplayName + " ×" + s.数量);
        }
    }

    // ================================================================ 条件检查

    void Update()
    {
        计时 += Time.deltaTime;

        // ★ 镜头兜底：接管超过时限还没交还（多半是演出协程被切场景/销毁打断）→ 自己还回去，
        //   否则 TopDownCamera 会被永久关着，玩家再也不能操作视角 ✗
        if (被停的相机脚本.Count > 0 && 镜头兜底交还时间 > 0f && Time.time > 镜头兜底交还时间)
        {
            Debug.LogWarning("[任务] 镜头超时兜底交还（演出协程可能被打断了）");
            交还相机();
        }
        if (计时 < 检查间隔) return;
        计时 = 0f;

        var db = 取库();
        if (db == null || 当前阶段.Count == 0) return;

        var 快照 = new List<string>(当前阶段.Keys);
        foreach (var 任务id in 快照)
        {
            var 阶段 = 取当前阶段(任务id);
            if (阶段 == null) continue;
            switch (阶段.条件)
            {
                case 任务条件.无:
                    完成当前阶段(任务id);
                    break;
                case 任务条件.等待秒数:
                    {
                        // ★ 演出用：进这一阶段后等 等待秒 就自动完成（"停留2s""等候60s"）
                        float 起;
                        if (阶段开始时间.TryGetValue(任务id, out 起) && Time.time - 起 >= 阶段.等待秒)
                            完成当前阶段(任务id);
                        break;
                    }
                case 任务条件.提交物品:
                    {
                        var 物品 = db.找物品(阶段.物品id);
                        if (物品 != null && 面板 != null && 面板.物品数量(物品) >= 阶段.数量) 完成当前阶段(任务id);
                        break;
                    }
                case 任务条件.到达:
                    {
                        var 玩家 = 物品使用器.取玩家物体();
                        if (玩家 != null && Vector3.Distance(玩家.transform.position, 阶段.坐标) <= 阶段.到达半径)
                            完成当前阶段(任务id);
                        break;
                    }
                case 任务条件.击杀:
                    检查击杀(任务id, 阶段);
                    break;
            }
        }
    }

    void 检查击杀(string 任务id, QuestDefinition 阶段)
    {
        foreach (var npc in FindObjectsOfType<NpcInstance>())
        {
            if (npc == null || !npc.IsDead) continue;
            if (!string.IsNullOrEmpty(阶段.目标npcId) && (npc.定义 == null || npc.定义.id != 阶段.目标npcId)) continue;
            完成当前阶段(任务id);
            return;
        }
    }

    void 订阅死亡()
    {
        foreach (var npc in FindObjectsOfType<NpcInstance>())
        {
            if (npc == null || 已订阅.Contains(npc)) continue;
            npc.Died += 处理死亡;
            已订阅.Add(npc);
        }
    }

    void 处理死亡(NpcInstance 谁)
    {
        if (谁 == null) return;
        string id = 谁.定义 != null ? 谁.定义.id : "";
        foreach (var 任务id in new List<string>(当前阶段.Keys))
        {
            var 阶段 = 取当前阶段(任务id);
            if (阶段 == null || 阶段.条件 != 任务条件.击杀) continue;
            if (!string.IsNullOrEmpty(阶段.目标npcId) && 阶段.目标npcId != id) continue;
            完成当前阶段(任务id);
        }
    }

    // ================================================================ 对话系统接线

    /// <summary>对话系统说完某一段时调这里（条件=对话 的阶段会因此完成）</summary>
    public void 通知对话(string 对话id, string npcId)
    {
        foreach (var 任务id in new List<string>(当前阶段.Keys))
        {
            var 阶段 = 取当前阶段(任务id);
            if (阶段 == null || 阶段.条件 != 任务条件.对话) continue;
            if (!string.IsNullOrEmpty(阶段.对话id) && 阶段.对话id != 对话id) continue;
            完成当前阶段(任务id);
        }
    }

    /// <summary>对话系统显示某一段时调这里：把这一段绑定的任务接/完成（对话表 触发任务 / 完成任务 列）</summary>
    public void 处理对话绑定(string 触发任务, string 完成任务)
    {
        if (!string.IsNullOrEmpty(触发任务)) 接取(触发任务);
        if (!string.IsNullOrEmpty(完成任务)) 完成当前阶段(完成任务);
    }

    // ================================================================ 调度 NPC

    void 执行动作(QuestDefinition 阶段)
    {
        if (阶段.动作 == 任务动作.无) return;

        var npc = 找NPC(阶段.动作目标npcId);
        if (npc == null)
        {
            Debug.LogWarning("[任务] 动作的目标 NPC 不在场景里：" + 阶段.动作目标npcId, 阶段);
            return;
        }

        switch (阶段.动作)
        {
            case 任务动作.播动画:
                {
                    var 动画 = npc.GetComponent<NpcAnimator>();
                    if (动画 != null) { 动画.Play(阶段.动作参数); }
                    else
                    {
                        var a = npc.GetComponent<Animator>();
                        if (a != null && !string.IsNullOrEmpty(阶段.动作参数)) a.CrossFade(阶段.动作参数, 0.15f);
                    }
                    Debug.Log("[任务] 调度：" + npc.name + " 播动画「" + 阶段.动作参数 + "」");
                    break;
                }
            case 任务动作.攻击玩家:
                {
                    // 靠好感度翻脸：低于「敌对好感阈值」→ 视玩家为敌 → 自动锁定玩家开打
                    var 实例 = npc.GetComponent<NpcInstance>();
                    if (实例 != null) 实例.改变好感度(-999f);
                    Debug.Log("[任务] 调度：" + npc.name + " 把玩家当成了敌人");
                    break;
                }
            case 任务动作.处决:
                {
                    // 直接打死（三幕大师兄一刀劈野猪）：TakeDamage(伤害, 是否已减免)
                    var 实例 = npc.GetComponent<NpcInstance>();
                    if (实例 != null) 实例.TakeDamage(9999999f, false);
                    Debug.Log("[任务] 调度：处决 " + npc.name);
                    break;
                }
            case 任务动作.走向:
                Debug.Log("[任务] 调度：" + npc.name + " 走向 " + 阶段.坐标);
                StartCoroutine(走过去(npc, 阶段.坐标, 阶段.动作速度));
                break;
            case 任务动作.飞到:
                Debug.Log("[任务] 调度：" + npc.name + " 飞到 " + 阶段.坐标);
                StartCoroutine(飞过去(npc, 阶段.坐标, 阶段.动作速度, 阶段.动作参数));
                break;
            case 任务动作.镜头看目标:
                Debug.Log("[任务] 调度：镜头对焦 " + (npc != null ? npc.name : "null"));
                StartCoroutine(镜头对焦(npc != null ? npc.transform : null, 阶段.镜头时长, 阶段.镜头高度));
                break;
            case 任务动作.镜头回玩家:
                Debug.Log("[任务] 调度：镜头回玩家");
                StartCoroutine(镜头回玩家(阶段.镜头时长, 阶段.镜头高度));
                break;
            case 任务动作.生成NPC:
                生成NPC(阶段);
                break;
            case 任务动作.销毁:
                Debug.Log("[任务] 调度：销毁 " + npc.name);
                Destroy(npc.gameObject);
                break;
        }
    }

    /// <summary>走过去：走行走状态 + 平移到坐标，到位切回待机</summary>
    System.Collections.IEnumerator 走过去(GameObject npc, Vector3 目标, float 速度)
    {
        if (npc == null) yield break;
        播状态(npc, "Walk");
        var t = npc.transform;
        while (t != null)
        {
            var 平 = new Vector3(目标.x - t.position.x, 0f, 目标.z - t.position.z);
            if (平.magnitude <= 0.15f) break;
            t.position = Vector3.MoveTowards(t.position, new Vector3(目标.x, t.position.y, 目标.z), Mathf.Max(0.1f, 速度) * Time.deltaTime);
            if (平.sqrMagnitude > 0.0001f)
                t.rotation = Quaternion.Slerp(t.rotation, Quaternion.LookRotation(平.normalized), 8f * Time.deltaTime);
            yield return null;
        }
        播状态(npc, "Idle");
    }

    /// <summary>
    /// 御风飞过去：**升空 → 前进（同时位移）→ 到点悬停**。
    /// 前进用的状态名从 <see cref="QuestDefinition.动作参数"/> 取，留空默认 `Yufeng_Forward`。
    /// （大师兄/村民这些同骨骼 NPC 都加了这几个状态，见 大师兄.controller）
    /// </summary>
    System.Collections.IEnumerator 飞过去(GameObject npc, Vector3 目标, float 速度, string 前进状态)
    {
        if (npc == null) yield break;
        string 前进 = string.IsNullOrEmpty(前进状态) ? "Yufeng_Forward" : 前进状态;
        播状态(npc, "Yufeng_TakeOff");
        yield return new WaitForSeconds(0.45f);
        播状态(npc, 前进);

        var t = npc.transform;
        while (t != null)
        {
            var 差 = 目标 - t.position;
            if (差.magnitude <= 0.2f) break;
            t.position = Vector3.MoveTowards(t.position, 目标, Mathf.Max(0.1f, 速度) * Time.deltaTime);
            var 平 = new Vector3(差.x, 0f, 差.z);
            if (平.sqrMagnitude > 0.0001f)
                t.rotation = Quaternion.Slerp(t.rotation, Quaternion.LookRotation(平.normalized), 8f * Time.deltaTime);
            yield return null;
        }
        播状态(npc, "Yufeng_Idle");
    }

    /// <summary>给 NPC 切动画状态（找不到那个状态时 Unity 只会打警告，不会崩）</summary>
    static void 播状态(GameObject npc, string 状态)
    {
        if (npc == null || string.IsNullOrEmpty(状态)) return;
        var a = npc.GetComponent<Animator>();
        if (a != null) a.CrossFade(状态, 0.2f);
    }

    // ================================================================ 镜头接管（演出用）
    //
    // 场景里的主相机是 `Main Camera`，挂的是 **TopDownCamera**（俯视跟随）+ TopDownCameraZoom。
    // 所以演出期间要**先把它们停掉**，否则每帧都会把镜头抢回玩家身上 ✗；演完再交还。

    readonly System.Collections.Generic.List<Behaviour> 被停的相机脚本 = new System.Collections.Generic.List<Behaviour>();

    [Tooltip("镜头兜底交还的时限（秒）：接管后最多霸占这么久，超时自动还给跟随脚本。\n" +
             "防的是「演出协程被打断（切场景 / 对象销毁 / 异常）→ 镜头一直被劫持」这种死法")]
    public float 镜头兜底上限 = 30f;

    float 镜头兜底交还时间 = -1f;

    void 接管相机()
    {
        if (被停的相机脚本.Count > 0) return;
        var cam = Camera.main;
        if (cam == null) return;
        foreach (var b in cam.GetComponents<Behaviour>())
        {
            if (b == null || !b.enabled) continue;
            string n = b.GetType().Name;
            if (n.Contains("TopDownCamera") || n.Contains("CameraZoom")) { b.enabled = false; 被停的相机脚本.Add(b); }
        }
        if (被停的相机脚本.Count > 0)
        {
            镜头兜底交还时间 = Time.time + Mathf.Max(1f, 镜头兜底上限);
            Debug.Log("[任务] 镜头接管：停了 " + 被停的相机脚本.Count + " 个跟随脚本（兜底 " + 镜头兜底上限 + "s）");
        }
    }

    void 交还相机()
    {
        foreach (var b in 被停的相机脚本) if (b != null) b.enabled = true;
        if (被停的相机脚本.Count > 0) Debug.Log("[任务] 镜头交还：" + 被停的相机脚本.Count + " 个跟随脚本恢复");
        被停的相机脚本.Clear();
        镜头兜底交还时间 = -1f;
    }

    /// <summary>把镜头平滑推到某个目标身上（停在它的斜后方，略俯视）</summary>
    System.Collections.IEnumerator 镜头对焦(Transform 目标, float 时长, float 高度)
    {
        var cam = Camera.main;
        if (cam == null || 目标 == null) { 交还相机(); yield break; }
        接管相机();

        Vector3 起 = cam.transform.position;
        Quaternion 起转 = cam.transform.rotation;
        Vector3 看向 = 目标.position + Vector3.up * 1.0f;
        Vector3 终 = 目标.position + new Vector3(0f, Mathf.Max(1.0f, 高度) + 1.2f, -3.2f);
        Quaternion 终转 = Quaternion.LookRotation(看向 - 终, Vector3.up);
        yield return 推镜头(cam, 起, 终, 起转, 终转, 时长);
    }

    /// <summary>镜头回到玩家身上（然后交还给跟随脚本）</summary>
    System.Collections.IEnumerator 镜头回玩家(float 时长, float 高度)
    {
        var cam = Camera.main;
        var 玩家 = 物品使用器.取玩家物体();
        if (cam == null || 玩家 == null) { 交还相机(); yield break; }
        接管相机();

        Vector3 起 = cam.transform.position;
        Quaternion 起转 = cam.transform.rotation;
        Vector3 看向 = 玩家.transform.position + Vector3.up * 1.0f;
        Vector3 终 = 玩家.transform.position + new Vector3(0f, Mathf.Max(1.0f, 高度) + 1.2f, -3.2f);
        Quaternion 终转 = Quaternion.LookRotation(看向 - 终, Vector3.up);
        yield return 推镜头(cam, 起, 终, 起转, 终转, 时长);
        交还相机();          // 回玩家之后把控制权还给 TopDownCamera
    }

    static System.Collections.IEnumerator 推镜头(Camera cam, Vector3 起, Vector3 终, Quaternion 起转, Quaternion 终转, float 时长)
    {
        float t = 0f;
        float 总 = Mathf.Max(0.05f, 时长);
        while (t < 总)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / 总));
            if (cam == null) yield break;
            cam.transform.position = Vector3.Lerp(起, 终, k);
            cam.transform.rotation = Quaternion.Slerp(起转, 终转, k);
            yield return null;
        }
        if (cam != null) { cam.transform.position = 终; cam.transform.rotation = 终转; }
    }

    /// <summary>
    /// 生成一个 NPC（三幕的野猪、大师兄都靠它）。
    /// **动作参数 = 预制体资源路径**（`Assets/resources/` 下、不带扩展名），位置取 `坐标`。
    /// 走 <see cref="NpcPrefabs.加载"/> —— 它内部处理了"同目录有同名 FBX 时 Resources.Load 会挑错"的坑。
    /// </summary>
    GameObject 生成NPC(QuestDefinition 阶段)
    {
        if (string.IsNullOrEmpty(阶段.动作参数))
        {
            Debug.LogWarning("[任务] 生成NPC 没填预制体路径（填在「动作参数」里，例：NPC/Demon/YeZhu/YeZhu）", 阶段);
            return null;
        }
        var 预制 = NpcPrefabs.加载(阶段.动作参数);
        if (预制 == null)
        {
            Debug.LogWarning("[任务] 生成NPC 载不到预制体：" + 阶段.动作参数, 阶段);
            return null;
        }
        var go = Instantiate(预制, 阶段.坐标, Quaternion.identity);
        go.name = 预制.name + "_任务生成";
        Debug.Log("[任务] 生成 " + go.name + " 于 " + 阶段.坐标, 阶段);
        return go;
    }

    static GameObject 找NPC(string npcId)
    {
        if (string.IsNullOrEmpty(npcId)) return null;
        foreach (var npc in FindObjectsOfType<NpcInstance>())
        {
            if (npc == null) continue;
            if (npc.定义 != null && npc.定义.id == npcId) return npc.gameObject;
        }
        return null;
    }

    // ================================================================ 自动接取

    void 自动接取()
    {
        var db = 取库();
        if (db == null) return;
        foreach (var 任务id in db.全部任务id())
        {
            var 首 = db.取阶段(任务id, 1);
            if (首 != null && 首.自动接取) 接取(任务id);
        }
    }
}
