using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 村庄布局生成器（第二版）—— 照着策划给的俯瞰参考图搭。
///
/// 参考图的要素：
///   · 一条小河绕着村子，左上一座石拱桥、左下一座木桥
///   · 中央一个广场，四周散布茅草屋/民居
///   · 右侧成片的菜地（条状垄沟）
///   · 广场上有石灯笼、水井、石塔
///   · 竹林、树木点缀；土路把各区域连起来
///
/// 素材：Environment1 的 cunzhuang/minju/fangzi/muqiao/qiaoliang/denglong/shuijing 等
/// （这些只有 FBX 没有 prefab，但 FBX 模型可以直接实例化）
///      + Environment2 的 S1_ 系列（树/石/蘑菇/道具）
///
/// 两步走：Build Village（摆位）→ Align Village To Ground（贴地）。
/// 后者必须等下一次菜单调用 —— 刚实例化的对象包围盒当帧无效。
///
/// 菜单：Cultivation / Build Village (v2)
/// </summary>
public static class VillageBuilder2
{
    const string Env1 = "Assets/resources/Environment1";
    const string Env2 = "Assets/resources/Environment2";
    const string 根节点名 = "Village";
    const float 地面Y = 0f;
    const int 随机种子 = 20260921;

    // 村子尺度
    const float 河内径 = 30f;    // 河道中心线半径
    const float 河宽 = 7f;

    // ---------------------------------------------------------------- 布局表

    struct 摆件
    {
        public string 关键词;    // 模型名片段，用来在素材库里找
        public float X, Z, Yaw, Scale;
        public 摆件(string k, float x, float z, float yaw = 0f, float scale = 1f)
        { 关键词 = k; X = x; Z = z; Yaw = yaw; Scale = scale; }
    }

    // 民居（围绕广场一圈）
    static readonly 摆件[] 房屋 =
    {
        new 摆件("cunzhuang_001", -14f,  14f,  135f),
        new 摆件("cunzhuang_002",   0f,  18f,  180f),
        new 摆件("cunzhuang_003",  14f,  14f, -135f),
        new 摆件("cunzhuang_004", -18f,   2f,   90f),
        new 摆件("cunzhuang_006",  18f,   2f,  -90f),
        new 摆件("fangzi_01_a",    -9f, -12f,   60f),
        new 摆件("minjuweijiang_001_01", 9f, -12f, -60f),
        new 摆件("minjuweijiang_001_02", 0f, -16f,   0f),
    };

    // 桥：一座拱桥（西北）、一座木桥（西南）
    static readonly 摆件[] 桥 =
    {
        new 摆件("qiaoliang_003", -河内径 * 0.72f, 河内径 * 0.72f, 135f),
        new 摆件("muqiao_001",    -河内径 * 0.72f, -河内径 * 0.72f, 45f),
    };

    // 广场上的东西
    static readonly 摆件[] 广场 =
    {
        new 摆件("shuijing_001",   0f,   2f,   0f),
        new 摆件("denglong_001",  -6f,   6f,   0f, 1.2f),
        new 摆件("denglong_001",   6f,   6f,   0f, 1.2f),
        new 摆件("denglong_002",  -6f,  -6f,   0f, 1.2f),
        new 摆件("denglong_002",   6f,  -6f,   0f, 1.2f),
        new 摆件("diaoqiao_001",   0f,  -8f,   0f),
    };

    // 菜地（条状垄沟，参考图右侧成片）
    static readonly 摆件[] 菜地 =
    {
        new 摆件("daocao_01_a",  24f,  10f,  0f, 1.4f),
        new 摆件("daocao_01_a",  24f,   2f,  0f, 1.4f),
        new 摆件("daocao_01_a",  24f,  -6f,  0f, 1.4f),
        new 摆件("daocao_002_a", 32f,  10f,  0f, 1.4f),
        new 摆件("daocao_002_a", 32f,   2f,  0f, 1.4f),
        new 摆件("daocao_002_a", 32f,  -6f,  0f, 1.4f),
        new 摆件("daocaoduo_001_a", 20f, 18f, 0f, 1.2f),
        new 摆件("daocaoduo_001_b", 28f, 18f, 0f, 1.2f),
    };

    // 出口（三个方向）
    static readonly (string 名, Vector3 位置, Vector3 朝向, Vector3 出口点)[] 出口 =
    {
        ("出口_北路", new Vector3(  0f, 0f,  38f), Vector3.forward, new Vector3(  0f, 0f,  46f)),
        ("出口_东路", new Vector3( 38f, 0f,   0f), Vector3.right,   new Vector3( 46f, 0f,   0f)),
        ("出口_西路", new Vector3(-38f, 0f,   0f), Vector3.left,    new Vector3(-46f, 0f,   0f)),
    };

    // ---------------------------------------------------------------- 构建

