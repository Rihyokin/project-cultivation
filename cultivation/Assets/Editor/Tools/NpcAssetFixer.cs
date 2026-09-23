using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// NPC 资产修复工具。解决两类"资产没做好处理"的问题：
///
/// **一、动画控制器是空壳**
/// `Assets/Animations/NPC/*.controller` 里的状态其实一个片段都没接
/// （实测 `BaiLuJing.controller` 引用了 0 个 GUID，所有 state.motion 都是 null）。
/// 真片段在 `Assets/resources/NPC/&lt;类&gt;/&lt;名字&gt;_Animation/&lt;名字&gt;@&lt;动作&gt;.FBX` 里。
/// 这个工具按名字把片段接回状态，并顺手把循环设置修好：
///   · Idle / Specialidle / Walk / Run / Fight → **循环**
///   · Attack* / Death / Wound / Dodge / Deffence → **不循环**（一次性）
///
/// 注意：FBX 里的片段不能直接改 `AnimationClipSettings`（只读），
/// 必须改 **ModelImporter.clipAnimations[].loopTime** 再重新导入。
///
/// **二、碰撞体被武器撑大**
/// 白鹿精的胶囊是 `height 2.6 / radius 1.3`（半径=半高，等于一个直径 2.6 米的球，
/// 乘根缩放 1.3 后 3.4 米），因为它是按"含武器的整体包围盒"撑出来的。
/// 结果 NPC 一靠近就把玩家挤开，永远够不到攻击距离。
/// 这个工具用 <see cref="NpcBodyBounds"/>（按体积排掉细长的武器）重算胶囊。
///
/// 菜单：修仙 / NPC 资产 / …
/// </summary>
public static class NpcAssetFixer
{
    const string 控制器目录 = "Assets/Animations/NPC";
    const string NPC资源目录 = "Assets/resources/NPC";

    // ============================================================ 一、动画

    [MenuItem("修仙/NPC 资产/检查动画控制器（只读）")]
    public static void 检查动画() => 处理动画(true);

    [MenuItem("修仙/NPC 资产/修复动画控制器（接片段 + 循环设置）")]
    public static void 修复动画()
    {
        if (!EditorUtility.DisplayDialog("修复 NPC 动画控制器",
            "会把 <角色>@<动作>.FBX 里的片段接回对应的控制器状态，\n" +
            "并按动作名设置循环（Idle/Walk/Run/Fight 循环，攻击/死亡/受击不循环）。\n\n" +
            "会重新导入一批 FBX，可能要等一会儿。继续？", "开始修复", "取消"))
            return;
        处理动画(false);
    }

