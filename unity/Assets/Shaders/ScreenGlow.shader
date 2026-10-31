Shader "StremioVR/ScreenGlow" {
 Properties { _ScreenSize ("Screen size and halo width", Vector)=(2.1,1.18125,0.7,0) }
 SubShader { Tags {"Queue"="Transparent" "RenderType"="Transparent"} Blend One One ZWrite Off Cull Off Pass {
 CGPROGRAM
 #pragma vertex vert
 #pragma fragment frag
 #pragma target 3.0
 #pragma multi_compile_instancing
 #include "UnityCG.cginc"
 struct appdata {float4 vertex:POSITION;float2 uv:TEXCOORD0;UNITY_VERTEX_INPUT_INSTANCE_ID};
 struct v2f {float4 vertex:SV_POSITION;float2 uv:TEXCOORD0;UNITY_VERTEX_OUTPUT_STEREO};
 float4 _ScreenSize; sampler2D _CinemaBounceField;float _CinemaBounceStrength;
 v2f vert(appdata v) {v2f o;UNITY_SETUP_INSTANCE_ID(v);UNITY_INITIALIZE_OUTPUT(v2f,o);UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);o.vertex=UnityObjectToClipPos(v.vertex);o.uv=v.uv;return o;}
 fixed4 frag(v2f i):SV_Target {
  float2 distance=max(abs(i.uv-.5)-.5,0)*_ScreenSize.xy;float d=length(distance);
  float falloff=exp(-d*4)*saturate(1-d/_ScreenSize.z);
  float3 color=tex2D(_CinemaBounceField,saturate(i.uv)).rgb;
  return float4(color*falloff*_CinemaBounceStrength*0.45,1);
 }
 ENDCG
 } }
}
