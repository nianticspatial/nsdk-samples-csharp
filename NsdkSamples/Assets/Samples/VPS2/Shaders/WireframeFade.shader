Shader "Custom/UniversalWireframe"
{
    Properties
    {
        [Header(Wireframe)]
        _WireColor ("Wire Color", Color) = (1,1,1,1)
        _WireThickness ("Wire Thickness", Range(0.01, 0.2)) = 0.05
        _WireScale ("Wire Scale", Range(1, 100)) = 20
        _WireIntensity ("Wire Intensity", Range(0,2)) = 1.0



        [Header(Base Appearance)]
        _BaseColor ("Base Color", Color) = (0.5,0.5,0.5,0.3)
        _UseVertexColor ("Use Vertex Color", Range(0,1)) = 1
        _MeshOpacity ("Mesh Opacity", Range(0,1)) = 0.3
        _LightenFactor ("Lighten Factor", Range(0,1)) = 0.7

        [Header(Texture Support)]
        _MainTex ("Texture", 2D) = "white" {}
        _TextureIntensity ("Texture Intensity", Range(0,1)) = 0.5

        [Header(Outline)]
        _OutlineWidth ("Outline Width", Range(0.0, 0.02)) = 0.005
        _OutlineColor ("Outline Color", Color) = (0,0,0,1)
        _OutlineViewAngleThreshold ("Outline View Angle Threshold", Range(0,1)) = 0.3
        _SilhouetteOnly ("Silhouette Only", Range(0,1)) = 0

        [Header(Transparency)]
        _Alpha ("Alpha", Range(0,1)) = 0.7

        [Header(Distance Fade)]
        _FadeStartDistance ("Fade Start Distance", Float) = 5.0
        _FadeEndDistance ("Fade End Distance", Float) = 10.0
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "RenderType"="Transparent"
            "IgnoreProjector"="True"
        }
        LOD 200

        // ================================================================
        // OUTLINE PASS - Creates subtle edge definition around mesh chunks
        // ================================================================
        Pass
        {
            Name "OUTLINE"
            // URP draws only the first untagged pass; distinct LightModes keep both,
            // drawn outline first.
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull Off
            ZWrite Off
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            // Outline-specific properties
            float _OutlineWidth;
            fixed4 _OutlineColor;
            float _FadeStartDistance;
            float _FadeEndDistance;
            float _OutlineViewAngleThreshold;
            float _SilhouetteOnly;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 worldPos : TEXCOORD0;
                float3 worldNormal : TEXCOORD1;
                float3 viewDir : TEXCOORD2;
            };

            v2f vert(appdata v)
            {
                v2f o;

                // Transform to world space
                float3 worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                float3 worldNormal = normalize(mul((float3x3)unity_ObjectToWorld, v.normal));

                // Calculate view direction for silhouette detection
                float3 viewDir = normalize(_WorldSpaceCameraPos - worldPos);

                // Expand vertex along normal for outline effect
                worldPos += worldNormal * _OutlineWidth;

                o.pos = mul(UNITY_MATRIX_VP, float4(worldPos, 1.0));
                o.worldPos = worldPos;
                o.worldNormal = worldNormal;
                o.viewDir = viewDir;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // Calculate distance from camera
                float distance = length(_WorldSpaceCameraPos - i.worldPos);

                // Calculate distance-based fade
                float distanceFade = 1.0 - smoothstep(_FadeStartDistance, _FadeEndDistance, distance);

                // If completely faded, discard the pixel
                if (distanceFade <= 0.001)
                    discard;

                // ================================
                // SILHOUETTE DETECTION - Reduces outline noise for photogrammetry
                // ================================
                float viewDot = abs(dot(normalize(i.worldNormal), normalize(i.viewDir)));

                // If silhouette-only mode is enabled, only show outlines on edges
                if (_SilhouetteOnly > 0.5)
                {
                    // Only render outline on silhouette edges (where normal is perpendicular to view)
                    if (viewDot > _OutlineViewAngleThreshold)
                        discard;
                }
                else
                {
                    // In normal mode, still reduce outline intensity based on view angle
                    float silhouetteFactor = 1.0 - smoothstep(0.0, _OutlineViewAngleThreshold, viewDot);
                    distanceFade *= lerp(1.0, silhouetteFactor, 0.7); // Blend between full and silhouette
                }

                // Apply distance fade to outline
                fixed4 outlineColor = _OutlineColor;
                float baseOutlineAlpha = outlineColor.a;
                outlineColor.a = baseOutlineAlpha * distanceFade;

                // Don't render nearly transparent outline pixels
                if (outlineColor.a < 0.01)
                    discard;

                return outlineColor;
            }
            ENDCG
        }

        // ================================================================
        // MAIN PASS - Renders textured mesh with wireframe overlay
        // ================================================================
        Pass
        {
            Name "MAIN"
            Tags { "LightMode"="UniversalForward" }
            Cull Off
            ZWrite Off
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 worldPos : TEXCOORD0;
                float3 worldNormal : TEXCOORD1;
                float4 vertexColor : COLOR;
                float2 uv : TEXCOORD2;
            };

            // Wireframe properties
            fixed4 _WireColor;
            float _WireThickness;
            float _WireScale;
            float _WireIntensity;



            // Base appearance properties
            fixed4 _BaseColor;
            float _UseVertexColor;
            float _MeshOpacity;
            float _LightenFactor;

            // Texture properties
            sampler2D _MainTex;
            float4 _MainTex_ST;
            float _TextureIntensity;

            // Transparency properties
            float _Alpha;

            // Distance fade properties
            float _FadeStartDistance;
            float _FadeEndDistance;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.worldNormal = normalize(mul((float3x3)unity_ObjectToWorld, v.normal));
                o.vertexColor = v.color;
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // ================================
                // DISTANCE FADE CALCULATION
                // ================================
                float distance = length(_WorldSpaceCameraPos - i.worldPos);
                float distanceFade = 1.0 - smoothstep(_FadeStartDistance, _FadeEndDistance, distance);

                // Early discard for completely faded pixels
                if (distanceFade <= 0.001)
                    discard;

                                // ================================
                // WIREFRAME PATTERN GENERATION
                // ================================
                // Create 3D grid pattern using world coordinates
                float3 worldGrid = i.worldPos * _WireScale;

                // Calculate wireframe lines in X, Y, and Z directions
                float wireX = abs(frac(worldGrid.x) - 0.5) * 2.0;
                float wireY = abs(frac(worldGrid.y) - 0.5) * 2.0;
                float wireZ = abs(frac(worldGrid.z) - 0.5) * 2.0;

                // Find closest grid line (creates intersecting pattern)
                float wire = min(wireX, min(wireY, wireZ));

                // Apply thickness and smoothing to wireframe
                float wireFactor = 1.0 - smoothstep(_WireThickness * 0.5, _WireThickness, wire);
                wireFactor = saturate(wireFactor * _WireIntensity);

                // ================================
                // BASE COLOR CALCULATION
                // ================================
                fixed4 baseColor = _BaseColor;

                // Apply vertex color if enabled
                if (_UseVertexColor > 0.5)
                {
                    baseColor = lerp(baseColor, i.vertexColor, _UseVertexColor);
                }

                // Apply texture if provided
                fixed4 texColor = tex2D(_MainTex, i.uv);
                baseColor = lerp(baseColor, texColor, _TextureIntensity);

                // Lighten the base color to make mesh less visually heavy
                baseColor.rgb = lerp(baseColor.rgb, float3(1,1,1), _LightenFactor);

                // Set base mesh opacity
                baseColor.a = _MeshOpacity;

                // ================================
                // COLOR AND ALPHA BLENDING
                // ================================
                // Blend base color with wireframe color (RGB only)
                fixed4 finalColor;
                finalColor.rgb = lerp(baseColor.rgb, _WireColor.rgb, wireFactor);

                // Calculate final alpha: combine base mesh opacity with wireframe visibility
                float meshAlpha = _MeshOpacity * _Alpha;
                float wireframeBoost = wireFactor * 0.3; // Add opacity where wireframe is present
                float baseAlpha = meshAlpha + wireframeBoost;
                finalColor.a = baseAlpha * distanceFade; // Apply distance fade

                // Discard nearly transparent pixels to prevent black artifacts
                if (finalColor.a < 0.01)
                    discard;

                return finalColor;
            }
            ENDCG
        }
    }

    Fallback "Transparent/Diffuse"
}
