using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// **村庄自然景观**（v4）：按用户手绘的参考图给 village.scene 补上
/// 地面 / 河流 / 村路 / 农田 / 森林 / 院墙 / 草丛。
///
/// 用户图上的标注（顶视，图右=+X、图上=+Z）：
///   · 黄 = 农田（河流西岸，条垄）      · 蓝 = 河流（过石桥、水车）
///   · 红 = 村路（南口→牌坊→村中心→城门；东西支路；过桥去农田）
///   · 绿 = 森林（西北 / 东 / 东南 / 西南 四片）
///   · 灰 = 院墙（围着 S1_jianzhu01_zm 那间大殿，城门就是它的门）
///
/// ## 关键设计（都是踩过的点，别乱改）
///
/// 1. **地面用 Unity Terrain**（不是一块平面）—— 才有真实起伏、分层混合的地表
///    （草地/石板/土路/沙石）、便宜的草 billboard，以及**真正的地面碰撞**
///    （village 场景原来一块地面都没有，玩家会直接掉下去）。
/// 2. **村内绝对压平到 y=0**：用户手摆的 25 个建筑就坐在 y=0，地形一拱它们就浮/陷。
///    所以 `村内` 矩形内高度恒为 0，外面才慢慢起起伏。
/// 3. **地形整体下沉一个"基准"**：Unity 的高度图只有 0..1（负高度存不下），
///    而河床要挖到 -1.7m，所以把地形 GameObject 放在 `y = -地形基准`，
///    让"高度图里 0.25"正好等于世界 y=0。**忘了这一步地面会整体抬 6 米** ✗
/// 4. **河是"刻"进地形的**：沿折线把高度压下去，再铺水面。
///    石桥 `S1_qiao01_tf`（@ -37,17，跨 18m）和水车（@ -39,-21）本来就摆在那儿，
///    河道必须从它们下面穿过去 —— 走向是"以现有建筑为准"反推的。
/// 5. **稻田 = 台地 + 土垄**：每块田一个台高（离河越远越高），垄是**真的网格**
///    （不是地形细节）—— 地形细节的密度粒度是 0.6m 一格，画不出 0.35m 宽的垄 ✗
/// 6. 全程幂等：重建先删旧 `村庄环境` 根节点，资产也重建。
///
/// 菜单：Cultivation / Build Village Nature (v4)
/// </summary>
public static class VillageNatureBuilder
{
    // ============================================================ 配置

    const string 根名 = "村庄环境";
    const string 资产目录 = "Assets/Art/VillageTerrain";

    /// <summary>地形左下角（世界 XZ）与边长：覆盖 X[-115,85] Z[-90,110]</summary>
    const float 地形X0 = -115f, 地形Z0 = -90f, 地形边长 = 200f;
    const float 地形高 = 24f;                  // 高度图 0..1 对应 24m
    const float 地形基准 = 地形高 * 0.25f;      // 高度图 0.25 ↔ 世界 y=0
    /// <summary>★ 必须是 **2ⁿ+1**（33/65/129/257/513/1025/2049）。
    /// 写 641 这种非法值时 Unity 会**自己 snap 成 1025**，而我的数组还是 641 →
    /// `SetHeights` 尺寸对不上、**整张高度图根本没写进去** → 地形全在 0（世界 −6m），整村沉下去 ✗</summary>
    const int 高度分辨率 = 1025;                 // 200m / 1024 ≈ 0.20m 一格
    const int 贴图分辨率 = 1024;
    const int 细节分辨率 = 256;                 // 0.78m 一格：再细草就密到爆

    /// <summary>村内绝对压平区（用户手摆建筑的范围 + 余量），世界高度恒 0</summary>
    static readonly Rect 村内 = new Rect(-52f, -52f, 86f, 124f);   // X[-52,34] Z[-52,72]

    /// <summary>河流中心线（世界 XZ）：北 → 西南。必须穿过石桥 (-37,17) 和水车 (-40,-21)</summary>
    static readonly Vector2[] 河 =
    {
        new Vector2(-22f,  96f), new Vector2(-26f,  70f), new Vector2(-29f,  52f),
        new Vector2(-33f,  34f), new Vector2(-37f,  17f), new Vector2(-39f,   2f),
        new Vector2(-40f, -21f), new Vector2(-45f, -40f), new Vector2(-55f, -55f),
        new Vector2(-70f, -66f), new Vector2(-96f, -78f),
    };
    const float 河半宽 = 4.6f;
    const float 河深 = 1.7f;
    const float 水面高 = -0.55f;

    /// <summary>
    /// 村路中心线（世界 XZ）。**只做中心线**：路是在**地形贴图层**里"磨"出来的（见 算地表()），
    /// 不另铺路面网格 —— 用户 2026-09-26：「退回去……很明显一开始的地形上就利用材质有铺路，
    /// 而不是你现在专门铺的路，非常的不自然……直接从地面着手」。
    ///
    /// 第一条是**主路**：从 <c>S1_caowu02_tf</c>（村门口，(-19,-40)）一路到**院门**（大会堂门口，(-1.7,33)）。
    /// 走线上必须**绕开连成一片的 fangzi 建筑群**（见 §46.6 的坑）。
    /// </summary>
    static readonly Vector2[][] 路 =
    {
        // 主路：村门口（草屋）→ 贴村西侧 → 院门
        new[] {
            new Vector2(-19f, -40f), new Vector2(-18.5f, -33f), new Vector2(-17f, -25f), new Vector2(-16.5f, -14f),
            new Vector2(-16f, -4f), new Vector2(-14f, 5f), new Vector2(-10f, 14f), new Vector2(-6f, 23f),
            new Vector2(-3f, 29f), new Vector2(-1.7f, 33f) },
        // 村中心那条东西街（东边出村 + 过桥去农田）
        new[] {
            new Vector2(48f, 13f), new Vector2(34f, 12f), new Vector2(22f, 11.5f), new Vector2(10f, 11f),
            new Vector2(-0.5f, 10f), new Vector2(-11f, 12.5f), new Vector2(-23f, 15.5f), new Vector2(-37f, 17f),
            new Vector2(-49f, 18.5f), new Vector2(-60f, 20f), new Vector2(-74f, 22f) },
        // 村南那条环西的土路（接村门口）
        new[] {
            new Vector2(-7f, -46f), new Vector2(-18f, -42f), new Vector2(-28f, -32f), new Vector2(-33f, -18f),
            new Vector2(-31f, -4f), new Vector2(-25f, 6f), new Vector2(-12f, 12.5f) },
        // 支路①：主路 → 西侧庑房 S1_fangwu01_tf（(-23,4)，只到它东边门口）
        new[] {
            new Vector2(-15.4f, -1f), new Vector2(-17.5f, 1.5f), new Vector2(-19.2f, 4f) },
        // 支路②：主路 → 西侧庑房 S1_fangwu03_tf（(-24,27)，同上）
        new[] {
            new Vector2(-8.4f, 20.5f), new Vector2(-13f, 24f), new Vector2(-18.4f, 26.5f) },
        // 农田里的小路：从桥路拐进两列田块之间（列间留了 3m；★ 跟着田一起斜）
        new[] {
            田局部到世界(new Vector2(-0.5f, 24f)), 田局部到世界(new Vector2(-0.5f, 10f)),
            田局部到世界(new Vector2(-0.5f, -2f)), 田局部到世界(new Vector2(-0.5f, -14f)),
            田局部到世界(new Vector2(-0.5f, -24f)) },
    };

    /// <summary>一块田：X0,Z0 = **田块局部**（相对 <see cref="田心"/> 的西南角）；台高 = 比地面抬起来多少</summary>
    struct 田块
    {
        public float X0, Z0, X1, Z1, 台高;
        public 田块(float x0, float z0, float x1, float z1, float h) { X0 = x0; Z0 = z0; X1 = x1; Z1 = z1; 台高 = h; }
    }

    /// <summary>整片农田的**旋转中心**（世界 XZ）。田块、田埂、稻垄、田水、小路都绕它转 <see cref="田角"/></summary>
    static readonly Vector2 田心 = new Vector2(-68f, -3f);

    /// <summary>农田整体**顺时针（俯视）倾斜**多少度。
    /// 用户 2026-09-26：「我是想稍微让他们往右侧倾斜一点点」—— 他画的那个黄框本身就是斜的
    /// （右上角比左上角低约 9m/36m ≈ 14°）</summary>
    const float 田角 = 14f;

    /// <summary>世界 XZ → 田块局部 XZ（绕 田心 反向转 田角）</summary>
    static Vector2 到田局部(Vector2 p)
    {
        float c = Mathf.Cos(田角 * Mathf.Deg2Rad), s = Mathf.Sin(田角 * Mathf.Deg2Rad);
        var d = p - 田心;
        return new Vector2(d.x * c - d.y * s, d.x * s + d.y * c);
    }

