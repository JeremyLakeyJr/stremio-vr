Shader "MovieNight/Mirror" {
 Properties { _MainTex ("Left reflection",2D)="black" {} _RightTex ("Right reflection",2D)="black" {} }
 SubShader { Tags {"RenderType"="Opaque"} Pass {
 CGPROGRAM
 #pragma vertex vert
 #pragma fragment frag
 #pragma multi_compile_instancing
 #include "UnityCG.cginc"
 struct appdata { float4 vertex:POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
 struct v2f { float4 pos:SV_POSITION; float4 screen:TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
 sampler2D _MainTex,_RightTex;
 v2f vert(appdata v) { v2f o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o); o.pos=UnityObjectToClipPos(v.vertex); o.screen=ComputeNonStereoScreenPos(o.pos); return o; }
 fixed4 frag(v2f i):SV_Target { UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i); float2 uv=i.screen.xy/i.screen.w;
 #if defined(UNITY_SINGLE_PASS_STEREO) || defined(UNITY_STEREO_INSTANCING_ENABLED) || defined(UNITY_STEREO_MULTIVIEW_ENABLED)
 if(unity_StereoEyeIndex==1) return tex2D(_RightTex,uv);
 #endif
 return tex2D(_MainTex,uv); }
 ENDCG
 } }
}
