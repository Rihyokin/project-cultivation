using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// **自己做的命中粒子特效生成器。** 不依赖任何第三方特效包 ——
/// 贴图是程序化画出来的，材质和预制体都在这里建，随时可以重跑覆盖。
///
/// 菜单：修仙 / 构建命中特效（ASCII：Cultivation / Build Hit FX）
///
/// 产出：
/// <code>
/// Assets/resources/FX/Hit_Physical.prefab        ← 基础命中（物理攻击的默认特效）
/// Assets/resources/FX/Hit_Physical_Heavy.prefab  ← 重击（高级攻击用）
/// Assets/resources/FX/Textures/*.png             ← 程序化贴图（星芒 / 光点）
/// Assets/resources/FX/Materials/*.mat            ← 加色混合材质
/// </code>
///
/// ### 特效构成（都是 playOnAwake 的一次性爆发）
///
/// | 层 | 基础 | 重击 |
/// |---|---|---|
/// | `Flash` 星芒闪光 | 尺寸 0.62 / 寿命 0.11s | 尺寸 1.15 / 寿命 0.18s |
/// | `Sparks` 飞溅火星 | 15 颗，速度 3.2~7 | **26 颗**，速度 3.2~9.5，更久 |
///
/// **没有冲击光环** —— 原来重击加过一个，用户说"有点突兀"，删了。
///
/// 朝向约定：**预制体的 +Z 指向"攻击者"那一侧**（和飞弹命中特效一致），
/// 所以调用方传 <c>Quaternion.LookRotation(攻击者位置 - 命中点)</c>。
/// </summary>
public static class HitFxBuilder
{
    const string 根目录 = "Assets/resources/FX";
    const string 贴图目录 = 根目录 + "/Textures";
    const string 材质目录 = 根目录 + "/Materials";

    public const string 基础命中路径 = "FX/Hit_Physical";
    public const string 重击命中路径 = "FX/Hit_Physical_Heavy";

    [MenuItem("修仙/构建命中特效")]
    [MenuItem("Cultivation/Build Hit FX")]
    public static void BuildFromMenu() => 构建全部();