    /// <summary>田块局部 XZ → 世界 XZ（绕 田心 正向转 田角）</summary>
    static Vector2 田局部到世界(Vector2 q)
    {
        float c = Mathf.Cos(田角 * Mathf.Deg2Rad), s = Mathf.Sin(田角 * Mathf.Deg2Rad);
        return 田心 + new Vector2(q.x * c + q.y * s, -q.x * s + q.y * c);
    }

    /// <summary>
    /// 农田地块（台地）。台高 = 该块比地面抬起来多少（离河越远越高 → 梯田感）。
    ///
    /// ★ 位置是用户 2026-09-26 在 Scene 视图里**圈出来**的：原来那 6 块夹在村子和河之间、
    ///   贴着村中心（X[-70,-46] Z[-10,33]）→「农田的位置也有问题，不是在这里，而是在这里」。
    ///   现在挪到村子**西南**的河湾外侧（中心 <see cref="田心"/>，两列三行，列间留 3m 走田埂小路），
    ///   并整体**顺时针斜 14°**（用户："稍微让他们往右侧倾斜一点点"）。
    ///   ⚠️ 下面的坐标是**局部**的（相对 田心），不是世界坐标。
    /// </summary>
    static readonly 田块[] 田 =
    {
        new 田块(-18f, -19f,  -2f,  -7f, 1.05f), new 田块( 1f, -19f, 18f, -7f, 0.85f),
        new 田块(-18f,  -4f,  -2f,   8f, 0.80f), new 田块( 1f,  -4f, 18f,  8f, 0.60f),
        new 田块(-18f,  11f,  -2f,  19f, 0.55f), new 田块( 1f,  11f, 18f, 19f, 0.35f),
    };

    /// <summary>森林区（世界矩形，xy = 左下角 XZ）</summary>
    static readonly Rect[] 林区 =
    {
        new Rect(-92f,  22f, 34f, 62f),   // 西北片
        new Rect( 30f,  -6f, 34f, 62f),   // 东片
        new Rect(  6f, -70f, 58f, 44f),   // 东南片
        new Rect(-72f, -70f, 24f, 32f),   // 西南片
    };

    /// <summary>院墙折线（围着大殿那块院；南墙在**院门**两侧断开）</summary>
    static readonly Vector2[][] 院墙 =
    {
        new[] { new Vector2(-18f, 33f), new Vector2(-4.1f, 33f) },    // 南墙西段（到门垛西侧）
        new[] { new Vector2(  0.7f, 33f), new Vector2(20f, 33f) },    // 南墙东段（门垛东侧起）
        new[] { new Vector2(-18f, 33f), new Vector2(-18f, 72f) },     // 西墙
        new[] { new Vector2( 20f, 33f), new Vector2(20f, 72f) },      // 东墙
        new[] { new Vector2(-18f, 72f), new Vector2(20f, 72f) },      // 北墙
    };

    /// <summary>院门位置（原来那个 `S1_chengmen01_tf` 就在这儿，已被本工具删掉换成自己生成的）</summary>
    static readonly Vector2 院门 = new Vector2(-1.7f, 33f);

    const string Env2 = "Assets/resources/Environment2";
    const string Env1 = "Assets/resources/Environment1";
    const string Env3 = "Assets/resources/Environment3";
    const string 树目录 = "Assets/Environment/Tree/";
    const string 地表贴图 = "Assets/Environment/Models/Materials/Scene/Textures/Dibiao/";
    const string 草贴图目录 = "Assets/Environment/Models/Materials/Scene/Textures/Grass/";
    const int 种子 = 20260926;

    // ============================================================ 菜单

    [MenuItem("Cultivation/Build Village Nature (v4)")]
    public static void Build()
    {
        if (EditorApplication.isPlaying) { Debug.LogError("[村庄环境] 请先退出 Play Mode"); return; }

        var 报告 = new System.Text.StringBuilder();
        var 场景 = EditorSceneManager.GetActiveScene();

        // ---- 幂等：先拆旧的 ----
        var 旧根 = GameObject.Find(根名);
        if (旧根 != null) Object.DestroyImmediate(旧根);
        foreach (var t in Object.FindObjectsOfType<Terrain>())
            if (t != null && t.gameObject.name == "地面") Object.DestroyImmediate(t.gameObject);
        if (AssetDatabase.IsValidFolder(资产目录)) AssetDatabase.DeleteAsset(资产目录);
        建目录(资产目录);

        var 根 = new GameObject(根名);
        Undo.RegisterCreatedObjectUndo(根, "Build Village Nature");

        // ★ 用户 2026-09-26：「把 S1_chengmen01_tf 删了，自己做一个院门，风格统一一下」
        //   —— 它原来摆在 (-1.7, 0, 32.9)，正好是院墙南墙的缺口；现在换成 建院门()
        var 旧城门 = GameObject.Find("S1_chengmen01_tf");
        if (旧城门 != null)
        {
            Object.DestroyImmediate(旧城门);
            报告.AppendLine("  已删除场景里的 S1_chengmen01_tf（换成自己生成的院门）");
        }

        var 地形 = 建地形(根.transform, 报告);
        建河面(根.transform, 报告);
        撒河岸石头(根.transform, 报告);
        建稻田(根.transform, 报告);
        建院墙(根.transform, 报告);
        建院门(根.transform, 报告);
        撒树(根.transform, 报告);
        撒灌木(根.transform, 报告);
        建空气墙(根.transform, 报告);
        补场景碰撞体(报告);          // ★ 给用户手摆的 24 个建筑/道具补碰撞体（幂等）

        EditorSceneManager.MarkSceneDirty(场景);
        AssetDatabase.SaveAssets();
        Debug.Log("[村庄环境] 完成\n" + 报告);
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
        AssetDatabase.CreateAsset(数据, 资产目录 + "/village_terrain.asset");
        数据.terrainLayers = 图层;

        // ★ 一律**回读实际分辨率**再算数组：
        //   Unity 会把非法的 heightmapResolution（不是 2ⁿ+1）**自己 snap**，
        //   数组尺寸和 terrainData 对不上时 SetHeights 是**静默失败**的 ——
        //   整张高度图全 0 → 地形整体落到 y=-6，整村沉下去 ✗（2026-09-26 实际踩到）
        int 实高图 = 数据.heightmapResolution;
        if (实高图 != 高度分辨率)
            报告.AppendLine("  ⚠ 高度图分辨率被 Unity 改成 " + 实高图 + "（要的是 " + 高度分辨率 + "），已按实际值重算");
        if (数据.alphamapResolution != 贴图分辨率)
            报告.AppendLine("  ⚠ 地表分辨率被改成 " + 数据.alphamapResolution);
        if (数据.detailWidth != 细节分辨率)
            报告.AppendLine("  ⚠ 细节分辨率被改成 " + 数据.detailWidth);

        数据.SetHeights(0, 0, 算高度(实高图));
        数据.SetAlphamaps(0, 0, 算地表());

        var 原型 = 建草原型();
        数据.detailPrototypes = 原型;
        刷草(数据, 原型.Length);
        报告.AppendLine("  地形：高度图实际 " + 实高图 + "² · 地表 " + 图层.Length + " 层 · 草原型 " + 原型.Length + " 种");

        var go = Terrain.CreateTerrainGameObject(数据);
        go.name = "地面";
        go.transform.SetParent(父, false);
        go.transform.position = new Vector3(地形X0, -地形基准, 地形Z0);   // ★ 见类注释第 3 条
        var 地 = go.GetComponent<Terrain>();
        地.drawInstanced = true;
        地.detailObjectDistance = 55f;
        地.detailObjectDensity = 1f;
        地.heightmapPixelError = 4f;
        EditorUtility.SetDirty(数据);
        return 地;
    }

    static DetailPrototype[] 建草原型()
    {
        var 名单 = new[] { "Grass018wx", "Grass050wx", "Grass039wx", "Grass073wx", "Grass091wx", "Grass019wx" };
        var 列表 = new List<DetailPrototype>();
        foreach (var n in 名单)
        {
            var t = AssetDatabase.LoadAssetAtPath<Texture2D>(草贴图目录 + n + ".tga");
            if (t == null) continue;
            列表.Add(new DetailPrototype
            {
                prototypeTexture = t,
                usePrototypeMesh = false,
                renderMode = DetailRenderMode.GrassBillboard,
                minWidth = 0.5f, maxWidth = 1.0f,
                minHeight = 0.35f, maxHeight = 0.95f,
                healthyColor = new Color(0.45f, 0.70f, 0.30f),
                dryColor = new Color(0.66f, 0.66f, 0.30f),
                noiseSpread = 0.5f,
            });
        }
        return 列表.ToArray();
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
                float 离村 = 到矩形外距离(p, 村内);

                // ① 起伏：村外才有，离村越远越明显
                float 起伏权 = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(2f, 26f, 离村));
                float h = 噪声(wx, wz) * 5.0f * 起伏权;

