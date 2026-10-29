Shader "MovieNight/EdgeSmooth" {
 Properties { _MainTex ("Video",2D)="black" {} _History ("History",2D)="black" {} }
 SubShader { Cull Off ZWrite Off ZTest Always Pass {
 CGPROGRAM
 #pragma vertex vert_img
 #pragma fragment frag
 #pragma target 3.0
 #include "UnityCG.cginc"
 sampler2D _MainTex,_History; float _Blend,_Available;
 float4 frag(v2f_img i):SV_Target {
  int edge=min(7,(int)(i.uv.x*8)); float3 color=0;
  [unroll] for(int j=0;j<16;j++) {
   float t=(j+0.5)/16; float2 uv;
   if(edge<4) uv=float2(edge<2?0.025:0.975,(edge%2)*0.5+t*0.5);
   else uv=float2(((edge-4)%2)*0.5+t*0.5,edge<6?0.025:0.975);
   color+=tex2D(_MainTex,uv).rgb;
  }
  return float4(lerp(tex2D(_History,float2((edge+0.5)/8,0.5)).rgb,color/16*_Available,_Blend),1);
 }
 ENDCG
 } }
}
