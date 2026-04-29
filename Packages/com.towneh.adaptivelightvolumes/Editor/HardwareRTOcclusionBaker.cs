using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace AdaptiveLightVolumes.Editor {

    public static class HardwareRTOcclusionBaker {

        private const string ComputeShaderPath = "Packages/com.towneh.adaptivelightvolumes/Editor/Shaders/ALV_OcclusionBakeCS.compute";
        private const float RayEndEpsilon = 0.01f;

        public static bool IsSupported => SystemInfo.supportsRayTracing;

        public static bool TryBake(BakedShadowedLight light, out string failureReason) {
            failureReason = null;

            if (!IsSupported) {
                failureReason = "SystemInfo.supportsRayTracing is false. Switch the editor's Graphics API to D3D12 (or Vulkan with RT) and ensure the GPU supports DXR / VK_KHR_ray_tracing.";
                return false;
            }

            var cs = AssetDatabase.LoadAssetAtPath<ComputeShader>(ComputeShaderPath);
            if (cs == null) {
                failureReason = $"Compute shader not found at '{ComputeShaderPath}'.";
                return false;
            }

            var res = light.BakeResolution;
            int voxelCount = res.x * res.y * res.z;
            int sampleCount = (light.ShadowRadius > 0f) ? Mathf.Max(1, light.ShadowSamples) : 1;

            Bounds bounds = light.GetBakeBoundsWorld();
            Vector3 cellSize = new Vector3(
                bounds.size.x / res.x,
                bounds.size.y / res.y,
                bounds.size.z / res.z);
            Vector3 origin = bounds.min + cellSize * 0.5f;

            Vector3[] offsets = OcclusionVolumeBaker.GenerateSampleOffsets(sampleCount, light.ShadowRadius, light.GetInstanceID());
            var offsets4 = new Vector4[sampleCount];
            for (int i = 0; i < sampleCount; i++) offsets4[i] = offsets[i];

            RayTracingAccelerationStructure rtas = null;
            ComputeBuffer offsetsBuffer = null;
            RenderTexture rt = null;

            try {
                rtas = new RayTracingAccelerationStructure(new RayTracingAccelerationStructure.Settings(
                    RayTracingAccelerationStructure.ManagementMode.Automatic,
                    RayTracingAccelerationStructure.RayTracingModeMask.Everything,
                    light.OccluderLayers));
                rtas.Build();

                offsetsBuffer = new ComputeBuffer(sampleCount, 16);
                offsetsBuffer.SetData(offsets4);

                rt = new RenderTexture(res.x, res.y, 0, RenderTextureFormat.RFloat, RenderTextureReadWrite.Linear) {
                    dimension = TextureDimension.Tex3D,
                    volumeDepth = res.z,
                    enableRandomWrite = true,
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear
                };
                rt.Create();

                int kernel = cs.FindKernel("BakeOcclusion");
                cs.SetRayTracingAccelerationStructure(kernel, "_ALV_RTAS", rtas);
                cs.SetTexture(kernel, "_ALV_BakeOutput", rt);
                cs.SetBuffer(kernel, "_ALV_SampleOffsets", offsetsBuffer);
                cs.SetVector("_ALV_BakeOrigin", origin);
                cs.SetVector("_ALV_BakeCellSize", cellSize);
                cs.SetInts("_ALV_BakeRes", res.x, res.y, res.z);
                cs.SetVector("_ALV_LightPos", light.transform.position);
                cs.SetInt("_ALV_SampleCount", sampleCount);
                cs.SetFloat("_ALV_RayMaxDistanceEps", RayEndEpsilon);

                int gx = (res.x + 3) / 4;
                int gy = (res.y + 3) / 4;
                int gz = (res.z + 3) / 4;
                cs.Dispatch(kernel, gx, gy, gz);

                var req = AsyncGPUReadback.Request(rt);
                req.WaitForCompletion();
                if (req.hasError) {
                    failureReason = "AsyncGPUReadback returned an error.";
                    return false;
                }

                var floatData = req.GetData<float>();
                var byteData = new byte[voxelCount];
                int totalOccludedSamples = 0;
                for (int i = 0; i < voxelCount; i++) {
                    float v = Mathf.Clamp01(floatData[i]);
                    byteData[i] = (byte)(v * 255f);
                    totalOccludedSamples += (int)((1f - v) * sampleCount + 0.5f);
                }

                var tex = new Texture3D(res.x, res.y, res.z, TextureFormat.R8, mipChain: false) {
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                    name = $"{light.name}_OcclusionVolume"
                };
                tex.SetPixelData(byteData, 0);
                tex.Apply(updateMipmaps: false, makeNoLongerReadable: true);

                OcclusionVolumeBaker.SaveAndAssign(light, tex);

                int totalRays = voxelCount * sampleCount;
                float occludedPct = totalRays > 0 ? 100f * totalOccludedSamples / totalRays : 0f;
                Debug.Log($"[ALV] HW RT baked '{light.name}' — {voxelCount} voxels × {sampleCount} samples ({totalRays} rays), {occludedPct:F1}% occluded.", light);
                return true;
            } catch (System.Exception e) {
                failureReason = $"Exception during HW RT bake: {e.Message}";
                return false;
            } finally {
                if (offsetsBuffer != null) offsetsBuffer.Release();
                if (rt != null) rt.Release();
                rtas?.Dispose();
            }
        }
    }
}
