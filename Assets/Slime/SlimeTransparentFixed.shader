Shader "Slime/TransparentFixed"
{
    // 修复版半透明史莱姆着色器 - 解决物体后方白色问题
    Properties
    {
        _Color ("Main Color", Color) = (0.2, 0.8, 0.7, 0.6)
        _Smoothness ("Smoothness", Range(0,1)) = 0.9
        _FresnelPower ("Fresnel Power", Range(0.5, 5)) = 2.5
        _RimColor ("Rim Color", Color) = (0.4, 1.0, 0.9, 1.0)
        _RimStrength ("Rim Strength", Range(0, 2)) = 1.0
        _Refraction ("Refraction Amount", Range(0, 0.1)) = 0.02
    }
    
    SubShader
    {
        Tags 
        { 
            "RenderPipeline" = "UniversalRenderPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
        }
        
        // 主渲染 Pass - 使用更简单的混合模式避免白色穿帮
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            
            // 使用预乘 Alpha 混合，避免白色穿帮
            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Back
            
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fog
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            
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
                float3 viewDirWS : TEXCOORD3;
            };
            
            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float _Smoothness;
                float _FresnelPower;
                float4 _RimColor;
                float _RimStrength;
                float _Refraction;
            CBUFFER_END
            
            Varyings vert(Attributes input)
            {
                Varyings output;
                
                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS);
                
                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.normalWS = normalInputs.normalWS;
                output.viewDirWS = GetWorldSpaceViewDir(positionInputs.positionWS);
                output.fogCoord = ComputeFogFactor(output.positionCS.z);
                
                return output;
            }
            
            half4 frag(Varyings input) : SV_Target
            {
                // 归一化向量
                float3 normalWS = normalize(input.normalWS);
                float3 viewDirWS = normalize(input.viewDirWS);
                
                // 主光照
                Light mainLight = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                float3 lightDir = normalize(mainLight.direction);
                
                // 漫反射 + 环境光
                float NdotL = saturate(dot(normalWS, lightDir));
                float3 diffuse = mainLight.color * mainLight.shadowAttenuation * NdotL * 0.7;
                float3 ambient = SampleSH(normalWS) * 0.5;
                
                // Blinn-Phong 高光
                float3 halfDir = normalize(lightDir + viewDirWS);
                float NdotH = saturate(dot(normalWS, halfDir));
                float specularPower = exp2(10 * _Smoothness + 1);
                float3 specular = mainLight.color * pow(NdotH, specularPower) * _Smoothness * 0.8;
                
                // Fresnel 效果 - 半透明物体的关键
                float NdotV = saturate(dot(normalWS, viewDirWS));
                float fresnel = pow(1.0 - NdotV, _FresnelPower);
                float3 rimLight = _RimColor.rgb * fresnel * _RimStrength;
                
                // 内部散射（背光）
                float backLight = saturate(dot(normalWS, -lightDir));
                float3 subsurface = mainLight.color * pow(backLight, 4) * 0.4;
                
                // 基础颜色
                float3 baseColor = _Color.rgb;
                
                // 合成最终颜色
                float3 finalColor = baseColor * (diffuse + ambient + subsurface) + specular + rimLight;
                
                // 透明度计算
                float alpha = _Color.a;
                // 边缘更透明（基于 Fresnel）
                alpha *= lerp(0.5, 1.0, fresnel);
                
                // 应用雾效
                finalColor = MixFog(finalColor, input.fogCoord);
                
                // 预乘 Alpha 处理（避免白色穿帮的关键）
                finalColor *= alpha;
                
                // 确保颜色在有效范围
                finalColor = max(float3(0, 0, 0), finalColor);
                alpha = saturate(alpha);
                
                return half4(finalColor, alpha);
            }
            ENDHLSL
        }
        
        // 简化的阴影 Pass（半透明物体通常投射软阴影）
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
                return 0;
            }
            ENDHLSL
        }
    }
    
    FallBack "Universal Render Pipeline/Lit"
}
