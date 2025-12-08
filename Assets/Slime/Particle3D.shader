Shader "Instanced/Particle3D" {
    // 史莱姆粒子实例化着色器：根据 ComputeBuffer 中的粒子位置和可选的各向异性协方差矩阵绘制小型粒子网格。
    // 顶点阶段：读取粒子位置 (StructuredBuffer<Particle>)，如启用 _Aniso 则用协方差矩阵对局部顶点进行缩放/拉伸以模拟各向异性核。
    // 片元阶段：简易光照 + SH 环境 + 根据粒子 ID 做 8 色循环调试，若未分配则使用统一材质颜色。
    Properties {
        _Color ("Color", Color) = (1, 1, 1, 1)
        _Size ("Size", float) = 0.035
        [Toggle] _UseToon ("Use Toon Shading", Float) = 0
        _ToonIntensity ("Toon Effect Intensity", Range(0, 1)) = 1.0
    }

    SubShader {
        Pass 
        {
            Tags { 
                "RenderPipeline" = "UniversalRenderPipeline" 
                "Queue" = "Geometry" 
                "RenderType" = "Opaque"
            }
            
            ZWrite On
            ZTest LEqual
            Cull Back
            
            HLSLPROGRAM

            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _USETOON_ON

            #pragma enable_d3d11_debug_symbols
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            // 和 C# 侧粒子结构保持一致 (位置 + ID)
            struct Particle {
                float3 x;
                int ID;
            };

            struct a2v {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct v2f {
                float4 pos : SV_POSITION;
                float4 uv : TEXCOORD0; // xyz = 粒子世界位置(未乘尺度)，w = 粒子 ID
                float3 normal : TEXCOORD1;
                float3 worldPos : TEXCOORD2;
            };

            float _Size;          // 粒子网格放缩
            float4 _Color;        // 默认颜色
            int _Aniso;           // >0 时使用协方差矩阵实现各向异性缩放
            float _ToonIntensity; // 卡通效果强度

            StructuredBuffer<Particle> _ParticleBuffer;      // 粒子数据
            StructuredBuffer<float4x4> _CovarianceBuffer;    // 各向异性矩阵 (由 CPU 端 Reconstruction 计算)

            // 卡通化光照 - 阶梯式明暗
            float ToonRamp(float lightIntensity, float steps)
            {
                float step = 1.0 / steps;
                return floor(lightIntensity / step) * step;
            }

            v2f vert (a2v v, uint id : SV_InstanceID) 
            {
                v2f o;
                // 如果启用各向异性, 从矩阵缓冲中读取, 否则单位矩阵
                float3x3 covMatrix = _Aniso > 0 ? (float3x3)_CovarianceBuffer[id] : float3x3(1,0,0,0,1,0,0,0,1);
                // 将网格顶点局部坐标映射到各向异性空间
                float3 anisoPos = mul(covMatrix, v.vertex.xyz);
                // 世界位置：粒子中心 (缩放0.1与 Simulation 中一致) + 各向异性顶点偏移 * 尺寸
                float3 worldPosition = (_ParticleBuffer[id].x * 0.1) + anisoPos * _Size;
                // 投影到裁剪空间
                o.pos = TransformWorldToHClip(worldPosition);
                o.normal = TransformObjectToWorldNormal(v.normal);
                o.worldPos = worldPosition;
                o.uv = float4(_ParticleBuffer[id].x, _ParticleBuffer[id].ID);
                
                return o;
            }
            // 调试颜色表 (8种)
            static const float4 colors[8] = {
                float4(1, 0, 0, 1),
                float4(1, 0.5, 0, 1),
                float4(1, 1, 0, 1),
                float4(0.5, 1, 0, 1),
                float4(0, 1, 0, 1),
                float4(0, 1, 0.5, 1),
                float4(0, 1, 1, 1),
                float4(0, 0, 1, 1),
            };

            float4 frag (v2f i) : SV_Target 
            {
                // 主光方向 + 球谐环境光混合
                float3 L = GetMainLight().direction;
                float3 V = normalize(_WorldSpaceCameraPos - i.worldPos);
                float NoL = max(0, dot(L, i.normal));
                float3 sh = SampleSH(i.normal);
                
                int id = round(i.uv.w); // 粒子 ID
                float4 color = id > -0.5 ?  colors[((uint)id) & 7] : _Color;
                
                #ifdef _USETOON_ON
                    // 卡通化光照
                    float toonNdotL = ToonRamp(NoL, 3.0); // 3 级明暗
                    float3 toonShading = color.rgb * (toonNdotL + sh * 0.5);
                    
                    // 边缘光（Rim Light）
                    float rim = pow(1.0 - max(0, dot(i.normal, V)), 3.0);
                    float3 rimColor = float3(0.5, 1.0, 1.0) * rim * 0.5;
                    
                    // 混合原始光照和卡通光照
                    float3 originalShading = color.rgb * (NoL + sh);
                    float3 finalColor = lerp(originalShading, toonShading + rimColor, _ToonIntensity);
                    
                    return float4(finalColor, 1);
                #else
                    // 原始光照
                    return float4(color.rgb*(NoL.xxx + sh), 1);
                #endif
            }

            ENDHLSL
        }
    }
}
