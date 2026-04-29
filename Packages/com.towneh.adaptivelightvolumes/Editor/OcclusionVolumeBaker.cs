using UnityEditor;
using UnityEngine;

namespace AdaptiveLightVolumes.Editor {

    public static class OcclusionVolumeBaker {

        public const string GeneratedFolder = "Assets/ALV-Generated";

        [MenuItem("CONTEXT/BakedShadowedLight/Bake Occlusion Volume")]
        private static void BakeFromMenu(MenuCommand cmd) {
            var light = (BakedShadowedLight)cmd.context;
            Bake(light);
        }

        public static void Bake(BakedShadowedLight light) {
            if (light == null) return;

            var res = light.BakeResolution;
            if (res.x <= 0 || res.y <= 0 || res.z <= 0) {
                Debug.LogError($"[ALV] Invalid bake resolution {res} on '{light.name}'.", light);
                return;
            }

            // TODO: implement RaycastCommand-based occlusion sampling against the static scene.
            //       For each voxel center in world space, raycast toward light.transform.position
            //       and store hit/miss as 0/1 in an R8 Texture3D. Hardware-RT path
            //       (RayTracingAccelerationStructure) can come later behind a toggle.
            var tex = new Texture3D(res.x, res.y, res.z, TextureFormat.R8, mipChain: false) {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                name = $"{light.name}_OcclusionVolume"
            };

            // Placeholder: fill with 1.0 (fully visible) so the runtime path is testable end-to-end.
            var data = new Color32[res.x * res.y * res.z];
            for (int i = 0; i < data.Length; i++) data[i] = new Color32(255, 0, 0, 0);
            tex.SetPixels32(data);
            tex.Apply(updateMipmaps: false, makeNoLongerReadable: true);

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

            Debug.Log($"[ALV] Baked placeholder occlusion for '{light.name}' at {assetPath} (full implementation pending).", light);
        }
    }
}
