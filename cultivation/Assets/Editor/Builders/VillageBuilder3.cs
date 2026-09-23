using System.Collections.Generic;
using UnityEditor;
using EditorSceneManagerAlias = UnityEditor.SceneManagement.EditorSceneManager;
using UnityEngine;

/// <summary>
/// 村庄 v3 —— 按参考图提取的关键词自己设计：
///   田园村落 / 茅草屋与瓦顶混搭 / 小河环绕+石桥木桥 / 菜地条垄 /
///   石灯笼·水井 / 土路 / 篝火炊烟 / 竹林树丛
///
/// 相比 v2 的改动：
///   1. 地面不用素材碎片，直接铺一块大平面 + 草地材质
///   2. 河道改成【整块环形 mesh】并下沉到地下 —— v2 用一圈 cube 拼，看着像铺石板
///   3. 建筑从 Env1 的 32 个 prefab 里挑，尺度统一
///   4. 加了土路（扁长方体压在地面上）、篝火（新资产的 VFX）
///
/// 两步：Build Village (v3) → Align Village To Ground (v3)
/// 菜单：Cultivation / Build Village (v3)
/// </summary>
public static class VillageBuilder3
{
    const string Env1 = "Assets/resources/Environment1";
    const string Env2 = "Assets/resources/Environment2";
    const string EnvNew = "Assets/Environment";
    const string 根节点名 = "Village";
    const float 地面Y = 0f;
    const int 种子 = 20260922;

    const float 河中心半径 = 34f;
    const float 河宽 = 8f;
    const float 河深 = 1.2f;

    struct 摆件
    {
        public string 关键词; public float X, Z, Yaw, Scale;
        public 摆件(string k, float x, float z, float yaw = 0f, float scale = 1f)
        { 关键词 = k; X = x; Z = z; Yaw = yaw; Scale = scale; }
    }

    // ---- 房屋：南北主路两侧，围着中央广场 ----
    static readonly 摆件[] 房屋 =
    {
        // 北侧主屋（最大的放中间）
        new 摆件("cunzhuang_001",   0f,  20f, 180f, 1.1f),
        new 摆件("cunzhuang_002", -15f,  16f, 150f),
        new 摆件("cunzhuang_003",  15f,  16f,-150f),
        // 东西两侧
        new 摆件("cunzhuang_004", -19f,   2f,  90f),
        new 摆件("cunzhuang_006",  19f,   2f, -90f),
        new 摆件("fangzi_01_a",   -15f, -12f,  60f, 0.95f),
        new 摆件("fangzi_01_a",    15f, -12f, -60f, 0.95f),
        // 南侧靠村口
        new 摆件("minjuweijiang_001_01", -8f, -22f,  20f, 0.9f),
        new 摆件("minjuweijiang_001_02",  8f, -22f, -20f, 0.9f),
    };

    // ---- 桥：东南西北各一，形式不同 ----
    static readonly 摆件[] 桥 =
    {
        new 摆件("qiaoliang_003",  -河中心半径, 0f,  90f),
        new 摆件("muqiao_001",     河中心半径, 0f, -90f),
        new 摆件("qiaoliang_002",  0f,  河中心半径, 180f),
        new 摆件("qiaoliang_001",  0f, -河中心半径,   0f),
    };

    // ---- 广场陈设 ----
    static readonly 摆件[] 广场 =
    {
        new 摆件("shuijing_001",   -5f,   3f,  0f, 1.1f),
        new 摆件("denglong_001",   -9f,   9f,  0f, 1.3f),
        new 摆件("denglong_001",    9f,   9f,  0f, 1.3f),
        new 摆件("denglong_002",   -9f,  -9f,  0f, 1.3f),
        new 摆件("denglong_002",    9f,  -9f,  0f, 1.3f),
        new 摆件("diaoqiao_001",    0f,  -6f,  0f, 1.2f),
        new 摆件("diaoxiang_001",   5f,   3f,180f, 1.1f),
    };

