Shader "VoxelBaker/URP/VoxelLit"
{
    Properties
    {
        _VoxelSize ("Voxel Size", Float) = 0.1
        _LocalOrigin ("Local Origin", Vector) = (0, 0, 0, 0)
        _PaletteTex ("Palette Texture", 2D) = "white" {}
        _BevelRoundness ("Bevel Roundness", Range(0.8, 1.0)) = 0.94
        _AOStrength ("AO Strength", Range(0, 1)) = 0.75
        _BaseColor ("Tint Color", Color) = (1, 1, 1, 1)
        _Metallic ("Metallic", Range(0, 1)) = 0.0
        _Smoothness ("Smoothness", Range(0, 1)) = 0.5
    }

    SubShader
    {
        Tags 
        { 
            "RenderType" = "Opaque" 
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
        }
        LOD 300

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct PackedVoxelGPU
            {
                uint packedPosition;
                uint packedAttributes;
                uint colorRGBA;
                uint voxelMeta;
            };

            #if defined(UNITY_PROCEDURAL_INSTANCING_ENABLED) || defined(SHADER_API_D3D11) || defined(SHADER_API_GLCORE) || defined(SHADER_API_GLES3) || defined(SHADER_API_METAL) || defined(SHADER_API_VULKAN)
            StructuredBuffer<PackedVoxelGPU> _VoxelBuffer;
            #endif

            CBUFFER_START(UnityPerMaterial)
                float4x4 _ObjectToWorldMatrix;
                float4 _LocalOrigin;
                float4 _BaseColor;
                float _VoxelSize;
                float _BevelRoundness;
                float _AOStrength;
                float _Metallic;
                float _Smoothness;
            CBUFFER_END

            Texture2D _PaletteTex;
            SamplerState sampler_PaletteTex;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                uint instanceID : SV_InstanceID;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float4 color : COLOR;
                float ao : TEXCOORD3;
            };

            float3 UnpackPosition(uint packed)
            {
                float x = (float)(packed & 0x3FF);
                float y = (float)((packed >> 10) & 0x3FF);
                float z = (float)((packed >> 20) & 0x3FF);
                return float3(x, y, z);
            }

            float4 UIntToColor(uint c)
            {
                float r = (float)(c & 0xFF) / 255.0;
                float g = (float)((c >> 8) & 0xFF) / 255.0;
                float b = (float)((c >> 16) & 0xFF) / 255.0;
                float a = (float)((c >> 24) & 0xFF) / 255.0;
                return float4(r, g, b, a);
            }

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;

                #if defined(UNITY_PROCEDURAL_INSTANCING_ENABLED) || defined(SHADER_API_D3D11) || defined(SHADER_API_GLCORE) || defined(SHADER_API_GLES3) || defined(SHADER_API_METAL) || defined(SHADER_API_VULKAN)
                PackedVoxelGPU voxel = _VoxelBuffer[input.instanceID];

                float3 gridPos = UnpackPosition(voxel.packedPosition);
                float3 localPos = _LocalOrigin.xyz + (gridPos + 0.5) * _VoxelSize;

                // 产生轻微圆角微缩效果
                float3 scaledOS = input.positionOS.xyz * (_VoxelSize * _BevelRoundness);
                float3 finalLocalPos = localPos + scaledOS;

                // 应用模型自身的旋转、位移与缩放矩阵
                float3 posWS = mul(_ObjectToWorldMatrix, float4(finalLocalPos, 1.0)).xyz;
                float3 normWS = normalize(mul((float3x3)_ObjectToWorldMatrix, input.normalOS));

                output.positionWS = posWS;
                output.positionCS = TransformWorldToHClip(posWS);
                output.normalWS = normWS;

                // 解包颜色与AO
                float4 directColor = UIntToColor(voxel.colorRGBA);
                uint aoByte = (voxel.packedAttributes >> 16) & 0xFF;
                float ao = (float)aoByte / 255.0;

                output.color = directColor * _BaseColor;
                output.ao = ao;
                #else
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.color = _BaseColor;
                output.ao = 1.0;
                #endif

                return output;
            }

            float4 frag(Varyings input) : SV_Target
            {
                float3 normalWS = normalize(input.normalWS);
                Light mainLight = GetMainLight(TransformWorldToShadowCoord(input.positionWS));

                // 漫反射光照
                float NdotL = saturate(dot(normalWS, mainLight.direction));
                float3 diffuse = mainLight.color * (NdotL * 0.75 + 0.25);

                // 环境光与微弱顶光模拟
                float upLight = saturate(normalWS.y * 0.5 + 0.5) * 0.2;
                float3 ambient = float3(0.35, 0.38, 0.42) + upLight;

                // 叠加 AO
                float aoFactor = lerp(1.0, input.ao, _AOStrength);
                float3 litColor = input.color.rgb * (diffuse + ambient) * aoFactor;

                return float4(litColor, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct PackedVoxelGPU
            {
                uint packedPosition;
                uint packedAttributes;
                uint colorRGBA;
                uint voxelMeta;
            };

            #if defined(UNITY_PROCEDURAL_INSTANCING_ENABLED) || defined(SHADER_API_D3D11) || defined(SHADER_API_GLCORE) || defined(SHADER_API_GLES3) || defined(SHADER_API_METAL) || defined(SHADER_API_VULKAN)
            StructuredBuffer<PackedVoxelGPU> _VoxelBuffer;
            #endif

            CBUFFER_START(UnityPerMaterial)
                float4x4 _ObjectToWorldMatrix;
                float4 _LocalOrigin;
                float _VoxelSize;
                float _BevelRoundness;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                uint instanceID : SV_InstanceID;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            float3 UnpackPosition(uint packed)
            {
                float x = (float)(packed & 0x3FF);
                float y = (float)((packed >> 10) & 0x3FF);
                float z = (float)((packed >> 20) & 0x3FF);
                return float3(x, y, z);
            }

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;

                #if defined(UNITY_PROCEDURAL_INSTANCING_ENABLED) || defined(SHADER_API_D3D11) || defined(SHADER_API_GLCORE) || defined(SHADER_API_GLES3) || defined(SHADER_API_METAL) || defined(SHADER_API_VULKAN)
                PackedVoxelGPU voxel = _VoxelBuffer[input.instanceID];
                float3 gridPos = UnpackPosition(voxel.packedPosition);
                float3 localPos = _LocalOrigin.xyz + (gridPos + 0.5) * _VoxelSize;
                float3 scaledOS = input.positionOS.xyz * (_VoxelSize * _BevelRoundness);
                float3 finalLocalPos = localPos + scaledOS;

                float3 posWS = mul(_ObjectToWorldMatrix, float4(finalLocalPos, 1.0)).xyz;
                output.positionCS = TransformWorldToHClip(posWS);
                #else
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                #endif

                return output;
            }

            float4 frag(Varyings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }
}
