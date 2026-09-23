using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// **战阵管理器。** 挂在玩家身上。
///
/// 职责：
///   · 盯着 <see cref="UIPanelData.战阵站位"/>，和「场上现有的真灵」做差量 ——
///     多出来的销毁、缺的生成、换格的改阵位
///   · 生成时把真灵**幽灵化**：缩小到 50%、换成 <c>Cultivation/GhostSpirit</c> 材质
///     （半透明 + 边缘冷光 + 上下流动）、关碰撞体、关血条光环、好感度改成不敌对
///   · 对外提供「神识范围」和「主人锁定的目标」给真灵用
///
/// **为什么不挂在 CharacterUI 上**：真灵要跟着玩家走，站位是玩家的本地坐标，
/// 神识也要跟着玩家的属性走；而且面板关着的时候真灵照样得动。
/// </summary>
public class SpiritFormationManager : MonoBehaviour
{
    [Header("引用（留空自动找）")]
    [Tooltip("战阵数据源。留空则去场景里找 CharacterUI 上的 UIPanelData")]
    public UIPanelData 面板数据;

    [Tooltip("取神识用。留空则在本体找 PlayerCombatStats")]
    public PlayerCombatStats 战斗属性;

    [Tooltip("取主人锁定的目标用。留空则在本体找 NpcTargeting")]
    public NpcTargeting 目标管理器;

    [Tooltip("主人（站位以它为原点）。留空 = 本物件")]
    public Transform 主人;

    [Header("外观")]
    [Tooltip("真灵模型缩放倍率（相对原 NPC 的大小）")]
    [Range(0.1f, 1.5f)]
    public float 模型缩放倍率 = 0.5f;

    [Tooltip("**真灵的「攻击距离」要不要跟着模型一起缩。**\n\n" +
             "1 = 完全跟着缩（默认）。0 = 不缩（= 修之前的行为）。\n\n" +
             "【bug·已修】模型缩到 0.5，**手也跟着短了一半**（实测冰魄怪 Attack1 的手臂前伸 " +
             "0.68 → 0.34 米），但 `攻击距离` 还是原尺寸的 2.5 米 —— 于是真灵站在 " +
             "2.5 米外挥砍，**实测命中那一刻指尖离目标表面还有 1.08 米**，" +
             "看着就是在打空气 ✗ 用户报的「动作怪怪的」就是它。\n\n" +
             "按缩放缩了之后，站位跟着手的长度走，挥砍就能贴到目标身上。\n" +
             "如果觉得贴太近（大目标身上会有点重叠，灵体可以重叠），往 0 调一点。")]
    [Range(0f, 1f)]
    public float 攻击距离随缩放 = 1f;

    [Tooltip("真灵材质。留空 = Resources.Load(\"Materials/GhostSpirit\")")]
    public Material 真灵材质;

    [Header("神识范围")]
    [Tooltip("神识为 0 时的基础范围（米）。和 basic_remoteattack_01 是同一套换算")]
    public float 基础神识范围 = 4f;

    [Tooltip("每 1 点神识增加多少米")]
    public float 每点神识范围 = 0.8f;

    [Tooltip("神识范围上限（米）")]
    public float 神识范围上限 = 40f;

    [Header("追击半径")]
    [Tooltip("真灵会去追**主人锁定的目标**，但离主人超过这个距离就不再往前（米）。\n\n" +
             "**这就是「真灵肯为你跑多远」**：\n" +
             "  · 调小 → 老实待在身边，但**打不到远处的怪**\n" +
             "  · 调大 → 会追出去很远\n\n" +
             "参考：敌对 NPC 的「脱战范围」是 20~22 米，所以这里默认 20，手感一致。\n" +
             "填 0 或负数 = 退回旧行为（只用「神识范围」，默认 14 米 ——\n" +
             "**14 米太小了**，用户反馈过「锁了 20 米外的怪，真灵一动不动」）")]
    public float 追击半径 = 20f;

    /// <summary>实际用的追击半径：填了「追击半径」就用它，否则退回「神识范围」</summary>
    public float 有效追击半径 => 追击半径 > 0f ? 追击半径 : 神识范围;

    [Header("生成")]
    [Tooltip("生成位置微调（怕刚好卡在地面里）")]
    public Vector3 生成位置微调 = new Vector3(0f, 0.05f, 0f);

    [Header("真灵之间的分离（不挤到一块儿）")]
    [Tooltip("两个真灵的水平距离小于这个值就互相推开（米）")]
    public float 真灵最小间距 = 1.4f;

    [Tooltip("分离强度 0~1。1 = 每帧把重叠一次推掉一半")]
    [Range(0f, 1f)]
    public float 分离强度 = 0.5f;

