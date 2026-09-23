// Camera -> character occlusion fade.
//
// WHAT IT DOES
//   Fades whatever is standing between the camera and the character, so the
//   character stays visible and controllable. Nothing else in the scene changes.
//
// THE CRITERION (this is the important part)
//   "Blocks the camera's view of the character" is NOT the same as "sits inside a
//   cylinder around the camera->character line". A cylinder also swallows the
//   ground under the character's feet (the axis floats ~2m above it), which is not
//   an occluder at all.
//
//   Nor is a screen-space DISC centred on the character good enough: a disc reaches
//   below the feet, so the ground right under the character still gets faded.
//
//   The correct test is two conditions:
//
//     1. the fragment overlaps the character's SCREEN RECTANGLE (feet -> head,
//        widened by a margin), and
//     2. the fragment is CLOSER TO THE CAMERA than the character is.
//
//   The ground below the feet falls outside that rectangle, so it is never touched.
//   This is a cone from the camera through the character's silhouette, cut off at
//   the character's depth. The rectangle is driven by the character's actual
//   on-screen size, so it stays correct at any zoom, and the soft edge is a
//   smoothstep outside the rectangle.
//
//   Partial fix history (why the two earlier criteria were wrong):
//     v1 cylinder around the axis      -> ground under the feet (axis floats ~2m up)
//     v2 screen disc centred on chest  -> ground below the feet (disc reaches down)
//
// WHY A CUSTOM SHADER INSTEAD OF A MATERIAL TRICK
//   Transparency is a per-DRAW-CALL property: it cannot be driven per object with
//   a MaterialPropertyBlock on the built-in shader, and a per-object alpha gives a
//   hard object-shaped edge instead of the soft edge we want. The test has to
//   happen PER FRAGMENT.
//
// WHY ALPHATEST QUEUE + ZWrite ON
//   "Queue"="AlphaTest" draws AFTER all opaque geometry, so blending lands on a
//   finished background. Keeping ZWrite On means faded objects still occlude each
//   other by depth instead of blending in arbitrary draw order.
//
// WHY addshadow
//   A surface shader with an alpha pragma does NOT generate a shadow caster unless
//   you ask for it. Without addshadow the faded objects silently lose their
//   shadows, which reads as "the shadows look wrong".
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
        #pragma surface surf Lambert alpha:fade addshadow
        #pragma target 3.0

        sampler2D _MainTex;
        fixed4 _Color;
        half _Cutoff;

        // ---- Driven every frame by OcclusionFade.cs (global uniforms) ----
        // xy = rectangle min, zw = rectangle max.
        // X is pre-multiplied by the aspect ratio, so BOTH axes are in units of
        // SCREEN HEIGHT -- that keeps the window's shape correct on wide screens.
        float4 _OcclRect;
        // x = character distance to the camera (m)
        // y = feather width (screen-height units)
        // z = strength (0..1)
        // w = enabled (0/1)
        float4 _OcclParams;

        struct Input
        {
            float2 uv_MainTex;
            float3 worldPos;
            float4 screenPos;
        };

        void surf (Input IN, inout SurfaceOutput o)
        {
            fixed4 c = tex2D(_MainTex, IN.uv_MainTex) * _Color;

            // Keep the source material's own silhouette. Legacy Diffuse has no
            // _Cutoff (so this is a no-op there); Transparent/Cutout/Diffuse needs it.
            clip(_Cutoff > 0.0h ? c.a - _Cutoff : 1.0h);

            o.Albedo = c.rgb;
            o.Alpha = c.a;

            if (_OcclParams.w > 0.5h && _OcclParams.z > 0.0h)
            {
                // (1) Does this fragment overlap the character's screen rectangle?
                float aspect = _ScreenParams.x / max(_ScreenParams.y, 1.0h);
                float2 uv = IN.screenPos.xy / max(IN.screenPos.w, 0.0001h);
                float2 p = float2(uv.x * aspect, uv.y);

                // Distance OUTSIDE the rectangle, in screen-height units.
                // Zero anywhere inside, so the whole silhouette is fully faded.
                // The ground below the feet sits outside -> never touched.
                float2 outside = max(max(_OcclRect.xy - p, p - _OcclRect.zw), 0.0);
                half covers = 1.0h - smoothstep(0.0h, max(_OcclParams.y, 0.0005h),
                                                length(outside));

                // (2) Must be in FRONT of the character. Without this the ground the
                //     character stands on gets faded too, because it sits inside the
                //     cone while occluding nothing.
                //     Ramped over ~0.55m so objects straddling the character's depth
                //     do not show a hard seam.
                float distToCam = distance(IN.worldPos, _WorldSpaceCameraPos);
                half inFront = 1.0h - smoothstep(_OcclParams.x - 0.60h,
                                                 _OcclParams.x - 0.05h, distToCam);

                half k = covers * inFront;
                o.Alpha = c.a * (1.0h - _OcclParams.z * k);
            }
        }
        ENDCG
    }

    Fallback "Legacy Shaders/Diffuse"
}
