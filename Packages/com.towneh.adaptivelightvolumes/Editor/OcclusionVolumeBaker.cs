using System;
using Unity.Collections;
using UnityEditor;
using UnityEngine;

namespace AdaptiveLightVolumes.Editor {

    public static class OcclusionVolumeBaker {

        public const string GeneratedFolder = "Assets/ALV-Generated";

        // Small offset shaved off the ray distance so a voxel adjacent to the light
        // doesn't self-hit a collider co-located with the light source.
        private const float RayEndEpsilon = 0.01f;

        // Rays-per-chunk for cancellable progress dispatch.
        // 16k keeps each ScheduleBatch.Complete() fast enough for snappy cancel.
        private const int RaysPerChunk = 16384;

        // Progress callback signature: (progress01, statusMessage) -> shouldCancel
        public delegate bool ProgressCallback(float progress01, string status);

        [MenuItem("CONTEXT/BakedShadowedLight/Bake Occlusion Volume")]
        private static void BakeFromMenu(MenuCommand cmd) {
            try {
                Bake((BakedShadowedLight)cmd.context, DefaultProgress);
            } finally {
                EditorUtility.ClearProgressBar();
            }
        }

        private static bool DefaultProgress(float progress, string status) {
            return EditorUtility.DisplayCancelableProgressBar("Baking Adaptive Light Volume", status, progress);
        }

        /// <summary>
        /// Bake an occlusion Texture3D for the given light.
        /// Returns true if completed, false if canceled by the progress callback.
        /// </summary>
        public static bool Bake(BakedShadowedLight light, ProgressCallback progress = null) {
            if (light == null) return false;

            var res = light.BakeResolution;
            if (res.x <= 0 || res.y <= 0 || res.z <= 0) {
                Debug.LogError($"[ALV] Invalid bake resolution {res} on '{light.name}'.", light);
                return false;
            }

            int voxelCount = res.x * res.y * res.z;
            int sampleCount = (light.ShadowRadius > 0f) ? Mathf.Max(1, light.ShadowSamples) : 1;
            int totalRays = voxelCount * sampleCount;

            Bounds bounds = light.GetBakeBoundsWorld();
            Vector3 lightPos = light.transform.position;

            Vector3 cellSize = new Vector3(
                bounds.size.x / res.x,
                bounds.size.y / res.y,
                bounds.size.z / res.z);
            Vector3 originVoxel = bounds.min + cellSize * 0.5f;

            Vector3[] sampleOffsets = GenerateSampleOffsets(sampleCount, light.ShadowRadius, light.GetInstanceID());

            var queryParams = new QueryParameters(
                layerMask: light.OccluderLayers,
                hitMultipleFaces: false,
                hitTriggers: QueryTriggerInteraction.Ignore,
                hitBackfaces: false);

            var commands = new NativeArray<RaycastCommand>(totalRays, Allocator.TempJob);
            var hits = new NativeArray<RaycastHit>(totalRays, Allocator.TempJob);

            try {
                int idx = 0;
                for (int z = 0; z < res.z; z++) {
                    for (int y = 0; y < res.y; y++) {
                        for (int x = 0; x < res.x; x++) {
                            Vector3 voxel = originVoxel + new Vector3(x * cellSize.x, y * cellSize.y, z * cellSize.z);
                            for (int s = 0; s < sampleCount; s++) {
                                Vector3 target = lightPos + sampleOffsets[s];
                                Vector3 toLight = target - voxel;
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
                }

                for (int rayStart = 0; rayStart < totalRays; rayStart += RaysPerChunk) {
                    int count = Mathf.Min(RaysPerChunk, totalRays - rayStart);
                    var cmdSlice = commands.GetSubArray(rayStart, count);
                    var hitSlice = hits.GetSubArray(rayStart, count);
                    RaycastCommand.ScheduleBatch(cmdSlice, hitSlice, 256, 1).Complete();

                    if (progress != null) {
                        float p = (float)(rayStart + count) / totalRays;
                        bool canceled = progress(p, $"'{light.name}' — {rayStart + count}/{totalRays} rays");
                        if (canceled) return false;
                    }
                }

                var data = new Color32[voxelCount];
                int totalOccludedSamples = 0;
                for (int v = 0; v < voxelCount; v++) {
                    int visible = 0;
                    int baseIdx = v * sampleCount;
                    for (int s = 0; s < sampleCount; s++) {
                        if (hits[baseIdx + s].colliderInstanceID == 0) visible++;
                    }
                    byte occl = (byte)((visible * 255) / sampleCount);
                    data[v] = new Color32(occl, 0, 0, 0);
                    totalOccludedSamples += sampleCount - visible;
                }

                var tex = new Texture3D(res.x, res.y, res.z, TextureFormat.R8, mipChain: false) {
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                    name = $"{light.name}_OcclusionVolume"
                };
                tex.SetPixels32(data);
                tex.Apply(updateMipmaps: false, makeNoLongerReadable: true);

                SaveAndAssign(light, tex);

                float occludedPct = 100f * totalOccludedSamples / totalRays;
                Debug.Log($"[ALV] Baked '{light.name}' — {voxelCount} voxels × {sampleCount} samples ({totalRays} rays), {occludedPct:F1}% occluded.", light);
                return true;
            } finally {
                if (commands.IsCreated) commands.Dispose();
                if (hits.IsCreated) hits.Dispose();
            }
        }

        private static Vector3[] GenerateSampleOffsets(int count, float radius, int seed) {
            var result = new Vector3[count];
            if (count == 1 || radius <= 0f) {
                for (int i = 0; i < count; i++) result[i] = Vector3.zero;
                return result;
            }
            var rand = new System.Random(seed);
            for (int i = 0; i < count; i++) {
                Vector3 p;
                do {
                    p = new Vector3(
                        (float)rand.NextDouble() * 2f - 1f,
                        (float)rand.NextDouble() * 2f - 1f,
                        (float)rand.NextDouble() * 2f - 1f);
                } while (p.sqrMagnitude > 1f);
                result[i] = p * radius;
            }
            return result;
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