    [Tooltip("单帧最多推开多少米（防止刚生成时弹飞）")]
    public float 单帧最大推移 = 0.12f;

    [Tooltip("打印生成 / 销毁日志")]
    public bool 打印日志 = true;

    /// <summary>场上现有的真灵：格子序号 → 真灵</summary>
    readonly Dictionary<int, FormationSpirit> 场上 = new Dictionary<int, FormationSpirit>();

    /// <summary>场上现有的真灵：格子序号 → 它是哪个定义（用来发现"同一格换了人"）</summary>
    readonly Dictionary<int, NpcDefinition> 场上定义 = new Dictionary<int, NpcDefinition>();

    /// <summary>分离用的临时列表（避免每帧分配）</summary>
    readonly List<FormationSpirit> 分离缓存 = new List<FormationSpirit>();

    /// <summary>主人（保证非空）</summary>
    public Transform 主人Transform => 主人 != null ? 主人 : transform;

    /// <summary>神识范围（米）= 基础 + 神识 × 系数，封顶</summary>
    public float 神识范围
        => Mathf.Min(神识范围上限, 基础神识范围 + 当前神识 * 每点神识范围);

    public float 当前神识 => 战斗属性 != null ? 战斗属性.当前神识 : 0f;

    /// <summary>主人现在锁定的目标（没有就 null）</summary>
    public NpcInstance 玩家锁定目标 => 目标管理器 != null ? 目标管理器.LockedNpc : null;

    /// <summary>已上阵的真灵数量</summary>
    public int 场上数量 => 场上.Count;

    /// <summary>取某个格子上的真灵（没有就 null）</summary>
    public FormationSpirit 取真灵(int 格) => 场上.TryGetValue(格, out var s) ? s : null;

    /// <summary>某个格子对应的世界站位</summary>
    public Vector3 取阵位世界位置(int 格)
        => SpiritFormationLayout.取世界位置(主人Transform, 格) + 生成位置微调;

    // ============================================================ 生命周期

    void Awake() => 解析引用();

    void OnEnable()
    {
        解析引用();
        if (面板数据 != null) 面板数据.Changed += 重建;
        重建();
    }

    void OnDisable()
    {
        if (面板数据 != null) 面板数据.Changed -= 重建;
        全部清掉();
    }

    void 解析引用()
    {
        if (主人 == null) 主人 = transform;

        if (战斗属性 == null) 战斗属性 = GetComponent<PlayerCombatStats>();
        if (战斗属性 == null) 战斗属性 = GetComponentInParent<PlayerCombatStats>();

        if (目标管理器 == null) 目标管理器 = GetComponent<NpcTargeting>();
        if (目标管理器 == null) 目标管理器 = GetComponentInParent<NpcTargeting>();

        if (面板数据 == null) 面板数据 = FindObjectOfType<UIPanelData>();

        if (真灵材质 == null) 真灵材质 = Resources.Load<Material>("Materials/GhostSpirit");
    }

    /// <summary>
    /// **真灵之间的软分离** —— 谁离谁太近就互相推开一点。
    ///
    /// 为什么不用物理碰撞：真灵的碰撞体是**故意关掉的**（无敌 / 不挡路 / 选不中 /
    /// 不参与玩家 AoE），而且它们的移动是直接写 <c>transform.position</c>，
    /// 挂 Rigidbody 反而会和 AI 的走位打架。所以这里做**成对的位置修正**：
    /// 重叠多少就各推一半，单帧限速防止抖。
    ///
    /// 放在 LateUpdate：AI 这一帧的走位已经算完了，最后再修位置，下一帧它照常决策。
    /// </summary>
    void LateUpdate()
    {
        if (场上.Count < 2) return;

        分离缓存.Clear();
        foreach (var kv in 场上)
            if (kv.Value != null) 分离缓存.Add(kv.Value);
        if (分离缓存.Count < 2) return;

        float 间距 = Mathf.Max(0.2f, 真灵最小间距);
        float 强度 = Mathf.Clamp01(分离强度);
        if (强度 <= 0f) return;

        bool 推过 = false;
        for (int i = 0; i < 分离缓存.Count; i++)
        {
            for (int j = i + 1; j < 分离缓存.Count; j++)
            {
                var a = 分离缓存[i];
                var b = 分离缓存[j];
                if (a == null || b == null) continue;

                Vector3 差 = b.transform.position - a.transform.position;
                差.y = 0f;
                float d = 差.magnitude;
                if (d >= 间距) continue;

                // 完全重合时给个确定的方向，别让 0 向量除出 NaN
                Vector3 向 = d > 1e-4f ? 差 / d : (a.阵位 <= b.阵位 ? Vector3.right : Vector3.left);

                float 推 = Mathf.Min((间距 - d) * 0.5f * 强度, Mathf.Max(0f, 单帧最大推移));
                if (推 <= 0f) continue;

                a.transform.position -= 向 * 推;
                b.transform.position += 向 * 推;
                推过 = true;
            }
        }

        // 推完重新贴地（不然会浮空 / 陷地）
        if (!推过) return;
        foreach (var s in 分离缓存)
            if (s != null) s.贴合地面();
    }

