Shader "StremioVR/ScreenGlow" {
 Properties { _ScreenFraction ("Screen fraction", Vector)=(0.65,0.65,0,0) }
 SubShader { Tags {"Queue"="Transparent" "RenderType"="Transparent"} Blend One One ZWrite Off Cull Off Pass {
 CGPROGRAM
 #pragma vertex vert
 #pragma fragment frag
 #pragma target 3.0
 #pragma multi_compile_instancing
 #include "UnityCG.cginc"
 struct appdata {float4 vertex:POSITION;float2 uv:TEXCOORD0;UNITY_VERTEX_INPUT_INSTANCE_ID};
 struct v2f {float4 vertex:SV_POSITION;float2 uv:TEXCOORD0;UNITY_VERTEX_OUTPUT_STEREO};
 float4 _ScreenFraction; sampler2D _CinemaBounceField;float _CinemaBounceStrength;
 v2f vert(appdata v) {v2f o;UNITY_SETUP_INSTANCE_ID(v);UNITY_INITIALIZE_OUTPUT(v2f,o);UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);o.vertex=UnityObjectToClipPos(v.vertex);o.uv=v.uv;return o;}
 fixed4 frag(v2f i):SV_Target {
  float2 p=(i.uv-0.5)*2; float2 d=abs(p)-_ScreenFraction.xy; if(d.x<0&&d.y<0) discard;
  float falloff=exp(-length(max(d,0))*7)*saturate((1-max(abs(p.x),abs(p.y)))*5);
  float2 screenUV=p/_ScreenFraction.xy*0.5+0.5;
  float3 color=tex2D(_CinemaBounceField,saturate(screenUV)).rgb;
  return float4(color*falloff*_CinemaBounceStrength*0.45,1);
 }
 ENDCG
 } }
}
