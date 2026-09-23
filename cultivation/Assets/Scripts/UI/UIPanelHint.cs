using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 面板底部的一句提示条。订阅 UIPanelData.Hint，
/// 弹出一句话并在若干秒后自动淡出（用于「装备栏已满」「请点一下空格」这类反馈）。
/// </summary>
public class UIPanelHint : MonoBehaviour
{
    [Header("引用")]
    public UIPanelData data;
    public Text label;

    [Tooltip("背景底条，会跟着一起显隐")]
    public Image background;

    [Header("外观")]
    public float 显示时长 = 3f;
    public Color 文字色 = new Color(0.98f, 0.92f, 0.70f);
    public Color 底色 = new Color(0.10f, 0.10f, 0.10f, 0.92f);

    float 剩余;

    void OnEnable()
    {
        if (data != null) data.Hint += Show;
        隐藏();
    }

    void OnDisable()
    {
        if (data != null) data.Hint -= Show;
    }

    void Update()
    {
        if (剩余 <= 0f) return;
        剩余 -= Time.unscaledDeltaTime;      // 面板暂停了游戏，得用 unscaled
        if (剩余 <= 0f) 隐藏();
    }

    /// <summary>弹一句提示</summary>
    public void Show(string message)
    {
        if (string.IsNullOrEmpty(message)) return;

        if (label != null) { label.text = message; label.color = 文字色; }
        if (background != null) background.color = 底色;

        显示(true);
        剩余 = 显示时长;
    }

    void 隐藏()
    {
        if (label != null) label.text = "";
        显示(false);
    }

    void 显示(bool on)
    {
        if (label != null) label.gameObject.SetActive(on);
        if (background != null) background.gameObject.SetActive(on);
    }

    // ---- ASCII 别名 ----
    public void ShowHint(string message) => Show(message);
}
