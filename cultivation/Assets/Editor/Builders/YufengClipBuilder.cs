using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 逐帧烘焙【凭虚御风】的四个 Humanoid 动画片段。
///
/// 做法：从现有的站立 Idle 片段采样出一套「基准站姿」肌肉值，
/// 再在它之上按策划给的提示词改姿势并叠加呼吸/飘动，最后写成肌肉曲线。
/// 以基准姿势为参照，可以避免直接猜 Unity 肌肉空间的正负号而搞反动作。
///
/// 产出（Assets/Animations/凭虚御风/）：
///   御风_升空      0.9s  不循环   站立 → 悬浮
///   御风_Idle      2.4s  循环     原地悬停，呼吸轻微上下浮动
///   御风_前进      1.4s  循环     重心前倾，双臂后掠，匀速前进感
///   御风_落地      0.7s  不循环   悬浮 → 站立，带一点屈膝缓冲
///
/// 说明：高度与位移由代码（YufengFlight / PlayerController）驱动，
/// 所以片段里【不写根位移】，避免和运行时逻辑打架。
///
/// 菜单：修仙 / 生成凭虚御风动画片段   （ASCII：Cultivation / Bake Yufeng Clips）
/// </summary>
public static class YufengClipBuilder
{
    const string OutDir = "Assets/Animations/凭虚御风";
    const string Lib1 = "Assets/resources/Animation Library/1/UAL1_Standard.fbx";
    const string BaseClipName = "Armature|Idle_Loop";
    const string FlyFbx = "Assets/resources/Animation Library/Flying.fbx";
    const string FlyClipName = "mixamo.com";

    // 姿势资产：前进用 PoseDisplay 抓的，悬停用 posedisplay2 抓的
    const string PoseDir = "Assets/Animations/凭虚御风";
    const string PoseMovingPath = PoseDir + "/御风_前进姿势.asset";
    const string PoseHoverPath = PoseDir + "/御风_悬停姿势.asset";
    const string SceneMovingObject = "PoseDisplay";
    const string SceneHoverObject = "posedisplay2";

    /// <summary>归一化后的站姿根高度，与 Idle 片段的 RootT.y 对齐</summary>
    const float 站姿根高度 = 0.946f;
    const int Fps = 30;

    // 肌肉名 → 下标
    static Dictionary<string, int> muscleIndex;

    /// <summary>策划在 Inspector 里拖的姿势参数</summary>
    static YufengPoseSettings settings;

    /// <summary>Mixamo 飞行动画的肌肉曲线（用来当御风的底子）</summary>
    static Dictionary<string, AnimationCurve> flyCurves;
    static float flyLength = 2.63f;
    static bool hasFly;

    /// <summary>场景里 PoseDisplay 摆的目标姿势（从它身上读出来的）</summary>
    static HumanPose scenePose;
    static bool hasScenePose;

    /// <summary>前进 / 悬停各自的目标姿势（来自姿势资产）</summary>
    static HumanPose poseMoving;
    static HumanPose poseHover;
    static bool hasPoseMoving;
    static bool hasPoseHover;

