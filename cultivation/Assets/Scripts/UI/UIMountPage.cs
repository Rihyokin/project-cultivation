using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// **坐骑页**：上方 = 坐骑 idle 动画展示（真 3D 预览），左下 = 拥有的坐骑（带滚动条），
/// 右下 = 选中坐骑信息。布局照概念图「坐骑ui」。
///
/// ## 3D 预览怎么做的（不用额外图层）
///
/// 在 **y = −4000** 的地方开一块「口袋场地」，把坐骑实例、一盏点光、一台相机都放在那里。
/// 相机 `clearFlags = SolidColor`、`farClipPlane` 只有 40 米 —— 游戏世界在 4000 米外，
/// 根本进不了视锥，所以**只画得到坐骑**，不需要动 `TagManager` 加图层。
///
/// 灯**必须自带**：那地方是空的，没有灯坐骑就是全黑的。
/// 用**点光**（range 25）而不是平行光 —— 平行光照亮全世界，会把游戏场景一起点亮。
///
/// > ⚠️ 【本工程踩过的坑】预览实例上的 Animator 一定要 `cullingMode = AlwaysAnimate`。
/// > Unity 会因为「离相机太远」把 Animator 剔除掉，表现是**姿势冻住不动**
/// > （见开发注意事项里的 Animator culling 坑）。
/// </summary>
[DisallowMultipleComponent]
public class UIMountPage : MonoBehaviour
{
    [Header("引用")]
    [Tooltip("预览图（RawImage），拿 RenderTexture 显示")]
    public RawImage 预览图;

    [Tooltip("拥有列表。选中项变化时换预览模型")]
    public UIEntryList 列表;

    [Tooltip("选中坐骑信息栏")]
    public UIEntryInfo 信息栏;

    [Tooltip("数据源，留空自动从 Canvas 上找")]
    public UIPanelData data;

    [Header("预览外观")]
    public Color 背景色 = new Color(0.15f, 0.16f, 0.19f, 1f);
    [Tooltip("预览贴图边长")]
    public int 贴图边长 = 512;
    [Tooltip("取景留白：1.0 = 正好贴边，越大越远")]
    public float 取景留白 = 1.25f;
    [Tooltip("模型抬离场地原点的高度，避免一半埋进地面")]
    public float 模型抬高 = 0f;
    [Tooltip("预览相机能不能被鼠标拖动旋转（左键拖）")]
    public bool 可以拖动旋转 = true;

    [Header("调试")]
    public bool 打印日志 = false;

    // ---- 运行时 ----
    RenderTexture 贴图;
    Camera 相机;
    Transform 场地;
    GameObject 当前模型;
    IPanelEntry 当前条目;
    float 当前偏航 = 200f, 当前俯仰 = 8f;
    float 环绕半径 = 6f;
    Vector3 环绕中心;

    const float 场地高度 = -4000f;

    // ============================================================ 生命周期

    void OnDestroy()
    {
        拆场地();
    }

    void OnEnable()
    {
        if (data == null) data = 列表 != null ? 列表.ResolveData() : null;
        if (列表 != null) 列表.SelectionChanged += 选中变化;
        建场地();
        // 列表在 OnEnable 里会重建并 Select(entries[0])，那一瞬间可能还没轮到我们订阅，
        // 所以这里主动拉一次当前选中项。
        选中变化(列表 != null ? 列表.Selected : null);
    }

    void OnDisable()
    {
        if (列表 != null) 列表.SelectionChanged -= 选中变化;
        if (场地 != null) 场地.gameObject.SetActive(false);
        if (相机 != null) 相机.enabled = false;
    }

    // ============================================================ 场地

