Shader "Custom/SHDebug"
{
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline" = "UniversalRenderPipeline" "Queue"="Geometry"}
        LOD 100

        Pass
        {
            HLSLPROGRAM

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Assets/Shaders/SH.hlsl"

            #pragma vertex vert
            #pragma fragment frag

            // 使用定点数存储小数, 因为 compute shader 的 InterlockedAdd 不支持 float
            CBUFFER_START(UnityPerMaterial)
                StructuredBuffer<int> _coefficientSH9; 
            CBUFFER_END

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float3 normal : TEXCOORD2;
                float4 vertex : SV_POSITION;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = TransformObjectToHClip(v.vertex);
                o.normal = TransformObjectToWorldNormal(v.normal);
                o.normal = normalize(o.normal);
                return o;
            }

            float4 frag (v2f i) : SV_Target
            {
                float3 dir = i.normal;

                float3 c[9];
                for(int i = 0; i < 9; i++)
                {
                    c[i].x = IntToFloat(_coefficientSH9[i*3+0]);
                    c[i].y = IntToFloat(_coefficientSH9[i*3+1]);
                    c[i].z = IntToFloat(_coefficientSH9[i*3+2]);
                }

                float3 irradiance = IrradianceSH9(c, dir);
                float3 Lo = irradiance / PI;

                return float4(Lo, 1.0);
            }
            ENDHLSL
        }
    }
}