    // ---- 菜地：东侧成片，条状 ----
    static readonly 摆件[] 菜地 =
    {
        new 摆件("daocao_01_a",  26f,  12f, 0f, 1.6f),
        new 摆件("daocao_01_a",  26f,   4f, 0f, 1.6f),
        new 摆件("daocao_01_a",  26f,  -4f, 0f, 1.6f),
        new 摆件("daocao_01_a",  26f, -12f, 0f, 1.6f),
        new 摆件("daocaoduo_001_a", 33f, 10f, 0f, 1.4f),
        new 摆件("daocaoduo_001_b", 33f, -6f, 0f, 1.4f),
        // 西侧也来一小片
        new 摆件("daocao_01_a", -26f,   8f, 0f, 1.4f),
        new 摆件("daocao_01_a", -26f,  -4f, 0f, 1.4f),
    };

    // ---- 三个出村路口（都开在北/东/西，避开南边的河与桥）----
    static readonly (string 名, float 角)[] 出口 =
    { ("出口_北路", 90f), ("出口_东路", 0f), ("出口_西路", 180f) };

    [MenuItem("Cultivation/Build Village (v3)")]
    public static void Build()
    {
        var scene = EditorSceneManagerAlias.GetActiveScene();

        var 旧的 = GameObject.Find(根节点名);
        if (旧的 != null) Object.DestroyImmediate(旧的);

        var 根 = new GameObject(根节点名);
        int 放了 = 0;
        var 没找到 = new List<string>();

        // ---- 地面 ----
        建地面(根.transform);
        放了++;

        // ---- 河道（整块环形 mesh，下沉）----
        建河道(根.transform);
        放了++;

        // ---- 土路：十字主路 ----
        建土路(根.transform);
        放了++;

        foreach (var b in 房屋) { if (放(根.transform, b, Env1)) 放了++; else 没找到.Add(b.关键词); }
        foreach (var b in 桥) { if (放(根.transform, b, Env1)) 放了++; else 没找到.Add(b.关键词); }
        foreach (var b in 广场) { if (放(根.transform, b, Env1)) 放了++; else 没找到.Add(b.关键词); }
        foreach (var b in 菜地) { if (放(根.transform, b, Env1)) 放了++; else 没找到.Add(b.关键词); }

        // ---- 植被：Env2 的树（已验证能渲染）+ 新资产的树 ----
        var rng = new System.Random(种子);
        var 植被 = new GameObject("植被").transform;
        植被.SetParent(根.transform, false);
        散布(Env2, "Tree/S1_shu001_tf.prefab", 16, 3.4f, 48f, 26f, rng, 植被, true);
        散布(Env2, "Tree/S1_shu003_tf.prefab", 12, 2.8f, 46f, 24f, rng, 植被, true);
        散布(Env2, "Tree/guanmu_010.prefab",   18, 2.0f, 46f, 18f, rng, 植被, true);
        散布(Env2, "Mogu/mogu_05.prefab",      14, 1.5f, 40f, 10f, rng, 植被, false);
        散布(Env2, "Stone/stone_06.prefab",    16, 2.2f, 44f, 12f, rng, 植被, false);
        散布(EnvNew, "Tree/environment_Tree_daqiandian_003_a.prefab", 6, 3.0f, 46f, 30f, rng, 植被, true);

        // ---- 篝火 + 萤火虫：给村子一点生活气 ----
        放特效(根.transform, 1.5f, 1.5f);
        放特效(根.transform, -12f, -6f);
        放特效(根.transform, 10f, -14f);

        // ---- 出村路口 ----
        var 出口根 = new GameObject("出村路口").transform;
        出口根.SetParent(根.transform, false);
        foreach (var (名, 角) in 出口)
        {
            float rad = 角 * Mathf.Deg2Rad;
            var 朝外 = new Vector3(Mathf.Cos(rad), 0f, Mathf.Sin(rad));

            // 路口开在河【外面】，这样玩家是"过桥出村"
            var go = new GameObject(名);
            go.transform.SetParent(出口根, false);
            go.transform.position = 朝外 * (河中心半径 + 河宽);
            go.transform.rotation = Quaternion.LookRotation(朝外, Vector3.up);
            var box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(12f, 5f, 8f);
            box.center = new Vector3(0f, 2.5f, 0f);

            // 门外放个传送门当标记
            var 门 = 找模型("chuansongmen");
            if (门 != null)
            {
                var m = (GameObject)PrefabUtility.InstantiatePrefab(门, go.transform);
                m.name = "传送门";
                启用全部部件(m);
                m.transform.position = 朝外 * (河中心半径 + 河宽 + 6f);
                m.transform.rotation = Quaternion.LookRotation(朝外, Vector3.up);
            }
            放了++;
        }

        EditorSceneManagerAlias.MarkSceneDirty(scene);
        Debug.Log("[VillageBuilder3] 摆位完成：放置 " + 放了 + "，未找到 " + 没找到.Count
                  + (没找到.Count > 0 ? "（" + string.Join(", ", 没找到.ToArray()) + "）" : "")
                  + "\n接着跑 Cultivation/Align Village To Ground (v3)");
    }

