using Unity.Collections;
using UnityEditor;
using UnityEngine;

namespace AdaptiveLightVolumes.Editor {

    public static class OcclusionVolumeBaker {

        public const string GeneratedFolder = "Assets/ALV-Generated";

        // Small offset shaved off the ray distance so a voxel adjacent to the light
        // doesn't self-hit a collider co-located with the light source.
        private const float RayEndEpsilon = 0.01f;

        [MenuItem("CONTEXT/BakedShadowedLight/Bake Occlusion Volume")]
        private static void BakeFromMenu(MenuCommand cmd) {
            Bake((BakedShadowedLight)cmd.context);
        }

        public static void Bake(BakedShadowedLight light) {
            if (light == null) return;

            var res = light.BakeResolution;
            if (res.x <= 0 || res.y <= 0 || res.z <= 0) {
                Debug.LogError($"[ALV] Invalid bake resolution {res} on '{light.name}'.", light);
                return;
            }

            int voxelCount = res.x * res.y * res.z;
            Bounds bounds = light.GetBakeBoundsWorld();
            Vector3 lightPos = light.transform.position;

            Vector3 cellSize = new Vector3(
                bounds.size.x / res.x,
                bounds.size.y / res.y,
                bounds.size.z / res.z);
            Vector3 originVoxel = bounds.min + cellSize * 0.5f;

            var queryParams = new QueryParameters(
                layerMask: light.OccluderLayers,
                hitMultipleFaces: false,
                hitTriggers: QueryTriggerInteraction.Ignore,
                hitBackfaces: false);

            var commands = new NativeArray<RaycastCommand>(voxelCount, Allocator.TempJob);
            var hits = new NativeArray<RaycastHit>(voxelCount, Allocator.TempJob);

            try {
                int idx = 0;
                for (int z = 0; z < res.z; z++) {
                    for (int y = 0; y < res.y; y++) {
                        for (int x = 0; x < res.x; x++) {
                            Vector3 voxel = originVoxel + new Vector3(x * cellSize.x, y * cellSize.y, z * cellSize.z);
                            Vector3 toLight = lightPos - voxel;
                            float dist = toLight.magnitude;
                            Vector3 dir = dist > 0f ? toLight / dist : Vector3.up;
                            commands[idx++] = new RaycastCommand(
                                voxel,
                                dir,
                                queryParams,
                                Mathf.Max(0f, dist - RayEndEpsilon));
                        }
                    }
                }

                var handle = RaycastCommand.ScheduleBatch(commands, hits, 256, 1);
                handle.Complete();

                var data = new Color32[voxelCount];
                int occludedCount = 0;
                for (int i = 0; i < voxelCount; i++) {
                    bool occluded = hits[i].colliderInstanceID != 0;
                    data[i] = occluded ? new Color32(0, 0, 0, 0) : new Color32(255, 0, 0, 0);
                    if (occluded) occludedCount++;
                }

                var tex = new Texture3D(res.x, res.y, res.z, TextureFormat.R8, mipChain: false) {
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                    name = $"{light.name}_OcclusionVolume"
                };
                tex.SetPixels32(data);
                tex.Apply(updateMipmaps: false, makeNoLongerReadable: true);

                SaveAndAssign(light, tex);

                float occludedPct = 100f * occludedCount / voxelCount;
                Debug.Log($"[ALV] Baked '{light.name}' — {voxelCount} voxels, {occludedPct:F1}% occluded.", light);
            } finally {
                if (commands.IsCreated) commands.Dispose();
                if (hits.IsCreated) hits.Dispose();
            }
        }

        private static void SaveAndAssign(BakedShadowedLight light, Texture3D tex) {
            if (!AssetDatabase.IsValidFolder(GeneratedFolder)) {
                AssetDatabase.CreateFolder("Assets", System.IO.Path.GetFileName(GeneratedFolder));
            }

            var scene = light.gameObject.scene;
            var sceneName = string.IsNullOrEmpty(scene.name) ? "Untitled" : scene.name;
            var assetPath = $"{GeneratedFolder}/{sceneName}_{light.GetInstanceID()}_occlusion.asset";

            var existing = AssetDatabase.LoadAssetAtPath<Texture3D>(assetPath);
            if (existing != null) {
                EditorUtility.CopySerialized(tex, existing);
                light.BakedOcclusion = existing;
            } else {
                AssetDatabase.CreateAsset(tex, assetPath);
                light.BakedOcclusion = tex;
            }

            EditorUtility.SetDirty(light);
            AssetDatabase.SaveAssets();
        }
    }
}
