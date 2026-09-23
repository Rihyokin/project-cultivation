// Camera -> character occlusion fade.
//
// WHY THIS EXISTS
//   With a top-down-ish camera, buildings / trees sitting between the camera and
//   the character hide the character. This shader fades exactly those fragments
//   that fall inside the cylinder whose axis is camera->character, so every other
//   pixel of the scene renders exactly as it did before.
//
// WHY A CUSTOM SHADER INSTEAD OF A MATERIAL SWAP TRICK
//   Transparency is a per-DRAW-CALL property. It cannot be driven per object with
//   a MaterialPropertyBlock on the built-in shader, and a per-object alpha would
//   give a hard object-shaped edge instead of the requested soft edge. The test
//   has to happen PER FRAGMENT, so we need our own shader.
//
// WHY ALPHATEST QUEUE + ZWrite ON
//   "Queue"="AlphaTest" means we draw AFTER all opaque geometry, so blending lands
//   on a finished background. Keeping ZWrite On means faded buildings still occlude
//   each other by depth, instead of blending in arbitrary draw order (which is what
//   makes naive alpha-blended scenery look broken).
//
// WHY IT LOOKS IDENTICAL OUTSIDE THE CYLINDER
//   The source material is "Legacy Shaders/Diffuse", which is just
//   Lambert lighting * (_MainTex * _Color). This shader replicates exactly that,
//   and alpha stays at _Color.a outside the cylinder so those pixels blend as if
//   fully opaque.
//
// Labels are ASCII only on purpose: see GhostSpirit.shader for the reason.
Shader "Cultivation/OcclusionFade"
{
    Properties
    {
        _MainTex ("Base (RGB)", 2D) = "white" {}
        _Color ("Main Color", Color) = (1,1,1,1)
        // 0 = no cutout. The environment also uses
        // "Legacy Shaders/Transparent/Cutout/Diffuse", whose _Cutoff is copied over.
        _Cutoff ("Alpha Cutoff", Range(0,1)) = 0
    }

    SubShader
    {
        Tags { "Queue"="AlphaTest" "RenderType"="TransparentCutout" "IgnoreProjector"="True" }
        LOD 200

        ZWrite On
        Blend SrcAlpha OneMinusSrcAlpha

        CGPROGRAM
        #pragma surface surf Lambert alpha:fade
        #pragma target 3.0

        sampler2D _MainTex;
        fixed4 _Color;
        half _Cutoff;

        // ---- Driven every frame by OcclusionFade.cs (global uniforms) ----
        float4 _OcclA;        // xyz = camera position
        float4 _OcclB;        // xyz = character position
        float4 _OcclParams;   // x = radius, y = feather, z = strength(0..1), w = tEnd
        float4 _OcclParams2;  // x = tStart, y = enabled(0/1)

        struct Input
        {
            float2 uv_MainTex;
            float3 worldPos;
        };

        void surf (Input IN, inout SurfaceOutput o)
        {
            fixed4 c = tex2D(_MainTex, IN.uv_MainTex) * _Color;

            // Keep the source material's own silhouette. Legacy Diffuse has no
            // _Cutoff (so this is a no-op there); Transparent/Cutout/Diffuse needs it.
            clip(_Cutoff > 0.0h ? c.a - _Cutoff : 1.0h);

            o.Albedo = c.rgb;
            o.Alpha = c.a;

            if (_OcclParams2.y > 0.5h && _OcclParams.z > 0.0h)
            {
                // Distance from this fragment to the camera->character segment.
                float3 axis = _OcclB.xyz - _OcclA.xyz;
                float axisLen2 = max(dot(axis, axis), 0.0001h);
                float t = dot(IN.worldPos - _OcclA.xyz, axis) / axisLen2;
                float3 proj = _OcclA.xyz + axis * t;
                float radial = distance(IN.worldPos, proj);

                // Radial falloff: 1 at the axis, 0 at radius+feather.
                // THIS is the soft edge -- a gradient, not a hard cylinder wall.
                half inside = 1.0h - smoothstep(_OcclParams.x, _OcclParams.x + _OcclParams.y, radial);

                // Keep the effect away from both ends: right at the camera, and
                // right at the character's feet (fading the ground he stands on
                // looks awful).
                half along = smoothstep(_OcclParams2.x, _OcclParams2.x + 0.08h, t)
                           * (1.0h - smoothstep(_OcclParams.w, 1.0h, t));

                half k = inside * along;
                o.Alpha = c.a * (1.0h - _OcclParams.z * k);
            }
        }
        ENDCG
    }

    Fallback "Legacy Shaders/Diffuse"
}
