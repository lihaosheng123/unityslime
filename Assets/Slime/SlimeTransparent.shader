Shader "Slime/TransparentSurface"
{
    // 半透明史莱姆表面着色器：保持青绿色半透明效果 + 正确深度处理
    Properties
    {
        _Color ("Main Color", Color) = (0.2, 0.8, 0.7, 0.7)
        _Smoothness ("Smoothness", Range(0,1)) = 0.9
        _Metallic ("Metallic", Range(0,1)) = 0.3
        _FresnelPower ("Fresnel Power", Range(0.5, 5)) = 2.5
        _RimColor ("Rim Color", Color) = (0.4, 1.0, 0.9, 1.0)
        _RimStrength ("Rim Strength", Range(0, 2)) = 1.0
        _DepthFade ("Depth Fade Distance", Range(0, 10)) = 2.0
        _AlphaCutoff ("Alpha Cutoff (for depth)", Range(0, 1)) = 0.1
    }
    
    SubShader
    {
        Tags 
        { 
            "RenderPipeline" = "UniversalRenderPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
        }
        
        // Pass 1: 深度预处理 (Alpha 测试，只写入主要可见部分的深度)
        Pass
        {
            Name "DepthPrepass"
            Tags { "LightMode" = "DepthOnly" }
            
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back
            
            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            
            struct Attributes
            {
                float4 positionOS : POSITION;
            };
            
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
            };
            
            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float _AlphaCutoff;
                float _DepthFade;
            CBUFFER_END
            
            Varyings DepthVert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs posInputs = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = posInputs.positionCS;
                output.positionWS = posInputs.positionWS;
                return output;
            }
            
            half4 DepthFrag(Varyings input) : SV_Target
            {
                // 基于距离的 Alpha 衰减
                float depth = length(GetCameraPositionWS() - input.positionWS);
                float fade = saturate(depth / _DepthFade);
                float alpha = _Color.a * (1.0 - fade * 0.5);
                
                // Alpha 测试：只有不透明部分写入深度
                clip(alpha - _AlphaCutoff);
                return 0;
            }
            ENDHLSL
        }
        
        // Pass 2: 主渲染 (半透明前向渲染)
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Back
            
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            
            // 确保包含深度纹理支持
            #define REQUIRE_DEPTH_TEXTURE
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            
            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };
            
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float fogCoord : TEXCOORD2;
                float depth : TEXCOORD3;
                float4 screenPos : TEXCOORD4;
            };
            
            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float _Smoothness;
                float _Metallic;
                float _FresnelPower;
                float4 _RimColor;
                float _RimStrength;
                float _DepthFade;
                float _AlphaCutoff;
            CBUFFER_END
            
            Varyings vert(Attributes input)
            {
                Varyings output;
                
                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS);
                
                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.normalWS = normalInputs.normalWS;
                output.fogCoord = ComputeFogFactor(output.positionCS.z);
                output.depth = length(GetCameraPositionWS() - positionInputs.positionWS);
                output.screenPos = ComputeScreenPos(output.positionCS);
                
                return output;
            }
            
            half4 frag(Varyings input) : SV_Target
            {
                // 检查深度遮挡（防止白色穿帮）
                float2 screenUV = input.screenPos.xy / input.screenPos.w;
                
                #if defined(REQUIRE_DEPTH_TEXTURE)
                    // 采样场景深度
                    float sceneDepth = SampleSceneDepth(screenUV);
                    float sceneDepthVS = LinearEyeDepth(sceneDepth, _ZBufferParams);
                    float fragmentDepthVS = LinearEyeDepth(input.positionCS.z, _ZBufferParams);
                    
                    // 如果片元在不透明物体后面很多，降低不透明度避免穿帮
                    float depthDiff = sceneDepthVS - fragmentDepthVS;
                    if (depthDiff < -0.01) // 在不透明物体后面
                    {
                        // 柔和消隐，避免完全消失
                        float occlusionFade = saturate(-depthDiff * 10.0);
                        if (occlusionFade > 0.95)
                            discard; // 完全在后面时直接丢弃
                    }
                #endif
                
                // 归一化法线和视线方向
                float3 normalWS = normalize(input.normalWS);
                float3 viewDirWS = normalize(GetCameraPositionWS() - input.positionWS);
                
                // 主光照
                Light mainLight = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                float3 lightDir = normalize(mainLight.direction);
                
                // Lambert 漫反射
                float NdotL = saturate(dot(normalWS, lightDir));
                float3 diffuse = mainLight.color * mainLight.shadowAttenuation * NdotL;
                
                // 环境光（球谐）
                float3 ambient = SampleSH(normalWS) * 0.8;
                
                // Blinn-Phong 高光
                float3 halfDir = normalize(lightDir + viewDirWS);
                float NdotH = saturate(dot(normalWS, halfDir));
                float specularPower = exp2(10 * _Smoothness + 1);
                float3 specular = mainLight.color * pow(NdotH, specularPower) * _Smoothness * 0.5;
                
                // Fresnel 边缘光（半透明物体的关键）
                float NdotV = saturate(dot(normalWS, viewDirWS));
                float fresnel = pow(1.0 - NdotV, _FresnelPower);
                float3 rimLight = _RimColor.rgb * fresnel * _RimStrength;
                
                // 附加光源
                #ifdef _ADDITIONAL_LIGHTS
                uint pixelLightCount = GetAdditionalLightsCount();
                for (uint lightIndex = 0u; lightIndex < pixelLightCount; ++lightIndex)
                {
                    Light light = GetAdditionalLight(lightIndex, input.positionWS);
                    float lightNdotL = saturate(dot(normalWS, light.direction));
                    diffuse += light.color * light.distanceAttenuation * light.shadowAttenuation * lightNdotL * 0.5;
                }
                #endif
                
                // 内部散射效果（模拟半透明）
                float backLight = saturate(dot(normalWS, -lightDir));
                float3 subsurface = mainLight.color * pow(backLight, 3) * 0.3;
                
                // 最终颜色合成
                float3 finalColor = _Color.rgb * (diffuse + ambient + subsurface) + specular + rimLight;
                
                // 深度衰减 (距离越远越透明)
                float depthFade = saturate(input.depth / _DepthFade);
                float alpha = _Color.a * (1.0 - depthFade * 0.3);
                
                // 增强边缘透明度
                alpha *= saturate(NdotV + 0.2);
                
                // 应用雾效
                finalColor = MixFog(finalColor, input.fogCoord);
                
                // 确保颜色值合法，防止白色穿帮
                finalColor = max(float3(0, 0, 0), finalColor);
                
                return half4(finalColor, alpha);
            }
            ENDHLSL
        }
        
        // Pass 3: 阴影投射 (可选，半透明物体通常不投射实心阴影)
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back
            
            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            
            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };
            
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };
            
            float3 _LightDirection;
            
            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float _AlphaCutoff;
            CBUFFER_END
            
            Varyings ShadowVert(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, _LightDirection));
                return output;
            }
            
            half4 ShadowFrag(Varyings input) : SV_Target
            {
                // 半透明阴影：只投射部分阴影
                clip(_Color.a - _AlphaCutoff);
                return 0;
            }
            ENDHLSL
        }
    }
    
    FallBack "Universal Render Pipeline/Lit"
}