    /// <summary>无确认弹窗的入口，方便从脚本 / 自动化调用</summary>
    public static void 处理动画(bool 只检查)
    {
        var 控制器们 = Directory.GetFiles(控制器目录, "*.controller");
        int 控制器数 = 0, 接上 = 0, 本来就对 = 0, 缺片段 = 0, 改了循环 = 0, 关了写默认 = 0;
        var 缺片段的例子 = new List<string>();
        var 缺模型的 = new List<string>();

        foreach (var 路径 in 控制器们)
        {
            string 名 = Path.GetFileNameWithoutExtension(路径);
            var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(路径);
            if (ctrl == null) continue;
            控制器数++;

            string 动画目录 = 找动画目录(名);
            if (动画目录 == null) { 缺模型的.Add(名); continue; }

            bool 动过 = false;
            foreach (var layer in ctrl.layers)
            {
                foreach (var st in layer.stateMachine.states)
                {
                    var 状态 = st.state;
                    if (状态.motion != null) { 本来就对++; continue; }

                    string fbx = Path.Combine(动画目录, 名 + "@" + 状态.name + ".FBX");
                    if (!File.Exists(fbx)) { 缺片段++; if (缺片段的例子.Count < 12) 缺片段的例子.Add(名 + "@" + 状态.name); continue; }

                    if (只检查) { 接上++; continue; }

                    bool 该循环 = 应该循环(状态.name);
                    if (设置FBX循环(fbx, 该循环)) 改了循环++;

                    var clip = 取片段(fbx);
                    if (clip == null) { 缺片段++; continue; }

                    状态.motion = clip;
                    接上++; 动过 = true;
                }
            }

            // 【保险】没接上片段的状态必须关掉 Write Defaults。
            //
            // Write Defaults = true 时，进入状态会把所有【没被动画驱动的属性重置成默认值】
            // —— 状态没有片段就等于「把所有骨骼重置成默认姿势」，
            // 蒙皮网格当场塌陷成一团（表现：姿势诡异 + 陷进地板）。
            // 这正是「狗也一样陷地」的原因，和体型 / 命名无关。
            foreach (var layer in ctrl.layers)
                foreach (var st in layer.stateMachine.states)
                    if (st.state.motion == null && st.state.writeDefaultValues)
                    {
                        if (!只检查) st.state.writeDefaultValues = false;
                        关了写默认++;
                        动过 = true;
                    }

            if (动过 && !只检查)
            {
                EditorUtility.SetDirty(ctrl);
                AssetDatabase.SaveAssets();
            }
        }

        string 头 = 只检查 ? "[NPC动画·检查] " : "[NPC动画·修复] ";
        Debug.Log(头 + "控制器 " + 控制器数 + " 个｜" + (只检查 ? "待接片段 " : "已接上 ") + 接上
            + "｜本来就接好的 " + 本来就对 + "｜找不到片段 " + 缺片段
            + "｜整个动画目录都缺 " + 缺模型的.Count + "｜关掉 Write Defaults " + 关了写默认
            + (只检查 ? "" : "｜调整了循环设置 " + 改了循环 + " 个 FBX"));
        if (缺片段的例子.Count > 0)
            Debug.Log(头 + "（前几个）找不到片段: " + string.Join(", ", 缺片段的例子));
        if (缺模型的.Count > 0)
            Debug.Log(头 + "没有对应的 _Animation 目录（这些 NPC 没有动画资源）: " + string.Join(", ", 缺模型的));
    }

    /// <summary>Idle / Walk / Run 这类要循环；攻击、死亡、受击是一次性</summary>
    static bool 应该循环(string 状态名)
    {
        switch (状态名)
        {
            case "Idle":
            case "Specialidle":
            case "Walk":
            case "Run":
            case "Fight":
                return true;
            default:
                return false;      // Attack* / Death / Wound / Dodge / Deffence
        }
    }

    /// <summary>找 &lt;名字&gt;_Animation 目录（大小写不敏感）</summary>
    static string 找动画目录(string 名)
    {
        if (!Directory.Exists(NPC资源目录)) return null;
        string 目标 = (名 + "_Animation").ToLowerInvariant();
        foreach (var 类目录 in Directory.GetDirectories(NPC资源目录))
            foreach (var 子 in Directory.GetDirectories(类目录))
                if (Path.GetFileName(子).ToLowerInvariant() == 目标) return 子;
        return null;
    }

    /// <summary>把 FBX 里那条片段的 loopTime 设成指定值（改了才重新导入）</summary>
    static bool 设置FBX循环(string fbx路径, bool 循环)
    {
        string assetPath = fbx路径.Replace('\\', '/');
        var imp = AssetImporter.GetAtPath(assetPath) as ModelImporter;
        if (imp == null) return false;

        var clips = imp.clipAnimations;
        if (clips == null || clips.Length == 0) clips = imp.defaultClipAnimations;
        if (clips == null || clips.Length == 0) return false;

        bool 变了 = false;
        foreach (var c in clips)
            if (c.loopTime != 循环) { c.loopTime = 循环; 变了 = true; }

        if (!变了) return false;
        imp.clipAnimations = clips;
        imp.SaveAndReimport();
        return true;
    }

