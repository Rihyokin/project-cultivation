using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// **特效资源清单工具** —— 导入新的特效资产包之后跑它，一键整理成一张可读的表。
///
/// 为什么需要：特效包里动辄几百个 prefab、命名还是英文缩写，
/// 光看目录根本不知道哪个能当子弹、哪个是命中爆炸、哪个是循环拖尾。
/// 这个工具把关键信息抠出来（路径 / 结构 / 循环 / 会不会自己移动 / 有没有脚本），
/// 并给出「适合当什么」的建议，最后写成一份 markdown。
///
/// 输出：工作区根目录下的 `特效资源清单.md`（不写进 Unity 工程，不污染 Assets）
///
/// 菜单：修仙 / 资源整理 / 生成特效资源清单
/// </summary>
public static class NpcEffectInventory
{
    const string 控制器目录 = "Assets/Animations";

    [MenuItem("修仙/资源整理/生成特效资源清单（当前选中目录）")]
    public static void 生成清单菜单()
    {
        string 目录 = 取选中目录();
        if (string.IsNullOrEmpty(目录))
        {
            Debug.LogWarning("[资源清单] 请先在 Project 窗口里选中一个目录（文件夹），再点这个菜单");
            return;
        }
        生成清单(目录);
    }

    static string 取选中目录()
    {
        var obj = Selection.activeObject;
        if (obj == null) return null;
        string path = AssetDatabase.GetAssetPath(obj);
        if (string.IsNullOrEmpty(path)) return null;
        return Directory.Exists(path) ? path : Path.GetDirectoryName(path).Replace('\\', '/');
    }

    // ============================================================ 主流程

    /// <summary>扫描一个目录（含子目录）下的所有 prefab，生成清单</summary>
    public static void 生成清单(string 目录)
    {
        if (!Directory.Exists(目录)) { Debug.LogError("[资源清单] 目录不存在：" + 目录); return; }

        var 全部 = new List<条目>();
        foreach (var f in Directory.GetFiles(目录, "*.prefab", SearchOption.AllDirectories))
            全部.Add(分析(f.Replace('\\', '/')));

        if (全部.Count == 0) { Debug.LogWarning("[资源清单] " + 目录 + " 下没有 prefab"); return; }

        // 按「建议用途」和「所在子目录」分组
        全部.Sort((a, b) =>
        {
            int c = string.CompareOrdinal(a.建议, b.建议);
            return c != 0 ? c : string.CompareOrdinal(a.相对目录, b.相对目录);
        });

        var 文本 = new StringBuilder();
        文本.AppendLine("# 特效资源清单");
        文本.AppendLine();
        文本.AppendLine("> 由 `NpcEffectInventory` 自动生成（菜单 **修仙 / 资源整理 / 生成特效资源清单**）");
        文本.AppendLine("> 扫描目录：`" + 目录 + "`　共 **" + 全部.Count + "** 个 prefab");
        文本.AppendLine();
        文本.AppendLine("`路径` 列可以直接填进 `NpcAttackConfig.子弹特效路径`（Resources 相对路径、不含扩展名）。");
        文本.AppendLine();

        // ---- 汇总 ----
        文本.AppendLine("## 汇总");
        文本.AppendLine();
        文本.AppendLine("| 建议用途 | 数量 |");
        文本.AppendLine("|---|---|");
        var 计数 = new Dictionary<string, int>();
        foreach (var e in 全部)
            计数[e.建议] = 计数.ContainsKey(e.建议) ? 计数[e.建议] + 1 : 1;
        foreach (var kv in new SortedDictionary<string, int>(计数))
            文本.AppendLine("| " + kv.Key + " | " + kv.Value + " |");
        文本.AppendLine();

        // ---- 明细 ----
        string 上个建议 = null;
        foreach (var e in 全部)
        {
            if (e.建议 != 上个建议)
            {
                上个建议 = e.建议;
                文本.AppendLine("## " + e.建议);
                文本.AppendLine();
                文本.AppendLine("| 名字 | 路径（填这个） | 粒子 | 循环 | 会移动 | 脚本 | 粒子大小 | 散布半径 | 粗估外径 |");
                文本.AppendLine("|---|---|---|---|---|---|---|");
            }
            文本.AppendLine("| " + e.名字
                + " | `" + e.资源路径 + "`"
                + " | " + e.粒子数
                + " | " + (e.循环 ? "是" : "")
                + " | " + (e.会移动 ? "是" : "")
                + " | " + (e.脚本.Length > 0 ? e.脚本 : "")
                + " | " + e.粒子大小.ToString("0.##")
                + " | " + e.散布半径.ToString("0.#")
                + " | " + e.规模.ToString("0.#")
                + " |");
        }

        // 写到工作区根目录（不污染 Unity 工程）
        string 根 = Directory.GetParent(Application.dataPath)?.Parent?.FullName ?? Application.dataPath;
        string 输出 = Path.Combine(根, "特效资源清单.md");
        File.WriteAllText(输出, 文本.ToString(), new UTF8Encoding(true));

        Debug.Log("[资源清单] 扫了 " + 全部.Count + " 个 prefab →\n  " + 输出
            + "\n  " + 汇总一行(全部));
        AssetDatabase.Refresh();
    }

