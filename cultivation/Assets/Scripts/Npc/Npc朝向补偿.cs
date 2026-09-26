using UnityEngine;

/// <summary>
/// **把 <see cref="NpcAiBase.模型朝向补偿"/> 在运行时补上**（这个值存不进 prefab，只能运行时设）。
///
/// 【为什么需要它】Tripo 那批村民模型的**视觉正面是 +X**，而项目的约定是 `transform.forward`（+Z）——
/// `NpcAiBase` 转身体时用的是
/// `transform.rotation = LookRotation(方向) * Euler(0, 模型朝向补偿, 0)`，而补偿**默认 0**，
/// 所以不补的话 NPC 会**永远侧对目标 90°**（用户实测报的"看到我就侧过去/背过去"）。
///
/// 【为什么不用别的办法】两条路都试过、都不行：
///   ① 转骨架 `Armature` → **会被动画片段覆盖**：实测 prefab 里烘成 (270,90,0)，
///      动画跑 1.3 秒后被打回 (270,0,0) —— 片段里有 Armature 的旋转轨道 ✗
///   ② 在根和模型之间插一层去转 → **片段的骨骼路径整体偏一级**（"Armature/Hips" → "补偿层/Armature/Hips"），
///      动画直接不播 ✗
/// 而 `模型朝向补偿` 是 AI **每帧自己乘上去**的 ✓ 所以这里是唯一稳的位置。
///
/// 【怎么用】挂在 NPC 预制体的**根**上（跟 `Animator` / `NpcInstance` 同一层），填好 `补偿角`。
/// AI（`NpcAiBase`）是运行时按 <see cref="NpcDefinition.类型"/> 装配的，不一定同一帧就在，
/// 所以这里只在前 <see cref="最多等"/> 秒里轮询找它；设好一次就 `enabled = false`，**没有每帧开销**。
/// </summary>
[DisallowMultipleComponent]
public class Npc朝向补偿 : MonoBehaviour
{
    [Tooltip("模型视觉正面相对 transform.forward 的偏差（度）。Tripo 村民 = 90")]
    public float 补偿角 = 90f;

    [Tooltip("最多等几秒（AI 是运行时装配的）。等不到会在 Console 里报出来")]
    public float 最多等 = 3f;

    NpcAiBase 大脑;
    float 计时;

    void Update()
    {
        if (大脑 == null) 大脑 = GetComponent<NpcAiBase>();

        if (大脑 != null)
        {
            大脑.模型朝向补偿 = 补偿角;      // 设一次就够
            enabled = false;
            return;
        }

        计时 += Time.deltaTime;
        if (计时 > 最多等)
        {
            Debug.LogWarning("[朝向补偿] " + name + " 上没等到 NpcAiBase，朝向补偿没生效"
                             + "（检查这个 NPC 的定义「类型」是不是人类/妖魔这类会装 AI 的）", this);
            enabled = false;
        }
    }

    // ---- ASCII 别名 ----
    public float YawOffset { get => 补偿角; set => 补偿角 = value; }
}