    static AnimationClip 取片段(string fbx路径)
    {
        string assetPath = fbx路径.Replace('\\', '/');
        foreach (var o in AssetDatabase.LoadAllAssetsAtPath(assetPath))
            if (o is AnimationClip c && !c.name.StartsWith("__preview__")) return c;
        return null;
    }

    // ============================================================ 二、碰撞体

    [MenuItem("修仙/NPC 资产/检查碰撞体（只读）")]
    public static void 检查碰撞体() => 处理碰撞体(true);

    [MenuItem("修仙/NPC 资产/修复碰撞体（按身躯重算胶囊，排除武器）")]
    public static void 修复碰撞体()
    {
        if (!EditorUtility.DisplayDialog("修复 NPC 碰撞体",
            "会按【身躯】渲染体的包围盒重算每个 NPC prefab 的 CapsuleCollider，\n" +
            "并排除细长的武器（按体积筛）。\n\n" +
            "这会直接改 prefab 资产。继续？", "开始修复", "取消"))
            return;
        处理碰撞体(false);
    }

    /// <summary>无确认弹窗的入口，方便从脚本 / 自动化调用</summary>
    public static void 处理碰撞体(bool 只检查)
    {
        var prefabs = new List<string>(Directory.GetFiles(NPC资源目录, "*.prefab", SearchOption.AllDirectories));
        int 改过 = 0, 没渲染体 = 0, 已经合适 = 0, 没胶囊 = 0;
        var 例子 = new List<string>();

        foreach (var 路径 in prefabs)
        {
            string assetPath = 路径.Replace('\\', '/');
            if (只检查)
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
                if (go == null) continue;
                报告(go, ref 改过, ref 没渲染体, ref 已经合适, ref 没胶囊, 例子);
                continue;
            }

            // 用 LoadPrefabContents 安全地改 prefab
            var 内容 = PrefabUtility.LoadPrefabContents(assetPath);
            if (内容 == null) continue;
            try
            {
                bool 变了 = 修一个(内容, ref 没渲染体, ref 已经合适, ref 没胶囊, 例子);
                if (变了) { PrefabUtility.SaveAsPrefabAsset(内容, assetPath); 改过++; }
            }
            finally { PrefabUtility.UnloadPrefabContents(内容); }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log((只检查 ? "[NPC碰撞体·检查] " : "[NPC碰撞体·修复] ")
            + "prefab " + prefabs.Count + " 个｜" + (只检查 ? "需要改 " : "已改 ") + 改过
            + "｜本来就合适 " + 已经合适 + "｜没有胶囊 " + 没胶囊 + "｜没有渲染体 " + 没渲染体);
        if (例子.Count > 0)
            Debug.Log((只检查 ? "[NPC碰撞体·检查] " : "[NPC碰撞体·修复] ") + "（前几个）" + string.Join("  |  ", 例子));
    }

    static void 报告(GameObject go, ref int 改过, ref int 没渲染体, ref int 已经合适, ref int 没胶囊, List<string> 例子)
        => 修一个(go, ref 没渲染体, ref 已经合适, ref 没胶囊, 例子);

