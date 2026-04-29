using System.Collections.Generic;
using UnityEngine;

namespace AdaptiveLightVolumes {

    [ExecuteAlways]
    [DefaultExecutionOrder(100)]
    [AddComponentMenu("Lighting/Adaptive Light Volumes/Baked Light Occlusion Manager")]
    public class BakedLightOcclusionManager : MonoBehaviour {

        public const int MaxLights = 8;

        private static BakedLightOcclusionManager s_Instance;
        private static readonly List<BakedShadowedLight> s_Lights = new List<BakedShadowedLight>(MaxLights);

        private static readonly int s_CountID         = Shader.PropertyToID("_ALV_LightCount");
        private static readonly int s_TypesID         = Shader.PropertyToID("_ALV_LightTypes");
        private static readonly int s_PositionsID     = Shader.PropertyToID("_ALV_LightPositions");
        private static readonly int s_DirectionsID    = Shader.PropertyToID("_ALV_LightDirections");
        private static readonly int s_ColorsID        = Shader.PropertyToID("_ALV_LightColors");
        private static readonly int s_RangesID        = Shader.PropertyToID("_ALV_LightRanges");
        private static readonly int s_SpotParamsID    = Shader.PropertyToID("_ALV_LightSpotParams");
        private static readonly int s_AreaParamsID    = Shader.PropertyToID("_ALV_LightAreaParams");
        private static readonly int s_BoundsMinID     = Shader.PropertyToID("_ALV_OcclusionBoundsMin");
        private static readonly int s_BoundsMaxID     = Shader.PropertyToID("_ALV_OcclusionBoundsMax");
        private static readonly int s_WorldToLightID  = Shader.PropertyToID("_ALV_WorldToLight");
        private static readonly int s_LightToWorldID  = Shader.PropertyToID("_ALV_LightToWorld");

        private static readonly int[] s_OcclusionTexIDs = BuildTexIDs("_ALV_OcclusionTex");
        private static readonly int[] s_CookieTexIDs    = BuildTexIDs("_ALV_CookieTex");

        private static readonly Vector4[] s_Types       = new Vector4[MaxLights];
        private static readonly Vector4[] s_Positions   = new Vector4[MaxLights];
        private static readonly Vector4[] s_Directions  = new Vector4[MaxLights];
        private static readonly Vector4[] s_Colors      = new Vector4[MaxLights];
        private static readonly Vector4[] s_Ranges      = new Vector4[MaxLights];
        private static readonly Vector4[] s_SpotParams  = new Vector4[MaxLights];
        private static readonly Vector4[] s_AreaParams  = new Vector4[MaxLights];
        private static readonly Vector4[] s_BoundsMin   = new Vector4[MaxLights];
        private static readonly Vector4[] s_BoundsMax   = new Vector4[MaxLights];
        private static readonly Matrix4x4[] s_WorldToLight = new Matrix4x4[MaxLights];
        private static readonly Matrix4x4[] s_LightToWorld = new Matrix4x4[MaxLights];

        private static int[] BuildTexIDs(string prefix) {
            var ids = new int[MaxLights];
            for (int i = 0; i < MaxLights; i++) ids[i] = Shader.PropertyToID($"{prefix}{i}");
            return ids;
        }

        public static void Register(BakedShadowedLight light) {
            if (light != null && !s_Lights.Contains(light)) s_Lights.Add(light);
        }

        public static void Unregister(BakedShadowedLight light) {
            s_Lights.Remove(light);
        }

        public static IReadOnlyList<BakedShadowedLight> RegisteredLights => s_Lights;

        private void OnEnable() {
            if (s_Instance != null && s_Instance != this) {
                Debug.LogWarning($"[ALV] Multiple BakedLightOcclusionManager instances; '{s_Instance.name}' is already active.", this);
                return;
            }
            s_Instance = this;
        }

        private void OnDisable() {
            if (s_Instance == this) s_Instance = null;
        }

        private void LateUpdate() {
            PushGlobals();
        }

        public static void PushGlobals() {
            int count = 0;
            for (int i = 0; i < s_Lights.Count && count < MaxLights; i++) {
                var l = s_Lights[i];
                if (l == null || !l.isActiveAndEnabled) continue;

                Vector3 p = l.transform.position;
                Vector3 fwd = l.transform.forward;

                s_Types[count]      = new Vector4((float)l.Type, 0f, 0f, 0f);
                s_Positions[count]  = new Vector4(p.x, p.y, p.z, 0f);
                s_Directions[count] = new Vector4(fwd.x, fwd.y, fwd.z, 0f);

                Color rgb = l.Color * l.Intensity;
                s_Colors[count] = new Vector4(rgb.r, rgb.g, rgb.b, l.FalloffExponent);

                s_Ranges[count] = new Vector4(l.Range, l.Range > 0f ? 1f / l.Range : 0f, 0f, 0f);

                float cosOuter = Mathf.Cos(l.SpotOuterAngle * 0.5f * Mathf.Deg2Rad);
                float cosInner = Mathf.Cos(l.SpotInnerAngle * 0.5f * Mathf.Deg2Rad);
                s_SpotParams[count] = new Vector4(cosOuter, cosInner, 0f, 0f);

                s_AreaParams[count] = new Vector4(l.AreaSize.x * 0.5f, l.AreaSize.y * 0.5f, 0f, 0f);

                Bounds b = l.GetBakeBoundsWorld();
                s_BoundsMin[count] = b.min;
                s_BoundsMax[count] = b.max;

                s_WorldToLight[count] = l.transform.worldToLocalMatrix;
                s_LightToWorld[count] = l.transform.localToWorldMatrix;

                Shader.SetGlobalTexture(s_OcclusionTexIDs[count], l.BakedOcclusion != null ? (Texture)l.BakedOcclusion : Texture2D.whiteTexture);
                Shader.SetGlobalTexture(s_CookieTexIDs[count], l.Cookie != null ? (Texture)l.Cookie : Texture2D.whiteTexture);

                count++;
            }

            Shader.SetGlobalFloat(s_CountID, count);
            Shader.SetGlobalVectorArray(s_TypesID, s_Types);
            Shader.SetGlobalVectorArray(s_PositionsID, s_Positions);
            Shader.SetGlobalVectorArray(s_DirectionsID, s_Directions);
            Shader.SetGlobalVectorArray(s_ColorsID, s_Colors);
            Shader.SetGlobalVectorArray(s_RangesID, s_Ranges);
            Shader.SetGlobalVectorArray(s_SpotParamsID, s_SpotParams);
            Shader.SetGlobalVectorArray(s_AreaParamsID, s_AreaParams);
            Shader.SetGlobalVectorArray(s_BoundsMinID, s_BoundsMin);
            Shader.SetGlobalVectorArray(s_BoundsMaxID, s_BoundsMax);
            Shader.SetGlobalMatrixArray(s_WorldToLightID, s_WorldToLight);
            Shader.SetGlobalMatrixArray(s_LightToWorldID, s_LightToWorld);
        }
    }
}