    public static void 构建全部()
    {
        确保目录();

        var 星芒 = 生成贴图(贴图目录 + "/hit_flash.png", 64, 画星芒);
        var 光点 = 生成贴图(贴图目录 + "/hit_dot.png", 32, 画光点);
        if (星芒 == null || 光点 == null) { Debug.LogError("[命中特效] 贴图生成失败"); return; }

        var 材质星芒 = 生成材质(材质目录 + "/FX_Hit_Flash.mat", 星芒);
        var 材质光点 = 生成材质(材质目录 + "/FX_Hit_Dot.mat", 光点);

        构建一个(基础命中路径, "Hit_Physical", 材质星芒, 材质光点, false);
        构建一个(重击命中路径, "Hit_Physical_Heavy", 材质星芒, 材质光点, true);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[命中特效] 已生成：\n  " + 根目录 + "/Hit_Physical.prefab（基础·物理攻击默认）\n  "
                  + 根目录 + "/Hit_Physical_Heavy.prefab（重击·高级攻击）");
    }

    // ============================================================ 预制体

    static void 构建一个(string 资源路径, string 名字, Material 星芒, Material 光点, bool 重击)
    {
        var 预制体路径 = 根目录 + "/" + 名字 + ".prefab";

        var 根 = new GameObject(名字);

        // ---- 1) 星芒闪光（挂在根上）----
        // 基础命中要**小**：命中特效是"啪一下"，不是放烟花
        var 闪 = 根.AddComponent<ParticleSystem>();
        建粒子(闪, 星芒,
            寿命: 重击 ? 0.18f : 0.11f,
            尺寸: 重击 ? 1.15f : 0.62f,
            速度: 0f,
            爆发: 1,
            重力: 0f,
            尺寸曲线: 曲线(0.45f, 1.15f),
            颜色: 渐变(new Color(1f, 0.97f, 0.86f, 1f), new Color(1f, 0.72f, 0.30f, 0f)),
            随机旋转: true,
            拉伸: false,
            面朝相机: true);

        // ---- 2) 飞溅火星（"物理打击"最主要的那一层）----
        var 火星Go = new GameObject("Sparks");
        火星Go.transform.SetParent(根.transform, false);
        var 火星 = 火星Go.AddComponent<ParticleSystem>();
        建粒子(火星, 光点,
            寿命: 重击 ? 0.6f : 0.36f,
            尺寸: 重击 ? 0.17f : 0.10f,
            速度: 0f,                       // 用 MinMax 单独设
            爆发: 重击 ? 26 : 15,
            重力: 1.1f,
            尺寸曲线: 曲线(1f, 0.2f),
            颜色: 渐变(new Color(1f, 0.93f, 0.68f, 1f), new Color(0.85f, 0.25f, 0.08f, 0f)),
            随机旋转: true,
            拉伸: true,
            面朝相机: true);
        var 火星主 = 火星.main;
        火星主.startSpeed = new ParticleSystem.MinMaxCurve(3.2f, 重击 ? 9.5f : 7f);
        火星主.startLifetime = new ParticleSystem.MinMaxCurve(火星主.startLifetime.constant * 0.65f, 火星主.startLifetime.constant);
        var 火星形 = 火星.shape;
        火星形.enabled = true;
        火星形.shapeType = ParticleSystemShapeType.Cone;
        火星形.angle = 重击 ? 78f : 58f;
        火星形.radius = 0.07f;

        // ---- 3) 不再有冲击光环 ----
        // 【用户反馈·已删】重击那版原来还加了一个"冲击光环"，用户说**有点突兀**，删掉了。
        // 重击和基础的区别现在就靠：**更大的星芒（1.15 : 0.62）+ 更多火星（26 : 15）+ 火星更快更久**。
        // 要再加环的话，记得环和星芒同心叠在一起会像个"齿轮"（见下面 画光环 的注释）。

        var 旧 = AssetDatabase.LoadAssetAtPath<GameObject>(预制体路径);
        if (旧 != null) AssetDatabase.DeleteAsset(预制体路径);
        PrefabUtility.SaveAsPrefabAsset(根, 预制体路径);
        Object.DestroyImmediate(根);
    }

    /// <summary>一套常用参数的粒子系统（三层都是用这个搭的，只是参数不同）</summary>
    static void 建粒子(ParticleSystem ps, Material 材质,
                       float 寿命, float 尺寸, float 速度, int 爆发, float 重力,
                       AnimationCurve 尺寸曲线, Gradient 颜色,
                       bool 随机旋转, bool 拉伸, bool 面朝相机)
    {
        var 主 = ps.main;
        主.duration = 0.5f;
        主.loop = false;
        主.playOnAwake = true;                       // ★ 一 Instantiate 就播
        主.startLifetime = 寿命;
        主.startSpeed = 速度;
        主.startSize = 尺寸;
        主.startRotation = 随机旋转 ? new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f) : 0f;
        主.gravityModifier = 重力;
        主.simulationSpace = ParticleSystemSimulationSpace.World;   // 命中特效留在原地，别跟着父物体
        主.maxParticles = 128;
        主.stopAction = ParticleSystemStopAction.None;              // 销毁交给调用方

        var 发射 = ps.emission;
        发射.enabled = true;
        发射.rateOverTime = 0f;
        发射.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)爆发) });

        var 形状 = ps.shape;
        形状.enabled = false;

        var 淡出 = ps.colorOverLifetime;
        淡出.enabled = true;
        淡出.color = new ParticleSystem.MinMaxGradient(颜色);

        var 缩放 = ps.sizeOverLifetime;
        缩放.enabled = true;
        缩放.size = new ParticleSystem.MinMaxCurve(1f, 尺寸曲线);

        var 渲染 = ps.GetComponent<ParticleSystemRenderer>();
        渲染.sharedMaterial = 材质;
        渲染.renderMode = 拉伸 ? ParticleSystemRenderMode.Stretch : ParticleSystemRenderMode.Billboard;
        if (拉伸)
        {
            渲染.velocityScale = 0.06f;
            渲染.lengthScale = 2.4f;
        }
        渲染.alignment = 面朝相机 ? ParticleSystemRenderSpace.View : ParticleSystemRenderSpace.World;
        渲染.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        渲染.receiveShadows = false;
    }

    static AnimationCurve 曲线(float 起, float 终)
        => AnimationCurve.EaseInOut(0f, 起, 1f, 终);

    static Gradient 渐变(Color 起, Color 终)
    {
        var g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(起, 0f), new GradientColorKey(终, 1f) },
            new[] { new GradientAlphaKey(起.a, 0f), new GradientAlphaKey(起.a * 0.75f, 0.35f), new GradientAlphaKey(0f, 1f) });
        return g;
    }

    // ============================================================ 贴图（程序化画）

    static void 确保目录()
    {
        建目录("Assets/resources");
        建目录(根目录);
        建目录(贴图目录);
        建目录(材质目录);
    }

    static void 建目录(string 路径)
    {
        if (AssetDatabase.IsValidFolder(路径)) return;
        var 父 = Path.GetDirectoryName(路径).Replace('\\', '/');
        var 名 = Path.GetFileName(路径);
        if (!AssetDatabase.IsValidFolder(父)) 建目录(父);
        AssetDatabase.CreateFolder(父, 名);
    }

    static Texture2D 生成贴图(string 路径, int 边长, System.Func<float, float, float> 取透明度)
    {
        var tex = new Texture2D(边长, 边长, TextureFormat.RGBA32, false);
        var 像素 = new Color32[边长 * 边长];
        for (int y = 0; y < 边长; y++)
        {
            for (int x = 0; x < 边长; x++)
            {
                // 归一化到 -1 ~ 1
                float u = (x + 0.5f) / 边长 * 2f - 1f;
                float v = (y + 0.5f) / 边长 * 2f - 1f;
                float a = Mathf.Clamp01(取透明度(u, v));
                像素[y * 边长 + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        }
        tex.SetPixels32(像素);
        tex.Apply();
        File.WriteAllBytes(路径, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);

        AssetDatabase.ImportAsset(路径, ImportAssetOptions.ForceUpdate);
        var ti = AssetImporter.GetAtPath(路径) as TextureImporter;
        if (ti != null)
        {
            ti.textureType = TextureImporterType.Default;
            ti.alphaIsTransparency = true;
            ti.mipmapEnabled = false;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.filterMode = FilterMode.Bilinear;
            ti.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(路径);
    }

    /// <summary>
    /// 四芒星 + 亮核（命中瞬间那一下的闪光）。
    /// 尖要**细**：第一版太粗，和光环叠起来看着像齿轮。
    /// </summary>
    static float 画星芒(float u, float v)
    {
        float r = Mathf.Sqrt(u * u + v * v);
        float 核 = Mathf.Pow(Mathf.Clamp01(1f - r * 2.1f), 1.4f);
        float 横 = Mathf.Exp(-(v * v) / (2f * 0.028f * 0.028f)) * Mathf.Exp(-(u * u) / (2f * 0.46f * 0.46f));
        float 竖 = Mathf.Exp(-(u * u) / (2f * 0.028f * 0.028f)) * Mathf.Exp(-(v * v) / (2f * 0.46f * 0.46f));
        return Mathf.Clamp01(核 * 1.2f + 横 * 0.95f + 竖 * 0.95f);
    }

    /// <summary>柔和光点（火星本体；拖尾靠粒子渲染器的 Stretch）</summary>
    static float 画光点(float u, float v)
    {
        float r = Mathf.Clamp01(Mathf.Sqrt(u * u + v * v));
        return Mathf.Pow(1f - r, 2.2f);
    }

    // 备注：原来还有一个「光环」贴图（软环，给重击当冲击波）。用户说重击那个环**有点突兀**，
    // 已经把环整个删掉了（贴图、材质、粒子层都没了）。要加回来的话注意两点：
    //   · 环和星芒**同心**叠在一起会读成"白色齿轮"，别让两者同时最亮
    //   · 环晚 0.03s 出现才像冲击波

    // ============================================================ 材质

    static Material 生成材质(string 路径, Texture2D 贴图)
    {
        var shader = 找粒子着色器();
        if (shader == null) { Debug.LogError("[命中特效] 找不到可用的粒子着色器"); return null; }

        var mat = AssetDatabase.LoadAssetAtPath<Material>(路径);
        if (mat == null) { mat = new Material(shader); AssetDatabase.CreateAsset(mat, 路径); }
        else mat.shader = shader;

        mat.mainTexture = 贴图;
        if (mat.HasProperty("_TintColor")) mat.SetColor("_TintColor", Color.white);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", Color.white);
        mat.renderQueue = 3000;                  // 透明队列
        EditorUtility.SetDirty(mat);
        return mat;
    }

    /// <summary>按优先级找一个能用的加色粒子着色器（不同 Unity 版本名字不一样）</summary>
    static Shader 找粒子着色器()
    {
        foreach (var 名 in new[]
                 {
                     "Legacy Shaders/Particles/Additive",
                     "Mobile/Particles/Additive",
                     "Particles/Standard Unlit",
                     "Sprites/Default",
                 })
        {
            var s = Shader.Find(名);
            if (s != null) return s;
        }
        return null;
    }

    // ---- ASCII 别名 ----
    public const string BasicHitPath = 基础命中路径;
    public const string HeavyHitPath = 重击命中路径;
    public static void BuildAll() => 构建全部();
}
