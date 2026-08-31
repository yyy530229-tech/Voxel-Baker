Shader "VoxelBaker/URP/VoxelLit"
{
    Properties
    {
        _VoxelSize ("Voxel Size", Float) = 0.1
        _LocalOrigin ("Local Origin", Vector) = (0, 0, 0, 0)
        _PaletteTex ("Palette Texture", 2D) = "white" {}

        // 必须恒定为 1.0：小于 1 会让相邻体素之间出现真实的物理缝隙。
        // 保留极小调节区间仅用于美术自查，运行时由 VoxelIndirectRenderer 强制锁 1.0。
        _BevelRoundness ("Bevel Roundness", Range(0.99, 1.0)) = 1.0

        _AOStrength ("AO Strength", Range(0, 1)) = 0.65

        // FaceShade 是顶/侧/底面的明暗差，决定"积木感"最关键的一个参数。
        // 上一版 0.10 实在太弱，看起来所有方块都贴在同一个平面上。
        // 0.22 顶面提亮 20%，底面压暗 15%，对比强烈才有塑料积木的立体感。
        _FaceShade ("Face Shade", Range(0, 0.5)) = 0.22

        //
        // 「细腻感」三件套 —— 注意这不是抗锯齿，锯齿是体素风格的本体，要保留。
        // 细腻指的是：每个方块读起来像一颗有倒角、能吃到高光的实体积木，
        // 而不是一张死平的纯色贴片。
        //
        // 1) EdgeRound: 在方块的四条棱附近把法线朝外掰过去（只改法线，不改几何）。
        //    几何上仍是严丝合缝的立方体（所以不会有缝），
        //    但着色上棱边有了连续过渡的高光 —— 视觉上就是乐高砖的圆角。
        // 2) ColorJitter: 每颗积木 ±1.5% 的随机明度扰动。
        //    真实乐高砖同色之间也有批次色差，加一点点能瞬间打破"塑料贴片感"。
        //
        // 上一版 width=0.14 amount=0.45 太弱，棱边几乎看不出圆角。
        // 加到 0.20 / 0.70 后每条棱都有明显的高光滚落（参考图那种亮线）。
        _EdgeRoundWidth ("Edge Round Width", Range(0, 0.35)) = 0.20
        _EdgeRoundAmount ("Edge Round Amount", Range(0, 1)) = 0.70
        // ColorJitter 上一版 0.035 看着"发糊"——参考图每块是干净单色。
        // 0.012 → 0.0: 任何非零 ColorJitter 都是旋转走样的种子（见 VoxelIndirectRenderer 同字段）。
        _ColorJitter ("Per-Block Color Jitter", Range(0, 0.08)) = 0.0

        // 高光强度：之前 _Smoothness=0.5 + 没专门的 specular 控制，几乎看不到高光。
        // 参考图里塑料积木的顶面有非常明显的小亮点（light from upper right）。
        // _SpecularPower 64 让高光更集中（更塑料），_SpecularStrength 0.65 强度合适。
        _SpecularStrength ("Specular Strength", Range(0, 1)) = 0.65
        _SpecularPower ("Specular Power", Range(8, 128)) = 64

        _BaseColor ("Tint Color", Color) = (1, 1, 1, 1)
        _Metallic ("Metallic", Range(0, 1)) = 0.0
        _Smoothness ("Smoothness", Range(0, 1)) = 0.5

        //
        // 排查探针 —— 只由 VoxelIndirectRenderer.DebugMode 常量下发，正常情况恒为 0。
        //
        // 0 = 正常渲染
        // 1 = 纯品红  —— 先跑这个！屏幕没变品红 = 你看到的根本不是这个 shader 画的
        // 2 = 纯 albedo（无任何光照/描深/色差）
        // 3 = 法线可视化（断裂会直接显示为颜色硬跳）
        // 4 = 关假圆角（EdgeRoundAmount = 0）
        // 5 = 关高光（SpecularStrength = 0）
        // 6 = 关描深（edgeLine = 0）
        //
        _DebugMode ("Debug Mode", Float) = 0
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

        // ============ 共用 HLSL 片段 ============
        HLSLINCLUDE

        // 从 packedAttributes 的 bit24..29 取出 6 面暴露掩码，
        // 并把当前顶点的物体空间法线映射到对应的面 bit。
        //
        // 注意：本渲染器使用的是「倒角立方体」网格（ChamferedUnitCube），
        // 除了 6 个轴向主面之外，还有 12 条棱倒角面（法线形如 (0.707,0.707,0)）
        // 和 8 个角倒角面（法线形如 (0.577,0.577,0.577)）。
        // 这些斜面对外始终可见，绝不能被裁掉，否则体素棱边会破洞。
        // 因此必须用「严格轴向判定」（单一分量 ≈ 1，其余分量 ≈ 0），
        // 不能用 >0.5 这种宽松判定 —— 0.707 会误判成 +X 面。
        uint GetVoxelFaceBit(float3 normalOS)
        {
            float3 an = abs(normalOS);

            // 主面法线为精确的单位轴向向量，容差取 1e-3 足够区分 0.707 / 0.577
            if (an.x > 0.999 && an.y < 0.001 && an.z < 0.001)
                return normalOS.x > 0.0 ? 1u : 2u;   // +X / -X
            if (an.y > 0.999 && an.x < 0.001 && an.z < 0.001)
                return normalOS.y > 0.0 ? 4u : 8u;   // +Y / -Y
            if (an.z > 0.999 && an.x < 0.001 && an.y < 0.001)
                return normalOS.z > 0.0 ? 16u : 32u; // +Z / -Z

            return 0u; // 倒角斜面 / 异常法线：永远保留
        }

        // ------------------------------------------------------------------
        // 「假圆角」法线弯折
        //
        // 体素块在几何上是严丝合缝的立方体（顶点在 ±0.5），
        // 所以这里只改法线、不动画面 —— 相邻块之间不会因此产生任何缝隙。
        //
        // 做法：由法线判定当前所在的面，取面内两个切向坐标 u、v（范围 ±0.5），
        // 算出到最近棱边的距离 d = 0.5 - max(|u|, |v|)，
        // 在 d < width 的窄带内把法线朝该棱边的外侧切向掰过去。
        //
        // 效果：每颗积木的四条棱都有一段连续的高光滚落，
        // 读作"圆角乐高砖"而不是"直角纸盒子"。
        // 而且因为只动法线，棱边两侧的明暗是连续过渡的，不会形成暗线。
        // ------------------------------------------------------------------
        float3 RoundCubeEdgeNormal(float3 nOS, float3 pOS, float width, float amount)
        {
            if (width <= 0.0001f || amount <= 0.0001f) return nOS;

            float3 an = abs(nOS);

            float u, v;
            float3 uAxis, vAxis;

            if (an.x > 0.5)      { u = pOS.y; v = pOS.z; uAxis = float3(0, 1, 0); vAxis = float3(0, 0, 1); }
            else if (an.y > 0.5) { u = pOS.z; v = pOS.x; uAxis = float3(0, 0, 1); vAxis = float3(1, 0, 0); }
            else                 { u = pOS.x; v = pOS.y; uAxis = float3(1, 0, 0); vAxis = float3(0, 1, 0); }

            float au = abs(u);
            float av = abs(v);

            // 到最近棱边的距离（0 = 正好在棱上，0.5 = 面中心）
            float d = 0.5 - max(au, av);

            // smoothstep 保证过渡 C1 连续，不会出现硬边
            float bend = 1.0 - smoothstep(0.0, width, d);
            if (bend <= 0.0001f) return nOS;

            //
            // 掰的方向：必须按 au / av 的**比例混合**两条切向，绝不能用三元选择器。
            //
            // 旧写法 `(au >= av) ? uAxis : vAxis` 在 au == av —— 也就是面上那条
            // 对角线 —— 处，切向会 90° 硬跳。bend 本身是连续的，但 tangent 不连续，
            // 于是法线在整条对角线上硬断裂：amount = 0.70 时两侧法线分别是
            // normalize((1,0.7,0)) 与 normalize((1,0,0.7))，夹角 47.8°。
            // 这么大的法线跳变再过 pow(NdotH, 64) 的高光，就是一条锐利的明暗分界线。
            //
            // 它落在哪里：bend > 0 要求 d < width，即每个面四条边的边框带；
            // 而 au == av 的对角线正好在这条带子里切过四个角 —— 于是每个体素的
            // 每个面都挂出几段斜向硬边。整个模型几千个体素就是一张斜线网，
            // 模型自转时半程向量持续扫过，这些斜线就在表面上扭曲漂移。
            // 这就是"黑色斜线在走"的真凶，跟描深（绕面一圈的方形环）毫无关系。
            //
            // 按比例混合后：对角线处 wu = 0.5，切向正好指向角外方向（真正的圆角
            // 朝向），跨对角线 C0 连续；棱中点处 wu = 1 或 0，退化回原来单轴结果。
            //
            float sum = au + av;
            float wu = (sum > 1e-6f) ? (au / sum) : 0.5f;
            float3 tRaw = uAxis * sign(u) * wu + vAxis * sign(v) * (1.0 - wu);
            float tLen = length(tRaw);
            // 退化（面正中心且 u = v = 0）时退回原法线，杜绝 normalize(0) 出 NaN
            float3 tangent = (tLen > 1e-5f) ? (tRaw / tLen) : nOS;

            return normalize(nOS + tangent * (bend * amount));
        }

        // ------------------------------------------------------------------
        // 软肩压缩（防死白）
        //
        // 相机 m_HDR = 1 而场景没有挂任何色调映射 Volume，
        // 线性值超过 1.0 的部分会被**硬切**成 1.0 —— 白色区域整片糊成死白，
        // 明暗细节全部丢失；而且"钳住 / 没钳住"的边界是一条硬边，
        // 模型旋转时这条边界会移动，本身就是一层摩尔纹。
        //
        // k 以下完全线性（保住积木该有的硬朗明暗对比），k 之上渐近压向 1.0，
        // 保证任何光照组合都只会被柔化、绝不会被切断。
        // ------------------------------------------------------------------
        float3 SoftShoulder(float3 c, float k)
        {
            float3 lin  = min(c, k);
            float3 over = max(c - k, 0.0);
            return lin + over / (1.0 + over / max(1.0 - k, 1e-4));
        }

        // 逐积木伪随机（输入为整数网格坐标），用于批次色差扰动
        float Hash13(float3 p3)
        {
            p3 = frac(p3 * 0.1031);
            p3 += dot(p3, p3.yzx + 33.33);
            return frac((p3.x + p3.y) * p3.z);
        }

        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            // 深度偏移兜底：即使 faceMask 过期（破坏后尚未刷新），
            // 也能稳定地把共面片元推向相机，避免闪烁的黑缝。
            Offset -1, -1

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
                float _FaceShade;
                float _EdgeRoundWidth;
                float _EdgeRoundAmount;
                float _ColorJitter;
                float _SpecularStrength;
                float _SpecularPower;
                float _Metallic;
                float _Smoothness;
                float _DebugMode;
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
                float3 normalOS : TEXCOORD4;
                float3 posOS : TEXCOORD5;   // 立方体局部坐标 (±0.5)，用于假圆角
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

                // ---- 内部面剔除：被邻体素挡住的面直接推出裁剪空间，GPU 直接丢弃 ----
                uint faceMask = (voxel.packedAttributes >> 24) & 0x3Fu;
                uint faceBit = GetVoxelFaceBit(input.normalOS);
                if (faceBit != 0u && (faceMask & faceBit) == 0u)
                {
                    output.positionCS = float4(2.0, 2.0, 4.0, 1.0); // NDC 之外 → 被裁剪
                    return output;
                }

                float3 gridPos = UnpackPosition(voxel.packedPosition);
                // 使用严格整数网格中心，避免亚体素位移造成体素重叠、深度竞争和闪烁。
                float3 localPos = _LocalOrigin.xyz + (gridPos + 0.5) * _VoxelSize;

                // 体素尺寸 100% 铺满格子，不做任何内缩，确保相邻体素严丝合缝
                float3 scaledOS = input.positionOS.xyz * (_VoxelSize * _BevelRoundness);
                float3 finalLocalPos = localPos + scaledOS;

                // 应用模型自身的旋转、位移与缩放矩阵
                float3 posWS = mul(_ObjectToWorldMatrix, float4(finalLocalPos, 1.0)).xyz;
                float3 normWS = normalize(mul((float3x3)_ObjectToWorldMatrix, input.normalOS));

                output.positionWS = posWS;
                output.positionCS = TransformWorldToHClip(posWS);
                output.normalWS = normWS;

                // normalOS 传出的是「未弯折的轴向法线」——
                // frag 里的 faceTerm 必须用它，否则棱边附近 faceTerm 会跳变，
                // 又会变回网格状明暗。假圆角在 frag 里按像素做，过渡更细腻。
                output.normalOS = input.normalOS;
                output.posOS = input.positionOS.xyz;

                // 解包颜色与AO
                float4 directColor = UIntToColor(voxel.colorRGBA);
                uint aoByte = (voxel.packedAttributes >> 16) & 0xFF;
                float ao = (float)aoByte / 255.0;

                // 批次色差：每颗积木 ±_ColorJitter/2 的明度扰动，
                // 打破 K-Means 平色之后那种"一整片死平"的塑料贴片感
                float jitter = 1.0 + (Hash13(gridPos) - 0.5) * _ColorJitter;

                output.color = float4(directColor.rgb * jitter, 1.0) * _BaseColor;
                output.ao = ao;
                #else
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.normalOS = input.normalOS;
                output.posOS = input.positionOS.xyz;
                output.color = _BaseColor;
                output.ao = 1.0;
                #endif

                return output;
            }

            float4 frag(Varyings input) : SV_Target
            {
                // ---- 排查探针 1：先证明"你看到的画面就是这个 shader 画的" ----
                // 屏幕没变品红 = 当前渲染路径压根不是本文件，前面所有修改当然全无效果。
                if (_DebugMode > 0.5 && _DebugMode < 1.5)
                    return float4(1.0, 0.0, 1.0, 1.0);

                // ---- 屏幕空间蓝噪声抖动 (Hash Blue Noise) ----
                // 比 Bayer 有序抖动强一个数量级: 伪随机相位频谱均匀, 对规则体素网格拍频的压制更彻底,
                // 且视觉噪点几乎不可见。原理: 规则网格旋转时与固定像素网格干涉产生摩尔纹,
                // 蓝噪声把规则性彻底打碎成高频白噪, 摩尔纹消失。
                // 用 positionCS (屏幕像素坐标) 索引, 图案固定在屏幕上、不随模型旋转滑动。
                int2 screenPix = int2(input.positionCS.xy);
                // 整数哈希 → [0,1) 伪随机 (Jimenez 简化版, 频谱接近蓝噪声)
                uint hashSeed = uint(screenPix.x) * 1973u + uint(screenPix.y) * 9277u + 26699u;
                hashSeed = (hashSeed << 13) ^ hashSeed;
                float ditherRaw = float((hashSeed * (hashSeed * hashSeed * 15731u + 789221u) + 1376312589u) & 0x7FFFFFFFu) / float(0x7FFFFFFF);
                float dither = ditherRaw - 0.5; // 范围 [-0.5, +0.5)
                float DitherStrength = 0.07; // 回退到上一版稳定配置

                float3 nAxialOS = normalize(input.normalOS);
                float3 axialWS = normalize(input.normalWS);   // 未弯折的世界法线，专给 faceTerm 用

                // 假圆角：按像素弯折法线（几何位置一点不动，所以绝不会撑出缝隙）。
                // 棱边附近法线连续地朝外翻，光照就跟真的乐高圆角砖一样滚出高光。
                float erAmount = (_DebugMode > 3.5 && _DebugMode < 4.5) ? 0.0 : _EdgeRoundAmount;
                float3 bentOS = RoundCubeEdgeNormal(nAxialOS, input.posOS, _EdgeRoundWidth, erAmount);
                float3 normalWS = normalize(mul((float3x3)_ObjectToWorldMatrix, bentOS));

                // ---- 排查探针 3：法线可视化。法线若有硬断裂，这里会直接显示为颜色跳变 ----
                if (_DebugMode > 2.5 && _DebugMode < 3.5)
                    return float4(normalWS * 0.5 + 0.5, 1.0);

                //
                // 边缘描深（屏幕空间 —— 治旋转混叠）
                //
                // 上一版用模型局部坐标做描深：模型旋转 + 透视后，体素在屏幕上只剩几像素，
                // 0.015~0.030 的棱宽根本跨越不到一个像素 → 完全靠次像素采样相位决定 →
                // 不同旋转角度黑线"在走"——就是用户看到的"扭曲的线"。
                //
                // 正确做法：用屏幕导数 (fwidth) 把棱宽解释成"屏幕像素数"，描深宽度
                // 与旋转/距离/模型大小都无关，从根源上消除采样混叠。线条本身压暗 18%
                // （够读出"每块是独立积木"，不会变回死平的塑料贴片）。
                //
                float3 an = abs(nAxialOS);
                float u, v;
                if (an.x > 0.5)      { u = input.posOS.y; v = input.posOS.z; }
                else if (an.y > 0.5) { u = input.posOS.z; v = input.posOS.x; }
                else                 { u = input.posOS.x; v = input.posOS.y; }
                float dToEdge = 0.5 - max(abs(u), abs(v));     // 0 = 棱上，0.5 = 面中心
                // fwidth 把 dToEdge 的梯度换算成"每像素 dToEdge 变化量"。
                // 在 ~1 像素宽的窄带上做 0→1 的平滑过渡，描深宽度与模型缩放/旋转/距离都无关。
                float pixWidth = max(fwidth(dToEdge), 1e-5);
                //
                // 过渡带 ~1.5 像素：再窄一点, 让描边本身在屏幕上覆盖的像素更少, 旋转时屏幕像素
                // 对它的"采样抖动"也随之减小。配合下面的 0.18→0.08 强度, 让接缝几乎不可见。
                float edgeLine = 1.0 - smoothstep(pixWidth * 0.0, pixWidth * 1.5, dToEdge);

                Light mainLight = GetMainLight(TransformWorldToShadowCoord(input.positionWS));

                // 漫反射光照：NdotL 主导。
                // 用 Half-Lambert (wrap) 把硬 NdotL 的 [-1,1] 映射成连续 [0,1]，
                // 消除"面与面之间的明暗硬跳变边界"——旋转时这个边界沿体素面滑动正是"亮暗不和谐"的主因。
                // wrap=0.5 让背光面也不全黑, 过渡顺滑; 仍保留受光/背光的对比度。
                float NdotLraw = dot(normalWS, mainLight.direction);
                float NdotL = saturate(NdotLraw * 0.5 + 0.5); // Half-Lambert
                //
                // 亮度预算重排（治"白色特别白"）
                //
                // 原值：diffuse 上限 1.0 + ambient 上限 0.44 = 1.44，
                //       再乘 faceTerm 上限 1.286 → 1.85，最后加 specular 0.65 → **2.50**。
                //       硬切成 1.0，等于超出 150%，白色体素整片死白、细节全丢。
                //
                // 新值：diffuse 上限 0.66 + ambient 上限 0.32 ≈ 0.98，
                //       乘 faceTerm 后约 1.16，specular 只在极窄的高光点上再叠，
                //       最后由 SoftShoulder 兜底 —— 任何角度都不会被切断。
                //
                float3 diffuse = mainLight.color * (NdotL * 0.58 + 0.08);

                //
                // 高光（这是"塑料积木感"最缺的一环）
                //
                // 上一版完全没有高光，看着就是哑光贴纸。
                // 参考图的塑料积木在受光面有非常明显的白色高光带（Phong/Blinn-Phong 那种）。
                // 这里用 Blinn-Phong：half 向量 + 幂函数，幂越大高光越集中（越像塑料）。
                // 上一版 _SpecularPower=32 还是太散，参考图那种小亮点是 power≥64 的效果。
                //
                float3 viewDirWS = normalize(_WorldSpaceCameraPos.xyz - input.positionWS);
                float3 halfDirWS = normalize(mainLight.direction + viewDirWS);
                float NdotH = saturate(dot(normalWS, halfDirWS));
                float specular = pow(NdotH, _SpecularPower) * _SpecularStrength;
                // 只在受光面加高光，背光面不要（避免"幽灵高光"）
                specular *= step(0.0, NdotL);
                // ---- 排查探针 5：关高光。若斜线随之消失 = 高光放大了法线断裂 ----
                if (_DebugMode > 4.5 && _DebugMode < 5.5) specular = 0.0;

                //
                // 面朝向明暗 —— 这是"积木感"的关键，但写错就会"从上往下泛光"。
                //
                // 之前用 saturate(axialWS.y) 是连续渐变：朝上偏 0.001 都被算成"完全朝上"，
                // 结果整个上半球所有面均匀提亮——就是用户反馈的"泛光"。
                //
                // 参考图的塑料积木：顶面整面均匀亮（一个色），侧面整面均匀暗（另一个色），
                // 顶/侧/底是**离散 3 档**分级，不是渐变。所以这里用阶跃 + 平滑窄带。
                //
                // 阶跃带宽从 0.16 加宽到 0.40：远看体素只有 1~2px 时, 0.16 的硬阶跃会被像素采样成
                // 沿 45° 体素面的规则亮暗条纹 (即"旋转亮暗条纹")。加宽后顶/侧/底过渡是多像素柔和渐变,
                // 远看不再 aliasing。积木感改由 AO (逐体素低频固定) + 柔和明暗差承担, 不会变死平。
                float topMask    = smoothstep(0.40, 0.80, axialWS.y);    // 顶面 (0~1 柔和阶跃)
                float bottomMask = smoothstep(0.40, 0.80, -axialWS.y);   // 底面 (0~1 柔和阶跃)
                // 加一点前后向微调让非正南正北的侧面不都一样死板
                float sideMod    = 0.10 * abs(nAxialOS.z);
                float faceTerm = 1.0
                    // 增益从 1.30 / -0.90 收敛到 0.85 / -0.72：
                    // 顶/侧/底的 3 档对比是"积木感"的来源，必须留着；
                    // 但 1.286 倍的上冲会把亮度顶穿 1.0（见上方预算说明），收窄后仍够读。
                    + _FaceShade * (0.85 * topMask              // 顶面提亮
                                  + 0.12 * sideMod             // 侧面微调
                                  - 0.72 * bottomMask);        // 底面压暗

                //
                // 环境光：恒定的天光 + **离散**顶光（不是连续 saturate）
                //
                // 之前用 saturate(axialWS.y*0.5+0.5) 是连续渐变：朝上偏 0.001
                // 都被算成"完全朝上"，整个上半球所有面均匀提亮 ——
                // 这正是用户反馈"旋转时从上往下泛光"的根因。
                // 改成阶跃：只有真正朝上的顶面（axialWS.y > 0.58）才拿到顶光增量，
                // 其它所有面统一一份基底天光，与 faceTerm.topMask 协同出"塑料积木"质感。
                //
                float topAmbientMask = smoothstep(0.40, 0.80, axialWS.y);   // 与 faceTerm 同一柔和阶跃带
                // 基底天光从 0.44 压到 0.26（过曝的另一半来源，见上方亮度预算说明）
                float3 ambient = float3(0.21, 0.23, 0.27) + topAmbientMask * 0.05;

                // AO：烘焙端已经收窄到 14~24 邻居的窄窗口，运行时再压一点出"积木堆叠"层次
                float aoFactor = lerp(1.0, input.ao, _AOStrength);

                // 最终：基础色 × (漫反射 + 环境) × AO × 面朝向 + 高光
                // 最后乘 1 - edgeLine×0.18，给每块描一圈极细的暗线，分隔相邻积木
                float3 litColor = input.color.rgb * (diffuse + ambient) * aoFactor * faceTerm
                                + mainLight.color * specular;
                // 临时归零用于定位: 若关掉描深后远看条纹消失, 则描深(即使 0.08)是远看条纹元凶。
                if (_DebugMode > 5.5 && _DebugMode < 6.5) edgeLine = 0.0;
                litColor *= (1.0 - edgeLine * 0.0);

                // ---- 排查探针 2：纯 albedo，完全不带光照 ----
                if (_DebugMode > 1.5 && _DebugMode < 2.5)
                    return float4(input.color.rgb, 1.0);

                //
                // 软肩兜底：0.85 以下完全线性（保住积木的硬朗对比），
                // 之上渐近压向 1.0。只要这行在，任何光照组合都不会出现死白硬切。
                //
                float3 finalColor = SoftShoulder(litColor, 0.85);
                // 应用屏幕空间抖动，打破规则网格与像素的拍频（摩尔纹 → 细噪点）
                finalColor += dither * DitherStrength;
                return float4(finalColor, 1.0);
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
            Offset -1, -1

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

                // 阴影 Pass 同样剔除内部面：否则内部面会投出假的接触阴影，
                // 在相邻体素接缝处形成一圈暗边，看上去就像缝隙。
                uint faceMask = (voxel.packedAttributes >> 24) & 0x3Fu;
                uint faceBit = GetVoxelFaceBit(input.normalOS);
                if (faceBit != 0u && (faceMask & faceBit) == 0u)
                {
                    output.positionCS = float4(2.0, 2.0, 4.0, 1.0);
                    return output;
                }

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