    // ============================================================ 差量重建

    /// <summary>把场上的真灵同步成数据源里那一套</summary>
    public void 重建()
    {
        解析引用();
        if (面板数据 == null) return;
        面板数据.EnsureLists();

        var 站位 = 面板数据.战阵站位;

        // ---- 1) 先清掉：不在阵上的、或者同一格换了人的 ----
        var 要删 = new List<int>();
        foreach (var kv in 场上)
        {
            int 格 = kv.Key;
            var 现在 = kv.Value;
            var 定义 = 站位 != null && 格 < 站位.Count ? 站位[格] : null;

            if (现在 == null) { 要删.Add(格); continue; }
            if (定义 == null) { 要删.Add(格); continue; }
            if (场上定义.TryGetValue(格, out var 老) && 老 != 定义) { 要删.Add(格); continue; }
        }
        foreach (var 格 in 要删) 销毁(格);

        // ---- 2) 再补上缺的 ----
        if (站位 == null) return;
        for (int 格 = 0; 格 < 站位.Count; 格++)
        {
            var 定义 = 站位[格];
            if (定义 == null) continue;
            if (场上.ContainsKey(格)) continue;
            生成(定义, 格);
        }
    }

    void 全部清掉()
    {
        var 全部 = new List<int>(场上.Keys);
        foreach (var 格 in 全部) 销毁(格);
        场上.Clear();
        场上定义.Clear();
    }

    void 销毁(int 格)
    {
        if (!场上.TryGetValue(格, out var 真灵)) { 场上.Remove(格); 场上定义.Remove(格); return; }

        场上.Remove(格);
        场上定义.Remove(格);

        if (真灵 != null)
        {
            if (打印日志) Debug.Log("[战阵] 下阵：" + 真灵.name + "（格子 " + 格 + "）", this);
            Destroy(真灵.gameObject);
        }
    }

    // ============================================================ 生成