    [MenuItem("修仙/生成凭虚御风动画片段")]
    [MenuItem("Cultivation/Bake Yufeng Clips")]
    public static void Bake()
    {
        var baseClip = LoadClip(Lib1, BaseClipName);
        if (baseClip == null) { Debug.LogError("[YufengClipBuilder] 找不到基准片段 " + BaseClipName); return; }

        BuildMuscleIndex();
        var stand = SampleStandingPose(baseClip);
        settings = YufengPoseSettings.LoadOrCreate();

        // 载入 Mixamo 飞行动画做底子
        var flyClip = LoadClip(FlyFbx, FlyClipName);
        hasFly = flyClip != null;
        if (hasFly)
        {
            flyLength = Mathf.Max(0.1f, flyClip.length);
            flyCurves = new Dictionary<string, AnimationCurve>();
            foreach (var b in AnimationUtility.GetCurveBindings(flyClip))
                flyCurves[b.propertyName] = AnimationUtility.GetEditorCurve(flyClip, b);
        }
        Debug.Log("[YufengClipBuilder] 飞行底子=" + (hasFly ? FlyClipName + " " + flyLength.ToString("F2") + "s" : "无"));

        // 先尝试从场景里的 PoseDisplay / posedisplay2 抓姿势存成资产，
        // 再统一从资产读 —— 这样烘焙只依赖资产，删掉场景对象也能重跑。
        CapturePoseFromScene(SceneMovingObject, PoseMovingPath);
        CapturePoseFromScene(SceneHoverObject, PoseHoverPath);

        var movingAsset = AssetDatabase.LoadAssetAtPath<YufengPoseAsset>(PoseMovingPath);
        var hoverAsset = AssetDatabase.LoadAssetAtPath<YufengPoseAsset>(PoseHoverPath);
        hasPoseMoving = movingAsset != null && movingAsset.IsValid;
        hasPoseHover = hoverAsset != null && hoverAsset.IsValid;
        if (hasPoseMoving) poseMoving = movingAsset.ToHumanPose();
        if (hasPoseHover) poseHover = hoverAsset.ToHumanPose();

        // 兼容：只有一份旧姿势时，前进/悬停都用它
        if (!hasPoseHover && hasPoseMoving) { poseHover = poseMoving; hasPoseHover = true; }
        if (!hasPoseMoving && hasPoseHover) { poseMoving = poseHover; hasPoseMoving = true; }

        scenePose = hasPoseMoving ? poseMoving : default;
        hasScenePose = hasPoseMoving;

        Debug.Log("[YufengClipBuilder] 姿势资产：前进=" + (hasPoseMoving ? "有" : "无")
                  + "  悬停=" + (hasPoseHover ? "有" : "无")
                  + (hasPoseMoving ? "（来源 " + movingAsset.来源对象 + "）" : ""));

        if (!AssetDatabase.IsValidFolder("Assets/Animations")) AssetDatabase.CreateFolder("Assets", "Animations");
        if (!AssetDatabase.IsValidFolder(OutDir)) AssetDatabase.CreateFolder("Assets/Animations", "凭虚御风");

        SaveClip("御风_升空", 0.90f, false, t => PoseTakeOff(stand, t));
        SaveClip("御风_Idle", 2.40f, true,  t => PoseHover(stand, t, 2.40f, false));
        SaveClip("御风_前进", 1.40f, true,  t => PoseHover(stand, t, 1.40f, true));
        SaveClip("御风_落地", 0.70f, false, t => PoseLand(stand, t));

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[YufengClipBuilder] 四个御风片段已烘焙到 " + OutDir);
    }

    // ================================================================ 姿势

    /// <summary>站立：直接用基准站姿</summary>
    static HumanPose PoseStand(HumanPose stand) => Clone(stand);

    /// <summary>
    /// 悬浮基准姿势。
    ///
    /// 【只动躯干，四肢一律保持基准站姿】—— 这一点很关键：
    /// 基准 Idle 本身的手臂就是自然垂在两侧、双腿并拢站直的，
    /// 正是「遗世独立的仙人」要的样子。之前画蛇添足去改手臂/腿，
    /// 结果因为肌肉空间正负号搞反，变成双手前平举、两腿张开、还抬着一只脚。
    /// 现在只做重心前倾这一件事。
    /// </summary>
    static HumanPose PoseHoverBase(HumanPose stand, float lean)
    {
        var p = Clone(stand);

        // 躯干整体前倾：腰 → 胸 → 上胸 一起走，力度主要压在上半身。
        //
        // 之前的问题：基准站姿里 Chest Front-Back 本身就是负值（约 -0.29），
        // 我又给颈/头加了反向补偿，等于把上半身扳了回去，
        // 结果只有腰在弯、胸和头立着 —— 看着就是"挺胸抬头"。
        // 现在颈/头【完全不补偿】，让它们跟着躯干一起前压。
        Add(p, "Spine Front-Back", lean * settings.前进_腰部占比);
        Add(p, "Chest Front-Back", lean * settings.前进_胸部占比);
        Add(p, "UpperChest Front-Back", lean * settings.前进_上胸占比);
        if (!Mathf.Approximately(settings.前进_头颈补偿, 0f))
        {
            Add(p, "Neck Nod Down-Up", lean * settings.前进_头颈补偿 * 0.6f);
            Add(p, "Head Nod Down-Up", lean * settings.前进_头颈补偿 * 0.4f);
        }

        return p;
    }
    /// <summary>升空：站立 → 悬浮，前段带一点蹬地起势</summary>
    static HumanPose PoseTakeOff(HumanPose stand, float t)
    {
        float k = Ease.OutCubic(Mathf.Clamp01(t / 0.90f));
        var a = PoseStand(stand);
        // 升空的落点用【悬停姿势】：状态机升空完先接的是御风_Idle，
        // 落点姿势和下一段起点一致，衔接才不会跳。
        var b = hasPoseHover ? Clone(poseHover) : PoseHoverBase(stand, 0.22f);
        var p = LerpPose(a, b, k);

        // 起势：最开始短短一下屈膝蓄力
        float crouch = Mathf.Sin(Mathf.Clamp01(t / 0.28f) * Mathf.PI) * settings.升空_蓄力幅度;
        Add(p, "Left Lower Leg Stretch", crouch);
        Add(p, "Right Lower Leg Stretch", crouch);
        return p;
    }