                // 外围一圈矮丘
                float 远 = Mathf.InverseLerp(46f, 74f, 离村);
                h += 远 * 远 * 7.5f;

                // ② 村内绝对压平到 0（★ 只压"地貌起伏"这一步，别把后面的河道一起乘掉）
                h *= Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(2f, 10f, 离村));

                // ③ 村路压平（土路要能走）
                float 离路 = 到路网距离(p);
                if (离路 < 6f)
                    h = Mathf.Lerp(h, 0f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(6f, 1.5f, 离路)));

                // ④ 农田台地（★ 先把世界点转进"田块局部"，田是斜的）
                var 田p = 到田局部(p);
                foreach (var t in 田)
                {
                    float d = 到矩形外距离(田p, Rect.MinMaxRect(t.X0, t.Z0, t.X1, t.Z1));
                    if (d > 3.5f) continue;
                    h = Mathf.Lerp(h, t.台高, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(3.5f, 0.6f, d)));
                }

                // ⑤ ★ 河道**放在最后刻**。
                //   放前面会被"村内压平 / 村路压平"乘掉 —— 桥和水车都在村里，
                //   结果整段河床是平的、水面(-0.55)被地面(0)盖住，从上方看根本没有河 ✗
                float 离河 = 到折线距离(p, 河);
                if (离河 < 河半宽 + 3.5f)
                {
                    float k = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(河半宽 + 3.5f, 河半宽 * 0.3f, 离河));
                    h -= 河深 * k;
                }

                高[j, i] = Mathf.Clamp01((h + 地形基准) / 地形高);
            }
        }
        return 高;
    }

    static float 噪声(float x, float z)
    {
        float a = Mathf.PerlinNoise(x * 0.021f + 11.3f, z * 0.021f + 7.7f) - 0.5f;
        float b = Mathf.PerlinNoise(x * 0.061f + 3.1f, z * 0.061f + 19.2f) - 0.5f;
        float c = Mathf.PerlinNoise(x * 0.17f + 5.5f, z * 0.17f + 2.2f) - 0.5f;
        return a * 1.5f + b * 0.6f + c * 0.18f;
    }

    /// <summary>低频噪声（0~1）：控制**路宽摆动**和村内铺装的不规则边界，波长几十米</summary>
    static float 路噪(float x, float z)
        => Mathf.PerlinNoise(x * 0.045f + 31.7f, z * 0.045f + 17.3f);

    /// <summary>中频噪声（0~1）：控制路"被踩出来的深浅"——有的地方磨得发亮、有的地方还剩草</summary>
    static float 磨损(float x, float z)
        => Mathf.PerlinNoise(x * 0.16f + 5.1f, z * 0.16f + 8.8f);

    /// <summary>地表分层：0 草地 · 1 石板（村内/院内）· 2 土（路与田）· 3 沙石（河岸河床）</summary>
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
                var 田p = 到田局部(p);      // 田块是斜的：判定一律在"田块局部"里做

                float 沙 = 0f, 土 = 0f, 石板 = 0f;

                float 离河 = 到折线距离(p, 河);
                if (离河 < 河半宽 + 3.2f)     // 沙滩带贴着河（原来 6m 太宽，河床像水泥渠）
                    沙 = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(河半宽 + 3.2f, 河半宽 * 0.4f, 离河));

                float 离路 = 到路网距离(p);
                if (离路 < 9f)
                {
                    // ★★ 路是**在地面材质里"磨"出来的**，关键是**别等宽、别等深**：
                    //   ① 宽度用低频噪声摆动（2.4~4.2m）→ 边缘不规则，不像贴上去的条带；
                    //   ② 深度用中频噪声摆动 → 有的地方踩得发白、有的地方草还没被踩掉；
                    //   ③ 权重最高只给 0.92 → 让底下的石板/草地透一点出来，是"磨出来的"而不是"盖上去的"
                    float 半宽 = 1.2f + 路噪(wx, wz) * 1.7f;
                    float 土权 = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(半宽 + 2.6f, 半宽 * 0.3f, 离路));
                    // ★ 用户 2026-09-26 认可的就是这一版手感（"这样自然就挺好的了"）：
                    //   权重压到 0.55~1.0，让底下的石板/草地透出来 —— **别再往明显了调** ✗
                    土权 *= 0.55f + 0.45f * 磨损(wx, wz);
                    土 = Mathf.Max(土, Mathf.Clamp01(土权) * 0.92f);
                }

                bool 在院子 = wx > -18f && wx < 20f && wz > 33f && wz < 72f;
                if (在院子)
                {
                    石板 = 0.95f;                          // 大殿院子：规规矩矩铺石板（有院墙围着，本来就该方正）
                }
                else
                {
                    // 村里的铺装：**不是矩形**，是按"离村中心的距离 + 噪声边界"长出来的，
                    // 边界会摆 3~5m，看着像常年踩出来的一片硬地，而不是盖了个方框 ✗
                    float 村距 = new Vector2(wx - 1f, wz + 9f).magnitude;
                    float 半径 = 15f + 路噪(wx * 1.9f, wz * 1.9f) * 8f;
                    石板 = Mathf.Max(石板, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(半径, 半径 - 7f, 村距)) * 0.85f);
                }

                foreach (var t in 田)
                {
                    float d = 到矩形外距离(田p, Rect.MinMaxRect(t.X0, t.Z0, t.X1, t.Z1));
                    if (d > 1.6f) continue;
                    土 = Mathf.Max(土, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1.6f, 0.2f, d)) * 0.95f);
                }

                土 = Mathf.Max(土, 沙 * 0.45f);
                石板 *= 1f - Mathf.Clamp01(土);          // ★ 有土的地方就不铺石板：路是"磨穿"铺装的
                石板 *= 1f - 沙;
                float 草 = Mathf.Clamp01(1f - Mathf.Max(沙, Mathf.Max(土, 石板)));

                float 总 = 草 + 石板 + 土 + 沙;
                if (总 < 0.0001f) { 草 = 1f; 总 = 1f; }
                图[j, i, 0] = 草 / 总;
                图[j, i, 1] = 石板 / 总;
                图[j, i, 2] = 土 / 总;
                图[j, i, 3] = 沙 / 总;
            }
        }
        return 图;
    }

    /// <summary>
    /// 刷草。★ 每一格**只选一种草**刷 1~3 株：
    /// 6 种原型如果同一格全刷，密度就是 6 倍，肉眼一片糊还烧显卡。
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

                float 离河 = 到折线距离(p, 河);
                float 离路 = 到路网距离(p);
                bool 在院子 = wx > -18f && wx < 20f && wz > 33f && wz < 72f;
                bool 在村里 = wx > -20f && wx < 22f && wz > -40f && wz < 22f;

                int 株 = 2;
                if (在院子) 株 = 0;
                else if (在村里) 株 = 1;
                // 路面上不长草（保持用户认可的那版宽度，别为了"看得见路"去扩它 ✗）
                if (离路 < 2.2f) 株 = 0;
                if (在田里(p, 0f)) 株 = 0;
                if (离河 < 河半宽 + 0.8f) 株 = 0;
                else if (离河 < 河半宽 + 4f) 株 = 3;
                if (株 == 0) continue;

                int 选 = (int)(哈希(i, j) % (uint)原型数);
                层[选][j, i] = 株;
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

    // ============================================================ 水面 / 河岸

    static Material 建水材质(string 名, Color 色)
    {
        var 贴 = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Environment/Terrain/Textures/terrain_water_002.dds");
        var m = new Material(Shader.Find("Standard"));
        m.SetFloat("_Mode", 3f);
        m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        m.SetInt("_ZWrite", 0);
        m.DisableKeyword("_ALPHATEST_ON");
        m.EnableKeyword("_ALPHABLEND_ON");
        m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        m.renderQueue = 3000;
        m.color = 色;
        m.SetFloat("_Glossiness", 0.85f);
        m.SetInt("_Cull", 0);            // ★ 双面：河面是程序拼的带子，从上方看必须可见
        if (贴 != null) { m.mainTexture = 贴; m.mainTextureScale = new Vector2(8f, 40f); }
        AssetDatabase.CreateAsset(m, 资产目录 + "/" + 名 + ".mat");
        return m;
    }

    /// <summary>建一个不带贴图的半透明"水"材质（纯色罩面）。★ 双面：水面是程序拼的带子，绕序不可靠</summary>
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
        m.SetInt("_Cull", 0);            // ★ 双面，免得绕序反了从上方看是空的
        m.renderQueue = 3000;
        m.color = 色;
        m.SetFloat("_Glossiness", 光滑);
        AssetDatabase.CreateAsset(m, 资产目录 + "/" + 名 + ".mat");
        return m;
    }

    static void 建河面(Transform 父, System.Text.StringBuilder 报告)
    {
        // ★ 别用高光水：glossiness 0.85 + 平面 + 平行光 = 整条河一片死白 ✗
        //   河面是程序拼的大平面，要的是"哑光的深青绿水"，高光只是点缀
        var 材质 = 建纯色水材质("河水面", new Color(0.13f, 0.31f, 0.34f, 0.88f), 0.35f);

        var 左 = new List<Vector3>(); var 右 = new List<Vector3>();
        for (int i = 0; i < 河.Length; i++)
        {
            Vector2 前 = 河[Mathf.Max(0, i - 1)], 后 = 河[Mathf.Min(河.Length - 1, i + 1)];
            Vector2 切 = (后 - 前).normalized;
            var 法 = new Vector2(-切.y, 切.x);
            float 半 = 河半宽 - 0.6f;
            左.Add(new Vector3(河[i].x + 法.x * 半, 水面高, 河[i].y + 法.y * 半));
            右.Add(new Vector3(河[i].x - 法.x * 半, 水面高, 河[i].y - 法.y * 半));
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

        var 网格 = new Mesh { name = "河面" };
        网格.SetVertices(顶点); 网格.SetUVs(0, uv); 网格.SetTriangles(三角, 0);
        // ★ 法线**直接写成朝上**：这是程序拼的带子，RecalculateNormals 会跟着绕序走，
        //   绕序一反过来（从上方看是背面）水面就直接隐形了 ✗
        var 法线s = new Vector3[顶点.Count];
        for (int i = 0; i < 法线s.Length; i++) 法线s[i] = Vector3.up;
        网格.normals = 法线s;
        网格.RecalculateBounds();
        AssetDatabase.CreateAsset(网格, 资产目录 + "/河面.asset");

        var go = new GameObject("河流");
        go.transform.SetParent(父, false);
        go.AddComponent<MeshFilter>().sharedMesh = 网格;
        go.AddComponent<MeshRenderer>().sharedMaterial = 材质;
        报告.AppendLine("  河流：" + (左.Count - 1) + " 段水面（y=" + 水面高 + "），材质半透明 Standard");
    }

    static void 撒河岸石头(Transform 父, System.Text.StringBuilder 报告)
    {
        var 组 = new GameObject("河岸石头").transform;
        组.SetParent(父, false);
        var 石头们 = new[] { "Stone/stone_04.prefab", "Stone/stone_06.prefab", "Stone/stone_08.prefab",
                             "Stone/stone_10.prefab", "Stone/stone_012.prefab" };
        var rng = new System.Random(种子 + 7);
        int 放了 = 0;

        for (int i = 0; i < 河.Length - 1; i++)
        {
            int 段内 = Mathf.CeilToInt(Vector2.Distance(河[i], 河[i + 1]) / 3.2f);
            for (int k = 0; k < 段内; k++)
            {
                var 点 = Vector2.Lerp(河[i], 河[i + 1], k / (float)Mathf.Max(1, 段内));
                Vector2 切 = (河[i + 1] - 河[i]).normalized;
                var 法 = new Vector2(-切.y, 切.x);
                foreach (var 侧 in new[] { 1f, -1f })
                {
                    if (rng.NextDouble() > 0.6) continue;
                    float 偏 = 河半宽 + 0.2f + (float)rng.NextDouble() * 1.8f;
                    var 位 = 点 + 法 * (偏 * 侧);
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Env2 + "/" + 石头们[rng.Next(石头们.Length)]);
                    if (prefab == null) continue;
                    var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, 组);
                    启用全部部件(go);
                    go.transform.localScale = Vector3.one * (0.5f + (float)rng.NextDouble() * 0.9f);
                    go.transform.localRotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
                    go.transform.position = new Vector3(位.x, 水面高 - 0.2f, 位.y);
                    放了++;
                }
            }
        }
        报告.AppendLine("  河岸石头：" + 放了 + " 块");
    }

    // ============================================================ 农田（台地 + 土垄 + 田水）

    static void 建稻田(Transform 父, System.Text.StringBuilder 报告)
    {
        // ★ 整片田挂在一个**转了 田角 的父节点**下，子物体全用"田块局部坐标"摆 ——
        //   这样田埂/稻垄/田水自动跟着斜，不用给每个盒子单独算旋转
        var 组 = new GameObject("农田").transform;
        组.SetParent(父, false);
        组.position = new Vector3(田心.x, 0f, 田心.y);
        组.rotation = Quaternion.Euler(0f, 田角, 0f);

        // 把"田块局部的 (x,z)"换算成世界 XZ —— 台地高度要按**世界位置**采样
        Vector3 局部到世界点(float lx, float lz)
        {
            var w = 组.TransformPoint(new Vector3(lx, 0f, lz));
            w.y = 地形高度(new Vector2(w.x, w.z));
            return w;
        }

        // 土（田埂）
        var 土材 = new Material(Shader.Find("Standard"));
        土材.color = new Color(0.42f, 0.32f, 0.20f);
        var 土贴 = AssetDatabase.LoadAssetAtPath<Texture2D>(地表贴图 + "dirt036.bmp");
        if (土贴 != null) { 土材.mainTexture = 土贴; 土材.mainTextureScale = new Vector2(3f, 3f); }
        AssetDatabase.CreateAsset(土材, 资产目录 + "/田埂.mat");

        // 稻（条垄）：用带 alpha 的草贴图做 cutout，看着是一行一行的稻子
        var 稻材 = new Material(Shader.Find("Standard"));
        稻材.color = new Color(0.46f, 0.72f, 0.26f);
        var 稻贴 = AssetDatabase.LoadAssetAtPath<Texture2D>(草贴图目录 + "Grass064wx.tga");
        if (稻贴 != null) { 稻材.mainTexture = 稻贴; 稻材.mainTextureScale = new Vector2(42f, 1f); }
        稻材.SetFloat("_Mode", 1f);
        稻材.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
        稻材.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
        稻材.SetInt("_ZWrite", 1);
        稻材.EnableKeyword("_ALPHATEST_ON");
        稻材.DisableKeyword("_ALPHABLEND_ON");
        稻材.SetFloat("_Cutoff", 0.3f);
        稻材.renderQueue = 2450;
        AssetDatabase.CreateAsset(稻材, 资产目录 + "/稻田.mat");

        // 田水：**纯色、不带贴图**。
        // 第一版挂着 terrain_water 那张偏白的贴图，整片田被提亮成"白板子" ✗；
        // 稻田水只要一层暗一点的青绿罩色就够（soil*(1-a) + tint*a 会自然压暗）。
        var 水材 = 建纯色水材质("田水", new Color(0.16f, 0.30f, 0.28f, 0.55f));

        // ★ 垄面用一个**共享的单位四边形资产**，每条垄只改 transform 缩放 ——
        //   每行 new 一个 Mesh 又会变成"场景里内嵌的匿名网格"，白白撑大 .scene
        var 垄面 = 平面网格(1f, 1f);
        AssetDatabase.CreateAsset(垄面, 资产目录 + "/垄面.asset");

        int 埂 = 0, 垄 = 0, 水 = 0;

        foreach (var t in 田)
        {
            float 宽 = t.X1 - t.X0, 长 = t.Z1 - t.Z0;

            // 四条田埂
            foreach (var (cx, cz, sx, sz) in new[]
            {
                (t.X0 + 宽 * 0.5f, t.Z0, 宽 + 0.5f, 0.5f),
                (t.X0 + 宽 * 0.5f, t.Z1, 宽 + 0.5f, 0.5f),
                (t.X0, t.Z0 + 长 * 0.5f, 0.5f, 长 + 0.5f),
                (t.X1, t.Z0 + 长 * 0.5f, 0.5f, 长 + 0.5f),
            })
            {
                var b = GameObject.CreatePrimitive(PrimitiveType.Cube);
                b.name = "田埂";
                b.transform.SetParent(组, false);
                b.transform.position = 局部到世界点(cx, cz) + Vector3.up * (t.台高 + 0.12f);
                b.transform.localRotation = Quaternion.identity;
                b.transform.localScale = new Vector3(sx, 0.4f, sz);
                b.GetComponent<Renderer>().sharedMaterial = 土材;
                Object.DestroyImmediate(b.GetComponent<Collider>());
                埂++;
            }

            // 条垄：沿**田块局部 X** 一条一条，间距 0.9m，垄面抬高 0.3m
            for (float z = t.Z0 + 0.75f; z < t.Z1 - 0.4f; z += 0.9f)
            {
                var 条 = new GameObject("稻垄");
                条.transform.SetParent(组, false);
                条.transform.position = 局部到世界点((t.X0 + t.X1) * 0.5f, z) + Vector3.up * (t.台高 + 0.30f);
                条.transform.localRotation = Quaternion.identity;
                条.transform.localScale = new Vector3(宽 - 1.0f, 1f, 0.62f);
                条.AddComponent<MeshFilter>().sharedMesh = 垄面;
                条.AddComponent<MeshRenderer>().sharedMaterial = 稻材;
                垄++;
            }

            // 田水（比垄面低）
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            q.name = "田水";
            q.transform.SetParent(组, false);
            q.transform.position = 局部到世界点(t.X0 + 宽 * 0.5f, t.Z0 + 长 * 0.5f) + Vector3.up * (t.台高 + 0.06f);
            q.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            q.transform.localScale = new Vector3(宽 - 0.4f, 长 - 0.4f, 1f);
            q.GetComponent<Renderer>().sharedMaterial = 水材;
            Object.DestroyImmediate(q.GetComponent<Collider>());
            水++;
        }

        报告.AppendLine("  农田：" + 田.Length + " 块台地（整体**顺时针斜 " + 田角 + "°**，中心 " + 田心.ToString("F0") + "）"
                         + " · 田埂 " + 埂 + " · 稻垄 " + 垄 + " · 田水 " + 水);
    }

    static Mesh 平面网格(float 宽, float 长)
    {
        var m = new Mesh();
        float hx = 宽 * 0.5f, hz = 长 * 0.5f;
        m.vertices = new[]
        {
            new Vector3(-hx, 0f, -hz), new Vector3(hx, 0f, -hz),
            new Vector3(hx, 0f, hz), new Vector3(-hx, 0f, hz),
        };
        m.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
        m.triangles = new[] { 0, 2, 1, 0, 3, 2 };
        m.RecalculateNormals(); m.RecalculateBounds();
        return m;
    }

    // ============================================================ 院墙

    /// <summary>
    /// **自己生成院墙**（不用素材）。
    ///
    /// 为什么不用 `S1_qiangmian01_zm.prefab`：
    ///   ① 那个 prefab 的**根节点自带旋转**，我用 `LookRotation(走向)` 直接覆盖了它
    ///      → 墙横过来 90°（用户报的"没有旋转 90 度"就是这个）；
    ///   ② 它厚 2.14m（照原场景里当承重墙用的），当院墙太胖；
    ///   ③ 自己拼还能顺手加瓦压顶/基座/转角垛，比例一次说清。
    ///
    /// 尺寸（都是"人尺度"）：墙厚 0.55 · 净高 2.5 · 压顶出檐 0.18 厚 0.22 · 基座 0.3 高。
    /// </summary>
    static void 建院墙(Transform 父, System.Text.StringBuilder 报告)
    {
        var 组 = new GameObject("院墙").transform;
        组.SetParent(父, false);
        已垛.Clear();      // 静态字段会跨次构建留着，不清就会第二次没有垛 ✗

        const float 厚 = 0.45f, 净高 = 2.45f, 基高 = 0.32f, 基出 = 0.10f;

        // ★ 材质全用 S1 那套 → 风格和村子一致（第一版用的是我自己的青色砖/青石，发灰发冷，不搭）
        var 墙材 = 载材质("S1_chengmen01e_tf.mat");        // 素净石墙（可平铺）
        var 垛材 = 载材质("S1_qiangmian01_zm_001.mat");    // 带雕饰的石墙面 → 垛子/转角
        var 瓦材 = 载材质("S1_jianzhu01_zm_001.mat");      // 大殿深青瓦 → 压顶
        if (墙材 == null) 墙材 = 建平铺材质("墙砖", 地表贴图 + "Stone007.bmp", new Color(0.68f, 0.66f, 0.62f), 1.6f);
        if (垛材 == null) 垛材 = 墙材;
        if (瓦材 == null) 瓦材 = 建平铺材质("墙帽", 地表贴图 + "Stone021.bmp", new Color(0.36f, 0.40f, 0.42f), 1.6f);

        int 段数 = 0, 垛数 = 0;
        var 角点 = new List<Vector2>();
        var rng = new System.Random(种子 + 11);

        foreach (var 段 in 院墙)
        {
            角点.Add(段[0]); 角点.Add(段[段.Length - 1]);

            for (int i = 0; i < 段.Length - 1; i++)
            {
                var a = 段[i]; var b = 段[i + 1];
                float 长 = Vector2.Distance(a, b);
                if (长 < 0.4f) continue;
                var 方向 = (b - a) / 长;
                var 中点 = (a + b) * 0.5f;
                float 地 = 地形高度(中点);
                var 朝向 = Quaternion.LookRotation(new Vector3(方向.x, 0f, 方向.y), Vector3.up);

                // 基座
                建盒子(组, "墙基", new Vector3(中点.x, 地 + 基高 * 0.5f, 中点.y), 朝向,
                       new Vector3(厚 + 基出 * 2f, 基高, 长 + 基出 * 2f), 墙材, false, 1.8f);
                // 墙身（沿走向平铺，竖向 1 格）
                建盒子(组, "墙身", new Vector3(中点.x, 地 + 基高 + 净高 * 0.5f, 中点.y), 朝向,
                       new Vector3(厚, 净高, 长), 墙材, true, 3.2f, 只横铺: true);
                // 瓦压顶（两坡 + 正脊）
                建瓦顶(组, "压顶", new Vector3(中点.x, 地 + 基高 + 净高, 中点.y), 朝向, 瓦材,
                       厚 * 0.5f + 0.26f, 长 + 0.44f, 26f, 0.26f, 0.13f);
                段数++;

                // ★ 沿线加**墙垛**（间距带随机，别太规整）：5~7m 一个，比墙身宽一点、高一点
                float 跑 = 0f;
                while (跑 + 3.4f < 长)
                {
                    跑 += 5.0f + (float)rng.NextDouble() * 2.2f;
                    if (跑 + 0.6f > 长) break;
                    var p = a + 方向 * 跑;
                    float g = 地形高度(p);
                    var q = Quaternion.LookRotation(new Vector3(方向.x, 0f, 方向.y), Vector3.up);
                    建盒子(组, "墙垛", new Vector3(p.x, g + (基高 + 净高 + 0.22f) * 0.5f, p.y), q,
                           new Vector3(厚 + 0.24f, 基高 + 净高 + 0.22f, 0.78f), 垛材, true, 2.6f, 只横铺: true);
                    建瓦顶(组, "垛顶", new Vector3(p.x, g + 基高 + 净高 + 0.22f, p.y), q, 瓦材,
                           厚 * 0.5f + 0.34f, 1.06f, 26f, 0.20f, 0.12f);
                    垛数++;
                }
            }
        }

        // 转角垛（每个真正的折点/接头）
        for (int i = 0; i < 角点.Count; i++)
        {
            var p = 角点[i];
            if (计数(角点, p) < 2) continue;
            if (已垛.Contains(p)) continue;
            已垛.Add(p);
            float 地 = 地形高度(p);
            建盒子(组, "转角垛", new Vector3(p.x, 地 + (基高 + 净高 + 0.30f) * 0.5f, p.y), Quaternion.identity,
                   new Vector3(厚 + 0.34f, 基高 + 净高 + 0.30f, 厚 + 0.34f), 垛材, true, 2.6f, 只横铺: true);
            建瓦顶(组, "转角垛顶", new Vector3(p.x, 地 + 基高 + 净高 + 0.30f, p.y), Quaternion.identity, 瓦材,
                   厚 * 0.5f + 0.40f, 厚 + 0.86f, 26f, 0.24f, 0.13f);
        }

        报告.AppendLine("  院墙：自己生成 " + 段数 + " 段（厚 " + 厚 + "m·净高 " + 净高 + "m，S1 石墙材质 + 两坡瓦压顶）"
                         + " · 沿线墙垛 " + 垛数 + " · 转角垛 " + 已垛.Count);
    }

    /// <summary>
    /// **自己生成的院门**（用户：「把 `S1_chengmen01_tf` 删了，自己做一个院门，风格统一一下」）。
    ///
    /// 结构（都是人尺度）：两根门垛 1.1×3.0 → 木过梁 → 匾 → 两坡瓦顶（出檐 0.35）
    /// → 两扇木门（用大殿那套带字门板材质）→ 门槛 + 两级踏步。
    /// </summary>
    static void 建院门(Transform 父, System.Text.StringBuilder 报告)
    {
        var 组 = new GameObject("院门").transform;
        组.SetParent(父, false);

        var 垛材 = 载材质("S1_qiangmian01_zm_001.mat");
        var 瓦材 = 载材质("S1_jianzhu01_zm_001.mat");
        var 木材 = 载材质("S1_chengmen01b_tf.mat");
        var 门材 = 载材质("S1_jianzhu01_zm_015.mat");
        var 阶材 = 载材质("S1_jianzhu01_zm_013.mat");
        if (垛材 == null) 垛材 = 建平铺材质("门垛", 地表贴图 + "Stone007.bmp", new Color(0.68f, 0.66f, 0.62f), 1.6f);
        if (瓦材 == null) 瓦材 = 建平铺材质("门瓦", 地表贴图 + "Stone021.bmp", new Color(0.36f, 0.40f, 0.42f), 1.6f);
        if (木材 == null) 木材 = 建平铺材质("门木", 地表贴图 + "dirt030.bmp", new Color(0.32f, 0.22f, 0.14f), 1.2f);
        if (门材 == null) 门材 = 木材;
        if (阶材 == null) 阶材 = 垛材;

        // 门沿 X 轴展开 → 用一个统一朝向，盒子的"长"落在世界 X 上
        var 朝 = Quaternion.LookRotation(Vector3.right, Vector3.up);
        float 地 = 地形高度(院门);
        var C = new Vector3(院门.x, 地, 院门.y);

        const float 洞宽 = 3.0f, 垛宽 = 1.1f, 垛高 = 3.0f;
        float 垛心 = 洞宽 * 0.5f + 垛宽 * 0.5f;          // 1.5 + 0.55

        // 门垛（左右各一）+ 基座
        foreach (var 侧 in new[] { -1f, 1f })
        {
            var 心 = C + 朝 * new Vector3(0f, 0f, 0f) + new Vector3(垛心 * 侧, 0f, 0f);
            建盒子(组, "门垛基", new Vector3(心.x, 地 + 0.18f, 心.z), 朝,
                   new Vector3(垛宽 + 0.30f, 0.36f, 垛宽 + 0.30f), 垛材, false, 1.8f);
            建盒子(组, "门垛", new Vector3(心.x, 地 + 0.36f + 垛高 * 0.5f, 心.z), 朝,
                   new Vector3(垛宽, 垛高, 垛宽), 垛材, true, 2.4f, 只横铺: true);
            建瓦顶(组, "垛帽", new Vector3(心.x, 地 + 0.36f + 垛高, 心.z), 朝, 瓦材,
                   垛宽 * 0.5f + 0.22f, 垛宽 + 0.34f, 28f, 0.22f, 0.12f);
        }

        // 过梁 + 匾
        建盒子(组, "过梁", new Vector3(C.x, 地 + 0.36f + 垛高 + 0.17f, C.z), 朝,
               new Vector3(0.30f, 0.34f, 洞宽 + 垛宽 * 2f), 木材, false, 1.6f);
        建盒子(组, "门匾", new Vector3(C.x, 地 + 0.36f + 垛高 + 0.58f, C.z), 朝,
               new Vector3(0.14f, 0.46f, 2.30f), 木材, false, 1.2f, 只横铺: true);

        // 院门瓦顶（比墙的高一档，出檐更大）
        建瓦顶(组, "门顶", new Vector3(C.x, 地 + 0.36f + 垛高 + 0.75f, C.z), 朝, 瓦材,
               1.55f, 洞宽 + 垛宽 * 2f + 1.6f, 27f, 0.72f, 0.16f);

        // 门扇（两扇，木门材质，中间留缝）
        foreach (var 侧 in new[] { -1f, 1f })
        {
            float w = 洞宽 * 0.5f - 0.05f;
            建盒子(组, "门扇", new Vector3(C.x + 侧 * (w * 0.5f + 0.03f), 地 + 0.30f + 1.28f, C.z + 0.09f), 朝,
                   new Vector3(0.09f, 2.56f, w), 门材, true, 1.3f, 只横铺: true);
        }

        // 门槛 + 两级踏步（朝村路那一侧 = 南边，z 更小）
        建盒子(组, "门槛", new Vector3(C.x, 地 + 0.10f, C.z), 朝, new Vector3(0.42f, 0.20f, 洞宽 + 0.2f), 阶材, false, 1.2f);
        建盒子(组, "踏步1", new Vector3(C.x, 地 + 0.08f, C.z - 0.55f), 朝, new Vector3(0.70f, 0.16f, 洞宽 + 0.9f), 阶材, false, 1.2f);
        建盒子(组, "踏步2", new Vector3(C.x, 地 + 0.03f, C.z - 1.10f), 朝, new Vector3(1.00f, 0.10f, 洞宽 + 1.5f), 阶材, false, 1.2f);

        报告.AppendLine("  院门：自己生成（门垛 1.1×3.0 · 洞宽 " + 洞宽 + "m · 木过梁 + 匾 + 两坡瓦顶 · 两扇木门 + 踏步）"
                         + "，位置 " + 院门.ToString("F0"));
    }

    /// <summary>两坡瓦压顶（沿"朝向"的走向）：局部 +Y 起坡，正脊在中间</summary>
    static void 建瓦顶(Transform 父, string 名, Vector3 底座中心, Quaternion 朝向, Material 瓦材,
                       float 半宽, float 长, float 倾角, float 脊高, float 帽厚)
    {
        if (瓦材 == null) return;
        float 斜 = 半宽 / Mathf.Cos(倾角 * Mathf.Deg2Rad);
        float 半 = 脊高 * 0.5f;

        var 左坡 = 朝向 * Quaternion.Euler(0f, 0f, 倾角);
        var 右坡 = 朝向 * Quaternion.Euler(0f, 0f, -倾角);
        建盒子(父, 名 + "左坡", 底座中心 + 朝向 * new Vector3(-半宽 * 0.5f, 半, 0f), 左坡,
               new Vector3(斜, 帽厚, 长), 瓦材, false, 1.1f, 只横铺: true);
        建盒子(父, 名 + "右坡", 底座中心 + 朝向 * new Vector3(半宽 * 0.5f, 半, 0f), 右坡,
               new Vector3(斜, 帽厚, 长), 瓦材, false, 1.1f, 只横铺: true);
        建盒子(父, 名 + "正脊", 底座中心 + 朝向 * new Vector3(0f, 脊高 + 帽厚 * 0.35f, 0f), 朝向,
               new Vector3(0.17f, 0.13f, 长 + 0.06f), 瓦材, false, 1.1f, 只横铺: true);
    }

    static readonly List<Vector2> 已垛 = new List<Vector2>();
    static int 计数(List<Vector2> 表, Vector2 v)
    {
        int n = 0;
        foreach (var x in 表) if ((x - v).sqrMagnitude < 0.01f) n++;
        return n;
    }

    const string S1材质目录 = "Assets/Environment/Models/Materials/Scene/Materials/";
    static Material 载材质(string 文件)
        => AssetDatabase.LoadAssetAtPath<Material>(S1材质目录 + 文件);

    /// <summary>
    /// 建一个盒子。`平铺米每格 > 0` 时用 **MaterialPropertyBlock** 设 `_MainTex_ST`：
    /// 立方体每面 UV 都是 0..1，不改 ST 的话长条会把贴图拉成一条 ✗
    /// （用 MPB 而不是 new Material，是为了不产生一堆材质实例）
    /// </summary>
    static GameObject 建盒子(Transform 父, string 名, Vector3 中心, Quaternion 朝向, Vector3 尺寸,
                             Material 材, bool 留碰撞, float 平铺米每格 = 0f, bool 只横铺 = false)
    {
        var b = GameObject.CreatePrimitive(PrimitiveType.Cube);
        b.name = 名;
        b.transform.SetParent(父, false);
        b.transform.position = 中心;
        b.transform.rotation = 朝向;
        b.transform.localScale = 尺寸;
        var r = b.GetComponent<Renderer>();
        r.sharedMaterial = 材;
        if (平铺米每格 > 0.01f)
        {
            float u = Mathf.Max(1f, 尺寸.z / 平铺米每格);
            float v = 只横铺 ? 1f : Mathf.Max(1f, 尺寸.y / 平铺米每格);
            var mpb = new MaterialPropertyBlock();
            mpb.SetVector("_MainTex_ST", new Vector4(u, v, 0f, 0f));
            r.SetPropertyBlock(mpb);
        }
        if (!留碰撞) Object.DestroyImmediate(b.GetComponent<Collider>());
        return b;
    }

    /// <summary>建一个"平铺"材质：立方体每面 UV 都是 0..1，靠 _MainTex_ST 控制砖缝大小</summary>
    static Material 建平铺材质(string 名, string 贴图路径, Color 色, float 一次几米)
    {
        var m = new Material(Shader.Find("Standard"));
        m.color = 色;
        var 贴 = AssetDatabase.LoadAssetAtPath<Texture2D>(贴图路径);
        if (贴 != null) m.mainTexture = 贴;
        var t = Mathf.Max(0.2f, 一次几米);
        m.mainTextureScale = new Vector2(t, t);
        m.SetFloat("_Glossiness", 0.08f);
        AssetDatabase.CreateAsset(m, 资产目录 + "/" + 名 + ".mat");
        return m;
    }

    /// <summary>
    /// **空气墙轮廓**（世界 XZ，闭合折线，顺时针）。
    ///
    /// 依据用户 2026-09-26 在 Scene 视图里画的黄圈：把**村子本体 + 大殿院子 + 东边那片林子**围起来，
    /// 而把**农田、石桥、水车、河道上游**留在墙外（和图上一致）。
    /// 标定基准：院墙 X[-18,20] Z[33,72]、大殿 (2.3,57)、河流/桥 (-37,17)。
    ///
    /// 想调就改这一串坐标（顺序 = 顺时针一圈，程序会自动首尾相连）。
    /// </summary>
    static readonly Vector2[] 空气墙轮廓 =
    {
        new Vector2(-16f,  74f),   // 北 · 院墙西侧外
        new Vector2( 12f,  74f),   // 北
        new Vector2( 27f,  58f),
        new Vector2( 30f,  40f),
        new Vector2( 44f,  33f),
        new Vector2( 60f,  14f),   // 东 · 伸进东边林子
        new Vector2( 60f,  -6f),
        new Vector2( 46f, -18f),
        new Vector2( 32f, -22f),
        new Vector2( 22f, -14f),
        new Vector2( 10f, -20f),
        new Vector2(  0f, -32f),   // 南
        new Vector2(-14f, -46f),   // 南西 · 村口草屋南侧（村门就在这儿）
        new Vector2(-30f, -42f),
        new Vector2(-34f, -12f),   // 西 · 西侧庑房外
        new Vector2(-28f,  20f),
        new Vector2(-22f,  50f),
    };

    // ============================================================ 空气墙

    /// <summary>
    /// 沿 <see cref="空气墙轮廓"/> 拉一圈**隐形墙**（照 3C_Testbed 的 `Boundary` 套路：
    /// 只有 BoxCollider、**没有渲染器**）。
    ///
    /// 每条边只放**一个**拉长的盒子（不是像 Boundary 那样每 1.5m 一段）—— 边是直的，一段就够，
    /// 省掉几十个物体。盒子高 16m（`御风 2.4m`、`坐骑最高 5.64m`，都拦得住）。
    ///
    /// 玩家想出去就得从这里以外绕 —— 所以**农田 / 石桥 / 水车 在墙外，暂时进不去**（和用户画的图一致）。
    /// </summary>
    static void 建空气墙(Transform 父, System.Text.StringBuilder 报告)
    {
        var 组 = new GameObject("空气墙").transform;
        组.SetParent(父, false);

        const float 厚 = 1.0f, 墙高 = 16f, 埋深 = 2f;   // 盒子从 y=-2 到 y=14
        int 段 = 0; float 总长 = 0f;

        for (int i = 0; i < 空气墙轮廓.Length; i++)
        {
            var a = 空气墙轮廓[i];
            var b = 空气墙轮廓[(i + 1) % 空气墙轮廓.Length];
            float 长 = Vector2.Distance(a, b);
            if (长 < 0.5f) continue;

            var 中点 = (a + b) * 0.5f;
            var 方向 = (b - a) / 长;

            var go = new GameObject("空气墙段_" + i.ToString("00"));
            go.transform.SetParent(组, false);
            go.transform.position = new Vector3(中点.x, -埋深 + 墙高 * 0.5f, 中点.y);
            go.transform.rotation = Quaternion.LookRotation(new Vector3(方向.x, 0f, 方向.y), Vector3.up);
            var box = go.AddComponent<BoxCollider>();
            box.size = new Vector3(厚, 墙高, 长);     // 局部 Z = 走向
            box.center = Vector3.zero;
            段++; 总长 += 长;
        }
        报告.AppendLine("  空气墙：" + 段 + " 段隐形墙（周长 " + 总长.ToString("F0") + "m，高 " + 墙高 + "m、无渲染器）");
    }

    // ============================================================ 给已有物件补碰撞体

    /// <summary>
    /// 给**场景里已有的东西**（用户手摆的 24 个建筑/道具、以及别的手工物件）补碰撞体，
    /// 免得玩家直接穿过去。`村庄环境` / `Player` / UI / 相机这些不动。
    ///
    /// ★ 用**每个渲染器自己的本地包围盒**加 BoxCollider，而不是整个物体的世界 AABB ——
    /// 那些建筑不少是转了角度的，世界 AABB 会肥出一大圈（斜着的房子会挡掉半条街）✗。
    /// 幂等：自己那层已经有 Collider 就跳过。
    /// </summary>
    static void 补场景碰撞体(System.Text.StringBuilder 报告)
    {
        string[] 跳过 = { "Player", "Main Camera", "Directional Light", "EventSystem",
                          "HudCanvas", "CharacterUI", "暂停菜单", "SpawnPoint", 根名 };

        int 加了 = 0, 已有 = 0, 根数 = 0;
        for (int si = 0; si < UnityEngine.SceneManagement.SceneManager.sceneCount; si++)
        {
            var 场景 = UnityEngine.SceneManagement.SceneManager.GetSceneAt(si);
            if (!场景.isLoaded) continue;
            foreach (var 根 in 场景.GetRootGameObjects())
            {
                bool 别碰 = false;
                foreach (var s in 跳过) if (根.name == s) { 别碰 = true; break; }
                if (别碰) continue;
                根数++;

                foreach (var r in 根.GetComponentsInChildren<Renderer>(true))
                {
                    if (r == null || r is ParticleSystemRenderer) continue;
                    var go = r.gameObject;
                    if (go.GetComponent<Collider>() != null) { 已有++; continue; }

                    var 盒 = go.AddComponent<BoxCollider>();
                    var mf = go.GetComponent<MeshFilter>();
                    var smr = r as SkinnedMeshRenderer;
                    if (mf != null && mf.sharedMesh != null)
                    { 盒.center = mf.sharedMesh.bounds.center; 盒.size = mf.sharedMesh.bounds.size; }
                    else if (smr != null)
                    { 盒.center = smr.localBounds.center; 盒.size = smr.localBounds.size; }
                    else
                    { 盒.center = r.localBounds.center; 盒.size = r.localBounds.size; }
                    加了++;
                }
            }
        }
        报告.AppendLine("  补碰撞体：扫了 " + 根数 + " 个根物体 → 新加 BoxCollider " + 加了
                         + " 个（已有 " + 已有 + " 个跳过）");
    }

    // ============================================================ 植被

    static void 撒树(Transform 父, System.Text.StringBuilder 报告)
    {
        var 组 = new GameObject("森林").transform;
        组.SetParent(父, false);
        var rng = new System.Random(种子);
        var 已放 = new List<Vector3>();
        int 放了 = 0;

        // ★ 用**目标高度**而不是缩放系数：各资源原生大小差 3 倍以上
        //   （Env2 的 S1_shu001 原生 24m、guanmu 只有 1.8m），
        //   写死系数会让某种树变成参天巨物、另一种小到看不见。
        // ★ 路径必须是**真实存在**的：第一版我照猜写了 Environment3/Tree/…，
        //   结果 7 种里 4 种加载不到，森林只剩 3 种 ✗（缺的会在报告里列出来）
        var 树种 = new List<(string, float, float)>
        {
            (Env2 + "/Tree/S1_shu001_tf.prefab", 6.0f, 9.0f),
            (Env2 + "/Tree/S1_shu002_tf.prefab", 4.5f, 6.5f),
            (Env2 + "/Tree/S1_shu003_tf.prefab", 5.5f, 8.5f),
            (树目录 + "environment_Tree_dashu_001_a.FBX", 7.0f, 11.0f),
            (树目录 + "environment_Tree_dashu_003_a.FBX", 6.0f, 9.0f),
            (树目录 + "environment_Tree_songshu_002_d.FBX", 6.0f, 9.5f),
            (树目录 + "environment_Tree_Green_005_d.FBX", 5.0f, 8.0f),
            (树目录 + "environment_Tree_Green_003_a.FBX", 5.0f, 8.0f),
            (树目录 + "environment_Tree_Green_001_a.FBX", 5.0f, 8.0f),
            (树目录 + "environment_Tree_zhangshu_01_a.FBX", 4.5f, 7.0f),
            (树目录 + "environment_Tree_taoshu_001_a.FBX", 3.5f, 5.5f),
            (树目录 + "environment_Tree_qingduanzhu_001_a.FBX", 3.0f, 5.5f),
            (树目录 + "environment_tree_purple_001.FBX", 4.0f, 6.0f),
            // ★ 不放黄叶/枯树/红叶那几种：用户参考图画的是**绿林**，
            //   第一版混了太多秋色，整片林子偏橘 ✗（要秋景再加回来）
        };
        var 没找到 = new List<string>();
        for (int i = 树种.Count - 1; i >= 0; i--)
            if (AssetDatabase.LoadAssetAtPath<GameObject>(树种[i].Item1) == null)
            { 没找到.Add(System.IO.Path.GetFileName(树种[i].Item1)); 树种.RemoveAt(i); }
        if (没找到.Count > 0) 报告.AppendLine("  ⚠ 树资源缺失（已跳过）：" + string.Join(", ", 没找到.ToArray()));
        if (树种.Count == 0) { 报告.AppendLine("  森林：一棵树都没找到 ✗"); return; }

        foreach (var 区 in 林区)
        {
            int 丛数 = Mathf.Max(4, Mathf.RoundToInt(区.width * 区.height / 190f));
            for (int c = 0; c < 丛数; c++)
            {
                var 心 = new Vector2(区.xMin + (float)rng.NextDouble() * 区.width,
                                     区.yMin + (float)rng.NextDouble() * 区.height);
                int 棵 = 4 + rng.Next(7);
                for (int k = 0; k < 棵; k++)
                {
                    float ang = (float)rng.NextDouble() * Mathf.PI * 2f;
                    float r = (float)rng.NextDouble() * 13f;
                    var p = 心 + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * r;
                    if (!可放(p, 已放, 3.2f)) continue;

                    var (路径, 最矮, 最高) = 树种[rng.Next(树种.Count)];
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(路径);
                    if (prefab == null) continue;

                    float 目标高 = Mathf.Lerp(最矮, 最高, (float)rng.NextDouble());
                    var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, 组);
                    启用全部部件(go);
                    go.transform.localScale = Vector3.one * (目标高 / 预制高(prefab));
                    go.transform.localRotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
                    go.transform.position = new Vector3(p.x, 地形高度(p), p.y);
                    补碰撞体(go);
                    已放.Add(p);
                    放了++;
                }
            }
        }
        报告.AppendLine("  森林：" + 放了 + " 棵（" + 林区.Length + " 片，按目标高 5~10m 缩放）");
    }

    static void 撒灌木(Transform 父, System.Text.StringBuilder 报告)
    {
        var 组 = new GameObject("灌木与蘑菇").transform;
        组.SetParent(父, false);
        var rng = new System.Random(种子 + 3);
        var 已放 = new List<Vector3>();
        int 放了 = 0;

        // (路径, 最矮, 最高)
        // ★ **不放蘑菇**（用户明确说讨厌 `Mogu/*` 那套粉色巨伞 ✗，而且它们原生 9m 高）
        var 种类 = new List<(string, float, float)>
        {
            (Env2 + "/Tree/guanmu_010.prefab", 0.9f, 1.7f),
            (Env2 + "/Tree/guanmu_012.prefab", 0.9f, 1.7f),
            (Env2 + "/Tree/guanmu_017.prefab", 0.9f, 1.7f),
            (Env2 + "/Tree/guanmu_022.prefab", 0.9f, 1.7f),
            (Env2 + "/Tree/guanmu_05.prefab",  0.9f, 1.7f),
            (Env2 + "/Tree/guanmu_024.prefab", 0.9f, 1.7f),
            (Env2 + "/Tree/YYZmantuoluo001.prefab", 0.8f, 1.5f),
            (Env1 + "/Grass/environment_grass_guanmu_001_a.FBX", 0.7f, 1.3f),
            (Env1 + "/Grass/environment_grass_guanmu_002_a.FBX", 0.7f, 1.3f),
            (Env1 + "/Grass/environment_grass_xiaocao_001_a.FBX", 0.4f, 0.9f),
            (Env1 + "/Grass/environment_grass_seahaicao_001_a.FBX", 0.5f, 1.1f),
        };
        种类.RemoveAll(x => AssetDatabase.LoadAssetAtPath<GameObject>(x.Item1) == null);

        for (int i = 0; i < 520 && 种类.Count > 0; i++)
        {
            var p = new Vector2(地形X0 + (float)rng.NextDouble() * 地形边长,
                                地形Z0 + (float)rng.NextDouble() * 地形边长);
            if (!可放(p, 已放, 1.7f)) continue;
            if (到折线距离(p, 河) < 河半宽 + 1.2f) continue;
            if (到路网距离(p) < 2.4f) continue;
            if (在田里(p, 0.5f)) continue;
            if (p.x > -18f && p.x < 20f && p.y > 33f && p.y < 72f) continue;   // 院子里不种

            var (路径, 最矮, 最高) = 种类[rng.Next(种类.Count)];
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(路径);
            if (prefab == null) continue;
            float 目标高 = Mathf.Lerp(最矮, 最高, (float)rng.NextDouble());
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, 组);
            启用全部部件(go);
            go.transform.localScale = Vector3.one * (目标高 / 预制高(prefab));
            go.transform.localRotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
            go.transform.position = new Vector3(p.x, 地形高度(p), p.y);
            已放.Add(p);
            放了++;
        }
        报告.AppendLine("  灌木/花草：" + 放了 + " 丛（0.4~1.7m，**不含蘑菇**）");
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

    // ============================================================ 工具

    static TerrainLayer[] 建图层们(System.Text.StringBuilder 报告)
    {
        var 定义 = new[]
        {
            ("草地", "grass009.bmp",  7f, 0.05f),
            ("石板", "dimian001.bmp", 5f, 0.30f),
            ("土路", "dirt036.bmp",   6f, 0.05f),
            ("沙石", "dirt022.bmp",   7f, 0.05f),
        };
        var 列表 = new List<TerrainLayer>();
        foreach (var (名, 文件, 平铺, 光滑) in 定义)
        {
            var 贴 = AssetDatabase.LoadAssetAtPath<Texture2D>(地表贴图 + 文件);
            if (贴 == null) { 报告.AppendLine("  ✗ 找不到贴图 " + 文件); continue; }
            var L = new TerrainLayer { name = 名, diffuseTexture = 贴, tileSize = new Vector2(平铺, 平铺), smoothness = 光滑 };
            AssetDatabase.CreateAsset(L, 资产目录 + "/图层_" + 名 + ".terrainlayer");
            列表.Add(L);
        }
        return 列表.ToArray();
    }

    /// <summary>地形世界高度（只在树/石头落地时用）</summary>
    static float 地形高度(Vector2 p)
    {
        var 地 = Object.FindObjectOfType<Terrain>();
        if (地 == null || 地.terrainData == null) return 0f;
        float u = Mathf.Clamp01((p.x - 地形X0) / 地形边长);
        float v = Mathf.Clamp01((p.y - 地形Z0) / 地形边长);
        return 地.transform.position.y + 地.terrainData.GetInterpolatedHeight(u, v);
    }

    static bool 在田里(Vector2 p, float 余量)
    {
        var q = 到田局部(p);        // ★ 田是斜的，先转进局部
        foreach (var t in 田)
            if (q.x > t.X0 - 余量 && q.x < t.X1 + 余量 && q.y > t.Z0 - 余量 && q.y < t.Z1 + 余量) return true;
        return false;
    }

    static bool 可放(Vector2 p, List<Vector3> 已放, float 最小间距)
    {
        if (p.x < 地形X0 + 2f || p.x > 地形X0 + 地形边长 - 2f) return false;
        if (p.y < 地形Z0 + 2f || p.y > 地形Z0 + 地形边长 - 2f) return false;
        if (到折线距离(p, 河) < 河半宽 + 1.5f) return false;
        if (到路网距离(p) < 2.8f) return false;
        if (在田里(p, 1.2f)) return false;
        if (p.x > -22f && p.x < 26f && p.y > -46f && p.y < 78f) return false;   // 村里/院子/城门内外不种树
        foreach (var q in 已放)
            if ((new Vector2(q.x, q.z) - p).sqrMagnitude < 最小间距 * 最小间距) return false;
        return true;
    }

    static float 到矩形外距离(Vector2 p, Rect r)
    {
        float dx = Mathf.Max(r.xMin - p.x, 0f, p.x - r.xMax);
        float dz = Mathf.Max(r.yMin - p.y, 0f, p.y - r.yMax);
        if (dx <= 0f && dz <= 0f)
            return -Mathf.Min(p.x - r.xMin, r.xMax - p.x, p.y - r.yMin, r.yMax - p.y);
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    static float 到折线距离(Vector2 p, Vector2[] 线)
    {
        float 最小 = float.MaxValue;
        for (int i = 0; i < 线.Length - 1; i++)
        {
            float d = 到线段距离(p, 线[i], 线[i + 1]);
            if (d < 最小) 最小 = d;
        }
        return 最小;
    }

    static float 到路网距离(Vector2 p)
    {
        float 最小 = float.MaxValue;
        foreach (var l in 路) { float d = 到折线距离(p, l); if (d < 最小) 最小 = d; }
        return 最小;
    }

    static float 到线段距离(Vector2 p, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        float L2 = ab.sqrMagnitude;
        if (L2 < 0.0001f) return (p - a).magnitude;
        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / L2);
        return (p - (a + ab * t)).magnitude;
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

    static void 启用全部部件(GameObject go)
    {
        foreach (var tr in go.GetComponentsInChildren<Transform>(true))
        {
            if (!tr.gameObject.activeSelf) tr.gameObject.SetActive(true);
            foreach (var r in tr.GetComponents<Renderer>())
                if (r != null && !r.enabled) r.enabled = true;
        }
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
