using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 文件轮询式的编辑器控制通道 —— 用来替代挂掉的 MCP 桥。
///
/// 原理：本脚本带 [InitializeOnLoad]，Unity 一编译完就会加载它；
/// 之后每帧检查命令文件，有命令就执行，把结果写回结果文件。
/// 这样不需要任何外部进程/端口，只要 Unity 活着就能被驱动。
///
/// 命令文件  D:\project：cultivation\.dsh\cmd.txt     每行一条命令
/// 结果文件  D:\project：cultivation\.dsh\result.txt
///
/// 支持的命令：
///   refresh                 AssetDatabase.Refresh()
///   menu:<菜单路径>          执行菜单项，例如 menu:修仙/生成开始界面场景
///   console:clear           清空 Console
///   console:get[:N]         取最近 N 条日志
///   play:on / play:off      进出播放模式
///   save                    保存当前场景 + 资产
///   open:<场景路径>          打开场景
///   echo:<文本>             回显（测试通道是否活着）
///   shot[:路径]             把主相机渲染成 PNG
///   dump                    打印当前场景结构与 Build Settings
/// </summary>
[InitializeOnLoad]
public static class DshBridge
{
    const string CmdPath = @"D:\project：cultivation\.dsh\cmd.txt";
    const string ResultPath = @"D:\project：cultivation\.dsh\result.txt";
    const string HeartbeatPath = @"D:\project：cultivation\.dsh\alive.txt";

    static double 上次心跳;
    static readonly List<string> 日志缓存 = new List<string>();

    static DshBridge()
    {
        Application.logMessageReceived += (msg, stack, type) =>
        {
            日志缓存.Add("[" + type + "] " + msg);
            if (日志缓存.Count > 400) 日志缓存.RemoveRange(0, 200);
        };
        EditorApplication.update += 轮询;
        Debug.Log("[DshBridge] 文件控制通道已启动");
    }