    /// <summary>落地：悬浮 → 站立，落地瞬间屈膝缓冲</summary>
    static HumanPose PoseLand(HumanPose stand, float t)
    {
        float k = Ease.OutCubic(Mathf.Clamp01(t / 0.70f));
        // 落地的起点同样用悬停姿势，从 Idle 进来时是连续的
        var a = hasPoseHover ? Clone(poseHover) : PoseHoverBase(stand, 0.22f);
        var b = PoseStand(stand);
        var p = LerpPose(a, b, k);

        // 触地缓冲：中段屈膝
        float absorb = Mathf.Sin(Mathf.Clamp01(t / 0.70f) * Mathf.PI) * settings.落地_缓冲幅度;
        Add(p, "Left Lower Leg Stretch", absorb);
        Add(p, "Right Lower Leg Stretch", absorb);
        return p;
    }

    /// <summary>
    /// 悬浮循环。moving=false 为原地悬停，moving=true 为御风前进。
    /// 肢体全部保持基准，只让躯干前倾 + 整体轻微上下浮动，
    /// 浮沉用正弦驱动，首尾自然闭合、可无缝循环。
    /// </summary>
    static HumanPose PoseHover(HumanPose stand, float t, float period, bool moving)
    {
        float phase01 = Mathf.Repeat(t / period, 1f);
        float phase = phase01 * Mathf.PI * 2f;

        // 基准：前进用 PoseDisplay 抓的姿势，悬停用 posedisplay2 抓的姿势；
        // 两份资产都没有时退回站立姿势。
        // 姿势本身就是策划要的效果，所以前倾参数默认是 0，只叠浮动和轻微摆动。
        HumanPose 基准;
        if (moving) 基准 = hasPoseMoving ? poseMoving : (hasPoseHover ? poseHover : stand);
        else        基准 = hasPoseHover  ? poseHover  : (hasPoseMoving ? poseMoving : stand);
        var p = Clone(基准);

        // 在飞行姿势之上叠前倾：前进时重心明显前移，悬停时几乎不动
        float lean = moving ? settings.前进_前倾总量 : settings.悬停_前倾总量;
        Add(p, "Spine Front-Back", lean * settings.前进_腰部占比);
        Add(p, "Chest Front-Back", lean * settings.前进_胸部占比);
        Add(p, "UpperChest Front-Back", lean * settings.前进_上胸占比);
        if (!Mathf.Approximately(settings.前进_头颈补偿, 0f))
        {
            Add(p, "Neck Nod Down-Up", lean * settings.前进_头颈补偿 * 0.6f);
            Add(p, "Head Nod Down-Up", lean * settings.前进_头颈补偿 * 0.4f);
        }

        // 上下浮动
        float bob = Mathf.Sin(phase) * (moving ? settings.前进_浮沉幅度 : settings.悬停_浮沉幅度);
        p.bodyPosition.y += bob;

        // 肢体轻微摆动：手臂左右反相微摆 + 手腕跟着飘，幅度很小，只为破掉"雕像感"
        float sway = Mathf.Sin(phase) * settings.摆动幅度;
        Add(p, "Left Arm Front-Back", sway);
        Add(p, "Right Arm Front-Back", -sway * 0.8f);
        Add(p, "Left Arm Down-Up", -sway * 0.4f);
        Add(p, "Right Arm Down-Up", sway * 0.4f);
        Add(p, "Left Forearm Stretch", Mathf.Sin(phase * 0.7f) * settings.摆动幅度 * 0.5f);
        Add(p, "Right Forearm Stretch", -Mathf.Sin(phase * 0.7f) * settings.摆动幅度 * 0.5f);
        Add(p, "Spine Left-Right", Mathf.Sin(phase * 0.5f) * settings.摆动幅度 * 0.6f);

        return p;
    }
    /// <summary>
    /// 从场景对象抓姿势并存成资产。对象不存在就跳过（保留已有资产）。
    /// </summary>
    static void CapturePoseFromScene(string objectName, string assetPath)
    {
        if (!TrySampleScenePose(objectName, out var pose)) return;

        var asset = AssetDatabase.LoadAssetAtPath<YufengPoseAsset>(assetPath);
        bool isNew = asset == null;
        if (isNew) asset = ScriptableObject.CreateInstance<YufengPoseAsset>();
        asset.FromHumanPose(pose, objectName);

        // 根高度归一化。
        // PoseDisplay 摆在世界哪个高度会被一起抓进 bodyPosition（posedisplay2 悬在 1.48 米，
        // 抓出来的根高就变成 3.97），而这段高度是会被动画真作用到角色身上的 —— 不处理的话
        // 一进御风角色就飞起来。角色的实际高度由 YufengFlight 代码控制，所以这里统一归到站姿高度。
        var src = GameObject.Find(objectName);
        float 世界偏移 = src != null ? src.transform.position.y : 0f;
        asset.bodyPosition.y = 站姿根高度 - Mathf.Max(0f, 0f);   // 直接归一到站姿高度
        asset.bodyPosition.x = 0f;
        asset.bodyPosition.z = 0f;
        Debug.Log("[YufengClipBuilder] " + objectName + " 根高归一化：抓到 "
                  + pose.bodyPosition.y.ToString("F3") + "（对象世界高度 " + 世界偏移.ToString("F2") + "）");
        if (isNew)
        {
            if (!AssetDatabase.IsValidFolder(PoseDir))
            {
                if (!AssetDatabase.IsValidFolder("Assets/Animations")) AssetDatabase.CreateFolder("Assets", "Animations");
                AssetDatabase.CreateFolder("Assets/Animations", "凭虚御风");
            }
            AssetDatabase.CreateAsset(asset, assetPath);
        }
        EditorUtility.SetDirty(asset);
        Debug.Log("[YufengClipBuilder] 已从 " + objectName + " 抓取姿势 -> " + assetPath);
    }

