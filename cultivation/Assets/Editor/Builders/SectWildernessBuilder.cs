using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// **宗门野外**（v1）：给 `Sect_Wilderness.scene` 生成一张"靠近宗门的野外"地图。
///
/// 用户要求（2026-09-27）：「这个场景的用处是靠近宗门的野外，需要你创建一张比较大的地图
/// （村庄的 4 倍大小左右），要求使用村庄的那种稍微有起伏地图生成，你看着河流怎么好看怎么放，
/// 树木组团放置，围合出一条环合的主干道，并且有一些分支路，路也是基于材质来体现而不是你创建一条，
/// 做一个生态感的森林」
///
/// ## 尺寸
/// 村庄是 **200×200**，这里 **400×400**（面积正好 4 倍），世界 XZ ∈ [−200, 200]。
///
/// ## 关键设计（沿用村庄那套踩过坑的做法，别乱改）
///
/// 1. **地面用 Unity Terrain**（不是平面）：真实起伏 + 分层混合地表 + 真正的地面碰撞。
/// 2. **地形整体下沉一个"基准"**：高度图只有 0..1（负高度存不下），河床要挖到 −2m 以下，
///    所以 Terrain 放在 `y = −地形基准`，让"高度图 0.25"= 世界 y=0。**忘了会整体抬 8.5m** ✗
/// 3. **高度分辨率必须是 2ⁿ+1**（写 641 这种非法值 Unity 会自己 snap 成 1025，
///    数组对不上 → `SetHeights` **静默失败** → 整张图全 0，地图整体沉下去 ✗）。一律**回读实际值**再算数组。
/// 4. **路是"在地面材质里磨出来的"**，不铺路面网格（用户 2026-09-26 定的调子）。关键三点：
///    ① 宽度用低频噪声摆动 ② 深度用中频噪声摆动 ③ 权重最高 0.92，让底下的草/林地透出来。
/// 5. **路要"长在地形上"**：不能像村庄那样一律压到 y=0（这是 400m 的野山，压平会切出一道深沟）。
///    这里改成压向 **平滑高度场**（把地形高度按 8m 网格低通滤波后的值）——
///    路自然贴着山势起伏，又不会一脚深一脚浅 ✓
/// 6. **河是"刻"进地形的，而且要顺着地形往下流**：
///    每个河点的水面高度 = 平滑高度场采样 − 0.9，再平滑 + **强制单调下降**（水往低处流）；
///    河面网格的顶点高度就按这个水位走（不是村庄那种一整条平的水平面）。
/// 7. **环合主干道**是程序按角度扫出来的一圈（半径带噪声摆动，不是正圆），
///    再挂 5 条分支路 + 2 条淡野径。
/// 8. **树是"成团"的**：先撒 26 个林团（各有自己的**主树种**），
///    再用"林密度场"做拒绝采样 → 团心密、边缘疏，天然有林窗和林缘；
///    树种按**生境**分（水边樟/桃、北坡松、东坡竹…）。
/// 9. 全程幂等：重建先删旧的 `野外环境` 根节点和资产目录。
///
/// 菜单：Cultivation / Build Sect Wilderness (v1)
/// </summary>
public static class SectWildernessBuilder
{
    // ============================================================ 配置

    const string 根名 = "野外环境";
    const string 资产目录 = "Assets/Art/WildernessTerrain";

    /// <summary>地形左下角（世界 XZ）与边长：覆盖 X[−200,200] Z[−200,200]（400×400 = 村庄的 4 倍面积）</summary>
    const float 地形X0 = -200f, 地形Z0 = -200f, 地形边长 = 400f;
    const float 地形高 = 34f;                 // 高度图 0..1 对应 34m
    const float 地形基准 = 地形高 * 0.25f;     // 8.5 → 高度图 0.25 ↔ 世界 y=0
    const int 高度分辨率 = 1025;               // 400m/1024 ≈ 0.39m 一格
    const int 贴图分辨率 = 1024;               // 0.39m 一格（村庄是 0.2m，这里地大，够用）
    const int 细节分辨率 = 512;                // 0.78m 一格（跟村庄一个密度，草才不会糊成一片 / 烧显卡）

    const int 种子 = 20260927;

    // ------------------------------------------------------------ 河流

    /// <summary>
    /// 主河道中心线（世界 XZ，西北山口进 → 东南出）。
    /// 走向完全是我按"好看"摆的：一条大 S 弯穿过谷地，中途在 (30,−50) 处**放开成一个水潭**（半宽 16m），
    /// 其它地方 3~8m。水面高度**不在这里写死**，由 <see cref="准备"/> 按地形算出来（见类注释第 6 条）。
    ///
    /// ★ 点的**疏密很重要**：河要从四周的山口下到谷底，落差有 20m 上下。
    ///   第一版在北段 45m 只用了一个点 → 水面变成一条 45m 长的大斜坡 ✗
    ///   现在按 ~25m 一个点铺开，落差摊到很多段上，才是一条"溪"而不是"滑梯"。
    /// </summary>
    static readonly Vector2[] 河点 =
    {
        new Vector2(-196f,  204f), new Vector2(-170f,  186f), new Vector2(-140f,  170f),
        new Vector2(-112f,  150f), new Vector2( -92f,  124f), new Vector2( -84f,   96f),
        new Vector2( -86f,   68f), new Vector2( -92f,   40f), new Vector2( -88f,   12f),
        new Vector2( -72f,  -12f), new Vector2( -48f,  -30f), new Vector2( -24f,  -42f),
        new Vector2(   4f,  -50f), new Vector2(  30f,  -50f), new Vector2(  56f,  -58f),
        new Vector2(  78f,  -78f), new Vector2(  92f, -104f), new Vector2( 100f, -132f),
        new Vector2( 116f, -160f), new Vector2( 150f, -186f), new Vector2( 196f, -206f),
    };

    /// <summary>主河道每个点的**半宽**（m）：中间那段 16m 就是"水潭"</summary>
    static readonly float[] 河半宽 =
    {
        3.0f, 3.2f, 3.4f, 3.6f, 3.8f, 4.0f, 4.2f, 4.6f, 5.0f, 5.4f, 6.0f,
        6.6f, 7.4f, 16.0f, 8.0f, 6.6f, 5.6f, 4.8f, 4.0f, 3.4f, 3.0f,
    };

    /// <summary>支流：从东北山坳下来汇入主河（源头很细 → 汇入口变宽）。
    /// 最后一个点**故意落在主河的河点上**，两条水面才会真的接上</summary>
    static readonly Vector2[] 支流点 =
    {
        new Vector2(76f, 104f), new Vector2(52f, 72f), new Vector2(26f, 44f), new Vector2(0f, 20f),
        new Vector2(-26f, -6f), new Vector2(-44f, -26f), new Vector2(-48f, -30f),
    };
    static readonly float[] 支流半宽 = { 1.2f, 1.6f, 2.0f, 2.4f, 2.7f, 2.9f, 3.0f };

    const float 河深 = 2.6f;                   // 河床比水面低多少
    const float 渡口浅 = 0.9f;                 // 路跨过河时，河床抬到什么程度（0.9 = 抬掉 90% 深度 → 一片浅滩）

    // ------------------------------------------------------------ 林窗（没有树的空地）

    static readonly Vector2[] 空地点 =
    {
        new Vector2(-78f, -84f), new Vector2(72f, 66f), new Vector2(-126f, 34f),
        new Vector2(104f, -44f), new Vector2(18f, 124f), new Vector2(-24f, -142f),
    };
    static readonly float[] 空地半 = { 20f, 24f, 18f, 22f, 24f, 20f };

    /// <summary>谷地中心留一大片**草甸**（不放林团）：玩家在这儿能看见天，也是视觉上的"呼吸口"</summary>
    static readonly Vector2 草甸心 = new Vector2(-6f, -6f);
    const float 草甸半 = 46f;

    // ------------------------------------------------------------ 刷怪区（用户要的留白）

    /// <summary>
    /// **专门空出来的刷怪区**：用户 2026-09-27「可以稍微留出一些空间用来放刷怪区」。
    /// 这几片地方**不长树**（林团、散生树都避开），草和花照长 → 一眼就能看出是"空地"，
    /// 场景里还会生成同名空物体 + 线框 gizmo 标出来，之后往那儿摆刷怪点即可。
    /// 位置是挑"环路围出来的地块里、彼此隔得开"的点，都离主干道有一段距离。
    /// </summary>
    static readonly Vector2[] 刷怪区 =
    {
        new Vector2(-58f, -56f), new Vector2(58f, 48f),
        new Vector2(-72f, 24f), new Vector2(34f, -74f),
    };
    const float 刷怪区半 = 26f;

    // ------------------------------------------------------------ 边界

    const float 谷底边 = 132f;                 // 这个半径以内是谷地，以外开始起山
    const float 山高 = 20f;                    // 最外圈抬多高（挡住地图边界，别让玩家看见虚空）

    const string Env2 = "Assets/resources/Environment2";
    const string Env1 = "Assets/resources/Environment1";
    const string 树目录 = "Assets/Environment/Tree/";
    const string 地表贴图 = "Assets/Environment/Models/Materials/Scene/Textures/Dibiao/";
    const string 草贴图目录 = "Assets/Environment/Models/Materials/Scene/Textures/Grass/";

    // ============================================================ 运行时准备的数据

