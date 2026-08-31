Shader "VoxelBaker/VoxelPreviewLit"
{
    //
    // 实时预览面板专用着色器。
    //
    // 存在理由：正式渲染走的是 Graphics.DrawMeshInstancedIndirect + 实例化属性
    // （AO、面掩码、调色板索引都打包在 per-instance 数据里），
    // 而预览只有一张合并好的普通 Mesh，根本没有那些实例化属性。
    // 如果预览图省事用个 Standard 材质，就会出现"预览挺好看、烘焙出来变了个样"。
    //
    // 所以这里把 VoxelLit 的光照数学原样搬过来，只把输入源换掉：
    //   · AO        → 已在 CPU 侧乘进顶点色（见 VoxelPreviewBuilder）
    //   · posOS     → 改用 UV1 传进来的"立方体内局部坐标"
    //   · 面剔除    → CPU 侧已按 faceMask 只生成暴露面，shader 不用再判
    // 其余（假圆角法线、Blinn-Phong 高光、离散三档面朝向、边缘描深）逐行一致。
    //
    Properties
    {
        _FaceShade        ("Face Shade",           Range(0, 0.5))  = 0.22
        _EdgeRoundWidth   ("Edge Round Width",     Range(0, 0.35)) = 0.20
        _EdgeRoundAmount  ("Edge Round Amount",    Range(0, 1))    = 0.70
        _SpecularStrength ("Specular Strength",    Range(0, 1))    = 0.65
        _SpecularPower    ("Specular Power",       Range(8, 128))  = 64

        // 排查探针：与 VoxelLit.shader 同一套语义。
        // 1 = 纯品红 —— 用来判定"预览窗口里画的是不是这个 shader"。
        _DebugMode        ("Debug Mode",           Float)          = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            ZWrite On
            ZTest LEqual
            Cull Back

            HLSLPROGRAM

            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float  _FaceShade;
                float  _EdgeRoundWidth;
                float  _EdgeRoundAmount;
                float  _SpecularStrength;
                float  _SpecularPower;
                float  _DebugMode;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 color      : COLOR;
                // 顶点在这颗积木内部的局部坐标，范围 [-0.5, 0.5]
                float3 cubeLocal  : TEXCOORD1;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float3 normalOS   : TEXCOORD2;
                float3 cubeLocal  : TEXCOORD3;
                float4 color      : COLOR;
            };

            //
            // 假圆角：按像素弯折法线，几何位置一点不动。
            // 只改法线不顶点，所以绝不可能把体素之间重新撑出缝隙。
            // 必须从"未弯折的轴向法线"取 faceTerm，否则棱边处 faceTerm 会跳变。
            //
            float3 RoundCubeEdgeNormal(float3 nOS, float3 pCube, float width, float amount)
            {
                if (width <= 0.0001f || amount <= 0.0001f) return nOS;

                float3 an = abs(nOS);
                float u, v;
                float3 uAxis, vAxis;

                if (an.x > 0.5)      { u = pCube.y; v = pCube.z; uAxis = float3(0,1,0); vAxis = float3(0,0,1); }
                else if (an.y > 0.5) { u = pCube.z; v = pCube.x; uAxis = float3(0,0,1); vAxis = float3(1,0,0); }
                else                 { u = pCube.x; v = pCube.y; uAxis = float3(1,0,0); vAxis = float3(0,1,0); }

                float d = 0.5 - max(abs(u), abs(v));
                float bend = 1.0 - smoothstep(0.0, width, d);
                if (bend <= 0.0001f) return nOS;

                //
                // 掰的方向：必须按 |u| / |v| 的**比例混合**两条切向，绝不能用三元选择器。
                // 旧写法在 |u| == |v|（面上的对角线）处切向 90° 硬跳 → 法线在整条对角线
                // 上硬断裂（amount=0.70 时两侧夹角 47.8°），过 pow(NdotH, 64) 高光后
                // 就是每个面四角各挂几段斜向硬边；模型自转时它们会扭曲漂移。
                // 按比例混合后对角线处权重 0.5、切向指向角外（真正的圆角朝向），C0 连续。
                // 与 VoxelLit.shader 的 RoundCubeEdgeNormal 必须保持一致。
                //
                float au = abs(u);
                float av = abs(v);
                float sum = au + av;
                float wu = (sum > 1e-6f) ? (au / sum) : 0.5f;
                float3 tRaw = uAxis * sign(u) * wu + vAxis * sign(v) * (1.0 - wu);
                float tLen = length(tRaw);
                // 退化（面正中心且 u = v = 0）时退回原法线，杜绝 normalize(0) 出 NaN
                float3 tangent = (tLen > 1e-5f) ? (tRaw / tLen) : nOS;

                return normalize(nOS + tangent * (bend * amount));
            }

            Varyings vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs posInput = GetVertexPositionInputs(input.positionOS.xyz);

                output.positionCS = posInput.positionCS;
                output.positionWS = posInput.positionWS;
                output.normalOS   = input.normalOS;
                output.normalWS   = TransformObjectToWorldNormal(input.normalOS);
                output.cubeLocal  = input.cubeLocal;
                output.color      = input.color;

                return output;
            }

            float4 frag(Varyings input) : SV_Target
            {
                // ---- 排查探针 1：品红。预览面板若没变品红 = 那边画的不是这个 shader ----
                if (_DebugMode > 0.5 && _DebugMode < 1.5)
                    return float4(1.0, 0.0, 1.0, 1.0);

                float3 nAxialOS = normalize(input.normalOS);
                float3 axialWS  = normalize(input.normalWS);   // 未弯折的世界法线，专给 faceTerm 用

                float3 bentOS   = RoundCubeEdgeNormal(nAxialOS, input.cubeLocal, _EdgeRoundWidth, _EdgeRoundAmount);

                // 用 URP 官方 API 而不是 VoxelLit 里的 _ObjectToWorldMatrix:
                // 那个矩阵是 VoxelLit 自己声明在 UnityPerMaterial 里的自定义字段, 专门服务
                // GPU instancing (每实例从 _VoxelBuffer 解出的变换)。本预览 shader 渲染的是
                // 合并后的普通 Mesh, 不走 instancing, 直接用 Unity 内置的对象→世界矩阵即可。
                // 与上面 vert() 里第 112 行的 TransformObjectToWorldNormal 保持同一套写法。
                float3 normalWS = normalize(TransformObjectToWorldNormal(bentOS));

                //
                // 边缘描深：每颗积木之间一道极细的暗线，是"读起来像独立积木"的关键。
                // 与假圆角互补 —— 圆角改法线改光照，描深直接改颜色，结果更硬。
                //
                float3 an = abs(nAxialOS);
                float u, v;
                if (an.x > 0.5)      { u = input.cubeLocal.y; v = input.cubeLocal.z; }
                else if (an.y > 0.5) { u = input.cubeLocal.z; v = input.cubeLocal.x; }
                else                 { u = input.cubeLocal.x; v = input.cubeLocal.y; }
                float dToEdge = 0.5 - max(abs(u), abs(v));
                // 与 VoxelLit 一致：用屏幕导数 fwidth 把棱宽锁成 ~1 像素，
                // 描深宽度与模型大小/旋转角度/相机距离都无关，避免次像素采样走样。
                float pixWidth = max(fwidth(dToEdge), 1e-5);
                float edgeLine = 1.0 - smoothstep(pixWidth * 0.0, pixWidth * 1.0, dToEdge);

                Light mainLight = GetMainLight(TransformWorldToShadowCoord(input.positionWS));

                // 漫反射：硬光 0.80/0.20，把受光面与背光面拉开
                float NdotL = saturate(dot(normalWS, mainLight.direction));
                float3 diffuse = mainLight.color * (NdotL * 0.80 + 0.20);

                // Blinn-Phong 高光：塑料积木感最缺的一环
                float3 viewDirWS = normalize(_WorldSpaceCameraPos.xyz - input.positionWS);
                float3 halfDirWS = normalize(mainLight.direction + viewDirWS);
                float NdotH = saturate(dot(normalWS, halfDirWS));
                float specular = pow(NdotH, _SpecularPower) * _SpecularStrength;
                specular *= step(0.0, NdotL);   // 只在受光面加高光，避免"幽灵高光"

                //
                // 面朝向明暗：离散 3 档（顶亮 / 侧中 / 底暗），不用连续渐变。
                // 连续渐变会让上半球所有面一起提亮 —— 就是之前那个"从上往下泛光"。
                //
                float topMask    = smoothstep(0.50, 0.66, axialWS.y);
                float bottomMask = smoothstep(0.50, 0.66, -axialWS.y);
                float sideMod    = 0.10 * abs(nAxialOS.z);
                float faceTerm = 1.0
                    + _FaceShade * (1.30 * topMask
                                  + 0.18 * sideMod
                                  - 0.90 * bottomMask);

                // 环境光：天光 + 微弱顶光，给背光面留一点可读性
                // 与 VoxelLit 一致：顶光必须是**离散阶跃**而不是连续 saturate。
                // 连续 saturate(axialWS.y*0.5+0.5) 会把整个上半球一起提亮（"泛白"根因）；
                // 这里只有真正朝上的顶面（axialWS.y > 0.58）才拿到增量，与 faceTerm 同带。
                float topAmbientMask = smoothstep(0.50, 0.66, axialWS.y);
                float3 ambient = float3(0.38, 0.41, 0.46) + topAmbientMask * 0.06;

                // AO 已在 CPU 侧乘进顶点色，这里不再重复计算
                float3 litColor = input.color.rgb * (diffuse + ambient) * faceTerm
                                + mainLight.color * specular;
                litColor *= (1.0 - edgeLine * 0.18);

                return float4(litColor, 1.0);
            }

            ENDHLSL
        }
    }
}
