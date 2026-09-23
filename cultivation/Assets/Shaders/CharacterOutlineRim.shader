// Player x-ray outline -- BORDER layer.
//
// WHY A DUPLICATE INSTEAD OF TOUCHING THE SCENE
//   The previous attempt faded whatever sat between the camera and the character,
//   which meant swapping materials on the whole environment. This shader is worn
//   only by a copy of the character's own meshes, so the scene is never touched.
//
// WHY ZTest Always
//   That is the entire point: the mark has to be readable THROUGH the building or
//   tree that hides the character. ZWrite stays Off so the marks never occlude
//   anything themselves.
//
// HOW THE BORDER IS MADE (this shader is half of it)
//   CharacterOutlineFill draws the plain silhouette FIRST (Queue "Overlay") and
//   stamps stencil = 1 wherever it landed. This pass then draws the mesh INFLATED
//   along its normals with front faces culled, but ONLY where the stencil is not
//   stamped -- so the part that would sit on top of the silhouette is discarded and
//   all that survives is the inflated ring around it.
//
//   A stencil is required here, not just pass ordering: with ZTest Always the hull's
//   far side covers the whole silhouette, so "fill then hull" would just paint the
//   character solid (that is exactly what the first version did).
//
//   This is why the queues are Fill="Overlay" (4000) then Rim="Overlay+1" (4001).
//
// STRUCTURE NOTE
//   The CGPROGRAM sits DIRECTLY in the SubShader with the render state at SubShader
//   level, and there is no explicit Pass block. That is the pattern GhostSpirit.shader
//   already uses successfully; wrapping a #pragma surface in Pass { ... } does NOT
//   parse here ("unexpected TOK_PASS").
//
// Unlit via Emission with Albedo 0: the mark must stay readable inside a shaded
// alley or at night, and Lambert lighting would darken it.
//
// Labels are ASCII only on purpose: see GhostSpirit.shader for the reason.
Shader "Cultivation/CharacterOutlineRim"
{
    Properties
    {
        _OutlineColor ("Outline Color", Color) = (1.0, 0.82, 0.25, 1.0)
        _OutlineWidth ("Outline Width (m)", Range(0, 0.15)) = 0.035
    }

    SubShader
    {
        Tags { "Queue"="Overlay+1" "RenderType"="Transparent" "IgnoreProjector"="True" }
        ZTest Always
        ZWrite Off
        Cull Front
        Blend SrcAlpha OneMinusSrcAlpha

        // Draw only where the silhouette pass did NOT stamp: that leaves the ring.
        Stencil
        {
            Ref 1
            Comp NotEqual
        }

        CGPROGRAM
        #pragma surface surf Unlit vertex:vert alpha:fade
        #pragma target 3.0

        fixed4 _OutlineColor;
        half _OutlineWidth;

        // NOTE: this version does NOT auto-generate the surface shader Input
        // struct -- leaving it implicit fails with `Unexpected identifier "Input"`.
        // GhostSpirit.shader declares it for the same reason.
        struct Input
        {
            float3 worldPos;
        };

        // Unlit: return the albedo straight out, ignoring every light. Lighting
        // would darken the mark in a shaded alley, and going through Lambert with
        // Albedo=0 + Emission did NOT come out as the requested colour.
        inline half4 LightingUnlit (SurfaceOutput s, half3 lightDir, half atten)
        {
            return half4(s.Albedo, s.Alpha);
        }

        void vert (inout appdata_full v)
        {
            v.vertex.xyz += v.normal * _OutlineWidth;
        }

        void surf (Input IN, inout SurfaceOutput o)
        {
            o.Albedo = _OutlineColor.rgb;
            o.Alpha = _OutlineColor.a;
        }
        ENDCG
    }

    Fallback Off
}