    void 建场地()
    {
        if (贴图 == null)
        {
            贴图 = new RenderTexture(Mathf.Max(128, 贴图边长), Mathf.Max(128, 贴图边长), 16, RenderTextureFormat.ARGB32);
            贴图.antiAliasing = 4;
            贴图.Create();
        }
        if (预览图 != null) 预览图.texture = 贴图;

        if (场地 != null) { 场地.gameObject.SetActive(true); 相机.enabled = true; 刷新相机(); return; }

        var 根 = new GameObject("坐骑预览场地");
        根.transform.position = new Vector3(0f, 场地高度, 0f);
        场地 = 根.transform;

        var 相机物体 = new GameObject("预览相机", typeof(Camera));
        相机物体.transform.SetParent(场地, false);
        相机 = 相机物体.GetComponent<Camera>();
        相机.clearFlags = CameraClearFlags.SolidColor;
        相机.backgroundColor = 背景色;
        相机.fieldOfView = 32f;
        相机.nearClipPlane = 0.05f;
        相机.farClipPlane = 60f;
        相机.cullingMask = ~0;              // 靠"离得远 + farClip 小"隔离，不靠图层
        相机.allowHDR = false;
        相机.allowMSAA = true;
        相机.depth = -100;                  // 排在主相机之前，别抢后处理
        相机.targetTexture = 贴图;

        // 自带灯：点光只照亮脚下这一小块，不会把游戏世界一起点亮
        加灯("主光", new Vector3(2.6f, 3.2f, -3.4f), 2.6f, new Color(1f, 0.97f, 0.90f), 26f);
        加灯("副光", new Vector3(-3.2f, 1.4f, -2.2f), 0.9f, new Color(0.72f, 0.80f, 1f), 20f);
        加灯("背光", new Vector3(0f, 2.0f, 3.6f), 0.8f, new Color(1f, 0.92f, 0.80f), 18f);

        刷新相机();
    }

    void 加灯(string 名, Vector3 位置, float 强度, Color 色, float 范围)
    {
        var go = new GameObject(名, typeof(Light));
        go.transform.SetParent(场地, false);
        go.transform.localPosition = 位置;
        var l = go.GetComponent<Light>();
        l.type = LightType.Point;
        l.range = 范围;
        l.intensity = 强度;
        l.color = 色;
        l.shadows = LightShadows.None;
    }

    void 拆场地()
    {
        清模型();
        if (场地 != null)
        {
            if (Application.isPlaying) Destroy(场地.gameObject); else DestroyImmediate(场地.gameObject);
            场地 = null; 相机 = null;
        }
        if (贴图 != null)
        {
            if (预览图 != null) 预览图.texture = null;
            贴图.Release();
            if (Application.isPlaying) Destroy(贴图); else DestroyImmediate(贴图);
            贴图 = null;
        }
    }

    // ============================================================ 选中 → 换模型

    void 选中变化(IPanelEntry entry)
    {
        当前条目 = entry;
        var m = entry as MountDefinition;

        if (信息栏 != null && m != null) 刷新信息文本(m);

        if (m == null) { 清模型(); if (预览图 != null) 预览图.color = new Color(1f, 1f, 1f, 0.35f); return; }
        if (预览图 != null) 预览图.color = Color.white;

        换模型(m);
    }

    void 刷新信息文本(MountDefinition m)
    {
        if (信息栏 == null) return;
        信息栏.Show(m);
        if (信息栏.descriptionText != null)
            信息栏.descriptionText.text = m.介绍 + "\n\n" + 增益文本(m);
    }

    /// <summary>把 27 项增益里**非 0** 的列成几行，给信息栏用</summary>
    public static string 增益文本(MountDefinition m)
    {
        if (m == null || m.增益 == null) return "";
        var 行 = new List<string>();
        for (int i = 0; i < AttributeUtil.Count; i++)
        {
            var t = (AttributeType)i;
            float v = m.增益[t];
            if (Mathf.Abs(v) < 1e-6f) continue;
            行.Add(AttributeUtil.GetDisplayName(t) + " +" + AttributeUtil.Format(t, v));
        }
        if (行.Count == 0) return "（无属性增益）";
        return "【乘骑增益】\n" + string.Join("　", 行.ToArray());
    }