    /// <summary>一条折线 + 每个顶点的"值"（路=路面高度 / 河=水面高度）。路和河共用这一个结构</summary>
    class 线
    {
        public string 名;
        public Vector2[] 点;
        public float[] 值;        // 路：路面高度；河：水面高度
        public float[] 半宽;      // 只有河用
        public float 宽系数;      // 只有路用：路的半宽系数
        public float 强度;        // 只有路用：路面材质最高权重
    }

    static readonly List<线> 路网 = new List<线>();
    static 线 主河, 支河;

    /// <summary>平滑高度场（8m 一格）：路高、水位都从它采样 —— 它的作用就是把地形"低通滤波"</summary>
    const float 平滑格 = 8f;
    static int 平滑数X, 平滑数Z;
    static float[,] 平滑场;

    // ============================================================ 菜单

    [MenuItem("Cultivation/Build Sect Wilderness (v1)")]
    public static void Build()
    {
        if (EditorApplication.isPlaying) { Debug.LogError("[野外环境] 请先退出 Play Mode"); return; }

        var 报告 = new System.Text.StringBuilder();
        var 场景 = EditorSceneManager.GetActiveScene();
        if (场景.path != "Assets/Scenes/Sect_Wilderness.scene")
            报告.AppendLine("  ⚠ 当前场景是「" + 场景.path + "」，不是 Sect_Wilderness.scene —— 确认一下再往下看");

        准备(报告);

        // ---- 幂等：先拆旧的 ----
        var 旧根 = GameObject.Find(根名);
        if (旧根 != null) Object.DestroyImmediate(旧根);
        foreach (var t in Object.FindObjectsOfType<Terrain>())
            if (t != null && t.gameObject.name == "地面") Object.DestroyImmediate(t.gameObject);
        if (AssetDatabase.IsValidFolder(资产目录)) AssetDatabase.DeleteAsset(资产目录);
        建目录(资产目录);

        var 根 = new GameObject(根名);
        Undo.RegisterCreatedObjectUndo(根, "Build Sect Wilderness");

        建地形(根.transform, 报告);
        建河面(根.transform, 报告);
        撒河岸(根.transform, 报告);      // 河岸石头 + 芦苇 + 荷叶
        撒树(根.transform, 报告);
        撒灌木(根.transform, 报告);
        撒花草(根.transform, 报告);
        撒散石(根.transform, 报告);
        建山口(根.transform, 报告);      // 北边出山口那两堆石头
        建刷怪区(根.transform, 报告);    // ★ 留白 + 标出来（用户要的刷怪区）

        EditorSceneManager.MarkSceneDirty(场景);
        AssetDatabase.SaveAssets();
        Debug.Log("[野外环境] 完成\n" + 报告);
    }

    // ============================================================ 准备：路网 / 高度场 / 水位

    static void 准备(System.Text.StringBuilder 报告)
    {
        路网.Clear();

        // ---- 环路（主干道）：按角度扫一圈，半径带噪声 → 不是正圆，也不会撞进山里 ----
        {
            var pts = new List<Vector2>();
            const int 段数 = 22;
            for (int i = 0; i <= 段数; i++)
            {
                float a = i / (float)段数 * Mathf.PI * 2f;
                float r = 132f + 20f * Mathf.Sin(a * 2f + 0.6f) + 9f * Mathf.Sin(a * 3f + 2.3f)
                                + 6f * Mathf.Sin(a * 5f + 1.1f);
                pts.Add(new Vector2(Mathf.Cos(a) * r * 1.08f, Mathf.Sin(a) * r));
            }
            路网.Add(new 线 { 名 = "环路", 点 = pts.ToArray(), 宽系数 = 2.7f, 强度 = 0.92f });
        }

        // ---- 分支路 ----
        // 宗门道：环路北边 → 一直走出地图北缘（这个场景就是"宗门外面"，所以这条路通向宗门）
        路网.Add(new 线 { 名 = "宗门道", 宽系数 = 2.4f, 强度 = 0.9f, 点 = new[]
        {
            环上(96f), new Vector2(10f, 152f), new Vector2(6f, 178f), new Vector2(3f, 202f),
        }});
        // 渡口：环路东侧 → 水潭边（到岸边就停，不过河）
        路网.Add(new 线 { 名 = "渡口路", 宽系数 = 1.9f, 强度 = 0.82f, 点 = new[]
        {
            环上(-16f), new Vector2(74f, -34f), new Vector2(60f, -46f), new Vector2(50f, -50f),
        }});
        // 林间空地：环路西南 → 林窗（那片空地是"人与自然"的集散地）
        路网.Add(new 线 { 名 = "空地路", 宽系数 = 1.9f, 强度 = 0.8f, 点 = new[]
        {
            环上(200f), new Vector2(-96f, -74f), new Vector2(-84f, -86f),
        }});
        // 上山路：环路东北 → 坡上（能俯瞰整片林子）
        路网.Add(new 线 { 名 = "上山路", 宽系数 = 1.7f, 强度 = 0.75f, 点 = new[]
        {
            环上(58f), new Vector2(112f, 92f), new Vector2(128f, 112f), new Vector2(132f, 126f),
        }});
        // 猎径：淡（强度低）—— 走进林子就淡掉，是"人/兽踩出来的"
        路网.Add(new 线 { 名 = "猎径", 宽系数 = 1.1f, 强度 = 0.5f, 点 = new[]
        {
            环上(148f), new Vector2(-34f, 86f), new Vector2(-18f, 74f),
        }});
        路网.Add(new 线 { 名 = "野径", 宽系数 = 1.0f, 强度 = 0.42f, 点 = new[]
        {
            环上(300f), new Vector2(84f, -118f), new Vector2(66f, -140f),
        }});

        // ---- 平滑高度场（8m 一格）----
        平滑数X = Mathf.CeilToInt(地形边长 / 平滑格) + 1;
        平滑数Z = 平滑数X;
        平滑场 = new float[平滑数Z, 平滑数X];
        for (int j = 0; j < 平滑数Z; j++)
            for (int i = 0; i < 平滑数X; i++)
                平滑场[j, i] = 基础高度(地形X0 + i * 平滑格, 地形Z0 + j * 平滑格);

        // 再把平滑场本身也过一遍 3×3 均值 —— 单靠 8m 采样还留着台阶感
        var 副本 = (float[,])平滑场.Clone();
        for (int j = 0; j < 平滑数Z; j++)
            for (int i = 0; i < 平滑数X; i++)
            {
                float s = 0f; int n = 0;
                for (int dj = -1; dj <= 1; dj++)
                    for (int di = -1; di <= 1; di++)
                    {
                        int y = j + dj, x = i + di;
                        if (y < 0 || x < 0 || y >= 平滑数Z || x >= 平滑数X) continue;
                        s += 副本[y, x]; n++;
                    }
                平滑场[j, i] = s / n;
            }

        // ---- 河：水位 = 平滑高度 − 0.9，再平滑 + 强制单调下降（水不能往高处流）----
        主河 = new 线 { 名 = "主河", 点 = 河点, 半宽 = 河半宽, 值 = 算水位(河点, -0.9f) };
        支河 = new 线 { 名 = "支流", 点 = 支流点, 半宽 = 支流半宽, 值 = 算水位(支流点, -0.9f) };
        // 支流汇入口对齐主河水位（否则两条水面接不上，会看见一道"水墙"）。
        // ★ 汇入点是**算出来**的（取离支流末端最近的主河点），别写死下标 —— 河道一改就错位
        {
            int 汇 = 0; float 最近 = float.MaxValue;
            for (int i = 0; i < 主河.点.Length; i++)
            {
                float d = Vector2.Distance(主河.点[i], 支河.点[支河.点.Length - 1]);
                if (d < 最近) { 最近 = d; 汇 = i; }
            }
            float 差 = 主河.值[汇] - 支河.值[支河.值.Length - 1];
            for (int i = 0; i < 支河.值.Length; i++) 支河.值[i] += 差;
            for (int i = 1; i < 支河.值.Length; i++)
                支河.值[i] = Mathf.Min(支河.值[i], 支河.值[i - 1]);
            报告.AppendLine("  支流汇入主河第 " + 汇 + " 点（距 " + 最近.ToString("F1") + "m）");
        }

        // ★ 林团必须在这里就造好：**地表（林下地被）也要用林密()**，
        //   而地表是在 建地形() 里刷的 —— 放到 撒树() 里就晚了，林地材质全是 0 ✗
        造林团(报告);

        报告.AppendLine("  路网 " + 路网.Count + " 条（环路 + 5 分支 + 野径）· 主河 " + 河点.Length + " 点 · 支流 "
                        + 支流点.Length + " 点");
        报告.AppendLine("  水位：北 " + 主河.值[0].ToString("F1") + "m → 南 " + 主河.值[主河.值.Length - 1].ToString("F1") + "m");
    }

    /// <summary>环路中心线上的一个点（按角度取）—— 分支路从它上面长出来，保证接得上</summary>
    static Vector2 环上(float 度)
    {
        float a = 度 * Mathf.Deg2Rad;
        float r = 132f + 20f * Mathf.Sin(a * 2f + 0.6f) + 9f * Mathf.Sin(a * 3f + 2.3f)
                        + 6f * Mathf.Sin(a * 5f + 1.1f);
        return new Vector2(Mathf.Cos(a) * r * 1.08f, Mathf.Sin(a) * r);
    }

