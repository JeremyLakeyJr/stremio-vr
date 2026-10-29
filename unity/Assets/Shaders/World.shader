Shader "StremioVR/World" {
 Properties { _VideoReactive ("Video light bar",Range(0,1))=0 _BakedLight ("Baked room lighting", 2D)="white" {} _HasBakedLight ("Use baked lighting", Float)=0 _EmissionMap ("Emission", 2D)="white" {} _EmissionColor ("Emission tint", Color)=(0,0,0,1) _Metallic ("Metallic", Range(0,1))=0 _MainTex ("Albedo", 2D)="white" {} _Color ("Tint", Color)=(1,1,1,1) _BumpMap ("Normal", 2D)="bump" {} _BumpScale ("Normal strength", Float)=1 _Glossiness ("Smoothness", Range(0,1))=0.25 }
 SubShader { Tags {"RenderType"="Opaque"} Pass {
 CGPROGRAM
 #pragma vertex vert
 #pragma fragment frag
 #pragma target 3.0
 #pragma multi_compile_instancing
 #include "UnityCG.cginc"
 struct appdata { float4 vertex:POSITION; float3 normal:NORMAL; float4 tangent:TANGENT; float2 uv:TEXCOORD0; float2 uv2:TEXCOORD1; UNITY_VERTEX_INPUT_INSTANCE_ID };
 struct v2f { float4 vertex:SV_POSITION; float2 uv:TEXCOORD0; float3 world:TEXCOORD1; float3 normal:TEXCOORD2; float3 tangent:TEXCOORD3; float3 bitangent:TEXCOORD4; float2 lightUV:TEXCOORD5; UNITY_VERTEX_OUTPUT_STEREO };
 sampler2D _MainTex,_BumpMap,_BakedLight,_EmissionMap; float4 _EmissionColor; float _HasBakedLight,_Metallic,_VideoReactive; float4 _MainTex_ST,_Color; float _BumpScale,_Glossiness;
 sampler2D _CinemaEdgeColors; float4 _CinemaBouncePositions[8]; float _CinemaBounceStrength,_CinemaAmbientDim;
 v2f vert(appdata v) { v2f o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_OUTPUT(v2f,o); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o); o.vertex=UnityObjectToClipPos(v.vertex); o.lightUV=v.uv2; o.uv=TRANSFORM_TEX(v.uv,_MainTex); o.world=mul(unity_ObjectToWorld,v.vertex).xyz; o.normal=UnityObjectToWorldNormal(v.normal); o.tangent=UnityObjectToWorldDir(v.tangent.xyz); o.bitangent=cross(o.normal,o.tangent)*v.tangent.w*unity_WorldTransformParams.w; return o; }
 float4 frag(v2f i):SV_Target {
  UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
  float3 mapped=UnpackNormal(tex2D(_BumpMap,i.uv)); mapped.xy*=_BumpScale;
  float3 n=normalize(i.normal*mapped.z+i.tangent*mapped.x+i.bitangent*mapped.y);
  float3 albedo=tex2D(_MainTex,i.uv).rgb*_Color.rgb; float3 view=normalize(_WorldSpaceCameraPos-i.world);
  float baseLight=(0.4+0.35*saturate(dot(n,normalize(float3(-0.4,0.8,0.5)))))*lerp(1,0.65,_CinemaAmbientDim);
  float3 bounce=0,specular=0,barColor=0; float barWeight=0;
  [unroll] for(int k=0;k<8;k++) {
   float3 delta=_CinemaBouncePositions[k].xyz-i.world; float d2=dot(delta,delta); float3 l=delta*rsqrt(max(d2,0.01));
   float3 edgeColor=tex2Dlod(_CinemaEdgeColors,float4((k+0.5)/8,0.5,0,0)).rgb; float attenuation=1/(1+d2*0.08); barColor+=edgeColor*attenuation; barWeight+=attenuation; float3 radiance=edgeColor*attenuation*_CinemaBounceStrength*0.25;
   bounce+=radiance*(0.15+0.85*saturate(dot(n,l)));
   specular+=radiance*pow(saturate(dot(n,normalize(l+view))),lerp(8,96,_Glossiness))*_Glossiness*0.25;
  }
  float3 baked=tex2D(_BakedLight,i.lightUV).rgb*lerp(1,0.65,_CinemaAmbientDim);
  float3 reflection=DecodeHDR(UNITY_SAMPLE_TEXCUBE_LOD(unity_SpecCube0,reflect(-view,n),(1-_Glossiness)*6),unity_SpecCube0_HDR);
  float3 emission=tex2D(_EmissionMap,i.uv).rgb*_EmissionColor.rgb;
  emission=lerp(emission,barColor/max(barWeight,0.001)*max(1,length(_EmissionColor.rgb))*_CinemaBounceStrength,_VideoReactive*_CinemaAmbientDim);
  return float4(albedo*lerp(baseLight.xxx,baked,_HasBakedLight)+(albedo+0.06)*bounce+specular+reflection*lerp(0.04,albedo,_Metallic)*_Glossiness+emission,1);
 }
 ENDCG
 } }
}
