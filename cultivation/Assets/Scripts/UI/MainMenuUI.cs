using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

/// <summary>
/// 开始界面的逻辑。
///
/// 布局按概念图：
///   · 背景是 begin_ui.png 全屏铺满
///   · 左侧竖排四个按钮：新游戏 / 读取存档 / 设置 / 退出
///   · 右侧弹出一个存档面板，里面一列槽位，每格左上「角色名 境界」、右下「最后存档时间」，
///     空槽显示「暂无存档」
///
/// 「新游戏」和「读取存档」共用同一个面板，只是模式不同：
///   新游戏模式 —— 只能点空槽，点了就建新档
///   读取模式   —— 只能点有档的槽，点了就载入
/// </summary>
public class MainMenuUI : MonoBehaviour
{
    [Header("面板")]
    public GameObject 存档面板;
    public Text 面板标题;
    public Transform 槽位容器;
    public Text 提示;

    [Header("按钮")]
    public Button 新游戏按钮;
    public Button 读取存档按钮;
    public Button 设置按钮;
    public Button 退出按钮;
    public Button 关闭按钮;

    [Header("外观")]
    public Font 字体;

    [Header("颜色")]
    public Color 槽位底色 = new Color(0.62f, 0.62f, 0.62f, 1f);
    public Color 槽位空底色 = new Color(0.5f, 0.5f, 0.5f, 1f);
    public Color 文字色 = new Color(0.1f, 0.1f, 0.1f, 1f);

    bool 新游戏模式;
    readonly List<GameObject> 槽位行 = new List<GameObject>();
    float 提示到期;

    // 删除确认：点第一次是「准备删除」，3 秒内再点一次才真删
    int 待删除槽位 = -1;
    float 确认删除到期;

    void Awake()
    {
        if (新游戏按钮 != null) 新游戏按钮.onClick.AddListener(() => 打开面板(true));
        if (读取存档按钮 != null) 读取存档按钮.onClick.AddListener(() => 打开面板(false));
        if (设置按钮 != null) 设置按钮.onClick.AddListener(() => 提示一下("设置功能尚未实现"));
        if (退出按钮 != null) 退出按钮.onClick.AddListener(退出游戏);
        // 关闭按钮原来是在生成器里用 AddListener 挂的，但那个监听器没被序列化下来
        // （实测 GetPersistentEventCount()==0），所以 ✕ 点了没反应。
        // 改成和其它按钮一样，运行时在 Awake 里接，一定生效。
        if (关闭按钮 != null) 关闭按钮.onClick.AddListener(关闭面板);

        if (存档面板 != null) 存档面板.SetActive(false);
        if (提示 != null) 提示.text = "";
    }

    void Update()
    {
        if (提示 != null && 提示.text.Length > 0 && Time.unscaledTime > 提示到期) 提示.text = "";

        // 「确认删除」窗口过期就撤销，避免用户过一会儿点删除结果直接删掉了
        if (待删除槽位 >= 0 && Time.unscaledTime > 确认删除到期)
        {
            待删除槽位 = -1;
            重建槽位();
        }
    }

    void 提示一下(string 内容)
    {
        if (提示 == null) return;
        提示.text = 内容;
        提示到期 = Time.unscaledTime + 3f;
    }

    // ---------------------------------------------------------------- 面板

    void 打开面板(bool 新游戏)
    {
        新游戏模式 = 新游戏;
        待删除槽位 = -1;        // 每次打开都重置删除确认状态
        if (存档面板 != null) 存档面板.SetActive(true);
        if (面板标题 != null) 面板标题.text = 新游戏 ? "选择存档位（新游戏）" : "读取存档";
        重建槽位();
    }

    public void 关闭面板()
    {
        if (存档面板 != null) 存档面板.SetActive(false);
    }

    void 重建槽位()
    {
        // 先 SetParent(null, false) 摘出去，再 Destroy。
        // Destroy 延迟到帧末才生效，只调 Destroy 的话：
        // 同一帧重建两次 → 旧行还挂在布局里、新行又加进来 → 槽位行成倍累积。
        // （项目里 UIEntryList 早就是这么写的，这里踩了同一个坑。）
        foreach (var go in 槽位行)
            if (go != null) { go.transform.SetParent(null, false); Destroy(go); }
        槽位行.Clear();
        if (槽位容器 == null) return;

        var 全部 = SaveSystem.读全部();
        for (int i = 0; i < SaveSystem.槽位数; i++) 建一行(i, 全部[i]);
    }

