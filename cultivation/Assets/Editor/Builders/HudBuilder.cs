using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 生成游戏内 HUD（左下角一小块，全部**方框**、不显示任何名字）：
///
///   ┌────┐ ┌─┐┌─┐┌─┐┌─┐┌─┐┌─┐
///   │功法│ │1││2││3││4││5││6│
///   └────┘ └─┘└─┘└─┘└─┘└─┘└─┘
///   气血 ▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓
///   灵力 ▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓
///   修炼 ▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓   ← 距「下一次修炼次数」的进度（杀怪实时增长）
///
/// 名字改成**鼠标悬停时弹出的半透明「信息幕布」**（挂 <see cref="HudHoverTarget"/>）。
///
/// 菜单：修仙 / 生成游戏界面 HUD   （ASCII：Cultivation / Build Player HUD）
///
/// 重复执行是安全的：会先删掉旧的 HudCanvas 再重建，
/// **不会碰 CharacterUI / PauseMenuCanvas**。
/// </summary>
public static class HudBuilder
{
    const string FontPath = "Assets/Fonts/SimHei.ttf";
    const string SpriteDir = "Assets/Resources/UI/Hud";
    const string CanvasName = "HudCanvas";

    // ---- 紧凑布局（参考分辨率 1920x1080，锚在左下角）----
    //
    // 【2026-09-22】底部新增「修炼次数进度条」→ 整块**往上挪了 21**，新条落在最底下：
    //   功法 / 技能格（顶）
    //   气血 → 灵力 → **修炼次数进度**（底）     ← 和用户给的标注图顺序一致
    const float 左内边距 = 20f;
    const float 底边距 = 17f;
    const float 功法尺寸 = 68f;
    const float 格子尺寸 = 46f;
    const float 格子间距 = 5f;
    const float 功法与技能间距 = 10f;
    const float 条高度 = 15f;
    const float 气血条Y = 59f;
    const float 灵力条Y = 38f;
    const float 修炼条Y = 17f;
    const float 功法Y = 80f;
    const float 幕布Y = 160f;
    const float 幕布宽 = 400f;
    const float 幕布高 = 215f;   // 够放 ~10 行正文（主动神通那栏的字段全列出来不会截断）

    static readonly Color 底盘色   = new Color(0.10f, 0.10f, 0.12f, 0.88f);
    static readonly Color 功法盘色 = new Color(0.14f, 0.12f, 0.10f, 0.90f);
    static readonly Color 条底色   = new Color(0.08f, 0.08f, 0.10f, 0.90f);
    static readonly Color 灰罩色   = new Color(0f, 0f, 0f, 0.62f);
    static readonly Color 冷却色   = new Color(0f, 0f, 0f, 0.66f);
    static readonly Color 幕布色   = new Color(0.04f, 0.05f, 0.07f, 0.78f);
    static readonly Color 幕布线色 = new Color(0.78f, 0.52f, 0.33f, 0.85f);
    static readonly Color 修炼色   = new Color(0.86f, 0.72f, 0.32f, 1f);

