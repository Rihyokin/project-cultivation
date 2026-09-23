// Player x-ray outline -- SILHOUETTE layer. See CharacterOutlineRim.shader for the
// full explanation; this is the other half of the pair.
//
// This pass draws the NORMAL mesh (no inflation) through everything, and STAMPS
// stencil = 1 where it landed. The rim pass (Queue "Overlay+1") then draws only
// where that stamp is absent, which is what turns an inflated blob into a ring.
//
// The fill is deliberately SEMI-TRANSPARENT by default: a solid fill reads as
// "the character teleported in front of the wall", while a translucent fill reads
// as "the character is behind this wall, here he is".
//
// Labels are ASCII only on purpose: see GhostSpirit.shader for the reason.
Shader "Cultivation/CharacterOutlineFill"
{
    Properties
    {
        _FillColor ("Silhouette Color (A = opacity)", Color) = (1.0, 0.82, 0.25, 0.35)
    }

    SubShader
    {
        Tags { "Queue"="Overlay" "RenderType"="Transparent" "IgnoreProjector"="True" }
        ZTest Always
        ZWrite Off
        Cull Back
        Blend SrcAlpha OneMinusSrcAlpha

        // Stamp the silhouette so the rim pass can punch its middle out.
        Stencil
        {
            Ref 1
            Comp Always
            Pass Replace
        }

        CGPROGRAM
        #pragma surface surf Unlit alpha:fade
        #pragma target 3.0

        fixed4 _FillColor;

        // NOTE: this version does NOT auto-generate the surface shader Input
        // struct -- leaving it implicit fails with `Unexpected identifier "Input"`.
        // GhostSpirit.shader declares it for the same reason.
        struct Input
        {
            float3 worldPos;
        };

        // Unlit: return the albedo straight out, ignoring every light. See
        // CharacterOutlineRim.shader for why Lambert+Emission was abandoned.
        inline half4 LightingUnlit (SurfaceOutput s, half3 lightDir, half atten)
        {
            return half4(s.Albedo, s.Alpha);
        }

        void surf (Input IN, inout SurfaceOutput o)
        {
            o.Albedo = _FillColor.rgb;
            o.Alpha = _FillColor.a;
        }
        ENDCG
    }

    Fallback Off
}
