Shader "MovieNight/EdgeSmooth" {
 Properties { _MainTex ("Video",2D)="black" {} _History ("History",2D)="black" {} }
 SubShader { Cull Off ZWrite Off ZTest Always Pass {
 CGPROGRAM
 #pragma vertex vert_img
 #pragma fragment frag
 #pragma target 3.0
 #include "UnityCG.cginc"
 sampler2D _MainTex,_History; float _Blend,_Available; float4 _SourceScaleOffset;
 float4 frag(v2f_img i):SV_Target {
  int sample=min(31,(int)(i.uv.x*32));int edge=sample/8,segment=sample%8; float3 color=0;
  [unroll] for(int j=0;j<16;j++) {
   float t=(j+0.5)/16; float2 uv;
   if(edge<2) uv=float2(edge==0?0.025:0.975,(segment+t)/8);
   else uv=float2((segment+t)/8,edge==2?0.025:0.975); // Physical bottom/top, then apply the display texture orientation.
   color+=tex2D(_MainTex,uv*_SourceScaleOffset.xy+_SourceScaleOffset.zw).rgb;
  }
  return float4(lerp(tex2D(_History,float2((sample+0.5)/32,0.5)).rgb,color/16*_Available,_Blend),1);
 }
 ENDCG
 } }
}
