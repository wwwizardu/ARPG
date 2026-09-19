Shader "ARPG/Attack Telegraph Unlit"
{
    Properties
    {
        _SurfaceAlpha ("Danger Surface Alpha", Range(0, 1)) = 0.14
        _ProgressAlpha ("Progress Alpha", Range(0, 1)) = 0.38
        _OutlineAlpha ("Outline Alpha", Range(0, 1)) = 0.9
        _OutlineWidth ("Outline Width", Range(0.001, 0.2)) = 0.035
        _PulseStrength ("Final Pulse Strength", Range(0, 1)) = 0.35
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
            Name "AttackTelegraph"
            Tags { "LightMode" = "Universal2D" }

            Blend SrcAlpha OneMinusSrcAlpha
            Cull Off
            ZWrite Off
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv0 : TEXCOORD0;
                float2 uv1 : TEXCOORD1;
                float2 uv2 : TEXCOORD2;
                float2 uv3 : TEXCOORD3;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 localPosition : TEXCOORD0;
                float2 timing : TEXCOORD1;
                float2 shapeData : TEXCOORD2;
                float detail : TEXCOORD3;
                half4 color : COLOR;
            };

            CBUFFER_START(UnityPerMaterial)
                float _SurfaceAlpha;
                float _ProgressAlpha;
                float _OutlineAlpha;
                float _OutlineWidth;
                float _PulseStrength;
            CBUFFER_END

            float _CurrentTime;

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.localPosition = input.uv0;
                output.timing = input.uv1;
                output.shapeData = input.uv2;
                output.detail = input.uv3.x;
                output.color = input.color;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float duration = max(0.0001, input.timing.y - input.timing.x);
                float progress = saturate((_CurrentTime - input.timing.x) / duration);
                float edgeDistance;
                float progressCoordinate;

                if (input.shapeData.x < 0.5)
                {
                    float radial = length(input.localPosition);
                    edgeDistance = 1.0 - radial;
                    progressCoordinate = radial;

                    float halfAngle = input.shapeData.y;
                    if (halfAngle < PI - 0.0001)
                    {
                        float angle = abs(atan2(input.localPosition.y, input.localPosition.x));
                        float sideDistance = sin(max(0.0, halfAngle - angle)) * radial;
                        edgeDistance = min(edgeDistance, sideDistance);
                    }
                }
                else
                {
                    float aspect = max(0.001, input.shapeData.y);
                    float2 capsulePoint = float2(input.localPosition.x * aspect, input.localPosition.y);
                    float closestX = clamp(capsulePoint.x, 0.0, aspect);
                    float capsuleDistance = length(capsulePoint - float2(closestX, 0.0));
                    edgeDistance = 1.0 - capsuleDistance;
                    progressCoordinate = saturate(input.localPosition.x);
                }

                float antialias = max(fwidth(edgeDistance), 0.001);
                float outline = 1.0 - smoothstep(_OutlineWidth - antialias, _OutlineWidth + antialias, edgeDistance);
                float progressEdge = max(fwidth(progressCoordinate), 0.002);
                float filled = 1.0 - smoothstep(progress - progressEdge, progress + progressEdge, progressCoordinate);

                float showSurface = 1.0 - step(1.5, input.detail);
                float showProgress = 1.0 - step(0.5, input.detail);
                float finalPulse = smoothstep(0.85, 1.0, progress) * (0.5 + 0.5 * sin(_CurrentTime * 35.0));
                float alpha = _SurfaceAlpha * showSurface
                    + _ProgressAlpha * filled * showProgress
                    + (_OutlineAlpha + finalPulse * _PulseStrength) * outline;
                alpha = saturate(alpha) * input.color.a;

                half3 baseColor = input.color.rgb;
                half3 progressColor = lerp(baseColor, half3(1.0, 0.85, 0.55), filled * showProgress * 0.65);
                half3 finalColor = lerp(progressColor, half3(1.0, 1.0, 1.0), outline * (0.35 + finalPulse * 0.65));
                return half4(finalColor, alpha);
            }
            ENDHLSL
        }
    }
}