    /// <summary>
    /// 算一条河的水位：取平滑高度 − 0.9，横向平滑 3 遍，然后**强制单调下降**。
    /// 为什么必须单调：水往低处流，水位有回升的地方看起来就是"水往山上爬"✗
    /// </summary>
    static float[] 算水位(Vector2[] 点, float 偏移)
    {
        int n = 点.Length;
        var v = new float[n];
        for (int i = 0; i < n; i++) v[i] = 平滑高度(点[i].x, 点[i].y) + 偏移;
        for (int pass = 0; pass < 3; pass++)
        {
            var c = (float[])v.Clone();
            for (int i = 0; i < n; i++)
            {
                float s = 0f; int k = 0;
                for (int d = -1; d <= 1; d++)
                {
                    int j = Mathf.Clamp(i + d, 0, n - 1);
                    s += c[j]; k++;
                }
                v[i] = s / k;
            }
        }
        for (int i = 1; i < n; i++) v[i] = Mathf.Min(v[i], v[i - 1]);
        return v;
    }

    /// <summary>平滑高度场的双线性采样</summary>
    static float 平滑高度(float x, float z)
    {
        if (平滑场 == null) return 基础高度(x, z);
        float fx = Mathf.Clamp((x - 地形X0) / 平滑格, 0f, 平滑数X - 1.001f);
        float fz = Mathf.Clamp((z - 地形Z0) / 平滑格, 0f, 平滑数Z - 1.001f);
        int i0 = (int)fx, j0 = (int)fz;
        float tx = fx - i0, tz = fz - j0;
        float a = Mathf.Lerp(平滑场[j0, i0], 平滑场[j0, i0 + 1], tx);
        float b = Mathf.Lerp(平滑场[j0 + 1, i0], 平滑场[j0 + 1, i0 + 1], tx);
        return Mathf.Lerp(a, b, tz);
    }

    /// <summary>
    /// **纯地貌**高度（不含路、不含河）：低频起伏 + 几个小丘 + 四周环山。
    /// 400m 的野外如果一味压平会很难看，所以这里保持"稍微有起伏"（跟村庄一个调子，只是幅度放大一点）。
    /// </summary>
    static float 基础高度(float x, float z)
    {
        float h = 噪声(x, z) * 6.5f;

        // 几个小丘（让地图有"藏东西的地方"）
        h += 丘(x, z, 44f, -78f, 30f, 7.5f);
        h += 丘(x, z, -118f, 62f, 36f, 9.0f);
        h += 丘(x, z, 126f, 118f, 32f, 8.5f);
        h += 丘(x, z, -132f, -108f, 34f, 7.0f);

        // 四周环山：谷底之外慢慢抬起来，把野外围成一个山谷（也挡住地图边界）。
        // ★ 用 smoothstep 而不是 山³：立方会在中段突然拔起 → 河从山口下来时落差全挤在一两段上，
        //   水面变成一条大斜坡 ✗（第一版就是）。smoothstep 的过渡带长得多。
        float d = new Vector2(x * 0.92f, z).magnitude;
        h += Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(谷底边, 202f, d)) * 山高;