    /// <summary>
    /// 读场景里某个对象当前摆出来的姿势。
    /// 用 HumanPoseHandler 直接把骨骼层级读成肌肉值 —— 这样策划在 Inspector 里
    /// 掰好姿势、放进场景，就能直接被烘焙器当成基准用，不用我去猜数值。
    /// </summary>
    static bool TrySampleScenePose(string objectName, out HumanPose pose)
    {
        pose = new HumanPose();
        var go = GameObject.Find(objectName);
        if (go == null) return false;

        var anim = go.GetComponentInChildren<Animator>(true);
        if (anim == null || anim.avatar == null || !anim.avatar.isHuman) return false;

        var handler = new HumanPoseHandler(anim.avatar, anim.transform);
        var p = new HumanPose();
        handler.GetHumanPose(ref p);
        handler.Dispose();

        if (p.muscles == null || p.muscles.Length == 0) return false;
        pose = p;
        return true;
    }

    /// <summary>
    /// 按 0~1 的相位采样 Mixamo 飞行动画的姿势。
    /// 直接沿用它的肌肉值，所以原动画的动作与节奏都被保留下来，
    /// 我们只在它之上叠前倾、浮沉这些额外调整。
    /// </summary>
    static HumanPose SampleFlyPose(float phase01)
    {
        var pose = new HumanPose { muscles = new float[HumanTrait.MuscleCount] };
        if (!hasFly) return pose;

        float time = Mathf.Repeat(phase01, 1f) * flyLength;
        float Eval(string prop) => flyCurves != null && flyCurves.TryGetValue(prop, out var c) && c != null ? c.Evaluate(time) : 0f;

        for (int i = 0; i < HumanTrait.MuscleCount; i++)
            pose.muscles[i] = Eval(HumanTrait.MuscleName[i]);

        // Mixamo 这条 Flying 是「水平超人式」飞行动作：身体的趴平是写在
        // 【脊椎肌肉】里的，不是靠根旋转，所以只改 RootQ 没用，还是会趴着。
        // 解法：躯干和头一律用站立姿势，只从飞行片段里取【四肢】的动作。
        // 这样得到的就是「立着身体、四肢在飘」的站姿御风。
        if (flyCurves != null)
        {
            for (int i = 0; i < HumanTrait.MuscleCount && i < pose.muscles.Length; i++)
            {
                string mn = HumanTrait.MuscleName[i];
                bool isLimb = mn.StartsWith("Left ") || mn.StartsWith("Right ");
                // 肩也算躯干侧，去掉，避免飞行时耸肩
                if (mn.StartsWith("Left Shoulder") || mn.StartsWith("Right Shoulder")) isLimb = false;
                if (isLimb) pose.muscles[i] = Eval(mn);
            }
        }

        // 根姿态只保留朝向，去掉俯仰/翻滚与水平位移（位移由代码驱动）
        var q = new Quaternion(Eval("RootQ.x"), Eval("RootQ.y"), Eval("RootQ.z"), Eval("RootQ.w"));
        if (q.x == 0f && q.y == 0f && q.z == 0f && q.w == 0f) q = Quaternion.identity;
        pose.bodyRotation = Quaternion.Euler(0f, q.eulerAngles.y, 0f);
        pose.bodyPosition = new Vector3(0f, Eval("RootT.y") * 0.15f, 0f);
        return pose;
    }

