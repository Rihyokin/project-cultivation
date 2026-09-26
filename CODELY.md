# CODELY.md — 工作区根目录记忆（D:\project：cultivation）

## 这是什么

一个修仙题材的 3D 游戏项目。**工作区根目录不是 Unity 工程**：

| 路径 | 是什么 |
|---|---|
| `cultivation/` | **真正的 Unity/Tuanjie 工程**（`Assets/` `Packages/` `ProjectSettings/`） |
| `lore/` | 设定文档与参考图（NPC / 功法 / 法宝 / 灵阵 / UI 草图 / 场景参考） |
| `Tripo3d_Unity_Bridge/` | Tripo 建模桥（已作为嵌入式包装进 `cultivation/Packages/`） |
| `.dsh/` | 控制通道脚本（见下） |
| `missionObjective.txt` | 当前任务清单 |
| `开发注意事项.md` | **踩坑记录，动手前必读**（1570 行，每一条都是真事） |
| `村庄场景生成说明.md` | **动 `village.scene` 前先看这份**：地形 + 材质方案、配置速查表、验收渲图手法（`开发注意事项.md` §46 是它的错题本） |
| `宗门野外生成说明.md` | **动 `Sect_Wilderness.scene` 前先看这份**：400×400 野外地图（路网当地块边界 → 组团填树）、河流水位/河槽、刷怪区留白、树种与地表贴图的**实测色值表**（`开发注意事项.md` §49 是它的错题本） |
| `AI开发注意事项.md` | **做 NPC 的 AI 前先看这份**（从哪入手 / 步骤清单 / 验收清单 / 还没做的） |
| `飞弹制作文档.md` | **要做飞弹（NPC 或玩家）前先看这份**（QFX 弹丸的解剖 / 两条路线 / 踩过的坑） |
| `子弹特效资源说明.md` | QFX 子弹包的分类与用法（三件套 × 15 主题） |
| `特效资源清单.md` | 75 个特效 prefab 的路径清单（自动生成） |

## 控制管线（MCP）

harness 已配好 Unity MCP，工具名形如 `mcp__unity__unity_editor`（共 17 个）。
链路：`node .dsh\unity-mcp-proxy.mjs` →(stdio)→ `codely serve unity-mcp` →(TCP)→
编辑器里的 `cn.tuanjie.codely.bridge`。

- **前置条件**：Tuanjie/Unity 编辑器必须开着（端口在 `cultivation/Temp/.com-unity-codely.json`）
- 体检：`powershell -NoProfile -ExecutionPolicy Bypass -File ".dsh\health.ps1"`
- 注册行在 `C:\Users\Rihyo\.dsh\profiles\desktop\cordis.patch.yml`（`id: mcp-unity`），**在工程目录之外**
- **中间的垫片不能删**：codely 的 inputSchema 不在 harness 支持的 JSON Schema 子集内
  （带 `$schema`、用 `anyOf`、`additionalProperties` 是 schema），而 mcp-client 是
  「全有或全无」——原始输出实测 0/17 通过，一个不合规则 17 个工具全丢。
  改完消毒逻辑用 `.dsh\_diag\validate.mjs` 本地验证，**不要靠重启试**
- 兜底通道：`DshBridge.cs` + `.dsh/cmd.txt`（详见 `开发注意事项.md` §5.5，含完整踩坑记录）

## 战斗系统

伤害公式唯一入口：`cultivation/Assets/Scripts/Combat/CombatCalculator.cs`。
两个**正交**轴（物理/特殊 × 普通攻击/主动神通）组合出四种攻击：
伤害属性决定走**暴击**还是**会心**，攻击类别决定用**普攻**还是**主动法术**加成/减免。
`AttackSpec` 由调用方显式传入——**特殊 ≠ 远程，别用「有没有子弹」推断**。

**敌对照显血条**：`对主角好感度 < 0` 的 NPC 不需要锁定就常显血量 UI
（`NpcIndicator.应显示血条`，每帧同步；开关 `敌对常显血条` / `死亡时隐藏血条`）。

详见 `开发注意事项.md` §十一与 §8.1。**尤其 11.3：免暴率 / 免会心率公式是反转的
（攻击方暴击 ≥ 受击方暴击抗性 → 必定暴击），当前数值下玩家打 104/111 个 NPC 都是必定暴击。**