    void 换模型(MountDefinition m)
    {
        清模型();
        if (场地 == null) 建场地();

        var prefab = m.加载模型();
        if (prefab == null)
        {
            if (打印日志) Debug.LogWarning("[坐骑页] 加载不到模型：" + m.模型资源路径, this);
            return;
        }

        当前模型 = Instantiate(prefab, 场地);
        当前模型.name = "预览_" + m.坐骑名称;
        当前模型.transform.localPosition = Vector3.zero;
        当前模型.transform.localRotation = Quaternion.identity;

        // 预览不参与任何物理 / 拾取
        foreach (var c in 当前模型.GetComponentsInChildren<Collider>(true)) c.enabled = false;
        foreach (var sm in 当前模型.GetComponentsInChildren<SkinnedMeshRenderer>(true)) sm.updateWhenOffscreen = true;
        foreach (var an in 当前模型.GetComponentsInChildren<Animator>(true))
        {
            an.cullingMode = AnimatorCullingMode.AlwaysAnimate;   // ★ 别让它被"离相机远"剔除
            an.applyRootMotion = false;
        }

        // 摆正：把包围盒中心对到场地原点，再整体抬高
        Bounds b = 取包围盒(当前模型);
        if (b.size.sqrMagnitude > 1e-6f)
            当前模型.transform.position += (场地.position + Vector3.up * 模型抬高) - b.center;

        b = 取包围盒(当前模型);
        环绕中心 = b.center;
        环绕半径 = Mathf.Max(0.5f, b.extents.magnitude);

        // 播待机动作
        string 动作 = string.IsNullOrEmpty(m.待机动作) ? "Idle" : m.待机动作;
        var na = 当前模型.GetComponentInChildren<NpcAnimator>();
        bool 播了 = false;
        if (na != null) 播了 = na.PlayAction(动作, true);
        if (!播了)
        {
            var an = 当前模型.GetComponentInChildren<Animator>();
            if (an != null && an.runtimeAnimatorController != null)
            {
                int 状态 = an.HasState(0, Animator.StringToHash(动作)) ? Animator.StringToHash(动作) : 0;
                an.Play(状态, 0, 0f);
            }
        }
        if (打印日志) Debug.Log("[坐骑页] 预览 " + m.坐骑名称 + " 动作=" + 动作 + " 播成功=" + 播了, this);

        刷新相机();
    }

    void 清模型()
    {
        if (当前模型 == null) return;
        if (Application.isPlaying) Destroy(当前模型); else DestroyImmediate(当前模型);
        当前模型 = null;
    }

    static Bounds 取包围盒(GameObject go)
    {
        var 渲染体 = go.GetComponentsInChildren<Renderer>(true);
        if (渲染体.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
        var b = 渲染体[0].bounds;
        for (int i = 1; i < 渲染体.Length; i++) b.Encapsulate(渲染体[i].bounds);
        return b;
    }

    // ============================================================ 相机 / 拖动

    void 刷新相机()
    {
        if (相机 == null) return;
        float 距 = 环绕半径 / Mathf.Tan(相机.fieldOfView * 0.5f * Mathf.Deg2Rad) * Mathf.Max(1.0f, 取景留白);
        var 旋转 = Quaternion.Euler(当前俯仰, 当前偏航, 0f);
        相机.transform.position = 环绕中心 + 旋转 * new Vector3(0f, 0f, -距);
        相机.transform.LookAt(环绕中心);
    }

    void Update()
    {
        if (相机 == null || 当前模型 == null) return;
        if (可以拖动旋转 && Input.GetMouseButton(0) && 指针在预览图上())
        {
            当前偏航 += Input.GetAxis("Mouse X") * 4f;
            当前俯仰 = Mathf.Clamp(当前俯仰 - Input.GetAxis("Mouse Y") * 3f, -70f, 70f);
        }
        // 场景 / 灯光可能在运行时变，每帧对一次很便宜
        刷新相机();
    }

    bool 指针在预览图上()
    {
        if (预览图 == null) return false;
        var rt = 预览图.rectTransform;
        return RectTransformUtility.RectangleContainsScreenPoint(rt, Input.mousePosition, null);
    }

    // ---- ASCII 别名 ----
    public void Refresh(IPanelEntry entry) => 选中变化(entry);
}