    // ================================================================ 烘焙

    static void SaveClip(string name, float length, bool loop, System.Func<float, HumanPose> poseAt)
    {
        var clip = new AnimationClip { name = name, frameRate = Fps };

        int frames = Mathf.RoundToInt(length * Fps);
        var curves = new Dictionary<string, AnimationCurve>();

        for (int f = 0; f <= frames; f++)
        {
            float t = f / (float)Fps;
            var pose = poseAt(t);

            AddKey(curves, "RootT.x", t, pose.bodyPosition.x);
            AddKey(curves, "RootT.y", t, pose.bodyPosition.y);
            AddKey(curves, "RootT.z", t, pose.bodyPosition.z);
            AddKey(curves, "RootQ.x", t, pose.bodyRotation.x);
            AddKey(curves, "RootQ.y", t, pose.bodyRotation.y);
            AddKey(curves, "RootQ.z", t, pose.bodyRotation.z);
            AddKey(curves, "RootQ.w", t, pose.bodyRotation.w);

            for (int i = 0; i < HumanTrait.MuscleCount && i < pose.muscles.Length; i++)
                AddKey(curves, HumanTrait.MuscleName[i], t, pose.muscles[i]);
        }

        foreach (var kv in curves)
        {
            var binding = EditorCurveBinding.FloatCurve("", typeof(Animator), kv.Key);
            AnimationUtility.SetEditorCurve(clip, binding, kv.Value);
        }

        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = loop;
        AnimationUtility.SetAnimationClipSettings(clip, settings);

        string path = OutDir + "/" + name + ".anim";
        var old = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (old != null) AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(clip, path);
    }

    static void AddKey(Dictionary<string, AnimationCurve> curves, string prop, float t, float v)
    {
        if (!curves.TryGetValue(prop, out var c))
        {
            c = new AnimationCurve();
            curves[prop] = c;
        }
        c.AddKey(new Keyframe(t, v));
    }

    // ================================================================ 工具

    static void BuildMuscleIndex()
    {
        muscleIndex = new Dictionary<string, int>();
        for (int i = 0; i < HumanTrait.MuscleCount; i++)
            if (!muscleIndex.ContainsKey(HumanTrait.MuscleName[i]))
                muscleIndex[HumanTrait.MuscleName[i]] = i;
    }

