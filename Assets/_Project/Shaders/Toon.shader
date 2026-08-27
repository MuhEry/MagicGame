// ---------------------------------------------------------------------------
// RecycleGame / Toon
// URP 17.3 (Unity 6000.3) - Meta Quest / Android VR icin optimize cel-shading.
// Takimin farkli kaynaklardan gelen assetlerini tek gorsel dile sokmak icin
// ortak shader olarak kullanilir.
//
// Tasarim kararlari:
//  * Outline ekran-uzayi post-process ile DEGIL, ters kabuk (inverted hull)
//    ile ciziliyor. Boylece depth+normals prepass, fullscreen blit ve MSAA
//    resolve maliyeti olusmuyor - Quest'te bu ucunun de bedeli cok agir.
//  * Baked lightmap + shadowmask destekli (sahne bu sekilde bake edilmis).
//  * Additional lights YOK: sahnedeki point isiklar Baked, directional isik
//    Mixed, URP Performance profilinde ek isiklar zaten kapali. Bos varyant
//    uretmiyoruz.
//  * Single-pass instanced stereo icin gerekli tum makrolar mevcut.
//  * Tum materyal ozellikleri UnityPerMaterial icinde -> SRP Batcher uyumlu.
// ---------------------------------------------------------------------------
Shader "RecycleGame/Toon"
{
    Properties
    {
        [MainTexture] _BaseMap("Base Map", 2D) = "white" {}
        [MainColor]   _BaseColor("Base Color", Color) = (1, 1, 1, 1)

        [Toggle(_ALPHATEST_ON)] _AlphaClip("Alpha Clip", Float) = 0
        _Cutoff("Alpha Cutoff", Range(0.0, 1.0)) = 0.5

        [Header(Toon Ramp)][Space(4)]
        _ShadeColor("Shade Color", Color) = (0.35, 0.39, 0.55, 1)
        [IntRange] _RampSteps("Ramp Steps", Range(1, 4)) = 1
        _RampThreshold("Ramp Threshold", Range(0.0, 1.0)) = 0.25
        _RampSmoothness("Ramp Smoothness", Range(0.001, 0.5)) = 0.03
        _ShadowStrength("Realtime Shadow Strength", Range(0.0, 1.0)) = 1
        _BakedGIStrength("Baked GI Strength", Range(0.0, 2.0)) = 1

        // Buyuk duz yuzeylerde (tavan, duvar, zemin) 0 birakilmali: lightmap'in
        // yumusak isik havuzlari kademelendirilince amip seklinde lekeler olusuyor.
        // Esyalarda (sira, sandalye, kutu) 0.5-0.7 arasi toon hissini veriyor.
        [Header(Baked GI Posterize)][Space(4)]
        _GIPosterize("GI Posterize Amount", Range(0.0, 1.0)) = 0.55
        [IntRange] _GISteps("GI Steps", Range(2, 8)) = 4
        _GISmoothness("GI Band Smoothness", Range(0.001, 0.5)) = 0.12

        [Header(Specular)][Space(4)]
        [Toggle(_TOON_SPECULAR)] _SpecularOn("Enable Specular", Float) = 0
        _SpecularColor("Specular Color", Color) = (1, 1, 1, 1)
        _SpecularSize("Specular Size", Range(0.0, 1.0)) = 0.25
        _SpecularSmoothness("Specular Smoothness", Range(0.001, 0.5)) = 0.02

        [Header(Rim)][Space(4)]
        [Toggle(_TOON_RIM)] _RimOn("Enable Rim", Float) = 0
        _RimColor("Rim Color", Color) = (1, 1, 1, 1)
        _RimSize("Rim Size", Range(0.0, 1.0)) = 0.75
        _RimSmoothness("Rim Smoothness", Range(0.001, 0.5)) = 0.05

        [Header(Emission)][Space(4)]
        [Toggle(_TOON_EMISSION)] _EmissionOn("Enable Emission", Float) = 0
        [HDR] _EmissionColor("Emission Color", Color) = (0, 0, 0, 1)

        [Header(Outline)][Space(4)]
        _OutlineColor("Outline Color", Color) = (0.07, 0.07, 0.09, 1)
        _OutlineWidth("Outline Width (cm at 2m)", Range(0.0, 5.0)) = 1.5
        [Toggle(_OUTLINE_SMOOTH_NORMALS)] _OutlineSmoothNormals("Use Baked Smooth Normals (UV3)", Float) = 0

        [Header(Rendering)][Space(4)]
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull", Float) = 2
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "UniversalMaterialType" = "Lit"
            "Queue" = "Geometry"
            "IgnoreProjector" = "True"
        }
        LOD 200

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        // SRP Batcher uyumu icin TUM materyal ozellikleri bu blokta olmali.
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4  _BaseColor;
            half4  _ShadeColor;
            half4  _SpecularColor;
            half4  _RimColor;
            half4  _EmissionColor;
            half4  _OutlineColor;
            half   _Cutoff;
            half   _RampSteps;
            half   _RampThreshold;
            half   _RampSmoothness;
            half   _ShadowStrength;
            half   _BakedGIStrength;
            half   _GIPosterize;
            half   _GISteps;
            half   _GISmoothness;
            half   _SpecularSize;
            half   _SpecularSmoothness;
            half   _RimSize;
            half   _RimSmoothness;
            half   _OutlineWidth;
            half   _AlphaClip;
            half   _SpecularOn;
            half   _RimOn;
            half   _EmissionOn;
            half   _OutlineSmoothNormals;
            half   _Cull;
        CBUFFER_END

        TEXTURE2D(_BaseMap);
        SAMPLER(sampler_BaseMap);
        ENDHLSL

        // -------------------------------------------------------------------
        // 1) OUTLINE - ters kabuk (inverted hull).
        // LightMode = SRPDefaultUnlit: URP'nin DrawObjectsPass'i bu tag'i
        // UniversalForward ile birlikte opaque blogunda cizer, yani ayri bir
        // Renderer Feature ya da fullscreen pass'e gerek yok.
        // Materyal bazinda kapatmak icin Material.SetShaderPassEnabled
        // kullanilir (Toon inspector'daki Outline Pass kutucugu).
        // -------------------------------------------------------------------
        Pass
        {
            Name "Outline"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            Cull Front
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex OutlineVertex
            #pragma fragment OutlineFragment

            #pragma shader_feature_local _OUTLINE_SMOOTH_NORMALS
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct OutlineAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                // Sadece keyword acikken bagli: kapaliyken bos bir vertex stream
                // baglamanin bedelini her outline'li objede odemeyelim.
                #if defined(_OUTLINE_SMOOTH_NORMALS)
                    float3 smoothNormalOS : TEXCOORD3;
                #endif
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct OutlineVaryings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                half   fogFactor  : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            OutlineVaryings OutlineVertex(OutlineAttributes input)
            {
                OutlineVaryings output = (OutlineVaryings)0;

                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                // Sert kenarli (split normal) modellerde kabuk yirtilmasin diye,
                // bake edilmis yumusatilmis normal varsa o kullanilir.
                #if defined(_OUTLINE_SMOOTH_NORMALS)
                    float3 extrudeOS = input.smoothNormalOS;
                #else
                    float3 extrudeOS = input.normalOS;
                #endif

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS   = normalize(TransformObjectToWorldNormal(extrudeOS));

                // Kalinlik EKRANDA sabit kalmali. Sabit dunya kalinligi kullanirsak
                // 20 cm'deki masa kenari sisman bir bant, 3 m'deki sandalye ise
                // gorunmez bir cizgi olur. Bu yuzden dunya kalinligini kameraya
                // olan mesafeyle olcekliyoruz (referans 2 m), asiri uc degerlerde
                // patlamasin diye siniriyoruz.
                float viewDistance = distance(GetCameraPositionWS(), positionWS);
                float widthScale   = clamp(viewDistance * 0.5, 0.1, 4.0);

                positionWS += normalWS * (_OutlineWidth * 0.01 * widthScale);

                output.positionCS = TransformWorldToHClip(positionWS);
                output.uv         = TRANSFORM_TEX(input.uv, _BaseMap);
                output.fogFactor  = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half4 OutlineFragment(OutlineVaryings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                #if defined(_ALPHATEST_ON)
                    half alpha = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).a * _BaseColor.a;
                    clip(alpha - _Cutoff);
                #endif

                half3 color = MixFog(_OutlineColor.rgb, input.fogFactor);
                return half4(color, 1.0h);
            }
            ENDHLSL
        }

        // -------------------------------------------------------------------
        // 2) FORWARD LIT - asil toon aydinlatma.
        // -------------------------------------------------------------------
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Cull [_Cull]
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex ToonVertex
            #pragma fragment ToonFragment

            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma shader_feature_local_fragment _TOON_SPECULAR
            #pragma shader_feature_local_fragment _TOON_RIM
            #pragma shader_feature_local_fragment _TOON_EMISSION

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile _ USE_LEGACY_LIGHTMAPS
            #pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
            #pragma multi_compile _ SHADOWS_SHADOWMASK
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "ToonLighting.hlsl"

            struct Attributes
            {
                float4 positionOS       : POSITION;
                float3 normalOS         : NORMAL;
                float2 uv               : TEXCOORD0;
                float2 staticLightmapUV : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                half3  normalWS   : TEXCOORD2;
                half   fogFactor  : TEXCOORD3;
                #if defined(LIGHTMAP_ON)
                    float2 staticLightmapUV : TEXCOORD4;
                #else
                    half3 vertexSH : TEXCOORD4;
                #endif
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings ToonVertex(Attributes input)
            {
                Varyings output = (Varyings)0;

                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs   normalInputs   = GetVertexNormalInputs(input.normalOS);

                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.normalWS   = normalInputs.normalWS;
                output.uv         = TRANSFORM_TEX(input.uv, _BaseMap);
                output.fogFactor  = ComputeFogFactor(positionInputs.positionCS.z);

                #if defined(LIGHTMAP_ON)
                    OUTPUT_LIGHTMAP_UV(input.staticLightmapUV, unity_LightmapST, output.staticLightmapUV);
                #else
                    OUTPUT_SH(normalInputs.normalWS, output.vertexSH);
                #endif

                return output;
            }

            half4 ToonFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half4 baseSample = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseColor;
                #if defined(_ALPHATEST_ON)
                    clip(baseSample.a - _Cutoff);
                #endif

                half3 albedo    = baseSample.rgb;
                half3 normalWS  = NormalizeNormalPerPixel(input.normalWS);
                half3 viewDirWS = normalize(GetWorldSpaceViewDir(input.positionWS));

                // --- Baked GI (lightmap veya SH probe) --------------------------
                #if defined(LIGHTMAP_ON)
                    half3 bakedGI    = SAMPLE_GI(input.staticLightmapUV, half3(0.0h, 0.0h, 0.0h), normalWS);
                    half4 shadowMask = SAMPLE_SHADOWMASK(input.staticLightmapUV);
                #else
                    half3 bakedGI    = SAMPLE_GI(float2(0.0, 0.0), input.vertexSH, normalWS);
                    half4 shadowMask = SAMPLE_SHADOWMASK(float2(0.0, 0.0));
                #endif
                bakedGI *= _BakedGIStrength;
                bakedGI = ToonPosterizeGI(bakedGI, _GISteps, _GISmoothness, _GIPosterize);

                // --- Ana isik (Mixed directional) -------------------------------
                #if defined(_MAIN_LIGHT_SHADOWS) || defined(_MAIN_LIGHT_SHADOWS_CASCADE) || defined(_MAIN_LIGHT_SHADOWS_SCREEN)
                    float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                #else
                    float4 shadowCoord = float4(0.0, 0.0, 0.0, 0.0);
                #endif

                Light mainLight = GetMainLight(shadowCoord, input.positionWS, shadowMask);
                MixRealtimeAndBakedGI(mainLight, normalWS, bakedGI);

                half shadowAtten = lerp(1.0h, mainLight.shadowAttenuation, _ShadowStrength);
                half ndotl = dot(normalWS, mainLight.direction);

                // Esigi kaydir, sonra kademelendir. Golge de bandin icinde carpiliyor
                // ki golge kenari da net ve toon kalsin.
                half lightTerm = saturate(ndotl + (0.5h - _RampThreshold)) * shadowAtten;
                half ramp = ToonBand(lightTerm, _RampSteps, _RampSmoothness);

                half3 shadeTint  = lerp(_ShadeColor.rgb, half3(1.0h, 1.0h, 1.0h), ramp);
                half3 directTerm = mainLight.color * mainLight.distanceAttenuation * shadeTint;

                half3 color = albedo * (directTerm + bakedGI);

                // --- Toon specular ----------------------------------------------
                #if defined(_TOON_SPECULAR)
                    half3 halfVec = normalize(mainLight.direction + viewDirWS);
                    half  ndoth   = saturate(dot(normalWS, halfVec));
                    half  spec    = ToonStep(ndoth, ToonSpecularThreshold(_SpecularSize), _SpecularSmoothness);
                    color += _SpecularColor.rgb * mainLight.color * spec * ramp;
                #endif

                // --- Rim ---------------------------------------------------------
                #if defined(_TOON_RIM)
                    half fresnel = 1.0h - saturate(dot(normalWS, viewDirWS));
                    half rim = ToonStep(fresnel, _RimSize, _RimSmoothness);
                    color += _RimColor.rgb * rim * ramp;
                #endif

                // --- Emission (lamba, ekran, isikli etiket) ----------------------
                #if defined(_TOON_EMISSION)
                    color += _EmissionColor.rgb;
                #endif

                color = MixFog(color, input.fogFactor);
                return half4(color, 1.0h);
            }
            ENDHLSL
        }

        // -------------------------------------------------------------------
        // 3) SHADOW CASTER
        // -------------------------------------------------------------------
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex ShadowVertex
            #pragma fragment ShadowFragment

            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct ShadowAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct ShadowVaryings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            ShadowVaryings ShadowVertex(ShadowAttributes input)
            {
                ShadowVaryings output = (ShadowVaryings)0;

                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS   = TransformObjectToWorldNormal(input.normalOS);

                #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                    float3 lightDirectionWS = normalize(_LightPosition - positionWS);
                #else
                    float3 lightDirectionWS = _LightDirection;
                #endif

                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));

                #if UNITY_REVERSED_Z
                    positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif

                output.positionCS = positionCS;
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return output;
            }

            half4 ShadowFragment(ShadowVaryings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                #if defined(_ALPHATEST_ON)
                    half alpha = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).a * _BaseColor.a;
                    clip(alpha - _Cutoff);
                #endif
                return 0;
            }
            ENDHLSL
        }

        // -------------------------------------------------------------------
        // 4) DEPTH ONLY
        // -------------------------------------------------------------------
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex DepthVertex
            #pragma fragment DepthFragment

            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma multi_compile_instancing

            struct DepthAttributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct DepthVaryings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            DepthVaryings DepthVertex(DepthAttributes input)
            {
                DepthVaryings output = (DepthVaryings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return output;
            }

            half4 DepthFragment(DepthVaryings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                #if defined(_ALPHATEST_ON)
                    half alpha = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).a * _BaseColor.a;
                    clip(alpha - _Cutoff);
                #endif
                return 0;
            }
            ENDHLSL
        }

        // -------------------------------------------------------------------
        // 5) DEPTH NORMALS - su an kullanilmiyor, ama SSAO / decal / ekran
        // uzayi efekti eklenirse hazir. Kapali oldugu surece calisma zamani
        // maliyeti yok.
        // -------------------------------------------------------------------
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            ZWrite On
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex DepthNormalsVertex
            #pragma fragment DepthNormalsFragment

            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct DepthNormalsAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct DepthNormalsVaryings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                half3  normalWS   : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            DepthNormalsVaryings DepthNormalsVertex(DepthNormalsAttributes input)
            {
                DepthNormalsVaryings output = (DepthNormalsVaryings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs   normalInputs   = GetVertexNormalInputs(input.normalOS);

                output.positionCS = positionInputs.positionCS;
                output.normalWS   = normalInputs.normalWS;
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return output;
            }

            half4 DepthNormalsFragment(DepthNormalsVaryings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                #if defined(_ALPHATEST_ON)
                    half alpha = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).a * _BaseColor.a;
                    clip(alpha - _Cutoff);
                #endif

                return half4(NormalizeNormalPerPixel(input.normalWS), 0.0h);
            }
            ENDHLSL
        }

        // -------------------------------------------------------------------
        // 6) META - lightmap bake'inde albedo'yu dogru vermek icin sart.
        // Bu pass olmadan sahne yeniden bake edilirse GI kararir.
        // -------------------------------------------------------------------
        Pass
        {
            Name "Meta"
            Tags { "LightMode" = "Meta" }

            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex ToonMetaVertex
            #pragma fragment ToonMetaFragment

            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma shader_feature_local_fragment _TOON_EMISSION

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/MetaInput.hlsl"

            struct MetaAttributes
            {
                float4 positionOS : POSITION;
                float2 uv0        : TEXCOORD0;
                float2 uv1        : TEXCOORD1;
                float2 uv2        : TEXCOORD2;
            };

            struct MetaVaryings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
            };

            MetaVaryings ToonMetaVertex(MetaAttributes input)
            {
                MetaVaryings output = (MetaVaryings)0;
                output.positionCS = MetaVertexPosition(input.positionOS, input.uv1, input.uv2,
                                                       unity_LightmapST, unity_DynamicLightmapST);
                output.uv = TRANSFORM_TEX(input.uv0, _BaseMap);
                return output;
            }

            half4 ToonMetaFragment(MetaVaryings input) : SV_Target
            {
                half4 baseSample = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseColor;
                #if defined(_ALPHATEST_ON)
                    clip(baseSample.a - _Cutoff);
                #endif

                MetaInput metaInput = (MetaInput)0;
                metaInput.Albedo = baseSample.rgb;
                #if defined(_TOON_EMISSION)
                    metaInput.Emission = _EmissionColor.rgb;
                #else
                    metaInput.Emission = half3(0.0h, 0.0h, 0.0h);
                #endif
                return UnityMetaFragment(metaInput);
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Unlit"
    CustomEditor "Denizinyeri.Tools.ToonShaderGUI"
}
