#ifndef NEURAL_LAB_PCSS_INCLUDED
#define NEURAL_LAB_PCSS_INCLUDED

// Project-local URP fork: directional-light PCSS. Zero restores the original PCF path.
#define NEURAL_LAB_MAIN_LIGHT_PCSS 1
#define NEURAL_LAB_PCSS_ANGULAR_DIAMETER_DEGREES 0.53
#define NEURAL_LAB_PCSS_BLOCKER_SAMPLES 32
#define NEURAL_LAB_PCSS_FILTER_SAMPLES 32
#define NEURAL_LAB_PCSS_MAX_SEARCH_DISTANCE_METERS 50.0
#define NEURAL_LAB_PCSS_MAX_SEARCH_RADIUS_TEXELS 32.0
#define NEURAL_LAB_PCSS_MAX_FILTER_RADIUS_TEXELS 32.0
#define NEURAL_LAB_PCSS_BLOCKER_BIAS_METERS 0.01

// Exact atlas rectangles and world-to-shadow scales supplied by MainLightShadowCasterPass.
// Rect: minUV.xy, maxUV.zw. Scale: UV/metre.xy, device-depth/metre.z, active.w.
float4 _NeuralLabPCSSCascadeRects[MAX_SHADOW_CASCADES];
float4 _NeuralLabPCSSCascadeScales[MAX_SHADOW_CASCADES];

static const float2 k_NeuralLabPCSSDisk[32] =
{
    float2(0.0, 0.0),
    float2(0.12700013, 0.00000000),
    float2(-0.16219953, 0.14858805),
    float2(0.02482724, -0.28289357),
    float2(0.20444200, 0.26665839),
    float2(-0.37517621, -0.06636339),
    float2(0.35539966, -0.22607617),
    float2(-0.11887423, 0.44220621),
    float2(-0.22670605, -0.43650871),
    float2(0.49186145, 0.17962701),
    float2(-0.51169988, 0.21122227),
    float2(0.24667315, -0.52712620),
    float2(0.18228519, 0.58115390),
    float2(-0.54940967, -0.31839413),
    float2(0.64452008, -0.14169593),
    float2(-0.39334057, 0.55948649),
    float2(-0.09087078, -0.70124354),
    float2(0.55785746, 0.47016286),
    float2(-0.75070128, 0.03104385),
    float2(0.54757895, -0.54491420),
    float2(-0.03663515, 0.79226897),
    float2(-0.52102313, -0.62435985),
    float2(0.82535817, 0.11105077),
    float2(-0.69932431, 0.48657164),
    float2(0.19109563, -0.84943921),
    float2(0.44199424, 0.77133888),
    float2(-0.86405645, -0.27565757),
    float2(0.83932088, -0.38778753),
    float2(-0.36361478, 0.86883892),
    float2(-0.32451818, -0.90224320),
    float2(0.86350804, 0.45383562),
    float2(-0.95914016, 0.25282627)
};

float2 NeuralLabPCSSClampUV(float2 uv, float4 rect, float2 texelSize)
{
    return clamp(uv, rect.xy + texelSize * 0.5, rect.zw - texelSize * 0.5);
}

