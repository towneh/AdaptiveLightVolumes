#ifndef ADAPTIVELIGHTVOLUMES_INCLUDED
#define ADAPTIVELIGHTVOLUMES_INCLUDED

// Adaptive Light Volumes - shader-side evaluator.
//
// V1 ships only the Point-light path. Spot and Area are scaffolded in the data
// model and pushed as globals by BakedLightOcclusionManager, but the evaluator
// branches below are intentionally TODO until those types are implemented.
//
// Designed to be referenced from a Shader Graph Custom Function Node:
//   - Source: this file
//   - Name:   EvaluateAdaptiveLights_float
//   - Inputs: float3 PositionWS, float3 NormalWS
//   - Output: float3 LightContribution

#define ALV_MAX_LIGHTS 8

float    _ALV_LightCount;
float4   _ALV_LightTypes[ALV_MAX_LIGHTS];        // x = type (0=point, 1=spot, 2=area)
float4   _ALV_LightPositions[ALV_MAX_LIGHTS];    // xyz = world position
float4   _ALV_LightDirections[ALV_MAX_LIGHTS];   // xyz = world forward
float4   _ALV_LightColors[ALV_MAX_LIGHTS];       // rgb = color * intensity, a = falloff exponent
float4   _ALV_LightRanges[ALV_MAX_LIGHTS];       // x = range, y = 1/range
float4   _ALV_LightSpotParams[ALV_MAX_LIGHTS];   // x = cos(outer/2), y = cos(inner/2)
float4   _ALV_LightAreaParams[ALV_MAX_LIGHTS];   // x = halfWidth, y = halfHeight
float4   _ALV_OcclusionBoundsMin[ALV_MAX_LIGHTS];
float4   _ALV_OcclusionBoundsMax[ALV_MAX_LIGHTS];
float4x4 _ALV_WorldToLight[ALV_MAX_LIGHTS];

// All ALV textures share sampler_LinearClamp (declared by URP's Common.hlsl,
// reachable from Core.hlsl). ps_4_0 caps at 16 samplers; with 8 occlusion +
// 8 cookie textures plus consumer-shader samplers (e.g. _BaseMap), giving
// each texture its own sampler exceeds the limit. The consumer shader is
// expected to include URP Core.hlsl before this file.
Texture3D    _ALV_OcclusionTex0;
Texture3D    _ALV_OcclusionTex1;
Texture3D    _ALV_OcclusionTex2;
Texture3D    _ALV_OcclusionTex3;
Texture3D    _ALV_OcclusionTex4;
Texture3D    _ALV_OcclusionTex5;
Texture3D    _ALV_OcclusionTex6;
Texture3D    _ALV_OcclusionTex7;

Texture2D    _ALV_CookieTex0;
Texture2D    _ALV_CookieTex1;
Texture2D    _ALV_CookieTex2;
Texture2D    _ALV_CookieTex3;
Texture2D    _ALV_CookieTex4;
Texture2D    _ALV_CookieTex5;
Texture2D    _ALV_CookieTex6;
Texture2D    _ALV_CookieTex7;

float3 _ALV_SampleCookie(int lightIdx, float2 uv) {
    if (lightIdx == 0) return _ALV_CookieTex0.SampleLevel(sampler_LinearClamp, uv, 0).rgb;
    if (lightIdx == 1) return _ALV_CookieTex1.SampleLevel(sampler_LinearClamp, uv, 0).rgb;
    if (lightIdx == 2) return _ALV_CookieTex2.SampleLevel(sampler_LinearClamp, uv, 0).rgb;
    if (lightIdx == 3) return _ALV_CookieTex3.SampleLevel(sampler_LinearClamp, uv, 0).rgb;
    if (lightIdx == 4) return _ALV_CookieTex4.SampleLevel(sampler_LinearClamp, uv, 0).rgb;
    if (lightIdx == 5) return _ALV_CookieTex5.SampleLevel(sampler_LinearClamp, uv, 0).rgb;
    if (lightIdx == 6) return _ALV_CookieTex6.SampleLevel(sampler_LinearClamp, uv, 0).rgb;
    return _ALV_CookieTex7.SampleLevel(sampler_LinearClamp, uv, 0).rgb;
}