    static string 汇总一行(List<条目> 全部)
    {
        var 计数 = new Dictionary<string, int>();
        foreach (var e in 全部)
            计数[e.建议] = 计数.ContainsKey(e.建议) ? 计数[e.建议] + 1 : 1;
        var sb = new StringBuilder();
        foreach (var kv in new SortedDictionary<string, int>(计数))
            sb.Append(kv.Key + " " + kv.Value + "  ");
        return sb.ToString();
    }

    // ============================================================ 分析一个 prefab

    class 条目
    {
        public string 名字;
        public string 资源路径;      // 可直接填进 子弹特效路径
        public string 相对目录;
        public string 建议;
        public int 粒子数;
        public bool 循环;
        public bool 会移动;
        public string 脚本 = "";
        public float 粒子大小;
        public float 散布半径;
        public float 规模;
    }

    static 条目 分析(string assetPath)
    {
        var e = new 条目 { 名字 = Path.GetFileNameWithoutExtension(assetPath) };
        e.资源路径 = 转Resources路径(assetPath);

        var go = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
        if (go == null) { e.建议 = "0 读不出来"; return e; }

        var 粒子们 = go.GetComponentsInChildren<ParticleSystem>(true);
        e.粒子数 = 粒子们.Length;

        bool 会移动 = false;
        var 脚本名 = new List<string>();

        // 粒子自己会不会飞
        foreach (var ps in 粒子们)
        {
            var main = ps.main;
            if (main.loop) e.循环 = true;

            var vel = ps.velocityOverLifetime;
            if (vel.enabled)
            {
                bool 有速度 = Mathf.Abs(vel.x.constantMax) > 0.01f
                           || Mathf.Abs(vel.y.constantMax) > 0.01f
                           || Mathf.Abs(vel.z.constantMax) > 0.01f;
                if (有速度) 会移动 = true;
            }
        }

        // 有没有脚本在推它（很多子弹包用脚本让特效飞）
        foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true))
            if (mb != null) 脚本名.Add(mb.GetType().Name);
        if (脚本名.Count > 0) 会移动 = true;
        e.脚本 = string.Join("/", 脚本名);

        e.会移动 = 会移动;

        // 规模：**别用 Renderer.bounds** —— prefab 没实例化进场景时它是退化的（量出来是 0）。
        // 对粒子特效改从「粒子大小 + 散布半径」估，这才是「要不要缩放」真正要看的东西。
        float 最大粒子 = 0f, 最大散布 = 0f;
        foreach (var ps in 粒子们)
        {
            最大粒子 = Mathf.Max(最大粒子, ps.main.startSize.constantMax);

            var shape = ps.shape;
            if (!shape.enabled) continue;
            switch (shape.shapeType)
            {
                case ParticleSystemShapeType.Sphere:
                case ParticleSystemShapeType.Hemisphere:
                case ParticleSystemShapeType.Circle:
                case ParticleSystemShapeType.Cone:
                case ParticleSystemShapeType.ConeShell:
                   最大散布 = Mathf.Max(最大散布, shape.radius);
                    break;
                case ParticleSystemShapeType.Box:
                    最大散布 = Mathf.Max(最大散布, shape.scale.x * 0.5f, shape.scale.z * 0.5f);
                    break;
            }
        }
        e.粒子大小 = 最大粒子;
        e.散布半径 = 最大散布;
        e.规模 = 最大散布 * 2f + 最大粒子;      // 粗估外径，够判断量级

        e.建议 = 判建议(e);
        e.相对目录 = Path.GetDirectoryName(assetPath).Replace('\\', '/');
        return e;
    }

    /// <summary>给个「适合当什么」的建议，方便挑</summary>
    static string 判建议(条目 e)
    {
        if (e.粒子数 == 0 && e.脚本.Length == 0) return "9 空壳（没粒子也没脚本）";
        if (e.循环) return "3 循环效果（拖尾 / 常驻）";
        if (e.会移动) return "1 飞行道具（适合当子弹）";
        if (e.粒子数 > 0) return "2 单次爆发（适合当命中 / 爆炸）";
        return "8 其他";
    }

    /// <summary>`Assets/resources/xxx/yyy.prefab` → `xxx/yyy`（Resources.Load 用的写法）</summary>
    static string 转Resources路径(string assetPath)
    {
        const string 前缀 = "Assets/resources/";
        string p = assetPath.Replace('\\', '/');
        if (p.StartsWith(前缀, System.StringComparison.OrdinalIgnoreCase)) p = p.Substring(前缀.Length);
        else if (p.StartsWith("Assets/Resources/", System.StringComparison.OrdinalIgnoreCase)) p = p.Substring("Assets/Resources/".Length);
        return p.Substring(0, p.Length - ".prefab".Length);
    }

    // ---- ASCII 别名 ----
    public static void BuildInventory(string folder) => 生成清单(folder);
}
