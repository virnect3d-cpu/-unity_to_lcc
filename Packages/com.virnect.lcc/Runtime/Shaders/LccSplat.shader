// Virnect LCC Splat billboard shader (v2.4 — 3-axis scale + sorted blend).
//
// XGrids LCC 에 회전 속성이 없어 스플랫은 isotropic 로 취급되는데, scale 은 3축이 따로 있다.
// view-plane 빌보드에서 가장 큰 축을 반지름으로 쓰는 단순 근사로 업그레이드했다.
// 블렌딩은 기존 SrcAlpha/OneMinusSrcAlpha + (옵션) ZWrite Off.
//
// 버텍스 입력:
//   POSITION   world pos (4 verts 공유)
//   COLOR      RGBA8
//   TEXCOORD0  corner uv (-1..1)
//   TEXCOORD1  (scale_x, scale_y)
//   TEXCOORD2  (scale_z, opacity)
Shader "Virnect/LccSplat"
{
    Properties
    {
        _Tint         ("Tint", Color) = (1,1,1,1)
        _ScaleMul     ("Scale Multiplier", Range(0.1, 5)) = 1.0
        _OpacityBoost ("Opacity Boost", Range(0, 1)) = 0.0
        _Falloff      ("Gaussian Falloff k", Range(0.5, 8)) = 4.0
        _MaxRadius    ("Max Radius (m, clamps outliers)", Range(0.01, 1.0)) = 0.08
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        ZTest LEqual
        Cull Off
        LOD 100

        Pass
        {
            Name "URPForward"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float4 _Tint;
            float  _ScaleMul;
            float  _OpacityBoost;
            float  _Falloff;
            float  _MaxRadius;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color      : COLOR;
                float2 corner     : TEXCOORD0;
                float2 scaleXY    : TEXCOORD1;  // (scale_x, scale_y)
                float2 scaleZOp   : TEXCOORD2;  // (scale_z, opacity)
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float4 color       : COLOR;
                float2 uv          : TEXCOORD0;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float4 worldPos = mul(unity_ObjectToWorld, float4(v.positionOS.xyz, 1.0));
                float4 viewPos  = mul(UNITY_MATRIX_V, worldPos);

                // XGrids: rotation 없음 + 3-axis scale (max≈6.82m outlier 존재).
                // 등방 근사: 가장 작은 축을 특성 반경으로 사용 → outlier 폭주 억제.
                // 추가로 _MaxRadius 로 하드 클램프.
                // 등방 근사: 3축 기하평균 = 등가 등방 반경. 큰 outlier(6m) 영향 완화.
                float vol = max(v.scaleXY.x * v.scaleXY.y * v.scaleZOp.x, 1e-9);
                float charRadius = pow(vol, 0.3333333);
                float radius = min(charRadius * _ScaleMul, _MaxRadius);
                radius = max(radius, 0.003);

                viewPos.xy += v.corner * radius;
                o.positionHCS = mul(UNITY_MATRIX_P, viewPos);
                o.color = v.color * _Tint;
                o.color.a = saturate(v.scaleZOp.y + _OpacityBoost);
                o.uv = v.corner;
                return o;
            }

            float4 frag(Varyings i) : SV_Target
            {
                float r2 = dot(i.uv, i.uv);
                if (r2 > 1.0) discard;
                float falloff = exp(-_Falloff * r2);
                float4 c = i.color;
                c.a *= falloff;
                if (c.a < 0.003) discard;
                return c;
            }
            ENDHLSL
        }
    }

    // Built-in fallback
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        ZTest LEqual
        Cull Off
        LOD 100

        Pass
        {
            Tags { "LightMode"="ForwardBase" }
            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float4 _Tint; float _ScaleMul; float _OpacityBoost; float _Falloff; float _MaxRadius;
            struct Attributes { float4 positionOS:POSITION; float4 color:COLOR; float2 corner:TEXCOORD0; float2 scaleXY:TEXCOORD1; float2 scaleZOp:TEXCOORD2; };
            struct Varyings   { float4 positionHCS:SV_POSITION; float4 color:COLOR; float2 uv:TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float4 worldPos = mul(unity_ObjectToWorld, float4(v.positionOS.xyz, 1.0));
                float4 viewPos  = mul(UNITY_MATRIX_V, worldPos);
                // 등방 근사: 3축 기하평균 = 등가 등방 반경. 큰 outlier(6m) 영향 완화.
                float vol = max(v.scaleXY.x * v.scaleXY.y * v.scaleZOp.x, 1e-9);
                float charRadius = pow(vol, 0.3333333);
                float radius = min(charRadius * _ScaleMul, _MaxRadius);
                radius = max(radius, 0.003);
                viewPos.xy += v.corner * radius;
                o.positionHCS = mul(UNITY_MATRIX_P, viewPos);
                o.color = v.color * _Tint;
                o.color.a = saturate(v.scaleZOp.y + _OpacityBoost);
                o.uv = v.corner;
                return o;
            }
            float4 frag(Varyings i) : SV_Target
            {
                float r2 = dot(i.uv, i.uv);
                if (r2 > 1.0) discard;
                i.color.a *= exp(-_Falloff * r2);
                if (i.color.a < 0.003) discard;
                return i.color;
            }
            ENDHLSL
        }
    }

    FallBack Off
}
