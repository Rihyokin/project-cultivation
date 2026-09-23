using System;
using UnityEngine;

/// <summary>
/// 一组战斗属性数值。用固定长度数组按 <see cref="AttributeType"/> 下标存放，
/// 可以像普通字段一样被 Unity 序列化，方便挂在 ScriptableObject / MonoBehaviour 上。
/// </summary>
[Serializable]
public class AttributeSet
{
    [SerializeField]
    float[] values = new float[AttributeUtil.Count];

    public AttributeSet() { }

    public AttributeSet(AttributeSet other) { CopyFrom(other); }

    /// <summary>按属性读写数值</summary>
    public float this[AttributeType type]
    {
        get
        {
            int i = (int)type;
            if (values == null || i < 0 || i >= values.Length) return 0f;
            return values[i];
        }
        set
        {
            int i = (int)type;
            if (values == null || i < 0 || i >= values.Length) return;
            values[i] = value;
        }
    }

    public void Clear()
    {
        if (values == null) { values = new float[AttributeUtil.Count]; return; }
        for (int i = 0; i < values.Length; i++) values[i] = 0f;
    }

    public void CopyFrom(AttributeSet other)
    {
        if (other == null) return;
        EnsureSize();
        for (int i = 0; i < values.Length; i++) values[i] = other.values[i];
    }

    /// <summary>逐项相加（叠加增益 / 汇总来源时用）</summary>
    public void Add(AttributeSet other)
    {
        if (other == null) return;
        EnsureSize();
        for (int i = 0; i < values.Length; i++) values[i] += other.values[i];
    }

    /// <summary>逐项乘以倍率（做百分比修正时用）</summary>
    public void Multiply(float factor)
    {
        EnsureSize();
        for (int i = 0; i < values.Length; i++) values[i] *= factor;
    }

    /// <summary>按等级线性缩放（功法/神通每级增益 × 等级）</summary>
    public AttributeSet ScaledBy(float factor)
    {
        var r = new AttributeSet();
        for (int i = 0; i < r.values.Length; i++) r.values[i] = this[(AttributeType)i] * factor;
        return r;
    }

    /// <summary>把非 0 项拼成可读文本，便于调试</summary>
    public string ToReadableString()
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < AttributeUtil.Count; i++)
        {
            float v = this[(AttributeType)i];
            if (Mathf.Approximately(v, 0f)) continue;
            if (sb.Length > 0) sb.Append("  ");
            sb.Append(AttributeUtil.GetDisplayName((AttributeType)i)).Append('+').Append(AttributeUtil.Format((AttributeType)i, v));
        }
        return sb.Length == 0 ? "（无）" : sb.ToString();
    }

    void EnsureSize()
    {
        if (values == null || values.Length != AttributeUtil.Count)
        {
            var old = values;
            values = new float[AttributeUtil.Count];
            if (old != null)
                for (int i = 0; i < Mathf.Min(old.Length, values.Length); i++) values[i] = old[i];
        }
    }
}