    [MenuItem("Cultivation/Align Village To Ground (v3)")]
    public static void AlignToGround()
    {
        var 根 = GameObject.Find(根节点名);
        if (根 == null) { Debug.LogError("[VillageBuilder3] 找不到 " + 根节点名); return; }

        int 调了 = 0, 算不出 = 0;
        foreach (Transform 子 in 根.transform)
        {
            if (子.name == "地面" || 子.name == "河道" || 子.name == "土路"
                || 子.name == "出村路口" || 子.name == "植被" || 子.name == "特效") continue;
            if (对齐一个(子)) 调了++; else 算不出++;
        }
        EditorSceneManagerAlias.MarkSceneDirty(EditorSceneManagerAlias.GetActiveScene());
        Debug.Log("[VillageBuilder3] 贴地对齐：调整 " + 调了 + "，无法计算 " + 算不出);
    }

    // ---------------------------------------------------------------- 地面/河/路

    static void 建地面(Transform 父)
    {
        var 地 = GameObject.CreatePrimitive(PrimitiveType.Plane);
        地.name = "地面";
        地.transform.SetParent(父, false);
        地.transform.localScale = new Vector3(14f, 1f, 14f);   // 140 × 140
        地.transform.position = Vector3.zero;
        var mat = 找材质("grass", "caodi", "dibiao", "dimian", "Grass");
        var r = 地.GetComponent<Renderer>();
        r.sharedMaterial = mat != null ? mat : 新材质(new Color(0.40f, 0.50f, 0.28f));
    }

    /// <summary>
    /// 河道：一整块【环形 mesh】，绕村子一圈，下沉到地面以下。
    ///
    /// v2 是用 64 个 cube 拼一圈，结果看着像铺了一圈石板（还浮在地面上）。
    /// 这里改成程序生成一个环形网格，一次成型，没有接缝。
    /// </summary>
    static void 建河道(Transform 父)
    {
        var go = new GameObject("河道");
        go.transform.SetParent(父, false);

        const int N = 96;
        float 外 = 河中心半径 + 河宽 * 0.5f;
        float 内 = 河中心半径 - 河宽 * 0.5f;

        var verts = new Vector3[N * 2];
        var uv = new Vector2[N * 2];
        var tris = new int[N * 6];
        for (int i = 0; i < N; i++)
        {
            float a = (float)i / N * Mathf.PI * 2f;
            float c = Mathf.Cos(a), s = Mathf.Sin(a);
            verts[i * 2] = new Vector3(c * 内, 0f, s * 内);
            verts[i * 2 + 1] = new Vector3(c * 外, 0f, s * 外);
            uv[i * 2] = new Vector2(0f, (float)i / N * 8f);
            uv[i * 2 + 1] = new Vector2(1f, (float)i / N * 8f);
        }
        for (int i = 0; i < N; i++)
        {
            int n = (i + 1) % N, t = i * 6;
            tris[t] = i * 2; tris[t + 1] = n * 2; tris[t + 2] = n * 2 + 1;
            tris[t + 3] = i * 2; tris[t + 4] = n * 2 + 1; tris[t + 5] = i * 2 + 1;
        }

        var m = new Mesh { name = "RingRiver" };
        m.vertices = verts; m.uv = uv; m.triangles = tris;
        m.RecalculateNormals(); m.RecalculateBounds();

        // 河面比地面低一点，看着像真的河道
        go.transform.position = new Vector3(0f, -河深, 0f);
        go.AddComponent<MeshFilter>().sharedMesh = m;
        var mr = go.AddComponent<MeshRenderer>();
        var mat = 找材质("shui", "water", "he");
        mr.sharedMaterial = mat != null ? mat : 新材质(new Color(0.20f, 0.42f, 0.52f));

        // 河岸：内外各一圈压边的矮墙，把河"嵌"进地里
        建河岸(父, 内 - 0.6f, 外 + 0.6f);
    }

