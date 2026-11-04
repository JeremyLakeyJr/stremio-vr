Shader "StremioVR/CinemaScreen" {
    Properties {
        _MainTex ("Screen Texture", 2D) = "black" {}
        _StereoMode ("Stereo Mode (0: 2D, 1: SBS, 2: OU)", Float) = 0
        _SwapEyes ("Swap Eyes", Float) = 0
        _HdrMode ("HDR Mode (0: SDR, 1: HLG, 2: HDR10/PQ, 3: Gamut Boost)", Float) = 0
        _Brightness ("Brightness", Range(0.2, 2.0)) = 1.0
        _Contrast ("Contrast", Range(0.5, 2.0)) = 1.0
        _Saturation ("Saturation", Range(0.0, 2.0)) = 1.0
    }
    SubShader {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 100
        Cull Off
        ZWrite On

        Pass {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            struct appdata {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float _StereoMode; // 0=2D, 1=SBS, 2=OU
            float _SwapEyes;   // 0=Normal, 1=Inverted
            float _HdrMode;    // 0=SDR, 1=HLG, 2=HDR10/PQ, 3=Gamut Boost
            float _Brightness;
            float _Contrast;
            float _Saturation;

            // ACES filmic tone-mapping for HDR video streams
            float3 ACESFilm(float3 x) {
                float a = 2.51f;
                float b = 0.03f;
                float c = 2.43f;
                float d = 0.59f;
                float e = 0.14f;
                return saturate((x * (a * x + b)) / (x * (c * x + d) + e));
            }

            v2f vert (appdata v) {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                int eye = unity_StereoEyeIndex;
                if (_SwapEyes > 0.5) {
                    eye = 1 - eye;
                }

                float2 uv = i.uv;
                if (_StereoMode > 0.5 && _StereoMode < 1.5) {
                    // Side-by-Side (SBS): eye 0 = [0, 0.5], eye 1 = [0.5, 1.0]
                    uv.x = uv.x * 0.5 + (eye == 0 ? 0.0 : 0.5);
                } else if (_StereoMode > 1.5 && _StereoMode < 2.5) {
                    // Over-Under (OU): eye 0 = [0.5, 1.0] (top), eye 1 = [0, 0.5] (bottom)
                    uv.y = uv.y * 0.5 + (eye == 0 ? 0.5 : 0.0);
                }

                fixed4 col = tex2D(_MainTex, uv);

                // HDR Tone-Mapping & Color Transfer Modes
                if (_HdrMode > 0.5 && _HdrMode < 1.5) {
                    // HLG tone-map
                    float3 linearCol = pow(max(col.rgb, 0.0), 2.2);
                    col.rgb = ACESFilm(linearCol * 1.6);
                } else if (_HdrMode > 1.5 && _HdrMode < 2.5) {
                    // HDR10 / PQ ST2084 to ACES filmic
                    float3 linearCol = pow(max(col.rgb, 0.0), 2.8);
                    col.rgb = ACESFilm(linearCol * 2.3);
                } else if (_HdrMode > 2.5) {
                    // Wide Gamut / Vibrance Boost
                    float3 linearCol = pow(max(col.rgb, 0.0), 1.25);
                    col.rgb = saturate(linearCol * 1.15);
                }

                // Brightness
                col.rgb *= _Brightness;

                // Contrast
                col.rgb = (col.rgb - 0.5) * _Contrast + 0.5;

                // Saturation
                float luma = dot(col.rgb, float3(0.2126, 0.7152, 0.0722));
                col.rgb = lerp(float3(luma, luma, luma), col.rgb, _Saturation);

                return col;
            }
            ENDCG
        }
    }
}
