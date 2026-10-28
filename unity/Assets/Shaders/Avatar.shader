Shader "StremioVR/Avatar" {
 Properties { _MainTex ("Face", 2D) = "white" {} _Color ("Tint", Color) = (1,1,1,1) _AmbientResponse ("Ambient response", Range(0,1)) = 1 }
 SubShader { Tags { "RenderType"="Opaque" } Pass {
 CGPROGRAM
 #pragma vertex vert
 #pragma fragment frag
 #pragma multi_compile_instancing
 #include "UnityCG.cginc"
 struct appdata { float4 vertex:POSITION; float3 normal:NORMAL; float2 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
 struct v2f { float4 vertex:SV_POSITION; float2 uv:TEXCOORD0; float shade:TEXCOORD1; float3 bounce:TEXCOORD2; UNITY_VERTEX_OUTPUT_STEREO };
 sampler2D _MainTex; float4 _Color; float4 _MainTex_ST; float4 _CinemaBounceColors[8]; float4 _CinemaBouncePositions[8]; float _CinemaBounceStrength; float _CinemaAmbientDim; float _AmbientResponse;
 v2f vert(appdata v) { v2f o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_OUTPUT(v2f,o); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o); o.vertex=UnityObjectToClipPos(v.vertex); o.uv=TRANSFORM_TEX(v.uv,_MainTex); o.shade=0.65+0.35*saturate(dot(UnityObjectToWorldNormal(v.normal),normalize(float3(-0.4,0.8,0.5)))); float3 world=mul(unity_ObjectToWorld,v.vertex).xyz; float3 normal=UnityObjectToWorldNormal(v.normal);
 [unroll] for(int i=0;i<8;i++) { float3 delta=_CinemaBouncePositions[i].xyz-world; float d2=dot(delta,delta); float diffuse=saturate(dot(normal,delta*rsqrt(max(d2,0.01)))); o.bounce+=_CinemaBounceColors[i].rgb*diffuse/(1+d2*0.3)*0.55; }
 return o; }
 fixed4 frag(v2f i):SV_Target { return fixed4(tex2D(_MainTex,i.uv).rgb*_Color.rgb*(i.shade*lerp(1,0.45,_CinemaAmbientDim*_AmbientResponse)+i.bounce*_CinemaBounceStrength*2*_AmbientResponse),1); }
 ENDCG
 } }
}
