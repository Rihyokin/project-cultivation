using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 用 resources/Environment2 的卡通村庄素材（S1_ 系列）搭一个初始小镇。
///
/// 用法：先跑 Build Village 摆位，再跑 Align Village To Ground 贴地。
/// 【必须分两步】—— 刚 Instantiate 出来的对象，Renderer.bounds 要等 Unity
/// 更新完才有效（当帧读全是 0），所以贴地对齐只能等下一次菜单调用。
///
/// 踩过的坑（都留在注释里，别再犯）：
///   1. 这些 prefab 是从大场景里抽出来的，自带 localPosition 带着原场景的高坐标（y≈59），
///      不能保留，摆位时必须先归零。
///   2. prefab 里大量部件是【未激活】的，不显式打开的话既渲染不出来、
///      也算不出包围盒（GetComponentsInChildren 不带 true 一个 Renderer 都找不到）。
///   3. 包围盒不能在实例化当帧算，结果都是错的。
///
/// 菜单：Cultivation / Build Village、Cultivation / Align Village To Ground
/// </summary>
public static class VillageBuilder
{
    const string ArtRoot = "Assets/resources/Environment2";
    const string 根节点名 = "Village";
    const float 地面Y = 0f;
    const int 随机种子 = 20260920;

    struct 摆件
    {
        public string 相对路径;
        public float X, Z, Yaw, Scale;
        public 摆件(string p, float x, float z, float yaw = 0f, float scale = 1f)
        { 相对路径 = p; X = x; Z = z; Yaw = yaw; Scale = scale; }
    }

    static readonly 摆件[] 建筑 =
    {
        new 摆件("Build/S1_juyitang01_tf.prefab",   0f,   22f,   180f),
        new 摆件("Build/S1_fangwu01_tf.prefab",   -12f,  10f,    90f),
        new 摆件("Build/S1_fangwu02_tf.prefab",    12f,  10f,   -90f),
        new 摆件("Build/S1_fangwu03_tf.prefab",   -12f,  -2f,    90f),
        new 摆件("Build/S1_fangwu04_tf.prefab",    12f,  -2f,   -90f),
        new 摆件("Build/S1_fangzi01_zm.prefab",     0f,  12f,   180f),
        new 摆件("Build/S1_caowu01_tf.prefab",    -13f, -14f,    90f),
        new 摆件("Build/S1_caowu02_tf.prefab",     13f, -14f,   -90f),
        new 摆件("Build/S1_chengmen01_tf.prefab",   0f, -22f,     0f),
        new 摆件("Build/S1_qiao01_tf.prefab",       0f, -32f,     0f),
    };

    static readonly string[] 树 = { "Tree/S1_shu001_tf.prefab", "Tree/S1_shu002_tf.prefab", "Tree/S1_shu003_tf.prefab" };
    static readonly string[] 灌木 = { "Tree/guanmu_05.prefab", "Tree/guanmu_010.prefab", "Tree/guanmu_012.prefab",
                                      "Tree/guanmu_014.prefab", "Tree/guanmu_017.prefab", "Tree/guanmu_021.prefab" };
    static readonly string[] 蘑菇 = { "Mogu/mogu_01.prefab", "Mogu/mogu_03.prefab", "Mogu/mogu_05.prefab",
                                      "Mogu/mogu_07.prefab", "Mogu/mogu_09.prefab" };
    static readonly string[] 石头 = { "Stone/stone_04.prefab", "Stone/stone_06.prefab", "Stone/stone_08.prefab",
                                      "Stone/stone_10.prefab", "Stone/stone_12.prefab" };