    /// <summary>返回 true 表示这个 prefab 的胶囊被（或需要被）改</summary>
    static bool 修一个(GameObject go, ref int 没渲染体, ref int 已经合适, ref int 没胶囊, List<string> 例子)
    {
        var 标签 = go.name + "(" + go.transform.localScale.x.ToString("0.##") + "×)";

        // 【重要】先把根缩放的补偿算掉：胶囊参数是本地空间的，
        // 但 NpcBodyBounds 给的是世界包围盒 → 会由 算胶囊 转成本地
        if (!NpcBodyBounds.算胶囊(go, out var 中心, out var 半径, out var 高度)) { 没渲染体++; return false; }

        var col = go.GetComponent<CapsuleCollider>();
        if (col == null) { 没胶囊++; return false; }

        bool 需要 = (col.center - 中心).magnitude > 0.05f
                 || Mathf.Abs(col.radius - 半径) > 0.05f
                 || Mathf.Abs(col.height - 高度) > 0.05f;

        if (!需要) { 已经合适++; return false; }

        if (例子.Count < 10)
            例子.Add(标签 + " 旧(r=" + col.radius.ToString("0.##") + " h=" + col.height.ToString("0.##") + ")"
                   + " → 新(r=" + 半径.ToString("0.##") + " h=" + 高度.ToString("0.##") + ")");

        col.direction = 1;                  // Y 轴
        col.center = 中心;
        col.radius = 半径;
        col.height = Mathf.Max(高度, 半径 * 2f);
        EditorUtility.SetDirty(col);

        // 【坑】把武器渲染体本身也去掉碰撞没用 —— 武器通常没有单独碰撞体，
        // 问题出在「胶囊是按含武器的整体包围盒撑的」，所以改的是胶囊参数本身。
        return true;
    }

    // ============================================================ 三、Root Motion
    //
    // 【2026-09-22 更新】prefab 上保持 applyRootMotion = **false** 仍然是对的，但**理由变了**：
    //
    //   · 当年关它的理由是「AI 用 transform 驱动移动，Root Motion 会和它打架，一次性片段位移累积 → 飞天」。
    //     这个理由现在依然成立 —— 所以**没有任何 AI 的 NPC**（练功木桩那类中立物件）
    //     必须保持 false，否则播 Wound/Attack 时会自己漂移。
    //
    //   · 但**有 AI 的 NPC** 现在由 `NpcAiBase.接管RootMotion()` 在 Awake 里把它设成 **true**，
    //     并实现 `OnAnimatorMove()` **只吃旋转、丢掉位移** ——
    //     因为攻击动作的「转体」烘在 Humanoid 的根旋转 `RootQ` 里，
    //     设成 false 等于把转体一起丢掉（冰魄怪"身体转不过来、手刺歪"就是这个）。
    //
    //   所以：**prefab 上留 false 是对的**（运行时 AI 会自己开），这个工具不用改行为，
    //   但它原来那段"必须关掉"的说明已经过时，别再照它推理。

    [MenuItem("修仙/NPC 资产/检查 Root Motion（只读）")]
    public static void 检查RootMotion() => 处理RootMotion(true);

    [MenuItem("修仙/NPC 资产/修复 Root Motion（prefab 上置 false；有 AI 的运行时会被 AI 接管）")]
    public static void 修复RootMotion()
    {
        if (!EditorUtility.DisplayDialog("修复 NPC 的 Root Motion",
            "会把所有 NPC prefab 的 Animator.applyRootMotion 置成 false。\n\n" +
            "**这是对的**：没有 AI 的 NPC（练功木桩那类）必须 false，\n" +
            "否则一次性片段（Wound / Attack）的位移会累积 →「挨一下直接飞天」。\n\n" +
            "**有 AI 的 NPC 不受影响** —— `NpcAiBase.接管RootMotion()` 会在 Awake 里把它开成 true，\n" +
            "并用自己的 `OnAnimatorMove()` **只吃旋转、丢掉位移**：\n" +
            "攻击动作的「转体」烘在根旋转里，全关掉的话身体就扭不过来（冰魄怪刺歪就是这个坑）。\n\n" +
            "会直接改 prefab 资产。继续？", "开始修复", "取消"))
            return;
        处理RootMotion(false);
    }

