Shader "MovieNight/BrowserYuv" {
 Properties { _MainTex("Y",2D)="black" {} _U("U",2D)="gray" {} _V("V",2D)="gray" {} }
 SubShader { Cull Off ZWrite Off ZTest Always Pass {
 CGPROGRAM
 #pragma vertex vert_img
 #pragma fragment frag
 #include "UnityCG.cginc"
 sampler2D _MainTex,_U,_V;
 fixed4 frag(v2f_img i):SV_Target {float2 uv=float2(i.uv.x,1-i.uv.y);float y=1.164383*(tex2D(_MainTex,uv).r-16.0/255);float u=tex2D(_U,uv).r-128.0/255,v=tex2D(_V,uv).r-128.0/255;float3 rgb=saturate(float3(y+1.596027*v,y-.391762*u-.812968*v,y+2.017232*u));
 #ifndef UNITY_COLORSPACE_GAMMA
 rgb=GammaToLinearSpace(rgb);
 #endif
 return fixed4(rgb,1);}
 ENDCG
 } }
}
