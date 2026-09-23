// Upgrade NOTE: replaced 'mul(UNITY_MATRIX_MVP,*)' with 'UnityObjectToClipPos(*)'

Shader "Custom/PostColorModel" {
	Properties {
		_MainTex ("Base (RGB)", 2D) = "white" {}
	}
	SubShader {
		Tags { "RenderType"="Opaque" }
		LOD 200
		Pass {
			ZTest Always Cull Off ZWrite Off
			Fog { Mode off }
			
 			CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
 			
            struct OutPut {
                float4 pos : SV_POSITION;  
                float2 uv : TEXCOORD0;
                //float2 uv2 : TEXCOORD1;
            };
            
            sampler2D _MainTex;
            sampler2D _ModelTex;
            
            half4 _ModelBackColor;
            half4 _ModelColor;
            
            float _Modulus;
            
            sampler2D _CameraDepthTexture;
            
            OutPut vert (appdata_img v)
            {
                OutPut o;
                o.pos = UnityObjectToClipPos (v.vertex);
                o.uv = v.texcoord.xy;
                //o.uv2 = TRANSFORM_TEX (v.texcoord, _BlurTex);
                 
                return o;
            }
 
            half4 frag (OutPut o) : COLOR
            {
            	fixed4 screenColor = tex2D(_MainTex, o.uv);
            	
                half4 modelColor = float4(0,0,0,0);
                float2 adaUV = o.uv;
                adaUV.y =(1.0 - adaUV.y);
                fixed4 modelScreenColor = tex2D( _ModelTex,adaUV );
                
                float4 color = float4(0,0,0,0);
                
                float4 offsetColor = _ModelBackColor - modelScreenColor;
                
                float distanceC = (abs(offsetColor.r)+abs(offsetColor.g)+abs(offsetColor.b) )/1.0f;
               	
               	modelColor = lerp(_ModelBackColor,_ModelColor,distanceC );
				                
               	color = lerp(screenColor,modelColor,_Modulus);    
                
                return color;
            	
            }
            ENDCG
		}
		
	} 
	FallBack "Diffuse"
}