    [MenuItem("修仙/生成游戏界面 HUD")]
    [MenuItem("Cultivation/Build Player HUD")]
    public static void Build()
    {
        var font = AssetDatabase.LoadAssetAtPath<Font>(FontPath);
        if (font == null) { Debug.LogError("[HudBuilder] 找不到中文字体 " + FontPath); return; }

        var 方图 = 确保方图();
        if (方图 == null) { Debug.LogError("[HudBuilder] 生成贴图失败"); return; }

        // 幂等：先删掉旧的
        foreach (var old in Object.FindObjectsOfType<PlayerHud>())
            if (old != null) Object.DestroyImmediate(old.gameObject);
        var 旧Canvas = GameObject.Find(CanvasName);
        if (旧Canvas != null) Object.DestroyImmediate(旧Canvas);

        // ---- Canvas ----
        var canvasGo = new GameObject(CanvasName, typeof(RectTransform));
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = -1;          // 让角色面板 / 暂停菜单盖在 HUD 上面
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        // 悬停需要射线：GraphicRaycaster + 场景里的 EventSystem（3C_Testbed 已有）
        canvasGo.AddComponent<GraphicRaycaster>();

        var root = canvasGo.GetComponent<RectTransform>();
        root.anchorMin = Vector2.zero;
        root.anchorMax = Vector2.one;
        root.offsetMin = Vector2.zero;
        root.offsetMax = Vector2.zero;

        var hud = canvasGo.AddComponent<PlayerHud>();

        // ---- 功法格（左）----
        var 功法组 = 锚左下("功法", root, 左内边距, 功法Y, 功法尺寸, 功法尺寸);
        hud.功法图标 = 建图("图标", 功法组, new Color(1f, 1f, 1f, 0f), 方图, 5f);
        悬停接收(功法组, hud, -1, 方图, 功法盘色);

        // ---- 主动技能 6 格 ----
        float 技能X = 左内边距 + 功法尺寸 + 功法与技能间距;
        float 技能Y = 功法Y + 功法尺寸 - 格子尺寸;      // 与功法格顶对齐
        float 栏宽 = 6f * 格子尺寸 + 5f * 格子间距;
        hud.技能格们 = new PlayerHud.技能格[6];

        for (int i = 0; i < 6; i++)
        {
            var 格 = 锚左下("Skill" + (i + 1), root, 技能X + i * (格子尺寸 + 格子间距), 技能Y, 格子尺寸, 格子尺寸);
            var 套装 = new PlayerHud.技能格();

            套装.图标 = 建图("图标", 格, new Color(1f, 1f, 1f, 0f), 方图, 4f);
            套装.灰罩 = 建图("灰罩", 格, 灰罩色, 方图, 0f);
            套装.冷却遮罩 = 建图("冷却遮罩", 格, 冷却色, 方图, 0f);
            var 遮罩 = 套装.冷却遮罩;
            遮罩.type = Image.Type.Filled;
            遮罩.fillMethod = Image.FillMethod.Vertical;          // 方框用上下扫更清楚
            遮罩.fillOrigin = (int)Image.OriginVertical.Top;      // 暗部从顶部起、向上消退
            遮罩.fillAmount = 1f;

            套装.冷却文字 = 建文本("冷却文字", 格, font, "", 20, TextAnchor.MiddleCenter, Color.white);
            铺满(套装.冷却文字.rectTransform);
            加描边(套装.冷却文字.gameObject);

            套装.按键文字 = 建文本("按键", 格, font, (i + 1).ToString(), 13, TextAnchor.LowerRight,
                                    new Color(0.92f, 0.92f, 0.92f, 1f));
            var 键Rt = 套装.按键文字.rectTransform;
            键Rt.anchorMin = Vector2.zero; 键Rt.anchorMax = Vector2.one;
            键Rt.offsetMin = new Vector2(0f, 1f); 键Rt.offsetMax = new Vector2(-4f, -1f);
            // 按键数字要压在任意颜色的技能图标上，没描边在亮色底上会看不见
            加描边(套装.按键文字.gameObject);

            // 底图最后加，再挪到最底层 —— 它同时是鼠标悬停的接收体
            悬停接收(格, hud, i, 方图, 底盘色);

            hud.技能格们[i] = 套装;
        }

        // ---- 气血 / 灵力 / 修炼次数进度条（横跨在格子下方，右端与技能栏对齐）----
        float 条宽 = 功法尺寸 + 功法与技能间距 + 栏宽;   // = 379，右端落在技能栏右缘
        hud.气血填充 = 建条(root, "气血", font, 左内边距, 气血条Y, 条宽, new Color(0.80f, 0.20f, 0.20f, 1f), 方图);
        hud.气血文字 = 找文字(root, "气血");
        hud.灵力填充 = 建条(root, "灵力", font, 左内边距, 灵力条Y, 条宽, new Color(0.26f, 0.54f, 0.88f, 1f), 方图);
        hud.灵力文字 = 找文字(root, "灵力");

        // 修炼次数进度：条 = 距离「下一次修炼次数」的进度（由 PlayerHud 每帧刷新）
        hud.修炼填充 = 建条(root, "修炼", font, 左内边距, 修炼条Y, 条宽, 修炼色, 方图);
        hud.修炼文字 = 找文字(root, "修炼");

        // ---- 信息幕布（最后加，保证画在最上层）----
        建幕布(root, font, hud, 方图);

        // ---- 接引用 ----
        hud.面板数据 = Object.FindObjectOfType<UIPanelData>();
        hud.生命 = Object.FindObjectOfType<PlayerVitals>();
        hud.施放器 = Object.FindObjectOfType<ActiveSkillCaster>();
        hud.修为 = Object.FindObjectOfType<PlayerCultivation>();

        EditorUtility.SetDirty(hud);
        var scene = SceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();

        Debug.Log("[HudBuilder] HUD 生成完成（方框紧凑版）。Canvas=" + CanvasName
                  + " 技能格=" + hud.技能格们.Length
                  + " 幕布=" + (hud.信息幕布 != null)
                  + " 修炼条=" + (hud.修炼填充 != null)
                  + " 面板数据=" + (hud.面板数据 != null)
                  + " 生命=" + (hud.生命 != null)
                  + " 施放器=" + (hud.施放器 != null)
                  + " 修为=" + (hud.修为 != null));
    }