    static readonly 摆件[] 道具 =
    {
        new 摆件("Thing/shuiche.prefab",            -8f, -26f,  90f),
        new 摆件("Thing/S1_xiaodianpu01_zm.prefab", -6f,   4f,  90f),
        new 摆件("Thing/S1_xiaodianpu01_zm.prefab",  6f,   4f, -90f),
        new 摆件("Thing/S1_xj_mutou01_tf.prefab",   -3f,  -7f,   0f),
        new 摆件("Thing/S1_xj_mutou01_tf.prefab",    3f,  -7f,  30f),
        new 摆件("Thing/S1_xj_zhuozi01_tf.prefab",  -4f,  -4f,   0f),
        new 摆件("Thing/S1_xj_zhuozi02_tf.prefab",   4f,  -4f,  20f),
        new 摆件("Thing/S1_xj_deng02_tf.prefab",    -7f,  -9f,   0f),
        new 摆件("Thing/S1_xj_deng02_tf.prefab",     7f,  -9f,   0f),
        new 摆件("Thing/S1_xj_lukuang01_tf.prefab", -9f,   5f,   0f),
        new 摆件("Thing/S1_xj_luzi01_tf.prefab",     9f,   5f,   0f),
        new 摆件("Thing/S1_diaoxiang04_tf.prefab",  -5f,  17f,   0f),
        new 摆件("Thing/S1_diaoxiang05_tf.prefab",   5f,  17f,   0f),
        new 摆件("Thing/S1_xj_paizi01_tf.prefab",    0f, -16f,   0f),
        new 摆件("Thing/S1_zahuo01_zm.prefab",      -9f,  -1f,  40f),
    };

    static readonly (string 名, Vector3 位置, Vector3 朝向)[] 出口 =
    {
        ("出口_北路", new Vector3(  0f, 0f,  40f), Vector3.forward),
        ("出口_东路", new Vector3( 34f, 0f,  -6f), Vector3.right),
        ("出口_西路", new Vector3(-34f, 0f,  -6f), Vector3.left),
    };

    [MenuItem("Cultivation/Build Village")]
    public static void Build()
    {
        var scene = EditorSceneManager.GetActiveScene();

        var 旧的 = GameObject.Find(根节点名);
        if (旧的 != null) Object.DestroyImmediate(旧的);

        var 根 = new GameObject(根节点名);
        var 报告 = new System.Text.StringBuilder();
        int 放了 = 0, 缺了 = 0;

        var 地面 = AssetDatabase.LoadAssetAtPath<GameObject>(ArtRoot + "/Build/S1_dimian01_zm.prefab");
        if (地面 != null) { 实例化(地面, new Vector3(0f, 0f, 0f), 0f, 6f, 根.transform, "地面"); 放了++; }
        else
        {
            var plane = GameObject.CreatePrimitive(PrimitiveType.Plane);
            plane.name = "地面";
            plane.transform.SetParent(根.transform, false);
            plane.transform.localScale = new Vector3(12f, 1f, 12f);
            报告.Append("  地面 prefab 缺失，用 Plane 兜底").Append('\n');
        }

        foreach (var b in 建筑)
        {
            if (实例化相对(b, 根.transform)) 放了++;
            else { 缺了++; 报告.Append("  缺: ").Append(b.相对路径).Append('\n'); }
        }

        foreach (var p in 道具)
        {
            if (实例化相对(p, 根.transform)) 放了++;
            else { 缺了++; 报告.Append("  缺: ").Append(p.相对路径).Append('\n'); }
        }

        var rng = new System.Random(随机种子);
        var 散布组 = new GameObject("植被与碎石").transform;
        散布组.SetParent(根.transform, false);

        // 参数按"别挡住建筑"来定：树原来是 22 棵 ×4.6 倍，把整个村子盖住了，
        // 现在减到 12 棵 ×3.0，并且内圈从 14 推到 22，让树林环在村子外面而不是压在上面。
        散布(树,   12, 3.0f, 44f, 内圈: 22f, rng, 散布组);
        散布(灌木, 12, 2.0f, 40f, 内圈: 16f, rng, 散布组);
        散布(蘑菇, 18, 1.6f, 40f, 内圈:  8f, rng, 散布组);
        散布(石头, 12, 2.2f, 40f, 内圈: 10f, rng, 散布组);

        var 出口根 = new GameObject("出村路口").transform;
        出口根.SetParent(根.transform, false);
        var 牌 = AssetDatabase.LoadAssetAtPath<GameObject>(ArtRoot + "/Thing/S1_xj_paizi01_tf.prefab");
        foreach (var (名, 位置, 朝向) in 出口)
        {
            var go = new GameObject(名);
            go.transform.SetParent(出口根, false);
            go.transform.position = 位置;
            go.transform.rotation = Quaternion.LookRotation(朝向.normalized, Vector3.up);

            var box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(8f, 4f, 6f);
            box.center = new Vector3(0f, 2f, 0f);

            if (牌 != null)
            {
                var p = (GameObject)PrefabUtility.InstantiatePrefab(牌, go.transform);
                p.name = "路牌";
                启用全部部件(p);
                p.transform.position = new Vector3(位置.x, 地面Y, 位置.z);
                p.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            }
            放了++;
        }

        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log("[VillageBuilder] 摆位完成：放置 " + 放了 + "，缺失 " + 缺了
                  + "。接着跑一次 Cultivation/Align Village To Ground 让它们贴地\n" + 报告);
    }