        return h;
    }

    static float 丘(float x, float z, float cx, float cz, float r, float 高)
    {
        float d = Mathf.Sqrt((x - cx) * (x - cx) + (z - cz) * (z - cz));
        if (d > r) return 0f;
        float t = 1f - d / r;
        return 高 * t * t * (3f - 2f * t);
    }

    static float 噪声(float x, float z)
    {
        float a = Mathf.PerlinNoise(x * 0.011f + 21.3f, z * 0.011f + 9.7f) - 0.5f;
        float b = Mathf.PerlinNoise(x * 0.033f + 7.1f, z * 0.033f + 23.2f) - 0.5f;
        float c = Mathf.PerlinNoise(x * 0.095f + 15.5f, z * 0.095f + 4.2f) - 0.5f;
        return a * 1.6f + b * 0.7f + c * 0.22f;
    }

    /// <summary>低频噪声（0~1）：控制**路宽摆动**，波长几十米</summary>
    static float 路噪(float x, float z)
        => Mathf.PerlinNoise(x * 0.045f + 31.7f, z * 0.045f + 17.3f);

    /// <summary>中频噪声（0~1）：控制路"被踩出来的深浅"——有的地方磨得发亮、有的地方还剩草</summary>
    static float 磨损(float x, float z)
        => Mathf.PerlinNoise(x * 0.16f + 5.1f, z * 0.16f + 8.8f);

    // ============================================================ 查询：路 / 河 / 空地 / 林密

    /// <summary>离路网最近的距离 + 那条路本身 + 路面高度（路高 = 该点的平滑高度）</summary>
    static float 查询路(Vector2 p, out 线 哪条, out float 路面高)
    {
        哪条 = null; 路面高 = 0f;
        float 最小 = float.MaxValue;
        foreach (var l in 路网)
            for (int i = 0; i < l.点.Length - 1; i++)
            {
                float d = 到线段距离(p, l.点[i], l.点[i + 1]);
                if (d < 最小) { 最小 = d; 哪条 = l; }
            }
        路面高 = 平滑高度(p.x, p.y);
        return 最小;
    }

    /// <summary>离河最近的距离；顺带取出该处的水面高度与半宽（都按段内插值）</summary>
    static float 查询河(Vector2 p, out float 水高, out float 半宽)
    {
        float 最小 = float.MaxValue; 水高 = 0f; 半宽 = 4f;
        foreach (var l in new[] { 主河, 支河 })
        {
            if (l == null) continue;
            for (int i = 0; i < l.点.Length - 1; i++)
            {
                var a = l.点[i]; var b = l.点[i + 1];
                float d = 到线段距离(p, a, b, out float t);
                if (d < 最小)
                {
                    最小 = d;
                    水高 = Mathf.Lerp(l.值[i], l.值[i + 1], t);
                    半宽 = Mathf.Lerp(l.半宽[i], l.半宽[i + 1], t);
                }
            }
        }
        return 最小;
    }

    /// <summary>在某个林窗/草甸/刷怪区里的程度（1 = 正中，0 = 外面）</summary>
    static float 空地权(Vector2 p)
    {
        float w = 0f;
        for (int i = 0; i < 空地点.Length; i++)
        {
            float d = Vector2.Distance(p, 空地点[i]);
            w = Mathf.Max(w, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(空地半[i], 空地半[i] * 0.55f, d)));
        }
        // 谷地中心那片草甸也算"空地"
        w = Mathf.Max(w, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(草甸半, 草甸半 * 0.6f,
                                                                      Vector2.Distance(p, 草甸心))));
        // ★ 刷怪区也要空（不然"留出来的空间"里全是树，摆了怪也看不见）
        for (int i = 0; i < 刷怪区.Length; i++)
            w = Mathf.Max(w, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(刷怪区半, 刷怪区半 * 0.5f,
                                                                          Vector2.Distance(p, 刷怪区[i]))));
        return w;
    }

    static bool 在刷怪区(Vector2 p, float 余量)
    {
        foreach (var c in 刷怪区)
            if (Vector2.Distance(p, c) < 刷怪区半 + 余量) return true;
        return false;
    }

    // ============================================================ 林团（树木组团放置）

    struct 林团
    {
        public Vector2 心; public float 半; public int 树种; public float 密;
    }
    static readonly List<林团> 林团表 = new List<林团>();

    /// <summary>
    /// 造林团。★ **顺序是"先路网、再填空档"**（用户 2026-09-27 的要求）：
    /// 「我强调了树木组团放置，你可以先建好路网，路中间的空间用组团的树木填充，现在这样有点乱」
    ///
    /// 做法：
    ///   ① 在谷地里铺一层 **24m 间距的抖动点阵**（抖动 ±6.5m，看不出网格感）；
    ///   ② 每个点先筛一遍 —— 离路 &lt; 20m 的丢掉（**路就是地块边界**）、贴河/草甸/林窗/刷怪区的丢掉、
    ///      爬上山脚的丢掉 → 剩下的就是"被路围出来的空档"；
    ///   ③ 再**贪心挑团心**：新团心跟已有团心至少隔 36m → 团与团之间天然留出缝，
    ///      看过去才是"一团一团的林子"，而不是糊成一片 ✗
    ///   ④ 每个团按**生境**给主树种（水边林/北坡松/东坡竹/西坡阔叶…），一眼能看出树种分区。
    /// </summary>
    static void 造林团(System.Text.StringBuilder 报告)
    {
        林团表.Clear();
        var rng = new System.Random(种子 + 101);

        // ① + ② 候选点：先建好路网，再看"哪里是路中间的空档"
        var 候选 = new List<Vector2>();
        const float 步距 = 20f;
        for (float z = 地形Z0 + 26f; z < 地形Z0 + 地形边长 - 26f; z += 步距)
            for (float x = 地形X0 + 26f; x < 地形X0 + 地形边长 - 26f; x += 步距)
            {
                var p = new Vector2(x + ((float)rng.NextDouble() - 0.5f) * 11f,
                                    z + ((float)rng.NextDouble() - 0.5f) * 11f);
                if (查询路(p, out _, out _) < 17f) continue;               // ★ 路是地块边界
                float 离河 = 查询河(p, out _, out float 半宽);
                if (离河 < 半宽 + 15f) continue;                            // 河岸留给护岸林，不放团
                if (空地权(p) > 0.18f) continue;                            // 草甸/林窗/刷怪区不放团
                if (new Vector2(p.x * 0.92f, p.y).magnitude > 176f) continue;  // 别爬上山
                候选.Add(p);
            }

        // ③ 打乱后贪心取团心，彼此至少隔 30m（≈ 团半径之和 → 团之间刚好留一条缝）
        for (int i = 候选.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            var t = 候选[i]; 候选[i] = 候选[j]; 候选[j] = t;
        }
        foreach (var c in 候选)
        {
            if (林团表.Count >= 40) break;
            bool 太近 = false;
            foreach (var t in 林团表)
                if (Vector2.Distance(t.心, c) < 30f) { 太近 = true; break; }
            if (太近) continue;

            float 离河 = 查询河(c, out _, out _);
            int 种;
            if (离河 < 60f) 种 = (rng.NextDouble() < 0.75) ? 3 : 5;         // 水边：樟林 / 桃林
            else if (c.y > 62f) 种 = (rng.NextDouble() < 0.7) ? 0 : 2;       // 北坡：松 / 古树
            else if (c.x > 66f) 种 = (rng.NextDouble() < 0.6) ? 4 : 1;       // 东坡：竹 / 阔叶
            else if (c.x < -66f) 种 = (rng.NextDouble() < 0.6) ? 1 : 6;      // 西坡：阔叶 / 混交
            else 种 = (rng.NextDouble() < 0.6) ? 6 : 1;

            林团表.Add(new 林团
            {
                心 = c,
                // ★ 团要**小而密**：半径 15~25m 塞 60~90 棵，树冠才会连成一片（"林"）
                半 = 15f + (float)rng.NextDouble() * 10f,
                树种 = 种,
                密 = 0.85f + (float)rng.NextDouble() * 0.35f,
            });
        }
        报告.AppendLine("  林团 " + 林团表.Count + " 个（候选点 " + 候选.Count
                        + " 个：路网先定 → 只在路与路之间的空档里成团，团心互相隔 36m 以上）");
    }

    /// <summary>林密度场：0 = 没树，1 = 林子最密处（团心）。树、灌木、林地材质都按它来</summary>
    static float 林密(Vector2 p)
    {
        float 密 = 0f;
        foreach (var t in 林团表)
        {
            float d = Vector2.Distance(p, t.心);
            if (d > t.半) continue;
            float k = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(t.半, t.半 * 0.25f, d));
            密 = Mathf.Max(密, k * t.密);
        }
        // 河谷林带 / 野外打底：留一点点本底密度，让林团之间不是光秃秃的
        // （★ 别调大：这个值会同时进"林下地被"材质，大了整张图发黄发暗 ✗）
        密 = Mathf.Max(密, 0.12f);
        return Mathf.Clamp01(密 * (1f - 空地权(p)));
    }

    /// <summary>该点属于哪个林团的主树种（取"权重最大"的那个团）</summary>
    static int 主树种(Vector2 p)
    {
        int 种 = 6; float 最大 = -1f;
        foreach (var t in 林团表)
        {
            float d = Vector2.Distance(p, t.心);
            if (d > t.半 * 1.15f) continue;
            float k = t.密 * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(t.半 * 1.15f, 0f, d));
            if (k > 最大) { 最大 = k; 种 = t.树种; }
        }
        return 种;
    }

    // ============================================================ 地形

    static Terrain 建地形(Transform 父, System.Text.StringBuilder 报告)
    {
        var 图层 = 建图层们(报告);

        var 数据 = new TerrainData();
        数据.heightmapResolution = 高度分辨率;
        数据.size = new Vector3(地形边长, 地形高, 地形边长);
        数据.alphamapResolution = 贴图分辨率;
        数据.baseMapResolution = 512;
        数据.SetDetailResolution(细节分辨率, 8);
        AssetDatabase.CreateAsset(数据, 资产目录 + "/wilderness_terrain.asset");
        数据.terrainLayers = 图层;

        // ★ 一律回读实际分辨率再算数组（见类注释第 3 条，村庄在这上面栽过）
        int 实高图 = 数据.heightmapResolution;
        if (实高图 != 高度分辨率)
            报告.AppendLine("  ⚠ 高度图分辨率被 Unity 改成 " + 实高图 + "（要的是 " + 高度分辨率 + "），已按实际值重算");
        if (数据.alphamapResolution != 贴图分辨率)
            报告.AppendLine("  ⚠ 地表分辨率被改成 " + 数据.alphamapResolution);
        if (数据.detailWidth != 细节分辨率)
            报告.AppendLine("  ⚠ 细节分辨率被改成 " + 数据.detailWidth);

        数据.SetHeights(0, 0, 算高度(实高图));
        数据.SetAlphamaps(0, 0, 算地表());

        var 原型 = 建草原型(报告);
        数据.detailPrototypes = 原型;
        刷草(数据, 原型.Length);

        var go = Terrain.CreateTerrainGameObject(数据);
        go.name = "地面";
        go.transform.SetParent(父, false);
        go.transform.position = new Vector3(地形X0, -地形基准, 地形Z0);   // ★ 见类注释第 2 条
        var 地 = go.GetComponent<Terrain>();
        地.drawInstanced = true;
        地.detailObjectDistance = 70f;
        地.detailObjectDensity = 1f;
        地.heightmapPixelError = 4f;
        EditorUtility.SetDirty(数据);
        报告.AppendLine("  地形：400×400m（村庄的 4 倍）· 高度图 " + 实高图 + "² · 地表 " + 图层.Length
                        + " 层 · 草原型 " + 原型.Length + " 种");
        return 地;
    }

    static float[,] 算高度(int 分辨率)
    {
        var 高 = new float[分辨率, 分辨率];
        float 步 = 地形边长 / (分辨率 - 1);

        for (int j = 0; j < 分辨率; j++)
        {
            float wz = 地形Z0 + j * 步;
            for (int i = 0; i < 分辨率; i++)
            {
                float wx = 地形X0 + i * 步;
                var p = new Vector2(wx, wz);

                float h = 基础高度(wx, wz);

                // ① 路：压向**平滑高度**（不是 0）→ 路贴着山势走，又平整能走
                float 离路 = 查询路(p, out _, out float 路面高);
                if (离路 < 9f)
                {
                    float 权 = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(9f, 0.6f, 离路));
                    h = Mathf.Lerp(h, 路面高, 权);
                }
                bool 在路口 = 离路 < 5.5f;

                // ② 河：先按"河谷"往下缓，再刻出河槽；都在最后做（放前面会被路压平覆盖掉）
                float 离河 = 查询河(p, out float 水高, out float 半宽);
                if (离河 < 52f)
                {
                    float 谷权 = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(52f, 半宽 + 3f, 离河));
                    float 谷底 = 水高 + 1.8f;
                    h = Mathf.Min(h, Mathf.Lerp(h, 谷底, 谷权 * 0.9f));      // ★ Min：只往下削，不往上填
                }
                // ★ 河槽要**宽而缓**：第一版只用了 半宽+3 做过渡（≈8m 内掉 2.6m）→
                //   看起来像一条水泥渠 ✗。现在过渡带到 半宽×2.6+9（≈20m），
                //   水面坐在一个浅碗里，两岸是缓坡，才像天然河道 ✓
                float 槽外 = 半宽 * 2.6f + 9f;
                if (离河 < 槽外)
                {
                    float 权 = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(槽外, 半宽 * 0.35f, 离河));
                    float 深 = 河深 * (在路口 ? (1f - 渡口浅) : 1f);          // 路跨河处留一片浅滩
                    h = Mathf.Min(h, Mathf.Lerp(h, 水高 - 深, 权));
                }

                高[j, i] = Mathf.Clamp01((h + 地形基准) / 地形高);
            }
        }
        return 高;
    }

    /// <summary>地表分层：0 草地 · 1 林下地被 · 2 土路 · 3 沙石（河滩/浅滩/山口石径）</summary>
    static float[,,] 算地表()
    {
        const int 层数 = 4;
        var 图 = new float[贴图分辨率, 贴图分辨率, 层数];
        float 步 = 地形边长 / (贴图分辨率 - 1);

        for (int j = 0; j < 贴图分辨率; j++)
        {
            float wz = 地形Z0 + j * 步;
            for (int i = 0; i < 贴图分辨率; i++)
            {
                float wx = 地形X0 + i * 步;
                var p = new Vector2(wx, wz);

                // ---- 沙石：河滩（贴着水边一条，越靠水越沙）----
                float 离河 = 查询河(p, out _, out float 半宽);
                float 沙 = 0f;
                if (离河 < 半宽 + 5f)
                    沙 = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(半宽 + 5f, 半宽 * 0.35f, 离河));

                // ---- 土路：宽度/深浅都带噪声（村庄那版用户认可的手感，别往明显里调）----
                float 土 = 0f;
                float 离路 = 查询路(p, out 线 哪条, out _);
                if (离路 < 10f && 哪条 != null)
                {
                    float 半宽路 = (1.15f + 路噪(wx, wz) * 1.7f) * 哪条.宽系数;
                    float 权 = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(半宽路 + 2.6f, 半宽路 * 0.3f, 离路));
                    权 *= 0.55f + 0.45f * 磨损(wx, wz);
                    土 = Mathf.Max(土, Mathf.Clamp01(权) * 哪条.强度);
                }

                // ---- 山口石径：宗门道最后那一截铺碎石（"到宗门了"的信号）----
                if (wz > 168f && Mathf.Abs(wx - 6f) < 14f)
                    沙 = Mathf.Max(沙, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(14f, 5f, Mathf.Abs(wx - 6f))) * 0.8f);

                // ---- 林下地被：林子密的地方地表发暗（有机质/落叶），是"生态感"的关键一笔 ----
                float 林 = 林密(p) * 0.78f;
                林 *= 1f - Mathf.Clamp01(土);
                林 *= 1f - 沙;

                土 = Mathf.Max(土, 沙 * 0.45f);
                float 草 = Mathf.Clamp01(1f - Mathf.Max(沙, Mathf.Max(土, 林)));

                float 总 = 草 + 林 + 土 + 沙;
                if (总 < 0.0001f) { 草 = 1f; 总 = 1f; }
                图[j, i, 0] = 草 / 总;
                图[j, i, 1] = 林 / 总;
                图[j, i, 2] = 土 / 总;
                图[j, i, 3] = 沙 / 总;
            }
        }
        return 图;
    }

    static readonly string[] 草名单 =
    {
        "Grass018wx", "Grass019wx", "Grass039wx", "Grass050wx", "Grass073wx",
        "Grass091wx", "Grass037wx", "Grass081wx",
        "Grass064wx",     // ★ 最后这个是**芦苇**（高、细），只往水边刷
    };
    const int 芦苇原型 = 8;

    static DetailPrototype[] 建草原型(System.Text.StringBuilder 报告)
    {
        var 列表 = new List<DetailPrototype>();
        for (int i = 0; i < 草名单.Length; i++)
        {
            var t = AssetDatabase.LoadAssetAtPath<Texture2D>(草贴图目录 + 草名单[i] + ".tga");
            if (t == null) { 报告.AppendLine("  ✗ 找不到草贴图 " + 草名单[i]); continue; }
            bool 芦 = (i == 芦苇原型);
            列表.Add(new DetailPrototype
            {
                prototypeTexture = t,
                usePrototypeMesh = false,
                renderMode = DetailRenderMode.GrassBillboard,
                minWidth = 芦 ? 0.7f : 0.5f,
                maxWidth = 芦 ? 1.5f : 1.0f,
                minHeight = 芦 ? 0.8f : 0.35f,
                maxHeight = 芦 ? 1.5f : 0.95f,
                healthyColor = 芦 ? new Color(0.52f, 0.66f, 0.32f) : new Color(0.45f, 0.70f, 0.30f),
                dryColor = new Color(0.66f, 0.66f, 0.30f),
                noiseSpread = 0.5f,
            });
        }
        return 列表.ToArray();
    }

    /// <summary>
    /// 刷草。★ 每一格**只选一种草**刷 1~3 株（9 种一起刷就是 9 倍密度，肉眼糊成一片还烧显卡）。
    /// 密度按"生境"给：草甸/空地 3 株、林下 1 株、路面 0、水边刷芦苇。
    /// </summary>
    static void 刷草(TerrainData 数据, int 原型数)
    {
        if (原型数 <= 0) return;
        var 层 = new int[原型数][,];
        for (int k = 0; k < 原型数; k++) 层[k] = new int[细节分辨率, 细节分辨率];

        float 步 = 地形边长 / (细节分辨率 - 1);
        for (int j = 0; j < 细节分辨率; j++)
        {
            float wz = 地形Z0 + j * 步;
            for (int i = 0; i < 细节分辨率; i++)
            {
                float wx = 地形X0 + i * 步;
                var p = new Vector2(wx, wz);

                float 离河 = 查询河(p, out _, out float 半宽);
                float 离路 = 查询路(p, out _, out _);

                if (离河 <= 半宽 + 0.5f) continue;                 // 水里不长
                if (离路 < 2.3f) continue;                          // 路面上不长草

                int 密;
                float 林 = 林密(p);
                if (离河 < 半宽 + 5f)
                {
                    // 河岸：芦苇（只在水边刷，不然整张图都是芦苇）
                    if (芦苇原型 >= 原型数) continue;
                    层[芦苇原型][j, i] = 3;
                    continue;
                }
                if (林 > 0.55f) 密 = 1;                             // 林下：稀
                else if (空地权(p) > 0.4f) 密 = 3;                  // 草甸/林窗：旺
                else 密 = 2;
                if (密 <= 0) continue;

                int 选 = (int)(哈希(i, j) % (uint)原型数);
                层[选][j, i] = 密;
            }
        }
        for (int k = 0; k < 原型数; k++) 数据.SetDetailLayer(0, 0, k, 层[k]);
    }

    static uint 哈希(int x, int y)
    {
        uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663);
        h ^= h >> 13; h *= 1274126177u; h ^= h >> 16;
        return h;
    }

    static TerrainLayer[] 建图层们(System.Text.StringBuilder 报告)
    {
        // ★★ 地表贴图**实测平均色**（都是偏米黄的，直接铺上去整张图发白）：
        //   grass009 (74,79,40) · dirt030 (92,78,62) · dirt036 (96,84,60) · dirt022 (103,94,63)
        //   → 所以这里用 `diffuseRemapMax` 给每层**压色**：
        //     草地压成更深的绿、林下地被压成深褐（落叶层），土路/沙石保持原色（路要亮一点才看得见）。
        var 定义 = new[]
        {
            ("草地",     "grass009.bmp",  7f, 0.05f, new Color(0.74f, 0.94f, 0.74f)),
            ("林下地被", "dirt030.bmp",   4f, 0.04f, new Color(0.42f, 0.36f, 0.30f)),
            ("土路",     "dirt036.bmp",   6f, 0.05f, Color.white),
            ("沙石",     "dirt022.bmp",   7f, 0.05f, Color.white),
        };
        var 列表 = new List<TerrainLayer>();
        foreach (var (名, 文件, 平铺, 光滑, 压色) in 定义)
        {
            var 贴 = AssetDatabase.LoadAssetAtPath<Texture2D>(地表贴图 + 文件);
            if (贴 == null) { 报告.AppendLine("  ✗ 找不到地表贴图 " + 文件); continue; }
            var L = new TerrainLayer
            {
                name = 名,
                diffuseTexture = 贴,
                tileSize = new Vector2(平铺, 平铺),
                smoothness = 光滑,
                diffuseRemapMin = Color.black,
                diffuseRemapMax = 压色,
            };
            AssetDatabase.CreateAsset(L, 资产目录 + "/图层_" + 名 + ".terrainlayer");
            列表.Add(L);
        }
        return 列表.ToArray();
    }

    // ============================================================ 水面

    static Material 建纯色水材质(string 名, Color 色, float 光滑 = 0.5f)
    {
        var m = new Material(Shader.Find("Standard"));
        m.SetFloat("_Mode", 3f);
        m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        m.SetInt("_ZWrite", 0);
        m.DisableKeyword("_ALPHATEST_ON");
        m.EnableKeyword("_ALPHABLEND_ON");
        m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        m.SetInt("_Cull", 0);            // ★ 双面：水面是程序拼的带子，绕序反了从上面看就是空的
        m.renderQueue = 3000;
        m.color = 色;
        m.SetFloat("_Glossiness", 光滑);  // ★ 别给高光：平面 + 平行光 = 整条河一片死白 ✗
        AssetDatabase.CreateAsset(m, 资产目录 + "/" + 名 + ".mat");
        return m;
    }

    static void 建河面(Transform 父, System.Text.StringBuilder 报告)
    {
        // ★ 水色要**深**、高光要**弱**：第一版 (0.12,0.30,0.33,0.88) + 光滑 0.35 在俯视/低角度看
        //   是一片惨白的反光，像积水的水泥地 ✗（平行光 + 大量水平面 = 大面积镜面高光）
        var 材质 = 建纯色水材质("野河水面", new Color(0.085f, 0.235f, 0.275f, 0.94f), 0.10f);
        建水带(父, "河流", 主河, 材质, "河面", 报告);
        建水带(父, "支流", 支河, 材质, "支流面", 报告);
    }

    /// <summary>
    /// 拼一条水面带子。★ 这里和村庄不一样：**每个顶点的高度 = 该处水位**（村子那条是一个水平面），
    /// 因为野外的河要顺着山谷往下流（水位在 <see cref="准备"/> 里算好了，是单调下降的）。
    /// </summary>
    static void 建水带(Transform 父, string 名, 线 河, Material 材质, string 网格名, System.Text.StringBuilder 报告)
    {
        var 左 = new List<Vector3>(); var 右 = new List<Vector3>();
        for (int i = 0; i < 河.点.Length; i++)
        {
            Vector2 前 = 河.点[Mathf.Max(0, i - 1)], 后 = 河.点[Mathf.Min(河.点.Length - 1, i + 1)];
            Vector2 切 = (后 - 前).normalized;
            var 法 = new Vector2(-切.y, 切.x);
            float 半 = Mathf.Max(0.6f, 河.半宽[i] - 0.5f);   // 稍微收一点，别让水面盖住河岸
            左.Add(new Vector3(河.点[i].x + 法.x * 半, 河.值[i], 河.点[i].y + 法.y * 半));
            右.Add(new Vector3(河.点[i].x - 法.x * 半, 河.值[i], 河.点[i].y - 法.y * 半));
        }

        var 顶点 = new List<Vector3>(); var uv = new List<Vector2>(); var 三角 = new List<int>();
        float 跑 = 0f;
        for (int i = 0; i < 左.Count; i++)
        {
            if (i > 0) 跑 += Vector3.Distance(左[i - 1], 左[i]);
            顶点.Add(左[i]); uv.Add(new Vector2(0f, 跑 / 14f));
            顶点.Add(右[i]); uv.Add(new Vector2(1f, 跑 / 14f));
        }
        for (int i = 0; i < 左.Count - 1; i++)
        {
            int a = i * 2, b = a + 1, c = a + 2, d = a + 3;
            三角.Add(a); 三角.Add(c); 三角.Add(b);
            三角.Add(b); 三角.Add(c); 三角.Add(d);
        }

        var 网格 = new Mesh { name = 网格名 };
        网格.SetVertices(顶点); 网格.SetUVs(0, uv); 网格.SetTriangles(三角, 0);
        var 法线s = new Vector3[顶点.Count];
        for (int i = 0; i < 法线s.Length; i++) 法线s[i] = Vector3.up;   // ★ 法线直接朝上（绕序不可靠）
        网格.normals = 法线s;
        网格.RecalculateBounds();
        AssetDatabase.CreateAsset(网格, 资产目录 + "/" + 网格名 + ".asset");

        var go = new GameObject(名);
        go.transform.SetParent(父, false);
        go.AddComponent<MeshFilter>().sharedMesh = 网格;
        go.AddComponent<MeshRenderer>().sharedMaterial = 材质;
        报告.AppendLine("  " + 名 + "：" + (左.Count - 1) + " 段水面（水位随地形下降 "
                        + 河.值[0].ToString("F1") + " → " + 河.值[河.值.Length - 1].ToString("F1") + "）");
    }

    // ============================================================ 河岸（石头 / 芦苇 / 荷叶）

    static void 撒河岸(Transform 父, System.Text.StringBuilder 报告)
    {
        var 组 = new GameObject("河岸").transform;
        组.SetParent(父, false);
        var rng = new System.Random(种子 + 7);
        var 石头们 = new[] { "Stone/stone_04.prefab", "Stone/stone_06.prefab", "Stone/stone_08.prefab",
                             "Stone/stone_010.prefab", "Stone/stone_012.prefab", "Stone/stone_013.prefab" };
        int 石 = 0, 苇 = 0, 荷 = 0;

        for (int i = 0; i < 主河.点.Length - 1; i++)
        {
            var A = 主河.点[i]; var B = 主河.点[i + 1];
            float 段长 = Vector2.Distance(A, B);
            int 内 = Mathf.CeilToInt(段长 / 4f);
            for (int k = 0; k < 内; k++)
            {
                float t = k / (float)Mathf.Max(1, 内);
                var 点 = Vector2.Lerp(A, B, t);
                float 半 = Mathf.Lerp(主河.半宽[i], 主河.半宽[i + 1], t);
                float 水 = Mathf.Lerp(主河.值[i], 主河.值[i + 1], t);
                Vector2 切 = (B - A).normalized;
                var 法 = new Vector2(-切.y, 切.x);

                foreach (var 侧 in new[] { 1f, -1f })
                {
                    // 石头：岸边（★ 数量与尺寸都收过：第一版 0.45 概率 × 0.6~1.9 倍，河岸全是巨石 ✗）
                    if (rng.NextDouble() < 0.20)
                    {
                        float 偏 = 半 + 0.4f + (float)rng.NextDouble() * 2.4f;
                        var 位 = 点 + 法 * (偏 * 侧);
                        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Env2 + "/" + 石头们[rng.Next(石头们.Length)]);
                        if (prefab != null)
                        {
                            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, 组);
                            启用全部部件(go); 标静态(go);
                            go.transform.localScale = Vector3.one * (0.35f + (float)rng.NextDouble() * 0.7f);
                            go.transform.localRotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
                            go.transform.position = new Vector3(位.x, 水 + 0.1f, 位.y);
                            石++;
                        }
                    }
                    // 芦苇：贴着水边长（芦苇原型是地形细节，这里补一批"能看见的实体草"）
                    if (rng.NextDouble() < 0.7)
                    {
                        float 偏 = 半 + 0.6f + (float)rng.NextDouble() * 3.2f;
                        var 位 = 点 + 法 * (偏 * 侧);
                        if (放一株草(rng, 组, 位, 0.8f, 1.7f)) 苇++;
                    }
                    // 荷叶：只在"水潭"那一段（半宽 > 10）
                    if (半 > 10f && rng.NextDouble() < 0.6)
                    {
                        float 偏 = ((float)rng.NextDouble() * 2f - 1f) * (半 - 2.5f);
                        var 位 = 点 + 法 * 偏;
                        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                            树目录 + (rng.NextDouble() < 0.5 ? "environment_Tree_heye_001_a.FBX" : "environment_Tree_heye_002_b.FBX"));
                        if (prefab == null)
                            prefab = AssetDatabase.LoadAssetAtPath<GameObject>(树目录 + "environment_Tree_hehua_001_a.FBX");
                        if (prefab != null)
                        {
                            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, 组);
                            启用全部部件(go); 标静态(go);
                            float 高 = 0.5f + (float)rng.NextDouble() * 0.7f;
                            go.transform.localScale = Vector3.one * (高 / 预制高(prefab));
                            go.transform.localRotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
                            go.transform.position = new Vector3(位.x, 水 - 0.15f, 位.y);
                            荷++;
                        }
                    }
                }
            }
        }
        报告.AppendLine("  河岸：石头 " + 石 + " · 芦苇 " + 苇 + " · 荷叶/荷花 " + 荷);
    }

    /// <summary>在河岸放一株实体草（用 Env1 的草模型，按目标高缩放）</summary>
    static readonly string[] 实体草 =
    {
        Env1 + "/Grass/environment_grass_seahaicao_001_a.FBX",
        Env1 + "/Grass/environment_grass_xiaocao_001_a.FBX",
        Env1 + "/Grass/environment_grass_guanmu_001_a.FBX",
        Env1 + "/Grass/environment_grass_guanmu_002_a.FBX",
    };
    static bool 放一株草(System.Random rng, Transform 组, Vector2 位, float 矮, float 高)
    {
        var 路径 = 实体草[rng.Next(实体草.Length)];
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(路径);
        if (prefab == null) return false;
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, 组);
        启用全部部件(go); 标静态(go);
        go.transform.localScale = Vector3.one * (Mathf.Lerp(矮, 高, (float)rng.NextDouble()) / 预制高(prefab));
        go.transform.localRotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
        go.transform.position = new Vector3(位.x, 地形高度(位), 位.y);
        return true;
    }

    // ============================================================ 植被

    /// <summary>
    /// 树种池。<c>林型池[i]</c> 就是第 i 种"林型"里能用的树（各带一个**目标高**区间）。
    /// ★ 用目标高而不是缩放系数：这些资源原生高度差 3 倍以上，写死系数会有的变巨物、有的看不见。
    /// ★ 不放蘑菇（用户明确讨厌 `Mogu/*` 那套粉色巨伞）；也不放黄叶/枯树/雪松（要的是**绿**的生态林）。
    /// </summary>
    static readonly (string 路, float 矮, float 高)[][] 林型池 =
    {
        // 0 松林
        new[] { (树目录 + "environment_Tree_songshu_002_d.FBX", 6.5f, 10.5f),
                (树目录 + "environment_Tree_Green_005_d.FBX", 5.5f, 8.5f) },
        // 1 阔叶林
        new[] { (树目录 + "environment_Tree_Green_001_a.FBX", 5.0f, 8.0f),
                (树目录 + "environment_Tree_Green_005_a.FBX", 5.5f, 8.5f),
                (Env2 + "/Tree/S1_shu002_tf.prefab", 4.5f, 7.0f) },
        // 2 古树（大树）
        new[] { (Env2 + "/Tree/S1_shu001_tf.prefab", 6.0f, 9.5f),
                (树目录 + "environment_Tree_dashu_001_a.FBX", 7.0f, 11.0f) },
        // 3 水边林（★ 不含 `environment_Tree_zhangshu_01_a`：实拍是一棵**苏铁/棕榈状的放射叶**，
        //   在温带林子里非常出戏，而且它原来占了 780 棵 ✗）
        new[] { (树目录 + "environment_Tree_Green_001_a.FBX", 5.0f, 8.0f),
                (树目录 + "environment_Tree_Green_005_a.FBX", 5.5f, 8.5f),
                (树目录 + "environment_Tree_qingduanzhu_001_a.FBX", 3.5f, 5.5f) },
        // 4 竹林
        new[] { (树目录 + "environment_Tree_qingduanzhu_001_a.FBX", 4.0f, 7.0f) },
        // 5 桃林（★ 混一半绿树：整团粉太假）
        new[] { (树目录 + "environment_Tree_taoshu_001_a.FBX", 3.5f, 6.0f),
                (树目录 + "environment_Tree_Green_005_a.FBX", 5.0f, 7.5f) },
        // 6 混交林
        new[] { (树目录 + "environment_Tree_songshu_002_d.FBX", 6.0f, 9.5f),
                (树目录 + "environment_Tree_Green_001_a.FBX", 5.0f, 8.0f),
                (树目录 + "environment_Tree_Green_005_a.FBX", 5.5f, 8.5f),
                (Env2 + "/Tree/S1_shu002_tf.prefab", 4.5f, 7.0f),
                (Env2 + "/Tree/S1_shu001_tf.prefab", 6.0f, 9.0f),
                (树目录 + "environment_Tree_qingduanzhu_001_a.FBX", 3.5f, 6.0f) },
    };

    // ★★ 树种是**实测**筛过的（在一个空场景里逐个渲俯视图、量树冠平均色）：
    //   `environment_Tree_dashu_003_a` 平均色 (49,29,1) → **100% 暖色 = 纯橘黄** ✗
    //   `S1_shu003_tf`                 平均色 (88,53,38) → 46% 暖色，也偏橘 ✗
    //   `environment_tree_purple_001`  平均色 (17,24,22) → 太暗，远看就是一团黑 ✗
    //   `environment_Tree_taoshu_001_a` 平均色 (74,45,46) → 桃花粉（有意保留，但别整片都是）
    //   用户要的是**绿的生态林**（村庄那次也说过"混了太多秋色，整片林子偏橘"）→ 这三种一律不进池子。

    static void 撒树(Transform 父, System.Text.StringBuilder 报告)
    {
        var 组 = new GameObject("森林").transform;
        组.SetParent(父, false);
        var rng = new System.Random(种子);
        var 已放 = new List<Vector3>();
        int 团内 = 0, 散生 = 0;
        var 用过的 = new int[林型池.Length];

        // ★ 分两趟撒，**不能**"全图随机采样 + 设上限"：
        //   那样候选点按面积摊，林团只占地图 ~1/4，上限一到先被散生树吃满，
        //   结果"团"里稀稀拉拉、团外到处都是 —— 完全不是"树木组团" ✗（第一版就是这么写的）
        //   第一趟按**团的面积×密度**分配名额，把团填满；第二趟才补少量散生树。

        // ---- 第一趟：林团内部 ----
        float 总权 = 0f;
        foreach (var t in 林团表) 总权 += t.半 * t.半 * t.密;
        // ★ 团多、每团少：**总棵数不变**，但覆盖的地块多得多 —— 场景文件大小跟总棵数走，
        //   而"森林感"跟**团的覆盖面积**走，所以宁可 40 个小团也不要 20 个大团 ✓
        const int 团内预算 = 1560;
        foreach (var t in 林团表)
        {
            int 名额 = Mathf.Max(12, Mathf.RoundToInt(团内预算 * (t.半 * t.半 * t.密) / Mathf.Max(1f, 总权)));
            int 放下 = 0, 试 = 0;
            while (放下 < 名额 && 试++ < 名额 * 14)
            {
                float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                float r = t.半 * Mathf.Sqrt((float)rng.NextDouble());     // sqrt → 圆面内均匀
                var p = t.心 + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                // 越靠团心越密（边缘自然稀疏 → 不会是个"圆饼"）
                if (rng.NextDouble() > Mathf.Lerp(1f, 0.32f, r / t.半)) continue;
                if (!可放(p, 已放, 2.9f)) continue;

                if (放一棵树(rng, 组, p, t.树种, 已放, 用过的)) 放下++;
            }
            团内 += 放下;
        }

        // ---- 第二趟：团外的散生树（★ 少：第一版撒了 260 棵，满地图零散单树，"乱"主要就是它）----
        for (int i = 0; i < 6000 && 散生 < 110; i++)
        {
            var p = new Vector2(地形X0 + 6f + (float)rng.NextDouble() * (地形边长 - 12f),
                                地形Z0 + 6f + (float)rng.NextDouble() * (地形边长 - 12f));
            if (rng.NextDouble() > 0.2f) continue;
            if (空地权(p) > 0.25f) continue;
            if (!可放(p, 已放, 9.0f)) continue;      // ★ 散生树之间离得更开，才像"野地里零星的几棵"
            if (放一棵树(rng, 组, p, 主树种(p), 已放, 用过的)) 散生++;
        }

        报告.AppendLine("  森林：" + (团内 + 散生) + " 棵（" + 林团表.Count + " 个林团里 " + 团内
                        + " 棵 + 团外散生 " + 散生 + " 棵）");
        报告.AppendLine("    各林型用树：" + string.Join(" · ", System.Array.ConvertAll(用过的, x => x.ToString())));
    }

    /// <summary>放一棵树（树种从对应林型池里随机取，按目标高缩放）</summary>
    static bool 放一棵树(System.Random rng, Transform 组, Vector2 p, int 型,
                          List<Vector3> 已放, int[] 用过的)
    {
        型 = Mathf.Clamp(型, 0, 林型池.Length - 1);
        // 10% 概率混一棵别的（纯林太假，天然林总有杂木）
        if (rng.NextDouble() < 0.10) 型 = rng.Next(林型池.Length);
        var 池 = 林型池[型];
        var (路径, 矮, 高) = 池[rng.Next(池.Length)];
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(路径);
        if (prefab == null) return false;

        float 目标高 = Mathf.Lerp(矮, 高, (float)rng.NextDouble());
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, 组);
        启用全部部件(go); 标静态(go);
        go.transform.localScale = Vector3.one * (目标高 / 预制高(prefab));
        go.transform.localRotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
        go.transform.position = new Vector3(p.x, 地形高度(p), p.y);
        补树干碰撞(go);
        已放.Add(p);
        用过的[型]++;
        return true;
    }

    static void 撒灌木(Transform 父, System.Text.StringBuilder 报告)
    {
        var 组 = new GameObject("灌木与藤蔓").transform;
        组.SetParent(父, false);
        var rng = new System.Random(种子 + 3);
        var 已放 = new List<Vector3>();
        int 放了 = 0;

        var 种类 = new List<(string, float, float)>
        {
            (Env2 + "/Tree/guanmu_010.prefab", 0.9f, 1.8f),
            (Env2 + "/Tree/guanmu_012.prefab", 0.9f, 1.8f),
            (Env2 + "/Tree/guanmu_017.prefab", 1.0f, 2.0f),
            (Env2 + "/Tree/guanmu_022.prefab", 0.9f, 1.8f),
            (Env2 + "/Tree/guanmu_05.prefab",  0.9f, 1.7f),
            (Env2 + "/Tree/guanmu_024.prefab", 0.9f, 1.7f),
            (Env2 + "/Tree/YYZmantuoluo001.prefab", 0.8f, 1.5f),
            (树目录 + "environment_tree_guanmu_001.FBX", 1.0f, 2.2f),
            (树目录 + "environment_Tree_tengman_001_a.FBX", 1.2f, 2.4f),
            (Env1 + "/Grass/environment_grass_guanmu_001_a.FBX", 0.7f, 1.4f),
        };
        种类.RemoveAll(x => AssetDatabase.LoadAssetAtPath<GameObject>(x.Item1) == null);

        for (int i = 0; i < 7000 && 放了 < 620; i++)
        {
            var p = new Vector2(地形X0 + 4f + (float)rng.NextDouble() * (地形边长 - 8f),
                                地形Z0 + 4f + (float)rng.NextDouble() * (地形边长 - 8f));
            float 林 = 林密(p);
            // ★ **林缘最密**：neither 全密林中心 nor 全空草地，0.2~0.75 这一带才是灌木带（生态上的"边缘效应"）
            float 该密 = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.05f, 0.35f, 林))
                       * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.7f, 1f, 林)) * 0.55f);
            if (rng.NextDouble() > 该密) continue;
            if (!可放(p, 已放, 1.6f)) continue;

            var (路径, 矮, 高) = 种类[rng.Next(种类.Count)];
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(路径);
            if (prefab == null) continue;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, 组);
            启用全部部件(go); 标静态(go);
            go.transform.localScale = Vector3.one * (Mathf.Lerp(矮, 高, (float)rng.NextDouble()) / 预制高(prefab));
            go.transform.localRotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
            go.transform.position = new Vector3(p.x, 地形高度(p), p.y);
            已放.Add(p);
            放了++;
        }
        报告.AppendLine("  灌木/藤蔓：" + 放了 + " 丛（**集中在林缘**，团心与空草地都稀）");
    }

    static void 撒花草(Transform 父, System.Text.StringBuilder 报告)
    {
        var 组 = new GameObject("花草").transform;
        组.SetParent(父, false);
        var rng = new System.Random(种子 + 5);
        var 已放 = new List<Vector3>();
        int 放了 = 0;

        var 种类 = new List<(string, float, float)>
        {
            (树目录 + "environment_tree_qihua_002.FBX", 0.5f, 1.0f),
            (树目录 + "environment_Tree_yellow flower_001_a.FBX", 0.4f, 0.9f),
            (树目录 + "environment_tree_purple_001.FBX", 0.5f, 1.1f),
            (树目录 + "environment_Tree_kucao_001_a.FBX", 0.5f, 1.2f),
            (Env1 + "/Grass/environment_grass_xiaocao_001_a.FBX", 0.4f, 0.9f),
        };
        种类.RemoveAll(x => AssetDatabase.LoadAssetAtPath<GameObject>(x.Item1) == null);

        for (int i = 0; i < 9000 && 放了 < 300; i++)
        {
            var p = new Vector2(地形X0 + 4f + (float)rng.NextDouble() * (地形边长 - 8f),
                                地形Z0 + 4f + (float)rng.NextDouble() * (地形边长 - 8f));
            float 林 = 林密(p);
            // 花主要开在**有光的地方**：林窗、草甸、林缘
            float 该密 = 0.25f + 0.75f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.5f, 0.95f, 空地权(p)));
            该密 *= Mathf.Lerp(1f, 0.35f, Mathf.Clamp01(林 / 0.8f));
            if (rng.NextDouble() > 该密) continue;
            if (!可放(p, 已放, 2.0f)) continue;

            var (路径, 矮, 高) = 种类[rng.Next(种类.Count)];
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(路径);
            if (prefab == null) continue;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, 组);
            启用全部部件(go); 标静态(go);
            go.transform.localScale = Vector3.one * (Mathf.Lerp(矮, 高, (float)rng.NextDouble()) / 预制高(prefab));
            go.transform.localRotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
            go.transform.position = new Vector3(p.x, 地形高度(p), p.y);
            已放.Add(p);
            放了++;
        }
        报告.AppendLine("  花草：" + 放了 + " 丛（开在林窗/草甸/林缘这些有光的地方）");
    }

    static void 撒散石(Transform 父, System.Text.StringBuilder 报告)
    {
        var 组 = new GameObject("散石").transform;
        组.SetParent(父, false);
        var rng = new System.Random(种子 + 11);
        var 已放 = new List<Vector3>();
        int 放了 = 0;
        var 石头们 = new[] { "Stone/stone_04.prefab", "Stone/stone_05.prefab", "Stone/stone_06.prefab",
                             "Stone/stone_07.prefab", "Stone/stone_08.prefab", "Stone/stone_09.prefab",
                             "Stone/stone_011.prefab", "Stone/stone_013.prefab" };

        for (int i = 0; i < 4000 && 放了 < 60; i++)
        {
            var p = new Vector2(地形X0 + 6f + (float)rng.NextDouble() * (地形边长 - 12f),
                                地形Z0 + 6f + (float)rng.NextDouble() * (地形边长 - 12f));
            if (rng.NextDouble() > 0.3f) continue;
            if (在刷怪区(p, 4f)) continue;                 // 刷怪区里别摆石头绊脚
            if (!可放(p, 已放, 9.0f)) continue;

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Env2 + "/" + 石头们[rng.Next(石头们.Length)]);
            if (prefab == null) continue;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, 组);
            启用全部部件(go); 标静态(go);
            // ★ 石头**别大**：第一版 0.7~3.1 倍，山坡上到处是几米见方的大石，像塌方 ✗
            go.transform.localScale = Vector3.one * (0.4f + (float)rng.NextDouble() * 1.0f);
            go.transform.localRotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
            go.transform.position = new Vector3(p.x, 地形高度(p) - 0.15f, p.y);
            if (go.GetComponentInChildren<Collider>() == null) 补碰撞体(go);
            已放.Add(p);
            放了++;
        }
        报告.AppendLine("  散石：" + 放了 + " 块（林子里石头本来就多）");
    }

    /// <summary>北边出山口摆两堆大石（"到宗门地界了"的路标；不建房子，保持纯野外）</summary>
    static void 建山口(Transform 父, System.Text.StringBuilder 报告)
    {
        var 组 = new GameObject("山口石").transform;
        组.SetParent(父, false);
        var rng = new System.Random(种子 + 13);
        int 放了 = 0;
        for (int 侧 = -1; 侧 <= 1; 侧 += 2)
            for (int k = 0; k < 3; k++)
            {
                var p = new Vector2(6f + 侧 * (6.5f + k * 2.6f), 186f - k * 5.5f);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Env2 + "/Stone/stone_012.prefab");
                if (prefab == null) continue;
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, 组);
                启用全部部件(go); 标静态(go);
                go.transform.localScale = Vector3.one * (1.6f + (float)rng.NextDouble() * 0.9f);
                go.transform.localRotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
                go.transform.position = new Vector3(p.x, 地形高度(p) - 0.2f, p.y);
                if (go.GetComponentInChildren<Collider>() == null) 补碰撞体(go);
                放了++;
            }
        报告.AppendLine("  山口石：" + 放了 + " 块（北边出山口，通宗门）");
    }

    /// <summary>
    /// 刷怪区：用户 2026-09-27「可以稍微留出一些空间用来放刷怪区」。
    /// 这里只放**空物体 + 一个画圈 gizmo 的标记组件**（<see cref="SpawnZone"/>），
    /// 地上一棵树都没有（<see cref="可放"/> 与 <see cref="空地权"/> 都避开它），
    /// 之后往场景里摆刷怪点/NPC 就行 —— 标出来是为了以后好找。
    /// </summary>
    static void 建刷怪区(Transform 父, System.Text.StringBuilder 报告)
    {
        var 组 = new GameObject("刷怪区").transform;
        组.SetParent(父, false);
        for (int i = 0; i < 刷怪区.Length; i++)
        {
            var go = new GameObject("刷怪区_" + (i + 1).ToString("D2"));
            go.transform.SetParent(组, false);
            var p = 刷怪区[i];
            go.transform.position = new Vector3(p.x, 地形高度(p), p.y);
            var z = go.AddComponent<SpawnZone>();
            z.半径 = 刷怪区半;
        }
        报告.AppendLine("  刷怪区：" + 刷怪区.Length + " 片（半径 " + 刷怪区半 + "m，不长树、草花照长）"
                        + " → " + string.Join("、", System.Array.ConvertAll(刷怪区, v => "(" + v.x + "," + v.y + ")")));
    }

    // ============================================================ 工具

    static bool 可放(Vector2 p, List<Vector3> 已放, float 最小间距)
    {
        if (p.x < 地形X0 + 3f || p.x > 地形X0 + 地形边长 - 3f) return false;
        if (p.y < 地形Z0 + 3f || p.y > 地形Z0 + 地形边长 - 3f) return false;
        if (查询河(p, out _, out float 半宽) < 半宽 + 2.2f) return false;
        if (查询路(p, out _, out _) < 4.5f) return false;      // 路上/路肩不种
        if (在刷怪区(p, 0f)) return false;                      // ★ 刷怪区一棵都不种
        foreach (var q in 已放)
            if ((new Vector2(q.x, q.z) - p).sqrMagnitude < 最小间距 * 最小间距) return false;
        return true;
    }

    /// <summary>地形世界高度（树/石头落地用）</summary>
    static float 地形高度(Vector2 p)
    {
        var 地 = Object.FindObjectOfType<Terrain>();
        if (地 == null || 地.terrainData == null) return 0f;
        float u = Mathf.Clamp01((p.x - 地形X0) / 地形边长);
        float v = Mathf.Clamp01((p.y - 地形Z0) / 地形边长);
        return 地.transform.position.y + 地.terrainData.GetInterpolatedHeight(u, v);
    }

    /// <summary>量一个预制体在 scale=1 时的世界高度（结果缓存）。★ 别拿它当"缩放系数"用</summary>
    static readonly Dictionary<string, float> 原高缓存 = new Dictionary<string, float>();
    static float 预制高(GameObject prefab)
    {
        if (原高缓存.TryGetValue(prefab.name, out var h)) return h;
        var 实例 = (GameObject)Object.Instantiate(prefab);
        实例.transform.position = Vector3.zero;
        实例.transform.rotation = Quaternion.identity;
        实例.transform.localScale = Vector3.one;
        bool 有 = false; var b = new Bounds();
        foreach (var r in 实例.GetComponentsInChildren<Renderer>(true))
        {
            if (r == null || r is ParticleSystemRenderer) continue;
            if (!有) { b = r.bounds; 有 = true; } else b.Encapsulate(r.bounds);
        }
        Object.DestroyImmediate(实例);
        h = 有 ? Mathf.Max(0.05f, b.size.y) : 1f;
        原高缓存[prefab.name] = h;
        return h;
    }

    /// <summary>
    /// 只给树一个**细树干碰撞体**（radius 0.35、高 3m），不是村庄那套"整棵树的大胶囊"。
    /// 原因：这片林子有 1400 棵，用大胶囊的话**御风/坐骑飞过树林会被树冠一路顶起来** ✗
    /// （见 开发注意事项 §48.3）；细树干既挡住"穿树而过"，飞过时又基本不受影响 ✓
    /// </summary>
    static void 补树干碰撞(GameObject go)
    {
        if (go.GetComponentInChildren<Collider>() != null) return;
        float 缩 = Mathf.Max(0.4f, go.transform.lossyScale.x);
        var cap = go.AddComponent<CapsuleCollider>();
        cap.radius = 0.34f * 缩;
        cap.height = 3.0f * 缩;
        cap.center = new Vector3(0f, cap.height * 0.5f, 0f);
    }

    static void 补碰撞体(GameObject go)
    {
        if (go.GetComponentInChildren<Collider>() != null) return;
        var 渲染s = go.GetComponentsInChildren<Renderer>();
        if (渲染s.Length == 0) return;
        var b = 渲染s[0].bounds;
        for (int i = 1; i < 渲染s.Length; i++) b.Encapsulate(渲染s[i].bounds);
        var cap = go.AddComponent<CapsuleCollider>();
        cap.radius = Mathf.Max(0.25f, Mathf.Min(b.size.x, b.size.z) * 0.16f);
        cap.height = Mathf.Max(1f, b.size.y * 0.85f);
        cap.center = new Vector3(0f, cap.height * 0.45f, 0f);
    }

    /// <summary>
    /// 标成 **BatchingStatic**：这片林子几千个同材质的小物件，
    /// 静态合批能省掉大量 draw call（都是不会动的树/石头/草，标了没有副作用）。
    /// </summary>
    static void 标静态(GameObject go)
    {
        try { GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic); }
        catch { /* 个别对象不能标就算了 */ }
    }

    static void 启用全部部件(GameObject go)
    {
        foreach (var tr in go.GetComponentsInChildren<Transform>(true))
        {
            if (!tr.gameObject.activeSelf) tr.gameObject.SetActive(true);
            foreach (var r in tr.GetComponents<Renderer>())
                if (r != null && !r.enabled) r.enabled = true;
        }
    }

    static float 到线段距离(Vector2 p, Vector2 a, Vector2 b) => 到线段距离(p, a, b, out _);

    static float 到线段距离(Vector2 p, Vector2 a, Vector2 b, out float t)
    {
        var ab = b - a;
        float L2 = ab.sqrMagnitude;
        if (L2 < 0.0001f) { t = 0f; return (p - a).magnitude; }
        t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / L2);
        return (p - (a + ab * t)).magnitude;
    }

    static void 建目录(string 路径)
    {
        if (AssetDatabase.IsValidFolder(路径)) return;
        var 父 = System.IO.Path.GetDirectoryName(路径).Replace('\\', '/');
        var 名 = System.IO.Path.GetFileName(路径);
        if (!AssetDatabase.IsValidFolder(父)) 建目录(父);
        AssetDatabase.CreateFolder(父, 名);
    }
}