    // ------------------------------------------------------------------ 部件

    /// <summary>给格子加「底图 + 悬停接收」。底图最后加再挪到最底层。</summary>
    static void 悬停接收(RectTransform 格, PlayerHud hud, int 槽位, Sprite 方图, Color 底色)
    {
        var 底 = 建图("底", 格, 底色, 方图, 0f);
        底.raycastTarget = true;                 // 唯一的射线接收体
        底.transform.SetAsFirstSibling();

        var hover = 格.gameObject.AddComponent<HudHoverTarget>();
        hover.hud = hud;
        hover.槽位 = 槽位;
    }

    /// <summary>一条带底、带填充、带数值文字的横条。名字挂在 Tag 上给外面找回来。</summary>
    static Image 建条(RectTransform root, string 名, Font font, float x, float y, float w, Color 色, Sprite 方图)
    {
        var 组 = 锚左下(名, root, x, y, w, 条高度);

        var 底 = 建图("底", 组, 条底色, 方图, 0f);
        底.transform.SetAsFirstSibling();

        var 填 = 建图("填充", 组, 色, 方图, 0f);
        填.type = Image.Type.Filled;
        填.fillMethod = Image.FillMethod.Horizontal;
        填.fillOrigin = (int)Image.OriginHorizontal.Left;
        填.fillAmount = 1f;

        var 文字 = 建文本("文字", 组, font, "0 / 0", 13, TextAnchor.MiddleCenter, Color.white);
        铺满(文字.rectTransform);
        // 描边是给「修炼次数」条准备的：只有它会经常**半格**，白字正好压在
        // 金填充 / 深底的交界上，不加边会糊掉。气血、灵力也一并加上，风格统一。
        加描边(文字.gameObject);

        return 填;
    }

    /// <summary>把刚建好的那条的「文字」子节点找回来</summary>
    static Text 找文字(RectTransform root, string 条名)
    {
        var 条 = root.Find(条名);
        if (条 == null) { Debug.LogWarning("[HudBuilder] 找不到条 " + 条名); return null; }
        var 文字 = 条.Find("文字");
        return 文字 != null ? 文字.GetComponent<Text>() : null;
    }

    /// <summary>鼠标悬停时弹出的半透明信息幕布（默认隐藏）</summary>
    static void 建幕布(RectTransform root, Font font, PlayerHud hud, Sprite 方图)
    {
        var 组 = 锚左下("信息幕布", root, 左内边距, 幕布Y, 幕布宽, 幕布高);

        var 底 = 组.gameObject.AddComponent<Image>();
        底.color = 幕布色;
        底.raycastTarget = false;               // 别挡住下面格子的悬停

        // 顶部一条橙色横线，和面板配色的强调色一致
        var 顶线 = 建图("顶线", 组, 幕布线色, 方图, 0f);
        顶线.rectTransform.anchorMin = new Vector2(0f, 1f);
        顶线.rectTransform.anchorMax = new Vector2(1f, 1f);
        顶线.rectTransform.pivot = new Vector2(0.5f, 1f);
        顶线.rectTransform.anchoredPosition = new Vector2(0f, 0f);
        顶线.rectTransform.sizeDelta = new Vector2(0f, 2f);

        hud.幕布名称 = 建文本("名称", 组, font, "", 19, TextAnchor.UpperLeft, new Color(0.96f, 0.92f, 0.84f));
        hud.幕布名称.rectTransform.anchorMin = new Vector2(0f, 1f);
        hud.幕布名称.rectTransform.anchorMax = new Vector2(1f, 1f);
        hud.幕布名称.rectTransform.pivot = new Vector2(0f, 1f);
        hud.幕布名称.rectTransform.anchoredPosition = new Vector2(12f, -10f);
        hud.幕布名称.rectTransform.sizeDelta = new Vector2(-100f, 26f);

        hud.幕布品阶 = 建文本("品阶", 组, font, "", 14, TextAnchor.UpperRight, new Color(0.85f, 0.62f, 0.36f));
        hud.幕布品阶.rectTransform.anchorMin = new Vector2(0f, 1f);
        hud.幕布品阶.rectTransform.anchorMax = new Vector2(1f, 1f);
        hud.幕布品阶.rectTransform.pivot = new Vector2(1f, 1f);
        hud.幕布品阶.rectTransform.anchoredPosition = new Vector2(-12f, -13f);
        hud.幕布品阶.rectTransform.sizeDelta = new Vector2(90f, 22f);

        var 分隔 = 建图("分隔线", 组, new Color(1f, 1f, 1f, 0.18f), 方图, 0f);
        分隔.rectTransform.anchorMin = new Vector2(0f, 1f);
        分隔.rectTransform.anchorMax = new Vector2(1f, 1f);
        分隔.rectTransform.pivot = new Vector2(0.5f, 1f);
        分隔.rectTransform.anchoredPosition = new Vector2(0f, -40f);
        分隔.rectTransform.sizeDelta = new Vector2(-24f, 1f);

        hud.幕布正文 = 建文本("正文", 组, font, "", 13, TextAnchor.UpperLeft, new Color(0.86f, 0.88f, 0.92f));
        hud.幕布正文.horizontalOverflow = HorizontalWrapMode.Wrap;
        hud.幕布正文.verticalOverflow = VerticalWrapMode.Truncate;
        var 正文Rt = hud.幕布正文.rectTransform;
        正文Rt.anchorMin = Vector2.zero;
        正文Rt.anchorMax = Vector2.one;
        正文Rt.offsetMin = new Vector2(12f, 10f);
        正文Rt.offsetMax = new Vector2(-12f, -46f);

        hud.信息幕布 = 组.gameObject;
        组.gameObject.SetActive(false);
    }