    [MenuItem("Cultivation/Align Village To Ground")]
    public static void AlignToGround()
    {
        var 根 = GameObject.Find(根节点名);
        if (根 == null) { Debug.LogError("[VillageBuilder] 找不到 " + 根节点名); return; }

        int 调了 = 0, 算不出 = 0;
        foreach (Transform 子 in 根.transform)
        {
            if (子.name == "出村路口") continue;
            if (对齐一个(子)) 调了++; else 算不出++;
        }

        foreach (Transform 子 in 根.transform)
        {
            if (子.name != "出村路口") continue;
            foreach (Transform 口 in 子)
                foreach (Transform 牌 in 口) 对齐一个(牌);
        }

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log("[VillageBuilder] 贴地对齐完成：调整 " + 调了 + " 个，无法计算 " + 算不出 + " 个");
    }

    /// <summary>把一个对象按包围盒底部对齐到地面。返回是否算出有效包围盒。</summary>
    static bool 对齐一个(Transform t)
    {
        // 必须带 true —— 部件默认可能是关着的，不带 true 一个 Renderer 都找不到
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

    static bool 实例化相对(摆件 b, Transform 父)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ArtRoot + "/" + b.相对路径);
        if (prefab == null) return false;
        实例化(prefab, new Vector3(b.X, 0f, b.Z), b.Yaw, b.Scale, 父, null);
        return true;
    }

    static GameObject 实例化(GameObject prefab, Vector3 位置, float yaw, float scale, Transform 父, string 改名)
    {
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, 父);
        if (!string.IsNullOrEmpty(改名)) go.name = 改名;
        go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
        if (!Mathf.Approximately(scale, 1f)) go.transform.localScale = Vector3.one * scale;

        启用全部部件(go);

        // 位置直接设 —— prefab 自带的高坐标（原场景残留）必须先丢掉
        go.transform.position = new Vector3(位置.x, 地面Y, 位置.z);
        return go;
    }

    static void 散布(string[] 路径, int 数量, float 大小, float 最大半径, float 内圈,
                      System.Random rng, Transform 父)
    {
        for (int i = 0; i < 数量; i++)
        {
            int 选 = rng.Next(路径.Length);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ArtRoot + "/" + 路径[选]);
            if (prefab == null) continue;

            float ang = (float)(rng.NextDouble() * Mathf.PI * 2.0);
            float r = Mathf.Lerp(内圈, 最大半径, (float)rng.NextDouble());
            float x = Mathf.Cos(ang) * r;
            float z = Mathf.Sin(ang) * r;
            if (Mathf.Abs(x) < 5.5f && z > -34f && z < 30f) x += x >= 0f ? 6f : -6f;

            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, 父);
            go.name = System.IO.Path.GetFileNameWithoutExtension(路径[选]) + "_" + i;
            go.transform.localRotation = Quaternion.Euler(0f, (float)(rng.NextDouble() * 360.0), 0f);

            启用全部部件(go);

            float s = 大小 / 4.6f;
            if (大小 >= 4f) s *= (float)(0.75 + rng.NextDouble() * 0.5);
            go.transform.localScale = Vector3.one * s;
            go.transform.position = new Vector3(x, 地面Y, z);
        }
    }
}