    [MenuItem("Cultivation/Build Village (v2)")]
    public static void Build()
    {
        var scene = EditorSceneManager.GetActiveScene();

        var 旧的 = GameObject.Find(根节点名);
        if (旧的 != null) Object.DestroyImmediate(旧的);

        var 根 = new GameObject(根节点名);
        var 报告 = new System.Text.StringBuilder();
        int 放了 = 0;
        var 没找到 = new List<string>();

        // ---- 1. 地面：自己生成一大块，别再指望素材包的碎片 ----
        //
        // 之前用 S1_dimian01_zm 缩放当地面，结果村庄"浮"在空底上 —— 那块模型太小、
        // 而且贴地对齐后跑到 y=69 去了。地面这种事自己生成最稳。
        var 地面 = GameObject.CreatePrimitive(PrimitiveType.Plane);
        地面.name = "地面";
        地面.transform.SetParent(根.transform, false);
        地面.transform.localScale = new Vector3(14f, 1f, 14f);      // 140x140
        地面.transform.position = new Vector3(0f, 0f, 0f);
        地面.GetComponent<Renderer>().sharedMaterial = 找地面材质();
        放了++;

        // ---- 2. 河道：一圈低下去的环形水面 ----
        建河道(根.transform);

        // ---- 3. 房屋 ----
        foreach (var b in 房屋) { if (放(根.transform, b, Env1)) 放了++; else 没找到.Add(b.关键词); }

        // ---- 4. 桥 ----
        foreach (var b in 桥) { if (放(根.transform, b, Env1)) 放了++; else 没找到.Add(b.关键词); }

        // ---- 5. 广场陈设 ----
        foreach (var b in 广场) { if (放(根.transform, b, Env1)) 放了++; else 没找到.Add(b.关键词); }

        // ---- 6. 菜地 ----
        foreach (var b in 菜地) { if (放(根.transform, b, Env1)) 放了++; else 没找到.Add(b.关键词); }

        // ---- 7. 植被（Env2，已经验证过能正常渲染）----
        var rng = new System.Random(随机种子);
        var 植被 = new GameObject("植被").transform;
        植被.SetParent(根.transform, false);
        散布(Env2, "Tree/S1_shu001_tf.prefab", 14, 3.2f, 46f, 26f, rng, 植被, 避开河: true);
        散布(Env2, "Tree/S1_shu003_tf.prefab", 10, 2.6f, 44f, 24f, rng, 植被, 避开河: true);
        散布(Env2, "Tree/guanmu_010.prefab",   14, 2.0f, 46f, 20f, rng, 植被, 避开河: true);
        散布(Env2, "Mogu/mogu_05.prefab",      16, 1.5f, 40f, 12f, rng, 植被, 避开河: false);
        散布(Env2, "Stone/stone_06.prefab",    14, 2.2f, 42f, 14f, rng, 植被, 避开河: false);

        // ---- 8. 出村路口 ----
        var 出口根 = new GameObject("出村路口").transform;
        出口根.SetParent(根.transform, false);
        foreach (var (名, 位置, 朝向, 出口点) in 出口)
        {
            var go = new GameObject(名);
            go.transform.SetParent(出口根, false);
            go.transform.position = 位置;
            go.transform.rotation = Quaternion.LookRotation(朝向.normalized, Vector3.up);
            var box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(10f, 5f, 8f);
            box.center = new Vector3(0f, 2.5f, 0f);

            // 出口外面放个传送门当视觉标记（有就用）
            var 门 = 找模型("chuansongmen");
            if (门 != null)
            {
                var m = (GameObject)PrefabUtility.InstantiatePrefab(门, go.transform);
                m.name = "传送门";
                启用全部部件(m);
                m.transform.position = 出口点;
                m.transform.rotation = Quaternion.LookRotation(朝向.normalized, Vector3.up);
            }
            放了++;
        }

        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log("[VillageBuilder2] 摆位完成：放置 " + 放了
                  + "，未找到 " + 没找到.Count + " 个素材"
                  + (没找到.Count > 0 ? "（" + string.Join(", ", 没找到.ToArray()) + "）" : "")
                  + "\n接着跑 Cultivation/Align Village To Ground (v2) 贴地");
    }

    [MenuItem("Cultivation/Align Village To Ground (v2)")]
    public static void AlignToGround()
    {
        var 根 = GameObject.Find(根节点名);
        if (根 == null) { Debug.LogError("[VillageBuilder2] 找不到 " + 根节点名); return; }

        int 调了 = 0, 算不出 = 0;
        foreach (Transform 子 in 根.transform)
        {
            // 地面/河道/出口不参与贴地
            if (子.name == "地面" || 子.name == "河道" || 子.name == "出村路口" || 子.name == "植被") continue;
            if (对齐一个(子)) 调了++; else 算不出++;
        }
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log("[VillageBuilder2] 贴地对齐：调整 " + 调了 + "，无法计算 " + 算不出);
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
        var 模型 = AssetDatabase.LoadAssetAtPath<GameObject>(素材根 + "/" + b.关键词) ?? 找模型(b.关键词);
        if (模型 == null) return false;
        var go = (GameObject)PrefabUtility.InstantiatePrefab(模型, 父);
        go.name = b.关键词 + "_" + Mathf.RoundToInt(b.X) + "_" + Mathf.RoundToInt(b.Z);
        启用全部部件(go);
        go.transform.localRotation = Quaternion.Euler(0f, b.Yaw, 0f);
        if (!Mathf.Approximately(b.Scale, 1f)) go.transform.localScale = Vector3.one * b.Scale;
        go.transform.position = new Vector3(b.X, 地面Y, b.Z);
        return true;
    }