    static void 建河岸(Transform 父, float 内, float 外)
    {
        var 根 = new GameObject("河岸").transform;
        根.SetParent(父, false);
        var mat = 找材质("stone", "shizhuan", "shiban", "Stone");

        const int N = 72;
        for (int i = 0; i < N; i++)
        {
            float a = (float)i / N * Mathf.PI * 2f;
            foreach (var (半径, 名) in new[] { (内, "内岸"), (外, "外岸") })
            {
                var seg = GameObject.CreatePrimitive(PrimitiveType.Cube);
                seg.name = 名 + i;
                seg.transform.SetParent(根, false);
                seg.transform.localScale = new Vector3(1.2f, 1.4f, 2f * Mathf.PI * 半径 / N + 0.5f);
                seg.transform.position = new Vector3(Mathf.Cos(a) * 半径, -0.7f, Mathf.Sin(a) * 半径);
                seg.transform.rotation = Quaternion.Euler(0f, -a * Mathf.Rad2Deg, 0f);
                var r = seg.GetComponent<Renderer>();
                r.sharedMaterial = mat != null ? mat : 新材质(new Color(0.45f, 0.44f, 0.40f));
                var c = seg.GetComponent<Collider>();
                if (c != null) Object.DestroyImmediate(c);
            }
        }
    }

