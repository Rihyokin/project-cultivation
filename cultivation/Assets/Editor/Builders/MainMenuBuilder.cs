using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;

/// <summary>
/// 生成开始界面场景。
///
/// 布局照概念图：
///   背景   begin_ui.png 全屏铺满（保持比例，多出来的部分裁掉）
///   标题   「修仙」居上
///   左侧   新游戏 / 读取存档 / 设置 / 退出 四个竖排按钮
///   右侧   存档面板（默认隐藏），里面 5 个槽位
///
/// 菜单：修仙 / 生成开始界面场景   （ASCII：Cultivation / Build Main Menu Scene）
/// </summary>
public static class MainMenuBuilder
{
    const string BgPath = "Assets/Resources/UI/MainMenu/begin_ui.png";
    const string FontPath = "Assets/Fonts/SimHei.ttf";
    const string ScenePath = "Assets/Scenes/StartScene.scene";
    const string GameScene = "3C_Testbed";

    [MenuItem("修仙/生成开始界面场景")]
    [MenuItem("Cultivation/Build Main Menu Scene")]
    public static void Build()
    {
        var font = AssetDatabase.LoadAssetAtPath<Font>(FontPath);
        if (font == null) { Debug.LogError("[MainMenuBuilder] 找不到中文字体 " + FontPath); return; }
        var bg = AssetDatabase.LoadAssetAtPath<Sprite>(BgPath);
        if (bg == null) { Debug.LogError("[MainMenuBuilder] 找不到背景图 " + BgPath + "（要把它的 Texture Type 设成 Sprite）"); return; }

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // ---- 相机（背景是 UI，放个纯色相机兜底）----
        var camGo = new GameObject("Main Camera");
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.05f, 0.09f, 0.10f, 1f);
        camGo.tag = "MainCamera";
        camGo.AddComponent<AudioListener>();

        // ---- Canvas ----
        var canvasGo = new GameObject("MenuCanvas");
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();

        var esGo = new GameObject("EventSystem");
        esGo.AddComponent<EventSystem>();
        esGo.AddComponent<StandaloneInputModule>();

        var root = canvasGo.GetComponent<RectTransform>();

        // ---- 背景：全屏铺满，保持比例 ----
        var bgImg = UIBuildUtils.CreateImage("Background", root, Color.white);
        UIBuildUtils.Stretch(bgImg.rectTransform);
        bgImg.sprite = bg;
        bgImg.type = Image.Type.Simple;
        bgImg.preserveAspect = false;   // 概念图是 16:9，直接铺满即可

