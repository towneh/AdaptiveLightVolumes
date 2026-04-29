using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AdaptiveLightVolumes.Editor {

    public static class AdaptiveLightVolumesMenu {

        private const string BakeAllMenu = "Tools/Adaptive Light Volumes/Bake All Lights in Active Scene";

        [MenuItem(BakeAllMenu)]
        public static void BakeAllLightsInActiveScene() {
            Scene scene = EditorSceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded) {
                EditorUtility.DisplayDialog("Adaptive Light Volumes", "No valid active scene.", "OK");
                return;
            }

            var lights = CollectLightsInScene(scene);
            if (lights.Count == 0) {
                EditorUtility.DisplayDialog(
                    "Adaptive Light Volumes",
                    $"No BakedShadowedLight components found in scene '{scene.name}'.",
                    "OK");
                return;
            }

            int baked = 0;
            try {
                for (int i = 0; i < lights.Count; i++) {
                    int captured = i;
                    int total = lights.Count;
                    bool completed = OcclusionVolumeBaker.Bake(lights[i], (sub, status) => {
                        float overall = (captured + sub) / total;
                        return EditorUtility.DisplayCancelableProgressBar(
                            "Baking Adaptive Light Volumes",
                            $"Light {captured + 1}/{total}: {status}",
                            overall);
                    });
                    if (!completed) {
                        Debug.Log($"[ALV] Bake canceled after {baked}/{lights.Count} lights.");
                        return;
                    }
                    baked++;
                }
                Debug.Log($"[ALV] Baked {baked} lights in scene '{scene.name}'.");
            } finally {
                EditorUtility.ClearProgressBar();
            }
        }

        private static System.Collections.Generic.List<BakedShadowedLight> CollectLightsInScene(Scene scene) {
            var result = new System.Collections.Generic.List<BakedShadowedLight>();
            var roots = scene.GetRootGameObjects();
            foreach (var root in roots) {
                result.AddRange(root.GetComponentsInChildren<BakedShadowedLight>(includeInactive: true));
            }
            return result;
        }
    }
}