half NeuralLabSampleMainLightPCSS(float4 shadowCoord, ShadowSamplingData samplingData, half4 shadowParams)
{
    // Derivatives must be evaluated before per-fragment early returns.
    float3 dx = ddx(shadowCoord.xyz);
    float3 dy = ddy(shadowCoord.xyz);
    float determinant = dx.x * dy.y - dx.y * dy.x;
    float2 receiverGradient = 0.0;
    if (abs(determinant) > 1e-12)
        receiverGradient = float2(dy.y * dx.z - dx.y * dy.z,
                                 dx.x * dy.z - dy.x * dx.z) / determinant;

    if (BEYOND_SHADOW_FAR(shadowCoord) || shadowParams.x <= 0.0)
        return half(1.0);

    int cascadeIndex = -1;
    UNITY_UNROLL
    for (int cascade = 0; cascade < MAX_SHADOW_CASCADES; ++cascade)
    {
        float4 rect = _NeuralLabPCSSCascadeRects[cascade];
        if (_NeuralLabPCSSCascadeScales[cascade].w > 0.0 &&
            all(shadowCoord.xy >= rect.xy) && all(shadowCoord.xy < rect.zw))
            cascadeIndex = cascade;
    }
    // Preserve the native result for coordinates outside the active atlas slices.
    if (cascadeIndex < 0)
        return SampleShadowmap(TEXTURE2D_ARGS(_MainLightShadowmapTexture, sampler_LinearClampCompare),
                               shadowCoord, samplingData, shadowParams, false);

    // A quad crossing cascade tiles has discontinuous UV derivatives.
    float2 tileSpan = _NeuralLabPCSSCascadeRects[cascadeIndex].zw -
                      _NeuralLabPCSSCascadeRects[cascadeIndex].xy;
    if (any(abs(dx.xy) > tileSpan * 0.25) || any(abs(dy.xy) > tileSpan * 0.25))
        receiverGradient = 0.0;

    float4 rect = _NeuralLabPCSSCascadeRects[cascadeIndex];
    float3 scale = _NeuralLabPCSSCascadeScales[cascadeIndex].xyz;
    float depthPerMetre = max(scale.z, 1e-8);
    float2 texelSize = samplingData.shadowmapSize.xy;
    float receiverDepth = shadowCoord.z;
    float tanHalfAngle = tan(NEURAL_LAB_PCSS_ANGULAR_DIAMETER_DEGREES * 0.008726646259971648);

    // Directional-light projection is orthographic: device depth is affine in world distance.
#if UNITY_REVERSED_Z
    float distanceFromNear = (1.0 - receiverDepth) / depthPerMetre;
#else
    float distanceFromNear = receiverDepth / depthPerMetre;
#endif
    float searchDistance = min(max(distanceFromNear, 0.0), NEURAL_LAB_PCSS_MAX_SEARCH_DISTANCE_METERS);
    float2 searchRadius = min(searchDistance * tanHalfAngle * scale.xy,
                             NEURAL_LAB_PCSS_MAX_SEARCH_RADIUS_TEXELS * texelSize);
    float blockerDistanceSum = 0.0;
    float blockerCount = 0.0;
    float depthBias = NEURAL_LAB_PCSS_BLOCKER_BIAS_METERS * depthPerMetre;

    // 1. Search unfiltered depths; average only samples closer to the light than the receiver.
    UNITY_UNROLL
    for (int i = 0; i < NEURAL_LAB_PCSS_BLOCKER_SAMPLES; ++i)
    {
        float2 uv = NeuralLabPCSSClampUV(shadowCoord.xy + k_NeuralLabPCSSDisk[i] * searchRadius, rect, texelSize);
        float depth = SAMPLE_TEXTURE2D_LOD(_MainLightShadowmapTexture, sampler_PointClamp, uv, 0).r;
        float receiverPlaneDepth = receiverDepth + dot(receiverGradient, uv - shadowCoord.xy);
#if UNITY_REVERSED_Z
        float gap = depth - receiverPlaneDepth;
#else
        float gap = receiverPlaneDepth - depth;
#endif
        if (gap > depthBias)
        {
            blockerDistanceSum += gap / depthPerMetre;
            blockerCount += 1.0;
        }
    }
    if (blockerCount < 0.5)
        return half(1.0);

    // 2. For a directional area emitter, penumbra radius = blocker/receiver separation * tan(radius).
    float penumbraWorld = (blockerDistanceSum / blockerCount) * tanHalfAngle;
    float2 filterRadius = min(penumbraWorld * scale.xy,
                             NEURAL_LAB_PCSS_MAX_FILTER_RADIUS_TEXELS * texelSize);

    // 3. Variable-radius PCF, restricted to the same cascade tile.
    float attenuation = 0.0;
    UNITY_UNROLL
    for (int i = 0; i < NEURAL_LAB_PCSS_FILTER_SAMPLES; ++i)
    {
        float2 uv = NeuralLabPCSSClampUV(shadowCoord.xy + k_NeuralLabPCSSDisk[i] * filterRadius, rect, texelSize);
        float receiverPlaneDepth = receiverDepth + dot(receiverGradient, uv - shadowCoord.xy);
        attenuation += SAMPLE_TEXTURE2D_SHADOW(_MainLightShadowmapTexture, sampler_LinearClampCompare,
                                               float3(uv, receiverPlaneDepth));
    }
    attenuation /= NEURAL_LAB_PCSS_FILTER_SAMPLES;
    return half(LerpWhiteTo(attenuation, shadowParams.x));
}

#endif