    // ------------------------------------------------------------------ 小工具

    static RectTransform 锚左下(string name, Transform parent, float x, float y, float w, float h)
    {
        var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = Vector2.zero;
        rt.pivot = Vector2.zero;
        rt.anchoredPosition = new Vector2(x, y);
        rt.sizeDelta = new Vector2(w, h);
        return rt;
    }

    static Image 建图(string name, Transform parent, Color color, Sprite sprite, float padding)
    {
        var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        铺满(rt, padding);
        var img = rt.gameObject.AddComponent<Image>();
        img.color = color;
        img.sprite = sprite;
        img.raycastTarget = false;
        return img;
    }

    static Text 建文本(string name, Transform parent, Font font, string content,
                       int size, TextAnchor anchor, Color color)
    {
        var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        var t = rt.gameObject.AddComponent<Text>();
        t.font = font;
        t.fontSize = size;
        t.text = content;
        t.alignment = anchor;
        t.color = color;
        t.raycastTarget = false;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        return t;
    }

    static void 铺满(RectTransform rt, float padding = 0f)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(padding, padding);
        rt.offsetMax = new Vector2(-padding, -padding);
    }

    /// <summary>给文字加一圈深色描边，保证压在任何颜色的图标上都看得清</summary>
    static void 加描边(GameObject go, float 粗细 = 1.2f)
    {
        var o = go.AddComponent<Outline>();
        o.effectColor = new Color(0f, 0f, 0f, 0.9f);
        o.effectDistance = new Vector2(粗细, -粗细);
    }

    // ------------------------------------------------------------------ 贴图资产

    /// <summary>确保有一张方形 sprite 资产。Image 没有 sprite 时【会忽略 fillAmount】，所以必须有。</summary>
    static Sprite 确保方图()
    {
        string path = SpriteDir + "/hud_square.png";
        var 已有 = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if (已有 != null) return 已有;

        确保目录("Assets/Resources");
        确保目录("Assets/Resources/UI");
        确保目录(SpriteDir);

        var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
        for (int y = 0; y < 4; y++)
            for (int x = 0; x < 4; x++)
                tex.SetPixel(x, y, Color.white);
        tex.Apply();

        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

        var imp = AssetImporter.GetAtPath(path) as TextureImporter;
        if (imp != null)
        {
            imp.textureType = TextureImporterType.Sprite;
            imp.spriteImportMode = SpriteImportMode.Single;
            imp.alphaIsTransparency = true;
            imp.mipmapEnabled = false;
            imp.filterMode = FilterMode.Bilinear;
            imp.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    static void 确保目录(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder)) return;
        string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
        string leaf = Path.GetFileName(folder);
        确保目录(parent);
        AssetDatabase.CreateFolder(parent, leaf);
    }
}