        // ---- 顶部标题「修仙」----
        var title = UIBuildUtils.CreateText("Title", root, font, "修仙", 150, TextAnchor.UpperCenter,
            new Color(0.06f, 0.09f, 0.10f, 1f));
        UIBuildUtils.Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f),
            new Vector2(0f, -230f), new Vector2(0f, -40f));
        var titleOutline = title.gameObject.AddComponent<Outline>();
        titleOutline.effectColor = new Color(0.75f, 0.95f, 1f, 0.85f);
        titleOutline.effectDistance = new Vector2(2f, -2f);

        // ---- 左侧按钮列 ----
        var 按钮列 = UIBuildUtils.CreateRect("Buttons", root);
        UIBuildUtils.Place(按钮列, new Vector2(0f, 0f), new Vector2(0f, 1f),
            new Vector2(60f, 0f), new Vector2(300f, 0f));
        按钮列.anchorMin = new Vector2(0f, 0.5f);
        按钮列.anchorMax = new Vector2(0f, 0.5f);
        按钮列.pivot = new Vector2(0f, 0.5f);
        按钮列.anchoredPosition = new Vector2(60f, -40f);
        按钮列.sizeDelta = new Vector2(240f, 320f);

        var layout = 按钮列.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 18f;
        layout.childForceExpandHeight = true;
        layout.childForceExpandWidth = true;
        layout.childControlHeight = true;
        layout.childControlWidth = true;

        var bNew = UIBuildUtils.CreateButton("BtnNewGame", 按钮列, font, "新游戏", 28);
        var bLoad = UIBuildUtils.CreateButton("BtnLoad", 按钮列, font, "读取存档", 28);
        var bSet = UIBuildUtils.CreateButton("BtnSettings", 按钮列, font, "设置", 28);
        var bQuit = UIBuildUtils.CreateButton("BtnQuit", 按钮列, font, "退出", 28);

        // ---- 存档面板（默认隐藏）----
        var panelBg = UIBuildUtils.CreateImage("SavePanel", root, new Color(0.86f, 0.87f, 0.87f, 0.96f));
        var pRt = panelBg.rectTransform;
        pRt.anchorMin = new Vector2(0.5f, 0.5f);
        pRt.anchorMax = new Vector2(0.5f, 0.5f);
        pRt.pivot = new Vector2(0.5f, 0.5f);
        pRt.sizeDelta = new Vector2(760f, 780f);
        pRt.anchoredPosition = new Vector2(150f, -20f);

        var 面板标题 = UIBuildUtils.CreateText("PanelTitle", pRt, font, "读取存档", 30, TextAnchor.UpperLeft,
            new Color(0.1f, 0.1f, 0.1f, 1f));
        UIBuildUtils.Place(面板标题.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f),
            new Vector2(34f, -70f), new Vector2(-34f, -22f));

        var closeBtn = UIBuildUtils.CreateButton("BtnClose", pRt, font, "✕", 26);
        var cRt = closeBtn.GetComponent<RectTransform>();
        cRt.anchorMin = new Vector2(1f, 1f); cRt.anchorMax = new Vector2(1f, 1f);
        cRt.pivot = new Vector2(1f, 1f);
        cRt.sizeDelta = new Vector2(52f, 52f);
        cRt.anchoredPosition = new Vector2(-18f, -18f);

        var 槽位容器 = UIBuildUtils.CreateRect("Slots", pRt);
        UIBuildUtils.Place(槽位容器, new Vector2(0f, 0f), new Vector2(1f, 1f),
            new Vector2(34f, 40f), new Vector2(-34f, -92f));
        var slotLayout = 槽位容器.gameObject.AddComponent<VerticalLayoutGroup>();
        slotLayout.spacing = 14f;
        slotLayout.childForceExpandHeight = false;
        slotLayout.childForceExpandWidth = true;
        slotLayout.childControlHeight = true;
        slotLayout.childControlWidth = true;
        slotLayout.childAlignment = TextAnchor.UpperCenter;

        // ---- 提示条 ----
        var 提示 = UIBuildUtils.CreateText("Hint", root, font, "", 24, TextAnchor.LowerCenter,
            new Color(0.95f, 0.85f, 0.6f, 1f));
        UIBuildUtils.Place(提示.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f),
            new Vector2(0f, 40f), new Vector2(0f, 96f));

        // ---- 逻辑组件 ----
        var ui = canvasGo.AddComponent<MainMenuUI>();
        ui.新游戏按钮 = bNew;
        ui.读取存档按钮 = bLoad;
        ui.设置按钮 = bSet;
        ui.退出按钮 = bQuit;
        ui.存档面板 = panelBg.gameObject;
        ui.面板标题 = 面板标题;
        ui.槽位容器 = 槽位容器;
        ui.提示 = 提示;
        ui.字体 = font;
        ui.关闭按钮 = closeBtn;

        // 面板在场景里就设成隐藏，编辑器里看着干净；运行时 MainMenuUI.Awake 也会再关一次
        panelBg.gameObject.SetActive(false);

        // ---- 保存场景 ----
        if (!AssetDatabase.IsValidFolder("Assets/Scenes")) AssetDatabase.CreateFolder("Assets", "Scenes");
        EditorSceneManager.SaveScene(scene, ScenePath);

        // ---- 加进 Build Settings，并把游戏场景也加进去 ----
        AddSceneToBuild(ScenePath);
        AddSceneToBuild("Assets/Scenes/" + GameScene + ".scene");

        Debug.Log("[MainMenuBuilder] 开始界面场景已生成：" + ScenePath);
    }

    static void AddSceneToBuild(string path)
    {
        var 现有 = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        foreach (var s in 现有) if (s.path == path) return;
        现有.Insert(0, new EditorBuildSettingsScene(path, true));
        EditorBuildSettings.scenes = 现有.ToArray();
        Debug.Log("[MainMenuBuilder] 已加入 Build Settings：" + path);
    }
}