float _ALV_SampleOcclusion(int lightIdx, float3 positionWS) {
    float3 minB = _ALV_OcclusionBoundsMin[lightIdx].xyz;
    float3 maxB = _ALV_OcclusionBoundsMax[lightIdx].xyz;
    float3 uvw = (positionWS - minB) / max(maxB - minB, 1e-5);
    if (any(uvw < 0.0) || any(uvw > 1.0)) {
        return 1.0; // outside the bake volume = treat as unoccluded
    }

    // Texture3D bindings are fixed-slot; the manager pushes per-light textures
    // to _ALV_OcclusionTex0..7. A loop with dynamic index won't sample dynamically
    // through Texture3D handles in HLSL, so we branch.
    if (lightIdx == 0) return _ALV_OcclusionTex0.SampleLevel(sampler_LinearClamp, uvw, 0).r;
    if (lightIdx == 1) return _ALV_OcclusionTex1.SampleLevel(sampler_LinearClamp, uvw, 0).r;
    if (lightIdx == 2) return _ALV_OcclusionTex2.SampleLevel(sampler_LinearClamp, uvw, 0).r;
    if (lightIdx == 3) return _ALV_OcclusionTex3.SampleLevel(sampler_LinearClamp, uvw, 0).r;
    if (lightIdx == 4) return _ALV_OcclusionTex4.SampleLevel(sampler_LinearClamp, uvw, 0).r;
    if (lightIdx == 5) return _ALV_OcclusionTex5.SampleLevel(sampler_LinearClamp, uvw, 0).r;
    if (lightIdx == 6) return _ALV_OcclusionTex6.SampleLevel(sampler_LinearClamp, uvw, 0).r;
    return _ALV_OcclusionTex7.SampleLevel(sampler_LinearClamp, uvw, 0).r;
}

float3 _ALV_EvaluatePointLight(int i, float3 positionWS, float3 normalWS) {
    float3 lp       = _ALV_LightPositions[i].xyz;
    float3 toLight  = lp - positionWS;
    float  dist     = length(toLight);
    float  range    = _ALV_LightRanges[i].x;
    if (dist > range || dist <= 0.0) return 0.0;

    float3 L        = toLight / dist;
    float  NoL      = saturate(dot(normalWS, L));
    if (NoL <= 0.0) return 0.0;

    float  distNorm    = saturate(dist * _ALV_LightRanges[i].y);
    float  falloffExp  = _ALV_LightColors[i].a;
    float  falloff     = pow(1.0 - distNorm, falloffExp);

    float  occlusion   = _ALV_SampleOcclusion(i, positionWS);

    return _ALV_LightColors[i].rgb * (NoL * falloff * occlusion);
}

float3 _ALV_EvaluateSpotLight(int i, float3 positionWS, float3 normalWS) {
    float3 lp       = _ALV_LightPositions[i].xyz;
    float3 toLight  = lp - positionWS;
    float  dist     = length(toLight);
    float  range    = _ALV_LightRanges[i].x;
    if (dist > range || dist <= 0.0) return 0.0;

    float3 L        = toLight / dist;
    float  NoL      = saturate(dot(normalWS, L));
    if (NoL <= 0.0) return 0.0;

    // Cone falloff: smoothstep between cosInner (full intensity) and cosOuter (0).
    // cosTheta is the angle between the light's forward and the direction from light
    // to the surface point; receivers in front of the spot have cosTheta > 0.
    float3 lightFwd = _ALV_LightDirections[i].xyz;
    float  cosTheta = dot(lightFwd, -L);
    float  cosOuter = _ALV_LightSpotParams[i].x;
    float  cosInner = _ALV_LightSpotParams[i].y;
    if (cosTheta < cosOuter) return 0.0;

    float  invCosRange = 1.0 / max(cosInner - cosOuter, 1e-5);
    float  cone        = saturate((cosTheta - cosOuter) * invCosRange);
    cone               = cone * cone;

    float  distNorm    = saturate(dist * _ALV_LightRanges[i].y);
    float  falloffExp  = _ALV_LightColors[i].a;
    float  falloff     = pow(1.0 - distNorm, falloffExp);

    float  occlusion   = _ALV_SampleOcclusion(i, positionWS);

    // Cookie projection: transform world pos into the light's local space and
    // project onto a plane at z=1, mapping the outer cone footprint to UV [0,1].
    // Manager binds Texture2D.whiteTexture when no cookie is set, so this is a
    // no-op multiply unless the user has assigned one.
    float3 localPos    = mul(_ALV_WorldToLight[i], float4(positionWS, 1.0)).xyz;
    float  invCosOuter = 1.0 / max(cosOuter, 1e-5);
    float  tanOuter    = sqrt(saturate(1.0 - cosOuter * cosOuter)) * invCosOuter;
    float2 projXY      = localPos.xy / max(localPos.z, 1e-4);
    float2 cookieUV    = saturate(projXY * (0.5 / max(tanOuter, 1e-5)) + 0.5);
    float3 cookie      = _ALV_SampleCookie(i, cookieUV);

    return _ALV_LightColors[i].rgb * cookie * (NoL * falloff * cone * occlusion);
}

void EvaluateAdaptiveLights_float(float3 PositionWS, float3 NormalWS, out float3 LightContribution) {
    float3 total = 0.0;
    int count = (int)_ALV_LightCount;
    [loop] for (int i = 0; i < count; i++) {
        float type = _ALV_LightTypes[i].x;
        if (type < 0.5) {
            total += _ALV_EvaluatePointLight(i, PositionWS, NormalWS);
        } else if (type < 1.5) {
            total += _ALV_EvaluateSpotLight(i, PositionWS, NormalWS);
        }
        // TODO: type < 2.5 -> Area evaluator (rectangular Lambert / LTC integration)
    }
    LightContribution = total;
}

#endif // ADAPTIVELIGHTVOLUMES_INCLUDED
