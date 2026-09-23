// Ghost / soul look for battle-formation spirits (战阵真灵).
//
// Built-in render pipeline surface shader. Deliberately ASCII-only: the project
// is Chinese-first everywhere else, but a shader that fails to compile because of
// a file-encoding hiccup is a very expensive failure mode, so labels stay English.
// The Chinese explanation lives in SpiritFormationManager / 开发注意事项.
//
// Three things make the "soul" read:
//   1. translucent body            (_Color.a, ~0.7 = the requested 30% transparency)
//   2. cold fresnel rim            (_RimColor / _RimPower / _RimStrength)
//   3. slow vertical shimmer       (_WaveScale / _WaveSpeed / _WaveStrength)
// ZWrite is off on purpose: a ghost must not occlude itself into a solid blob.
Shader "Cultivation/GhostSpirit"
{
    Properties
    {
        _MainTex ("Main Texture", 2D) = "white" {}
        _Color ("Body Tint (RGB) / Opacity (A)", Color) = (0.62, 0.86, 1.0, 0.70)
        _BodyBrightness ("Body Brightness", Range(0, 2)) = 0.85

        _RimColor ("Rim Color", Color) = (0.55, 0.92, 1.0, 1.0)
        _RimPower ("Rim Power", Range(0.5, 8)) = 2.6
        _RimStrength ("Rim Strength", Range(0, 4)) = 1.5
        _RimAlphaBoost ("Rim Alpha Boost", Range(0, 1)) = 0.25

        _WaveScale ("Wave Scale", Range(0.2, 12)) = 3.0
        _WaveSpeed ("Wave Speed", Range(0, 4)) = 1.1
        _WaveStrength ("Wave Strength", Range(0, 1)) = 0.30
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
        }

        ZWrite Off
        Cull Off
        Blend SrcAlpha OneMinusSrcAlpha

        CGPROGRAM
        #pragma surface surf Lambert alpha:fade
        #pragma target 3.0

        sampler2D _MainTex;
        fixed4 _Color;
        half _BodyBrightness;
        fixed4 _RimColor;
        half _RimPower;
        half _RimStrength;
        half _RimAlphaBoost;
        half _WaveScale;
        half _WaveSpeed;
        half _WaveStrength;

        struct Input
        {
            float2 uv_MainTex;
            float3 worldNormal;
            float3 viewDir;
            float3 worldPos;
        };

        void surf (Input IN, inout SurfaceOutput o)
        {
            fixed4 tex = tex2D(_MainTex, IN.uv_MainTex);
            fixed4 body = tex * _Color;

            // 1) Fresnel rim: bright where the surface turns away from the camera.
            half rim = 1.0h - saturate(dot(normalize(IN.worldNormal), normalize(IN.viewDir)));
            rim = pow(rim, _RimPower);

            // 2) Vertical shimmer travelling up the body.
            half wave = sin(IN.worldPos.y * _WaveScale - _Time.y * _WaveSpeed) * 0.5h + 0.5h;

            // Body dims a little where the wave is low, so the silhouette breathes.
            half bodyGain = 1.0h - _WaveStrength * 0.5h + _WaveStrength * wave;
            o.Albedo = body.rgb * _BodyBrightness * bodyGain;

            o.Emission = _RimColor.rgb * (rim * _RimStrength + wave * _WaveStrength * 0.35h);

            // Edges stay more solid than the middle: keeps the silhouette readable
            // even at low opacity, which is what sells "spirit" instead of "faded prop".
            o.Alpha = saturate(body.a * (1.0h - _RimAlphaBoost + _RimAlphaBoost * rim));
        }
        ENDCG
    }

    Fallback "Legacy Shaders/Transparent/Diffuse"
}