    /// <summary>在基准值上叠加一个偏移</summary>
    static void Add(HumanPose pose, string muscle, float delta)
    {
        if (muscleIndex == null) BuildMuscleIndex();
        if (!muscleIndex.TryGetValue(muscle, out int i)) return;
        if (i >= pose.muscles.Length) return;
        pose.muscles[i] = Mathf.Clamp(pose.muscles[i] + delta, -1f, 1f);
    }

    static HumanPose Clone(HumanPose src)
    {
        return new HumanPose
        {
            bodyPosition = src.bodyPosition,
            bodyRotation = src.bodyRotation,
            muscles = (float[])src.muscles.Clone(),
        };
    }

    static HumanPose LerpPose(HumanPose a, HumanPose b, float k)
    {
        var p = Clone(a);
        p.bodyPosition = Vector3.Lerp(a.bodyPosition, b.bodyPosition, k);
        p.bodyRotation = Quaternion.Slerp(a.bodyRotation, b.bodyRotation, k);
        for (int i = 0; i < p.muscles.Length; i++)
            p.muscles[i] = Mathf.Lerp(a.muscles[i], b.muscles[i], k);
        return p;
    }

    /// <summary>从站立 Idle 片段 t=0 处采样出一套基准姿势</summary>
    static HumanPose SampleStandingPose(AnimationClip clip)
    {
        var binds = AnimationUtility.GetCurveBindings(clip);
        var map = new Dictionary<string, AnimationCurve>();
        foreach (var b in binds) map[b.propertyName] = AnimationUtility.GetEditorCurve(clip, b);

        float Eval(string prop) => map.TryGetValue(prop, out var c) && c != null ? c.Evaluate(0f) : 0f;

        var pose = new HumanPose { muscles = new float[HumanTrait.MuscleCount] };
        for (int i = 0; i < HumanTrait.MuscleCount; i++)
            pose.muscles[i] = Eval(HumanTrait.MuscleName[i]);

        // Mixamo 这条 Flying 是「水平超人式」飞行动作：身体的趴平是写在
        // 【脊椎肌肉】里的，不是靠根旋转，所以只改 RootQ 没用，还是会趴着。
        // 解法：躯干和头一律用站立姿势，只从飞行片段里取【四肢】的动作。
        // 这样得到的就是「立着身体、四肢在飘」的站姿御风。
        if (flyCurves != null)
        {
            for (int i = 0; i < HumanTrait.MuscleCount && i < pose.muscles.Length; i++)
            {
                string mn = HumanTrait.MuscleName[i];
                bool isLimb = mn.StartsWith("Left ") || mn.StartsWith("Right ");
                // 肩也算躯干侧，去掉，避免飞行时耸肩
                if (mn.StartsWith("Left Shoulder") || mn.StartsWith("Right Shoulder")) isLimb = false;
                if (isLimb) pose.muscles[i] = Eval(mn);
            }
        }

        // 基准站姿：原样取 Idle 片段的根位移与根旋转。
        //
        // 注意：这里【绝对不能】做任何缩放或清零。
        // 之前有一次批量替换误伤了这里，把 RootT.y 乘了 0.15
        // （0.942 → 0.142），角色整体下沉 0.8 米，起飞/落地就穿到地板下面去了。
        var q = new Quaternion(Eval("RootQ.x"), Eval("RootQ.y"), Eval("RootQ.z"), Eval("RootQ.w"));
        if (q.x == 0f && q.y == 0f && q.z == 0f && q.w == 0f) q = Quaternion.identity;
        pose.bodyRotation = q;
        pose.bodyPosition = new Vector3(Eval("RootT.x"), Eval("RootT.y"), Eval("RootT.z"));
        return pose;
    }

    static AnimationClip LoadClip(string fbx, string clipName)
    {
        foreach (var o in AssetDatabase.LoadAllAssetsAtPath(fbx))
            if (o is AnimationClip c && c.name == clipName) return c;
        return null;
    }

    static class Ease
    {
        public static float OutCubic(float x) => 1f - Mathf.Pow(1f - x, 3f);
    }
}