## 主动技能系统

技能栏 6 格（`UIPanelData.主动技能`，神通/法宝/灵阵共用）→ **第 N 格 = 快捷键 N**。
施放器 `cultivation/Assets/Scripts/Abilities/ActiveSkillCaster.cs` 挂在 Player 上；
范围伤害执行体是同目录的 `AreaSkillRunner.cs`。数值全在 `Assets/Data/Tables/主动神通表.csv`。

`尝试施放(槽位)` 是 public —— **测整条链路不用模拟按键**（见 `开发注意事项.md` §十二）。

**特效大小跟着 `范围` 走**：`缩放 = 范围 ÷ 特效基准半径`（`ActiveSkillCaster` 上的字段），
只缩 X/Z。**必须同时把实例上的粒子系统从 World 切成 Local 空间** —— World 空间下
transform 缩放只摊开粒子、不放大单个粒子，大小跟不住范围。**换特效资源要重新标定**
`特效基准半径`：`Effect_13_DangerClose`（焚天炎术现用）= **49.1 米**，
`Effect_13_Base/Effect_13_Explosion` = 9.9 米。

**朝向也要管**：`Effect_13_DangerClose` 是**倒着做**的 —— 原样生成时粒子世界 Y = −100~0
（从地底往上冒）。`ActiveSkillCaster.特效旋转` 默认 `(180,0,0)` 翻正，
翻后 Y = 0~100 才是「从天而降砸地」。**换特效资源时这个值也要改。**
> 这种"反向"用俯视相机看不出来（上下都只是一团火），必须打粒子世界坐标或开侧视相机。

## 游戏内 HUD

左下角一小块，**全是方框、不显示名字**：

```
┌────┐ ┌─┐┌─┐┌─┐┌─┐┌─┐┌─┐
│功法│ │1││2││3││4││5││6│
└────┘ └─┘└─┘└─┘└─┘└─┘└─┘
气血 ▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓
灵气 ▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓
```

生成器 `cultivation/Assets/Editor/Builders/HudBuilder.cs`（菜单 **修仙/生成游戏界面 HUD**，幂等，
只重建 `HudCanvas`）；运行时刷新 `Assets/Scripts/UI/PlayerHud.cs`；
悬停转发 `Assets/Scripts/UI/HudHoverTarget.cs`（挂在每个格子上）。

- **名字改成鼠标悬停弹出的半透明信息幕布**，移开消失
- 冷却：图标变暗 + 中央实时倒计时（≥1 秒整数、最后一秒一位小数）+
  暗部遮罩 `fillAmount = 冷却比例`（1→0，**竖向自上而下逐渐消退**）

> 必记的坑：**`Image` 没有 sprite 时 `fillAmount` 会被忽略**；
> **HUD 是 ScreenSpaceOverlay，`Camera.Render()` 抓不到，要用 `ScreenCapture`**（且偶尔会抓到空白帧）；
> **`EventSystem.current` 在 Edit Mode 是 null**，悬停只能在 Play Mode 测。
> 详见 `开发注意事项.md` §十三。

## 动手前的硬约束

1. **没有 git**——改大文件前先 `Copy-Item X.cs X.cs.bak`。历史上一次脚本失误删掉 630 行。
2. **PowerShell 执行策略是 Restricted**——跑 `.ps1` 必须带 `-ExecutionPolicy Bypass`，或 dot-source。
3. **编码**：`.cs` 和 `.csv` 必须带 UTF-8 BOM；不要用 `Set-Content`/`Out-File` 写它们。
4. **别用 PowerShell 拼多行 C#**——用 `edit`/`write` 工具（`\n` 不是转义符，踩过 4 次）。
5. **判断编译是否成功看时间戳**，不要读 `Editor.log` 尾巴（只追加，旧报错会一直在）。
6. **路径含全角冒号**（`D:\project：cultivation`）——编辑器内 `.NET` 反射的
   `Assembly.GetName()`/`CodeBase` 会抛 `Illegal byte sequence`，别这么用。
7. 类名/文件名全 ASCII，public 成员用中文。

> 详细版全部在 `开发注意事项.md`——**尤其第 0 节（开工前必读）、第八节（NPC/AI 的坑）、
> 第十一节（伤害公式）、第十二节（主动技能 + 特效坑）、第十三节（HUD）**。
