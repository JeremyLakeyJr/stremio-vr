Shader "MovieNight/BounceField" {
 Properties { _MainTex("Edge colors",2D)="black" {} }
 SubShader { Cull Off ZWrite Off ZTest Always Pass {
 CGPROGRAM
 #pragma vertex vert_img
 #pragma fragment frag
 #pragma target 3.0
 #include "UnityCG.cginc"
 sampler2D _MainTex;
 float4 frag(v2f_img i):SV_Target {
  float3 color=0;float weights=0;
  [unroll]for(int side=0;side<4;side++){
   float along=side<2?i.uv.y:i.uv.x;
   float segment=clamp(along*8-.5,0,7);
   float distance=abs(side<2?i.uv.x-(side==0?0:1):i.uv.y-(side==2?0:1));
   float weight=1/(.02+distance*distance);
   float3 sample=tex2D(_MainTex,float2((side*8+segment+.5)/32,.5)).rgb*.5;
   sample+=tex2D(_MainTex,float2((side*8+max(0,segment-.65)+.5)/32,.5)).rgb*.25;
   sample+=tex2D(_MainTex,float2((side*8+min(7,segment+.65)+.5)/32,.5)).rgb*.25;
   color+=sample*weight;weights+=weight;
  }
  return float4(color/weights,1);
 }
 ENDCG
 } }
}
