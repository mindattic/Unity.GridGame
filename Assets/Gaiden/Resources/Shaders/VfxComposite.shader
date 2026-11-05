// GG-T269: turns the effects camera's render (additive particles leave alpha at 0) into straight alpha that
// UI Toolkit can blend over the board: alpha = brightest channel, color un-premultiplied by it.
Shader "Hidden/Gaiden/VfxComposite"
{
    Properties { _MainTex ("Texture", 2D) = "black" {} }
    SubShader
    {
        ZTest Always Cull Off ZWrite Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            fixed4 frag (v2f_img i) : SV_Target
            {
                fixed4 c = tex2D(_MainTex, i.uv);
                float a = saturate(max(c.a, max(c.r, max(c.g, c.b))));
                return a > 0.002 ? fixed4(saturate(c.rgb / a), a) : fixed4(0, 0, 0, 0);
            }
            ENDCG
        }
    }
}