    void 建一行(int 槽位, SaveData 数据)
    {
        bool 有档 = 数据 != null && !数据.是空的;
        bool 可点 = 新游戏模式 ? !有档 : 有档;

        var 行 = UIBuildUtils.CreateImage("Slot" + 槽位, 槽位容器, 有档 ? 槽位底色 : 槽位空底色);
        var 行Rt = 行.rectTransform;
        var le = 行.gameObject.AddComponent<LayoutElement>();
        le.minHeight = 78f; le.preferredHeight = 78f;

        // UIBuildUtils.CreateImage 默认把 raycastTarget 关掉了（它当底板用）。
        // 但这一行要当按钮，必须打开 —— 否则 Button 永远收不到点击，
        // 表现就是"点存档位没反应"。这是 bug1/bug2 的根因。
        行.raycastTarget = true;

        var 按钮 = 行.gameObject.AddComponent<Button>();
        按钮.targetGraphic = 行;
        按钮.interactable = 可点;
        int 捕获 = 槽位;
        按钮.onClick.AddListener(() => 点槽位(捕获));

        // 左上：角色名 + 境界
        var 左上 = UIBuildUtils.CreateText("Name", 行Rt, 字体,
            有档 ? (数据.角色名 + "    " + 数据.境界) : "暂无存档", 22, TextAnchor.UpperLeft, 文字色);
        UIBuildUtils.Place(左上.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 1f),
            new Vector2(16f, 0f), new Vector2(-16f, -14f));

        if (有档)
        {
            var 右下 = UIBuildUtils.CreateText("Time", 行Rt, 字体, 数据.最后存档时间, 18, TextAnchor.LowerRight, 文字色);
            UIBuildUtils.Place(右下.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 1f),
                new Vector2(16f, 12f), new Vector2(-130f, 0f));

            // 删除按钮：贴在行的右侧。
            // 它是行的【子 Button】，点它会先被它自己消费掉，不会冒泡到整行的 Button，
            // 所以不会"点删除反而读档"。
            var 删 = UIBuildUtils.CreateButton("Delete" + 槽位, 行Rt, 字体,
                待删除槽位 == 槽位 ? "确认删除" : "删除", 18);
            var 删Rt = 删.GetComponent<RectTransform>();
            删Rt.anchorMin = new Vector2(1f, 0.5f);
            删Rt.anchorMax = new Vector2(1f, 0.5f);
            删Rt.pivot = new Vector2(1f, 0.5f);
            删Rt.sizeDelta = new Vector2(96f, 44f);
            删Rt.anchoredPosition = new Vector2(-14f, 0f);

            var 删底色 = 删.GetComponent<Image>();
            if (删底色 != null) 删底色.color = 待删除槽位 == 槽位
                ? new Color(0.85f, 0.35f, 0.3f, 1f)
                : new Color(0.45f, 0.45f, 0.45f, 1f);

            int 捕获2 = 槽位;
            删.onClick.AddListener(() => 请求删除(捕获2));
        }

        槽位行.Add(行.gameObject);
    }

    /// <summary>
    /// 删除需要点两次：第一次变成「确认删除」，3 秒内再点一次才真删。
    /// 存档删了就没了，不值得为省一次点击冒险。
    /// </summary>
    void 请求删除(int 槽位)
    {
        if (待删除槽位 == 槽位 && Time.unscaledTime < 确认删除到期)
        {
            待删除槽位 = -1;
            SaveSystem.删档并补位(槽位);
            提示一下("已删除存档位 " + (槽位 + 1) + "，后面的存档已往前补位");
            重建槽位();          // 立刻刷新，能看到补位结果
            return;
        }

        待删除槽位 = 槽位;
        确认删除到期 = Time.unscaledTime + 3f;
        提示一下("再点一次「确认删除」");
        重建槽位();              // 让按钮变成红色「确认删除」
    }

    void 点槽位(int 槽位)
    {
        var 数据 = SaveSystem.读档(槽位);

        if (新游戏模式)
        {
            if (数据 != null && !数据.是空的) { 提示一下("该存档位已有存档"); return; }
            数据 = SaveSystem.建新档();
            SaveSystem.存档(槽位, 数据);
        }
        else
        {
            if (数据 == null || 数据.是空的) { 提示一下("该存档位暂无存档"); return; }
        }

        SaveSystem.当前存档 = 数据;
        SaveSystem.当前槽位 = 槽位;
        进游戏(数据);
    }

    void 进游戏(SaveData 数据)
    {
        string 场景 = string.IsNullOrEmpty(数据.场景名) ? "3C_Testbed" : 数据.场景名;
        Debug.Log("[MainMenu] 进入场景 " + 场景 + "（槽位 " + SaveSystem.当前槽位 + "）");
        SceneManager.LoadScene(场景);
    }

    void 退出游戏()
    {
        Debug.Log("[MainMenu] 退出游戏");
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // ---- ASCII 别名 ----
    public void OpenSlots(bool newGame) => 打开面板(newGame);
    public void CloseSlots() => 关闭面板();
    public void Quit() => 退出游戏();
}