    static void 轮询()
    {
        // 心跳，方便外部确认 Unity 还活着
        if (EditorApplication.timeSinceStartup - 上次心跳 > 2.0)
        {
            上次心跳 = EditorApplication.timeSinceStartup;
            try { File.WriteAllText(HeartbeatPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")); } catch { }
        }

        if (!File.Exists(CmdPath)) return;

        string 内容;
        try { 内容 = File.ReadAllText(CmdPath); }
        catch { return; }

        // 读完立刻删掉，避免重复执行
        try { File.Delete(CmdPath); } catch { }

        var 输出 = new StringBuilder();
        foreach (var 原始行 in 内容.Split('\n'))
        {
            var 行 = 原始行.Trim('\r', ' ', '\t');
            if (行.Length == 0) continue;
            try { 执行(行, 输出); }
            catch (Exception e) { 输出.Append("!! ").Append(行).Append(" 异常: ").Append(e.Message).Append('\n'); }
        }

        try { File.WriteAllText(ResultPath, 输出.ToString()); } catch { }
    }

    static void 执行(string 行, StringBuilder 输出)
    {
        int 冒号 = 行.IndexOf(':');
        string 命令 = 冒号 < 0 ? 行 : 行.Substring(0, 冒号);
        string 参数 = 冒号 < 0 ? "" : 行.Substring(冒号 + 1);

        switch (命令)
        {
            case "echo":
                输出.Append("OK echo: ").Append(参数).Append('\n');
                break;

            case "refresh":
                AssetDatabase.Refresh();
                输出.Append("OK refresh\n");
                break;

            case "menu":
                {
                    bool ok = EditorApplication.ExecuteMenuItem(参数);
                    输出.Append(ok ? "OK menu: " : "FAIL menu（没这个菜单项？）: ").Append(参数).Append('\n');
                }
                break;

            case "console":
                if (参数 == "clear") { 日志缓存.Clear(); 输出.Append("OK console cleared\n"); }
                else
                {
                    int n = 30;
                    int c2 = 参数.IndexOf(':');
                    if (c2 >= 0) int.TryParse(参数.Substring(c2 + 1), out n);
                    int 起 = Mathf.Max(0, 日志缓存.Count - n);
                    输出.Append("=== console 最近 ").Append(日志缓存.Count - 起).Append(" 条 ===\n");
                    for (int i = 起; i < 日志缓存.Count; i++) 输出.Append(日志缓存[i]).Append('\n');
                }
                break;

            case "play":
                EditorApplication.isPlaying = 参数 == "on";
                输出.Append("OK play=").Append(参数).Append('\n');
                break;

            case "save":
                UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes();
                AssetDatabase.SaveAssets();
                输出.Append("OK save\n");
                break;

            case "open":
                UnityEditor.SceneManagement.EditorSceneManager.OpenScene(参数);
                输出.Append("OK open: ").Append(参数).Append('\n');
                break;

            case "shot":
                {
                    // 把当前场景的主相机渲染成 PNG，用于视觉确认
                    var cam = Camera.main;
                    if (cam == null) { 输出.Append("FAIL shot: 没有 Main Camera\n"); break; }
                    int w = 1280, h = 720;

                    // 截图是要看场景/角色，不是看 UI —— 把 Canvas 整个临时关掉。
                    // （之前试图把 Overlay 切成 ScreenSpaceCamera，结果 UI 反而盖满整张图。）
                    var 关掉的Canvas = new System.Collections.Generic.List<GameObject>();
                    foreach (var c in UnityEngine.Object.FindObjectsOfType<Canvas>())
                        if (c.gameObject.activeSelf) { c.gameObject.SetActive(false); 关掉的Canvas.Add(c.gameObject); }
                    var rt = new RenderTexture(w, h, 24);
                    var 旧target = cam.targetTexture;
                    cam.targetTexture = rt;
                    cam.Render();
                    RenderTexture.active = rt;
                    var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                    tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                    tex.Apply();
                    cam.targetTexture = 旧target;
                    RenderTexture.active = null;
                    var png = tex.EncodeToPNG();
                    string outPath = 参数.Length > 0 ? 参数 : @"D:\project：cultivation\cultivation\screenshots\dsh_shot.png";
                    System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(outPath));
                    System.IO.File.WriteAllBytes(outPath, png);
                    foreach (var g in 关掉的Canvas) g.SetActive(true);
                    UnityEngine.Object.DestroyImmediate(rt);
                    UnityEngine.Object.DestroyImmediate(tex);
                    输出.Append("OK shot: ").Append(outPath).Append('\n');
                }
                break;

            case "bird":
                {
                    // 鸟瞰截图：临时把主相机抬高俯视，拍完还原。用于看村庄整体布局。
                    var cam = Camera.main;
                    if (cam == null) { 输出.Append("FAIL bird: 没有 Main Camera\n"); break; }
                    var 旧pos = cam.transform.position;
                    var 旧rot = cam.transform.rotation;
                    var 旧fov = cam.fieldOfView;

                    // 参数: 高度[,半径]  默认 高度55 半径62
                    float 高 = 55f, 半径 = 62f;
                    if (!string.IsNullOrEmpty(参数))
                    {
                        var parts = 参数.Split(',');
                        if (parts.Length > 0) float.TryParse(parts[0], out 高);
                        if (parts.Length > 1) float.TryParse(parts[1], out 半径);
                    }

                    // 鸟瞰是要看地形，不是看 UI —— 把 Canvas 整个临时关掉，
                    // 否则角色面板之类的会盖满屏幕（第一次拍就是这样）。
                    var 关掉的Canvas = new System.Collections.Generic.List<GameObject>();
                    foreach (var c in UnityEngine.Object.FindObjectsOfType<Canvas>())
                        if (c.gameObject.activeSelf) { c.gameObject.SetActive(false); 关掉的Canvas.Add(c.gameObject); }

                    var look = new Vector3(0f, 0f, 0f);
                    cam.transform.position = new Vector3(半径, 高, -半径);
                    cam.transform.rotation = Quaternion.LookRotation(look - cam.transform.position, Vector3.up);
                    cam.fieldOfView = 55f;

                    int w = 1400, h = 900;
                    var rt = new RenderTexture(w, h, 24);
                    cam.targetTexture = rt; cam.Render();
                    RenderTexture.active = rt;
                    var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                    tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
                    cam.targetTexture = null; RenderTexture.active = null;
                    string op = @"D:\project：cultivation\cultivation\screenshots\bird.png";
                    System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(op));
                    System.IO.File.WriteAllBytes(op, tex.EncodeToPNG());
                    UnityEngine.Object.DestroyImmediate(rt); UnityEngine.Object.DestroyImmediate(tex);

                    cam.transform.position = 旧pos; cam.transform.rotation = 旧rot; cam.fieldOfView = 旧fov;
                    foreach (var g in 关掉的Canvas) g.SetActive(true);
                    输出.Append("OK bird: ").Append(op).Append('\n');
                }
                break;

            case "shot2":
                {
                    // 用 Unity 自己的截图路径（ScreenCapture）作对照，
                    // 验证我那个 Camera.Render() 版本是不是渲染得偏暗。
                    string outPath = 参数.Length > 0 ? 参数 : @"D:\project：cultivation\cultivation\screenshots\dsh_shot2.png";
                    System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(outPath));
                    ScreenCapture.CaptureScreenshot(outPath);
                    输出.Append("OK shot2 (ScreenCapture): ").Append(outPath)
                        .Append("   isPlaying=").Append(EditorApplication.isPlaying).Append('\n');
                }
                break;

            case "dump":
                {
                    var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
                    输出.Append("=== 场景 ").Append(scene.name).Append(" ===\n");
                    foreach (var root in scene.GetRootGameObjects())
                    {
                        输出.Append("* ").Append(root.name).Append("  [").Append(root.GetComponents<Component>().Length).Append(" comps]\n");
                        foreach (Transform c in root.transform)
                        {
                            输出.Append("    - ").Append(c.name)
                              .Append("  pos=").Append(c.position.ToString("F2"));
                            var rs = c.GetComponentsInChildren<Renderer>();
                            if (rs.Length > 0)
                            {
                                var b = rs[0].bounds;
                                for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
                                输出.Append("  size=").Append(b.size.ToString("F1"))
                                  .Append("  y范围=").Append(b.min.y.ToString("F2")).Append("~").Append(b.max.y.ToString("F2"));
                            }
                            输出.Append('\n');
                            if (c.childCount > 0 && c.childCount <= 6)
                                foreach (Transform gc in c) 输出.Append("        .").Append(gc.name)
                                    .Append(" pos=").Append(gc.position.ToString("F1")).Append('\n');
                        }
                    }
                    输出.Append("Build Settings:\n");
                    foreach (var s in EditorBuildSettings.scenes)
                        输出.Append("   ").Append(s.enabled ? "[x] " : "[ ] ").Append(s.path).Append('\n');
                }
                break;

            default:
                输出.Append("?? 未知命令: ").Append(行).Append('\n');
                break;
        }
    }

    // ---- 供菜单手动确认通道可用 ----
    [MenuItem("修仙/测试控制通道")]
    [MenuItem("Cultivation/Test Dsh Bridge")]
    public static void 测试()
    {
        Debug.Log("[DshBridge] 控制通道正常，心跳文件 " + HeartbeatPath);
    }
}