    static void 建土路(Transform 父)
    {
        var 根 = new GameObject("土路").transform;
        根.SetParent(父, false);
        var mat = 找材质("lu", "road", "dirt", "dimian", "Dibiao");

        // 南北主路
        段(根, "主路_南北", new Vector3(0f, 0.02f, 0f), new Vector3(9f, 0.04f, 56f));
        // 东西横路
        段(根, "横路_东西", new Vector3(0f, 0.02f, 2f), new Vector3(52f, 0.04f, 7f));
        // 通往四座桥的支路
        段(根, "支路_北", new Vector3(0f, 0.02f, 河中心半径 - 6f), new Vector3(7f, 0.04f, 14f));
        段(根, "支路_南", new Vector3(0f, 0.02f, -(河中心半径 - 6f)), new Vector3(7f, 0.04f, 14f));
        段(根, "支路_东", new Vector3(河中心半径 - 6f, 0.02f, 0f), new Vector3(14f, 0.04f, 7f));
        段(根, "支路_西", new Vector3(-(河中心半径 - 6f), 0.02f, 0f), new Vector3(14f, 0.04f, 7f));

        void 段(Transform p, string 名, Vector3 位, Vector3 尺寸)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = 名;
            cube.transform.SetParent(p, false);
            cube.transform.position = 位;
            cube.transform.localScale = 尺寸;
            var r = cube.GetComponent<Renderer>();
            r.sharedMaterial = mat != null ? mat : 新材质(new Color(0.52f, 0.45f, 0.33f));
            var c = cube.GetComponent<Collider>();
            if (c != null) Object.DestroyImmediate(c);
        }
    }

    static void 放特效(Transform 父, float x, float z)
    {
        var 根 = GameObject.Find(根节点名);
        var 组 = 根.transform.Find("特效");
        if (组 == null) { 组 = new GameObject("特效").transform; 组.SetParent(根.transform, false); }

        var 火 = 找模型("fire0001") ?? 找模型("fire");
        if (火 != null)
        {
            var g = (GameObject)PrefabUtility.InstantiatePrefab(火, 组);
            g.name = "篝火";
            启用全部部件(g);
            g.transform.position = new Vector3(x, 0f, z);
        }
        var 萤 = 找模型("yinghuochong");
        if (萤 != null)
        {
            var g = (GameObject)PrefabUtility.InstantiatePrefab(萤, 组);
            g.name = "萤火虫";
            启用全部部件(g);
            g.transform.position = new Vector3(x + 3f, 1.5f, z + 2f);
        }
    }

    // ---------------------------------------------------------------- 工具

    static bool 对齐一个(Transform t)
    {
        var 渲染器 = t.GetComponentsInChildren<Renderer>(true);
        if (渲染器.Length == 0) return false;
        bool 有 = false; var b = new Bounds();
        foreach (var r in 渲染器)
        {
            if (r is ParticleSystemRenderer) continue;
            if (!有) { b = r.bounds; 有 = true; } else b.Encapsulate(r.bounds);
        }
        if (!有 || b.size.y <= 0f) return false;
        t.position += new Vector3(0f, 地面Y - b.min.y, 0f);
        return true;
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

    static bool 放(Transform 父, 摆件 b, string 素材根)
    {
        var 模型 = 找模型(b.关键词);
        if (模型 == null) return false;
        var go = (GameObject)PrefabUtility.InstantiatePrefab(模型, 父);
        go.name = b.关键词 + "_" + Mathf.RoundToInt(b.X) + "_" + Mathf.RoundToInt(b.Z);
        启用全部部件(go);
        go.transform.localRotation = Quaternion.Euler(0f, b.Yaw, 0f);
        if (!Mathf.Approximately(b.Scale, 1f)) go.transform.localScale = Vector3.one * b.Scale;
        go.transform.position = new Vector3(b.X, 地面Y, b.Z);
        return true;
    }

    static readonly Dictionary<string, GameObject> 模型缓存 = new Dictionary<string, GameObject>();
    static readonly List<string> 全部模型 = new List<string>();

    static GameObject 找模型(string 关键词)
    {
        if (模型缓存.TryGetValue(关键词, out var c)) return c;
        if (全部模型.Count == 0)
        {
            foreach (var g in AssetDatabase.FindAssets("t:GameObject", new[] { Env1, Env2, EnvNew, "Assets/resources/Environment3" }))
            {
                var p = AssetDatabase.GUIDToAssetPath(g);
                if (p.EndsWith(".prefab") || p.EndsWith(".FBX") || p.EndsWith(".fbx")) 全部模型.Add(p);
            }
        }
        var k = 关键词.ToLower();
        foreach (var p in 全部模型)
            if (p.EndsWith(".prefab") && System.IO.Path.GetFileNameWithoutExtension(p).ToLower().Contains(k))
            { var m = AssetDatabase.LoadAssetAtPath<GameObject>(p); 模型缓存[关键词] = m; return m; }
        foreach (var p in 全部模型)
            if (System.IO.Path.GetFileNameWithoutExtension(p).ToLower().Contains(k))
            { var m = AssetDatabase.LoadAssetAtPath<GameObject>(p); 模型缓存[关键词] = m; return m; }
        模型缓存[关键词] = null;
        return null;
    }

    static Material 找材质(params string[] 关键词)
    {
        foreach (var g in AssetDatabase.FindAssets("t:Material", new[] { Env1, Env2, EnvNew }))
        {
            var p = AssetDatabase.GUIDToAssetPath(g);
            var n = System.IO.Path.GetFileNameWithoutExtension(p).ToLower();
            foreach (var k in 关键词)
                if (n.Contains(k.ToLower())) return AssetDatabase.LoadAssetAtPath<Material>(p);
        }
        return null;
    }

    static Material 新材质(Color c)
    {
        var m = new Material(Shader.Find("Standard"));
        m.color = c;
        return m;
    }

    static void 散布(string 素材根, string 相对路径, int 数量, float 大小, float 最大半径, float 内圈,
                      System.Random rng, Transform 父, bool 避开河)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(素材根 + "/" + 相对路径);
        if (prefab == null) return;

        for (int i = 0; i < 数量; i++)
        {
            float ang = (float)(rng.NextDouble() * Mathf.PI * 2.0);
            float r = Mathf.Lerp(内圈, 最大半径, (float)rng.NextDouble());
            if (避开河 && Mathf.Abs(r - 河中心半径) < 河宽) r += 河宽 * 1.5f;

            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, 父);
            go.name = System.IO.Path.GetFileNameWithoutExtension(相对路径) + "_" + i;
            go.transform.localRotation = Quaternion.Euler(0f, (float)(rng.NextDouble() * 360.0), 0f);
            启用全部部件(go);
            float s = 大小 / 4.6f;
            if (大小 >= 2f) s *= (float)(0.8 + rng.NextDouble() * 0.4);
            go.transform.localScale = Vector3.one * s;
            go.transform.position = new Vector3(Mathf.Cos(ang) * r, 地面Y, Mathf.Sin(ang) * r);
        }
    }
}
