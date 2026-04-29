using UnityEngine;

namespace AdaptiveLightVolumes {

    public enum LightType {
        Point,
        Spot,
        Area
    }

    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("Lighting/Adaptive Light Volumes/Baked Shadowed Light")]
    public class BakedShadowedLight : MonoBehaviour {

        [Header("Common")]
        public LightType Type = LightType.Point;

        [ColorUsage(showAlpha: false, hdr: true)]
        public Color Color = Color.white;

        [Min(0f)] public float Intensity = 1f;
        [Min(0f)] public float Range = 10f;
        [Min(0.1f)] public float FalloffExponent = 2f;

        [Header("Spot (Type = Spot)")]
        [Range(1f, 179f)] public float SpotOuterAngle = 60f;
        [Range(0f, 179f)] public float SpotInnerAngle = 45f;
        [Tooltip("Optional projected cookie texture. Mapped onto the cone footprint; pixels outside the cone are clamped to the texture border. Set Wrap Mode = Clamp on the texture for clean edges.")]
        public Texture2D Cookie;

        [Header("Area (Type = Area)")]
        [Tooltip("Width and height of the rectangular area light, in meters.")]
        public Vector2 AreaSize = new Vector2(2f, 2f);

        [Tooltip("If true, the rectangle emits from both faces (useful for hanging emissive panels). If false, only the +Z (transform.forward) face emits and receivers behind the rect's plane are skipped.")]
        public bool TwoSided = false;

        [Header("Occlusion Bake")]
        [Tooltip("World-space size of the occlusion volume baked around this light.")]
        public Vector3 BakeExtents = new Vector3(10f, 10f, 10f);

        [Tooltip("Voxel resolution of the baked occlusion Texture3D.")]
        public Vector3Int BakeResolution = new Vector3Int(32, 32, 32);

        [Tooltip("Layers considered as occluders during the bake. Typically the static-environment layer; defaults to all.")]
        public LayerMask OccluderLayers = -1;

        [Tooltip("Number of jittered samples per voxel used to soften shadow edges. 1 = hard binary shadows.")]
        [Range(1, 64)] public int ShadowSamples = 16;

        [Tooltip("Physical radius of the light source for shadow softening. Larger values produce softer penumbras. 0 = hard shadows regardless of sample count.")]
        [Min(0f)] public float ShadowRadius = 0.1f;

        [Tooltip("Use hardware ray tracing to bake occlusion against scene MeshRenderers when supported. Falls back to CPU RaycastCommand against Colliders if not available.")]
        public bool UseHardwareRT = true;

        [Tooltip("Texture3D produced by OcclusionVolumeBaker. Assigned at bake time; sampled at runtime by the shader.")]
        public Texture3D BakedOcclusion;

        public Bounds GetBakeBoundsWorld() {
            return new Bounds(transform.position, BakeExtents);
        }

        private void OnEnable() {
            BakedLightOcclusionManager.Register(this);
        }

        private void OnDisable() {
            BakedLightOcclusionManager.Unregister(this);
        }

        private void OnValidate() {
            if (SpotInnerAngle > SpotOuterAngle) SpotInnerAngle = SpotOuterAngle;
        }

        private void OnDrawGizmosSelected() {
            Gizmos.color = new Color(1f, 0.85f, 0.3f, 0.3f);
            Gizmos.DrawWireCube(transform.position, BakeExtents);

            Gizmos.color = new Color(1f, 0.85f, 0.3f, 1f);
            switch (Type) {
                case LightType.Point: DrawPointGizmo(); break;
                case LightType.Spot:  DrawSpotGizmo();  break;
                case LightType.Area:  DrawAreaGizmo();  break;
            }
        }

        private void DrawPointGizmo() {
            Gizmos.DrawWireSphere(transform.position, Range);
        }

        private void DrawSpotGizmo() {
            Vector3 origin = transform.position;
            Vector3 forward = transform.forward;
            float halfOuter = SpotOuterAngle * 0.5f * Mathf.Deg2Rad;
            float radius = Range * Mathf.Tan(halfOuter);
            Vector3 tip = origin + forward * Range;
            Gizmos.DrawLine(origin, tip + transform.right * radius);
            Gizmos.DrawLine(origin, tip - transform.right * radius);
            Gizmos.DrawLine(origin, tip + transform.up * radius);
            Gizmos.DrawLine(origin, tip - transform.up * radius);
            Gizmos.DrawWireSphere(tip, radius);
        }

        private void DrawAreaGizmo() {
            var prev = Gizmos.matrix;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(AreaSize.x, AreaSize.y, 0f));
            Gizmos.matrix = prev;
            Gizmos.DrawLine(transform.position, transform.position + transform.forward * Range);
        }
    }
}
