Shader "Hidden/MRLab/HW03Bloom" {
Properties { _MainTex ("Source", 2D) = "white" {} }
SubShader { Cull Off ZWrite Off ZTest Always
CGINCLUDE
#include "UnityCG.cginc"
sampler2D _MainTex, _GlowTex, _SourceStill;
float4 _MainTex_TexelSize;
float4 bright(v2f_img i):SV_Target {
 float3 c=tex2D(_SourceStill,i.uv).rgb;
 float b=max(c.r,max(c.g,c.b)); return float4(c*max(b-.65,0)/max(b,.001),1);
}
float2 _Step;
float4 blur(v2f_img i):SV_Target {
 float3 c=0; float w=0;
 for(int k=-6;k<=6;k++){float a=exp(-k*k/12.0);c+=a*tex2D(_MainTex,i.uv+k*_Step).rgb;w+=a;}
 return float4(c/w,1);
}
float4 combine(v2f_img i):SV_Target {return float4(tex2D(_SourceStill,i.uv).rgb+.18*tex2D(_GlowTex,i.uv).rgb,1);}
ENDCG
Pass { CGPROGRAM
#pragma vertex vert_img
#pragma fragment bright
ENDCG }
Pass { CGPROGRAM
#pragma vertex vert_img
#pragma fragment blur
ENDCG }
Pass { CGPROGRAM
#pragma vertex vert_img
#pragma fragment combine
ENDCG }
} }

