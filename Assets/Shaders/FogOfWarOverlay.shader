Shader "ARPG/Fog Of War Overlay"
{
    // 시야·안개 덮개 (FOG_OF_WAR_DESIGN §3). 플레이어 실제 위치에서의 거리로 어둡기를 정한다 — 시야 경계에서
    // 어두워진 뒤 멀수록 더 어둡게. 시야 안의 벽 뒤와 마을 시설 뒤 그림자는 가려지기 시작한 곳에서 옅게 시작해
    // 안쪽으로 들어갈수록 시야 밖 안개와 같은 밝기까지 진해진다.
    // 창 텍스처(플레이어 주변 64×64칸): R 벽에 가리지 않음, B 시설에도 가리지 않음 (G·A는 쓰지 않는다).
    Properties
    {
        [NoScaleOffset] _FogTex ("Fog Window (R clear of walls, B clear of facilities)", 2D) = "black" {}
        _FogColor ("Fog Color", Color) = (0.02, 0.025, 0.05, 1)
        [HideInInspector] _FogWindow ("Window (xy origin, zw 1/size)", Vector) = (0, 0, 0.015625, 0.015625)
        [HideInInspector] _FogVision ("Vision (xy center, z radius, w fade start offset)", Vector) = (0, 0, 9, 2)
        [HideInInspector] _FogShape ("Shape (x falloff power, y far brightness, z far distance, w shadow depth)", Vector) = (2.65, 0.3, 12, 3)
        [HideInInspector] _FogShadow ("Shadow edge brightness (x behind walls, y behind facilities)", Vector) = (0.5, 0.6, 0, 0)
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "FogOfWar"
            Tags { "LightMode" = "Universal2D" }

            Blend SrcAlpha OneMinusSrcAlpha
            Cull Off
            ZWrite Off
            ZTest Always

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 positionWS : TEXCOORD0;
            };

            TEXTURE2D(_FogTex);
            SAMPLER(sampler_FogTex);

            CBUFFER_START(UnityPerMaterial)
                half4 _FogColor;
                float4 _FogWindow;
                float4 _FogVision;
                float4 _FogShape;
                float4 _FogShadow;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(positionWS);
                output.positionWS = positionWS.xy;
                return output;
            }

            // 그림자 마스크: x 벽에 가림, y 벽이나 시설에 가림. 창 밖은 클램프로 가장자리 텍셀(가림)을 읽지만 그곳은 거리 밝기가
            // 이미 안개 밝기라 결과가 같다. 분기 안에서 읽으므로 미분이 필요 없는 LOD 0 샘플을 쓴다(밉맵 없음).
            float2 SampleShadow(float2 positionWS)
            {
                half4 window = SAMPLE_TEXTURE2D_LOD(_FogTex, sampler_FogTex, (positionWS - _FogWindow.xy) * _FogWindow.zw, 0);
                return float2(1.0 - window.r, 1.0 - window.b);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 toCenter = _FogVision.xy - input.positionWS;
                float distanceToCenter = length(toCenter);

                // 거리 어둡기: 시작부터 끝까지 한 곡선으로 낮춘다. 반경에서 두 smoothstep을 접합하면 기울기가 0이 되어 고리가 보인다.
                // .x는 곡선 지수다. 반경에서 약 52%(가장 어두운 밝기 30% 기준)를 지나가되 그 지점에서 어두워지는 속도가 멈추지 않는다.
                float fadeStart = _FogVision.z - _FogVision.w;
                float falloff = smoothstep(fadeStart, max(_FogShape.z, fadeStart + 0.001), distanceToCenter);
                float brightness = lerp(_FogShape.y, 1.0, pow(abs(1.0 - falloff), max(_FogShape.x, 0.001)));

                // 그림자: 거리 밝기를 안개 밝기(.y) 쪽으로 끌어내린다. 안개 밝기보다 어두워지지 않으므로 깊은 그림자는 시야 밖과
                // 같은 어둠으로 이어지고, 거리 밝기가 이미 안개 밝기인 .z 밖은 읽을 필요가 없다.
                [branch]
                if (distanceToCenter < _FogShape.z)
                {
                    float2 position = input.positionWS;
                    float2 inward = toCenter / max(distanceToCenter, 0.0001);
                    float2 side = float2(-inward.y, inward.x);
                    float depthRange = max(_FogShape.w, 0.001);
                    float tapStep = depthRange / 3.0;

                    // 플레이어 쪽으로 깊이의 1/3·2/3·전부만큼 들어간 세 점: 가린 곳이 플레이어 쪽으로 얼마나 이어지나 = 그림자 깊이.
                    // 보이는 칸에서 플레이어 쪽은 보이는 칸이라 벽면·벽 앞 땅으로 번지지 않고, 그림자 옆선의 칸 계단만 채운다.
                    // 플레이어를 지나치지 않도록 거리에서 자른다(플레이어 칸은 항상 보임).
                    float2 inner = (SampleShadow(position + inward * min(tapStep, distanceToCenter))
                        + SampleShadow(position + inward * min(tapStep * 2.0, distanceToCenter))
                        + SampleShadow(position + inward * min(depthRange, distanceToCenter))) * (1.0 / 3.0);

                    // 시선과 직각인 양옆 두 점씩: 한쪽이라도 금방 보이는 곳이 나오면 폭이 좁은 그림자라 깊어지지 않는다(작은 시설 뒤는 옅게 남음).
                    float sideA = SampleShadow(position + side * tapStep).y + SampleShadow(position + side * (tapStep * 2.0)).y;
                    float sideB = SampleShadow(position - side * tapStep).y + SampleShadow(position - side * (tapStep * 2.0)).y;
                    float depth = saturate(min(inner.y, min(sideA, sideB) * 0.5));

                    // 가장자리 밝기(_FogShadow 벽 .x, 시설 .y)를 안개 쪽으로 끌어내리는 비율로 바꾸고, 깊이에 따라 1(안개 밝기)까지 올린다.
                    float2 coverage = max(SampleShadow(position), inner);
                    float wallShare = saturate(coverage.x / max(coverage.y, 0.0001));
                    float2 edgeAmount = saturate((1.0 - _FogShadow.xy) / max(1.0 - _FogShape.y, 0.001));
                    float shadowAmount = saturate(coverage.y * lerp(lerp(edgeAmount.y, edgeAmount.x, wallShare), 1.0, depth));
                    brightness = lerp(brightness, _FogShape.y, shadowAmount);
                }

                float alpha = 1.0 - brightness;
            #if !defined(UNITY_COLORSPACE_GAMMA)
                // 선형 색 공간에서는 알파가 선형 값끼리 섞여 눈에는 덜 어두워 보인다(알파 0.7 → 약 43%).
                // 밝기 값을 눈에 보이는 밝기 기준으로 맞춘다: 남는 밝기 (1 - a)를 감마 2.2로 선형화.
                alpha = 1.0 - pow(abs(1.0 - alpha), 2.2); // alpha는 0~1이라 abs는 컴파일러 경고만 없앤다
            #endif
                return half4(_FogColor.rgb, alpha);
            }
            ENDHLSL
        }
    }
}
