using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// **玩家身上的临时增益**（丹药、buff 之类）。挂在 Player 上，没有就按需自建。
///
/// 结算路线：<c>PlayerCombatStats.Recalculate()</c> 先算「基础属性 + 当前功法」，
/// 最后调 <see cref="应用到"/> 把这里的增益叠上去 —— 所以增益**不会写进任何数据资产**，
/// 到期自动消失，属性回到原样。
///
/// 同名同属性的增益会**叠加数值、刷新时长**（吃两颗回春丹 = +10%，时长取较长的那个）。
/// </summary>
[DisallowMultipleComponent]
public class 玩家临时增益 : MonoBehaviour
{
    public struct 增益
    {
        public string 名字;
        public AttributeType 属性;
        public float 固定值;
        public float 百分比;
        public float 总时长;
        public float 剩余;

        /// <summary>剩余比例（做 buff 图标倒计时用）</summary>
        public float 进度 => 总时长 <= 0f ? 0f : Mathf.Clamp01(剩余 / 总时长);
    }

    readonly List<增益> 列表 = new List<增益>();
    PlayerCombatStats 战斗属性;

    public int 数量 => 列表.Count;
    public 增益 取(int i) => 列表[i];

    void Awake()
    {
        战斗属性 = GetComponent<PlayerCombatStats>();
    }

    /// <summary>加一个增益（同名同属性 → 叠加数值 + 刷新时长）</summary>
    public void 加(string 名字, AttributeType 属性类型, float 固定值, float 百分比, float 秒)
    {
        for (int i = 0; i < 列表.Count; i++)
        {
            if (列表[i].名字 == 名字 && 列表[i].属性 == 属性类型)
            {
                var b = 列表[i];
                b.固定值 += 固定值;
                b.百分比 += 百分比;
                b.总时长 = Mathf.Max(b.总时长, 秒);
                b.剩余 = Mathf.Max(b.剩余, 秒);
                列表[i] = b;
                重算();
                return;
            }
        }
        列表.Add(new 增益
        {
            名字 = 名字,
            属性 = 属性类型,
            固定值 = 固定值,
            百分比 = 百分比,
            总时长 = 秒,
            剩余 = 秒
        });
        重算();
    }

    public void 清空()
    {
        if (列表.Count == 0) return;
        列表.Clear();
        重算();
    }

    /// <summary>把当前所有增益叠到一份属性表上：先加固定值，再按"加完固定值之后的量"算百分比</summary>
    public void 应用到(AttributeSet 表)
    {
        if (表 == null) return;
        for (int i = 0; i < 列表.Count; i++)
        {
            var b = 列表[i];
            float 加后 = 表[b.属性] + b.固定值;
            表[b.属性] = 加后 + 加后 * b.百分比;
        }
    }

    void Update()
    {
        if (列表.Count == 0) return;

        bool 变了 = false;
        for (int i = 列表.Count - 1; i >= 0; i--)
        {
            var b = 列表[i];
            b.剩余 -= Time.deltaTime;
            if (b.剩余 <= 0f)
            {
                Debug.Log("[增益] 「" + b.名字 + "」到时间了，" + b.属性 + " 的加成收回");
                列表.RemoveAt(i);
                变了 = true;
            }
            else 列表[i] = b;
        }
        if (变了) 重算();
    }

    void 重算()
    {
        if (战斗属性 == null) 战斗属性 = GetComponent<PlayerCombatStats>();
        if (战斗属性 != null) 战斗属性.Recalculate();
    }
}