    /// <summary>无确认弹窗的入口</summary>
    public static void 处理RootMotion(bool 只检查)
    {
        var prefabs = new List<string>(Directory.GetFiles(NPC资源目录, "*.prefab", SearchOption.AllDirectories));
        int 开着 = 0, 本来就关 = 0, 没有Animator = 0, 改过 = 0, 存不下 = 0;
        var 例子 = new List<string>();
        var 坏prefab = new List<string>();

        foreach (var 路径 in prefabs)
        {
            string assetPath = 路径.Replace('\\', '/');

            if (只检查)
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
                if (go == null) continue;
                看一个(go, ref 开着, ref 本来就关, ref 没有Animator, 例子);
                continue;
            }

            var 内容 = PrefabUtility.LoadPrefabContents(assetPath);
            if (内容 == null) continue;
            try
            {
                var an = 内容.GetComponent<Animator>();
                if (an == null) { 没有Animator++; continue; }
                if (!an.applyRootMotion) { 本来就关++; continue; }

                // 【坑】prefab 里**有丢失的脚本**时 PrefabUtility.SaveAsPrefabAsset 会直接报错拒绝保存
                //（"You are trying to save a Prefab with a missing script"）。
                // 这种 prefab 本身就已经是坏的，跳过并单独列出来，别把整批修复带崩。
                bool 有丢失脚本 = false;
                foreach (var c in 内容.GetComponentsInChildren<Component>(true))
                    if (c == null) { 有丢失脚本 = true; break; }
                if (有丢失脚本) { 存不下++; 坏prefab.Add(内容.name); continue; }

                if (例子.Count < 10) 例子.Add(内容.name);
                an.applyRootMotion = false;
                EditorUtility.SetDirty(an);
                PrefabUtility.SaveAsPrefabAsset(内容, assetPath);
                改过++;
            }
            finally { PrefabUtility.UnloadPrefabContents(内容); }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        string 头 = 只检查 ? "[RootMotion·检查] " : "[RootMotion·修复] ";
        if (只检查)
            Debug.Log(头 + "prefab " + prefabs.Count + " 个｜**prefab 上开着 Root Motion** " + 开着
                + "（有 AI 的没关系，运行时会被接管；**没 AI 的要修**）"
                + "｜本来就关 " + 本来就关 + "｜没 Animator " + 没有Animator
                + (例子.Count > 0 ? "\n       开着的例子: " + string.Join(", ", 例子) : ""));
        else
            Debug.Log(头 + "已置 false " + 改过 + " 个｜本来就关 " + 本来就关 + "｜没 Animator " + 没有Animator
                + "｜**有丢失脚本、保存不了** " + 存不下
                + (例子.Count > 0 ? "\n       改过: " + string.Join(", ", 例子) : "")
                + (坏prefab.Count > 0 ? "\n       ★ 坏 prefab（有 Missing 脚本，要单独修或删）: " + string.Join(", ", 坏prefab) : ""));
    }

    static void 看一个(GameObject go, ref int 开着, ref int 本来就关, ref int 没有Animator, List<string> 例子)
    {
        var an = go.GetComponent<Animator>();
        if (an == null) { 没有Animator++; return; }
        if (an.applyRootMotion) { 开着++; if (例子.Count < 10) 例子.Add(go.name); }
        else 本来就关++;
    }

    // ============================================================ 四、模型尺寸体检

    /// <summary>
    /// 检查 NPC 的**模型大小**是不是跑偏了。
    ///
    /// 为什么要专门查：动物的模型是外部导入的，**根缩放各不一样**（实测 6~14），
    /// 只要有一个忘了设（例如 `XiaoLu_01` 根缩放是 1、别的都是 10 上下），
    /// 它就会小到只有 **0.13 米** —— 比小鸡还小两倍，可**单看它自己完全正常**，
    /// 只有跟同类摆在一起才看得出来。
    ///
    /// 判定办法：**按类型分组取中位数**，凡高度不到中位数 1/3、或超过 3 倍的就报出来。
    /// 只读，不改任何东西。
    /// </summary>
    [MenuItem("修仙/NPC 资产/检查模型尺寸（只读）")]
    public static void 检查模型尺寸() => 体检尺寸();