    /// <summary>按名字片段在素材库里找模型（prefab 或 FBX 都行），带缓存</summary>
    static readonly Dictionary<string, GameObject> 模型缓存 = new Dictionary<string, GameObject>();
    static readonly List<string> 全部模型 = new List<string>();

    static GameObject 找模型(string 关键词)
    {
        if (模型缓存.TryGetValue(关键词, out var c)) return c;

        if (全部模型.Count == 0)
        {
            foreach (var g in AssetDatabase.FindAssets("t:GameObject", new[] { Env1, Env2, "Assets/resources/Environment3" }))
            {
                var p = AssetDatabase.GUIDToAssetPath(g);
                if (p.EndsWith(".prefab") || p.EndsWith(".FBX") || p.EndsWith(".fbx")) 全部模型.Add(p);
            }
        }

        // prefab 优先
        foreach (var p in 全部模型)
            if (System.IO.Path.GetFileNameWithoutExtension(p).ToLower().Contains(关键词.ToLower()) && p.EndsWith(".prefab"))
            { var m = AssetDatabase.LoadAssetAtPath<GameObject>(p); 模型缓存[关键词] = m; return m; }

        foreach (var p in 全部模型)
            if (System.IO.Path.GetFileNameWithoutExtension(p).ToLower().Contains(关键词.ToLower()))
            { var m = AssetDatabase.LoadAssetAtPath<GameObject>(p); 模型缓存[关键词] = m; return m; }

        模型缓存[关键词] = null;
        return null;
    }

    /// <summary>生成一圈环形河道（水面 + 河岸压边）</summary>
    static void 建河道(Transform 父)
    {
        var 根 = new GameObject("河道").transform;
        根.SetParent(父, false);

        var 水mat = 找水材质();

        const int N = 64;
        for (int i = 0; i < N; i++)
        {
            float a = (float)i / N * Mathf.PI * 2f;
            var seg = GameObject.CreatePrimitive(PrimitiveType.Cube);
            seg.name = "河段_" + i;
            seg.transform.SetParent(根, false);
            seg.transform.localScale = new Vector3(河宽, 0.3f, 2f * Mathf.PI * 河内径 / N + 0.6f);
            seg.transform.position = new Vector3(Mathf.Cos(a) * 河内径, -0.15f, Mathf.Sin(a) * 河内径);
            seg.transform.rotation = Quaternion.Euler(0f, -a * Mathf.Rad2Deg, 0f);
            var r = seg.GetComponent<Renderer>();
            if (水mat != null) r.sharedMaterial = 水mat;
            else r.sharedMaterial = 新材质(new Color(0.22f, 0.45f, 0.55f, 1f));
            var col = seg.GetComponent<Collider>();
            if (col != null) Object.DestroyImmediate(col);
        }
    }

    static Material 找地面材质()
    {
        foreach (var g in AssetDatabase.FindAssets("t:Material", new[] { "Assets/Environment", Env2 }))
        {
            var p = AssetDatabase.GUIDToAssetPath(g);
            var n = System.IO.Path.GetFileNameWithoutExtension(p).ToLower();
            if (n.Contains("caodi") || n.Contains("grass") || n.Contains("dibiao") || n.Contains("dimian"))
                return AssetDatabase.LoadAssetAtPath<Material>(p);
        }
        return 新材质(new Color(0.42f, 0.52f, 0.30f, 1f));
    }

    static Material 找水材质()
    {
        foreach (var g in AssetDatabase.FindAssets("t:Material", new[] { Env1, Env2, "Assets/Environment" }))
        {
            var p = AssetDatabase.GUIDToAssetPath(g);
            var n = System.IO.Path.GetFileNameWithoutExtension(p).ToLower();
            if (n.Contains("shui") || n.Contains("water")) return AssetDatabase.LoadAssetAtPath<Material>(p);
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

            // 别落在河道里
            if (避开河 && Mathf.Abs(r - 河内径) < 河宽 * 0.8f) r += 河宽 * 1.2f;

            float x = Mathf.Cos(ang) * r;
            float z = Mathf.Sin(ang) * r;

            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, 父);
            go.name = System.IO.Path.GetFileNameWithoutExtension(相对路径) + "_" + i;
            go.transform.localRotation = Quaternion.Euler(0f, (float)(rng.NextDouble() * 360.0), 0f);
            启用全部部件(go);
            float s = 大小 / 4.6f;
            if (大小 >= 2f) s *= (float)(0.8 + rng.NextDouble() * 0.4);
            go.transform.localScale = Vector3.one * s;
            go.transform.position = new Vector3(x, 地面Y, z);
        }
    }
}