    FormationSpirit 生成(NpcDefinition 定义, int 格)
    {
        if (定义 == null) return null;

        if (string.IsNullOrEmpty(定义.模型资源路径))
        {
            Debug.LogWarning("[战阵]「" + 定义.DisplayName + "」没有配「模型资源路径」，上不了阵", this);
            return null;
        }

        var prefab = NpcPrefabs.加载(定义.模型资源路径);
        if (prefab == null)
        {
            Debug.LogWarning("[战阵]「" + 定义.DisplayName + "」的模型路径加载不到："
                             + 定义.模型资源路径 + "（要相对 Assets/resources，且不带扩展名）", this);
            return null;
        }
        if (prefab.GetComponent<NpcInstance>() == null)
        {
            // 【坑】同目录下有一个和 prefab 同名的 .FBX 时，Resources.Load 会挑到那个 FBX ——
            // 拿到 FBX 的真灵**走不动也放不出招**（没有 NpcInstance → 移动速度 0；没有 AnimatorController）。
            // NpcPrefabs.加载() 已经处理了这种情况，这里是最后一道保险。
            Debug.LogWarning("[战阵]「" + 定义.DisplayName + "」的模型路径指到了**模型文件**而不是预制体："
                             + 定义.模型资源路径 + "。真灵会变成不会动的木头人，请改成对应的 .prefab", this);
            return null;
        }

        Vector3 位 = 取阵位世界位置(格);
        var go = Instantiate(prefab, 位, 主人Transform.rotation);
        go.name = "战阵真灵_" + 定义.id;

        // ---- 1) 把 prefab 自带的 AI 摘掉（本工程的 prefab 上没挂 AI，这里只是保险）----
        // 【坑】必须用 DestroyImmediate：Destroy 是帧末生效，而 NpcInstance.Start 里那句
        // NpcAiBase.确保() 会 GetComponent 到"正在等待销毁"的那个敌对 AI，
        // 于是它以为"已经有 AI 了"、不再装真灵 AI —— 结果真灵一个 AI 都没有。
        foreach (var ai in go.GetComponents<NpcAiBase>())
            if (ai != null && !(ai is FormationSpirit)) DestroyImmediate(ai);

        // ---- 2) 装真灵 AI（要在 NpcInstance.Start 之前，确保() 才认得它）----
        var 真灵 = go.AddComponent<FormationSpirit>();
        真灵.管理器 = this;
        真灵.阵位 = 格;
        真灵.打印真灵日志 = 打印日志;

        // ---- 3) 无敌 / 不挡路 / 不显示血条光环 ----
        // 关掉碰撞体：玩家选不中它、玩家自己的 AoE 打不到它、怪也打不到它、走路不会互相挤
        foreach (var c in go.GetComponentsInChildren<Collider>(true))
            if (c != null) c.enabled = false;

        // 【用户要求】「无限血量」—— 真灵是主人的灵体，绝不能被任何东西打死
        // （包括漏过来的 AoE、场景伤害、以及"好感度改错了被自己人打"）
        foreach (var inst in go.GetComponentsInChildren<NpcInstance>(true))
            if (inst != null) inst.无敌 = true;

        // 【坑·已修】NpcIndicator 在 **Awake 里就把血条建好了**，
        // 所以光 `enabled = false` 是不够的 —— 它的 LateUpdate（负责同步显隐）也不再跑，
        // 于是那条血条会**永远挂在真灵头顶**（实测截图里三只真灵都顶着红血条）。
        // 正确做法：先用它的公开接口把状态关掉，再把它建出来的那一层整个拆掉。
        foreach (var ind in go.GetComponentsInChildren<NpcIndicator>(true))
        {
            if (ind == null) continue;
            ind.SetSelected(false);
            ind.SetLocked(false);
            ind.enabled = false;

            var 指示器层 = ind.transform.Find("IndicatorRoot");
            if (指示器层 != null) Destroy(指示器层.gameObject);   // 环 + 血条都挂在它下面
        }

        // ---- 4) 好感度改成不敌对 ----
        // 否则玩家的普攻自动索敌（找好感度 < 0 的）会把自己的真灵当成敌人
        foreach (var inst in go.GetComponentsInChildren<NpcInstance>(true))
            if (inst != null) inst.改变好感度(1000f);

        // ---- 5) 缩小 + 幽灵化 ----
        go.transform.localScale = prefab.transform.localScale * 模型缩放倍率;
        幽灵化(go);

        // ---- 6) **手短了，站位也得跟着短** ----
        // 模型缩到 0.5，Attack1 的手臂前伸也从 0.68 米变成 0.34 米；
        // 但 攻击距离 是从 prefab 抄过来的原尺寸值（2.5 米）—— 不缩的话真灵会站在
        // 2.5 米外挥砍，实测命中瞬间指尖离目标表面还差 1.08 米，看着就是在打空气 ✗
        float 距离系数 = Mathf.Lerp(1f, 模型缩放倍率, Mathf.Clamp01(攻击距离随缩放));
        真灵.攻击距离 *= 距离系数;
        真灵.出膛高度 *= 距离系数;      // 弹道退化到"胸口高度"时也别高过头顶

        场上[格] = 真灵;
        场上定义[格] = 定义;

        if (打印日志)
            Debug.Log("[战阵] 上阵：" + 定义.DisplayName + "（格子 " + 格 + "，缩放 "
                      + 模型缩放倍率.ToString("0.##") + "）", this);
        return 真灵;
    }

    /// <summary>把整只真灵的渲染体换成幽灵材质（保留原贴图）</summary>
    void 幽灵化(GameObject go)
    {
        if (真灵材质 == null)
        {
            Debug.LogWarning("[战阵] 找不到真灵材质（Materials/GhostSpirit），真灵会是原色实体", this);
            return;
        }

        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
        {
            if (r == null) continue;

            var 原材质 = r.sharedMaterial;
            var 新材质 = new Material(真灵材质);

            // 保留原模型的贴图，只换"怎么渲染"
            if (原材质 != null && 原材质.HasProperty("_MainTex") && 新材质.HasProperty("_MainTex"))
                新材质.mainTexture = 原材质.mainTexture;

            r.sharedMaterial = 新材质;
            r.shadowCastingMode = ShadowCastingMode.Off;    // 幽灵不投影
            r.receiveShadows = false;
        }
    }

    // ---- ASCII 别名 ----
    public float SenseRange => 神识范围;
    public int DeployedCount => 场上数量;
    public FormationSpirit GetSpirit(int slot) => 取真灵(slot);
    public Vector3 SlotWorldPosition(int slot) => 取阵位世界位置(slot);
    public void Rebuild() => 重建();
}