    public static void 体检尺寸()
    {
        var 表 = new Dictionary<string, List<尺寸条目>>();

        foreach (var g in AssetDatabase.FindAssets("t:NpcDefinition", new[] { "Assets/Data/Generated/NpcDefinition" }))
        {
            var def = AssetDatabase.LoadAssetAtPath<NpcDefinition>(AssetDatabase.GUIDToAssetPath(g));
            if (def == null) continue;

            var prefab = 找prefab(def.id);
            if (prefab == null) continue;

            var go = AssetDatabase.LoadAssetAtPath<GameObject>(prefab);
            if (go == null) continue;
            if (!NpcBodyBounds.取(go, out var b)) continue;

            string 类型 = def.类型.ToString();
            if (!表.ContainsKey(类型)) 表[类型] = new List<尺寸条目>();
            表[类型].Add(new 尺寸条目 { 名 = def.id, 高 = b.size.y, 缩放 = go.transform.localScale.x });
        }

        int 报出 = 0;
        var 报告 = new System.Text.StringBuilder();

        foreach (var kv in 表)
        {
            if (kv.Value.Count < 3) continue;
            var 高们 = new List<float>();
            foreach (var e in kv.Value) 高们.Add(e.高);
            高们.Sort();
            float 中位 = 高们[高们.Count / 2];
            if (中位 <= 0.001f) continue;

            foreach (var e in kv.Value)
            {
                float 比 = e.高 / 中位;
                if (比 >= 0.34f && 比 <= 3f) continue;

                报出++;
                报告.AppendLine("  [!] " + e.名.PadRight(24) + " 类型=" + kv.Key.PadRight(4)
                    + " 高=" + e.高.ToString("0.000").PadLeft(8) + " 米"
                    + "  根缩放=" + e.缩放.ToString("0.##").PadLeft(5)
                    + "  同类中位=" + 中位.ToString("0.00") + " 米（是它的 " + 比.ToString("0.00") + " 倍）");
            }
        }

        Debug.Log("[尺寸体检] 扫了 " + 表.Count + " 个类型｜尺寸可疑 " + 报出 + " 个"
            + (报出 > 0
               ? "\n        判据：高度不到同类中位数的 1/3、或超过 3 倍\n" + 报告
               + "        注意：这只是「离群值」，不一定是错的 —— 小鸡本来就该比狗小得多，\n"
               + "              这种天生就小的会误报，要人判断。真正要看的是那种「一个人特别离谱」的。"
               : " —— 全部在同类型合理范围内"));

        var 全 = new System.Text.StringBuilder();
        foreach (var kv in 表)
        {
            全.AppendLine("── " + kv.Key + "（" + kv.Value.Count + " 个）");
            var 排序 = new List<尺寸条目>(kv.Value);
            排序.Sort((a, b) => a.高.CompareTo(b.高));
            foreach (var e in 排序)
                全.AppendLine("   " + e.名.PadRight(24) + " 高=" + e.高.ToString("0.000").PadLeft(8)
                    + " 米  根缩放=" + e.缩放.ToString("0.##"));
        }
        Debug.Log("[尺寸体检·全表]\n" + 全);
    }

    class 尺寸条目 { public string 名; public float 高; public float 缩放; }

    /// <summary>按定义 id 找 prefab（`beast_gou` → `Animal/Gou/Gou_01.prefab`）</summary>
    static string 找prefab(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        int i = id.IndexOf('_');
        if (i <= 0) return null;
        string 关键字 = id.Substring(i + 1).ToLowerInvariant();

        foreach (var 类目录 in Directory.GetDirectories(NPC资源目录))
        {
            if (Path.GetFileName(类目录).EndsWith("_Animation")) continue;
            foreach (var 子 in Directory.GetDirectories(类目录))
            {
                if (Path.GetFileName(子).EndsWith("_Animation")) continue;
                if (Path.GetFileName(子).ToLowerInvariant() != 关键字) continue;

                var ps = Directory.GetFiles(子, "*.prefab");
                if (ps.Length > 0) return ps[0].Replace('\\', '/');
            }
        }
        return null;
    }
}
