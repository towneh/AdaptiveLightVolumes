using UnityEditor;
using UnityEngine;

namespace AdaptiveLightVolumes.Editor {

    [CustomEditor(typeof(BakedLightOcclusionManager))]
    public class BakedLightOcclusionManagerEditor : UnityEditor.Editor {

        public override bool RequiresConstantRepaint() => true;

        public override void OnInspectorGUI() {
            EditorGUILayout.LabelField("Registered Lights", EditorStyles.boldLabel);

            var lights = BakedLightOcclusionManager.RegisteredLights;
            int max = BakedLightOcclusionManager.MaxLights;

            if (lights.Count == 0) {
                EditorGUILayout.HelpBox("No BakedShadowedLight components are registered. Add one to a GameObject in this scene.", MessageType.Info);
            } else {
                int activeCount = 0;
                foreach (var l in lights) {
                    if (l != null && l.isActiveAndEnabled) activeCount++;
                }
                if (activeCount > max) {
                    EditorGUILayout.HelpBox(
                        $"{activeCount} active lights registered, but the manager binds at most {max}. Excess lights will not contribute.",
                        MessageType.Warning);
                }

                int boundCount = 0;
                for (int i = 0; i < lights.Count; i++) {
                    var l = lights[i];
                    if (l == null) continue;

                    bool active = l.isActiveAndEnabled;
                    bool willBind = active && boundCount < max;
                    if (active && willBind) boundCount++;

                    using (new EditorGUILayout.HorizontalScope()) {
                        EditorGUILayout.LabelField($"{i}", GUILayout.Width(20f));
                        using (new EditorGUI.DisabledScope(true)) {
                            EditorGUILayout.ObjectField(l, typeof(BakedShadowedLight), true);
                        }
                        EditorGUILayout.LabelField(l.Type.ToString(), GUILayout.Width(50f));
                        string status = willBind ? "bound" : (active ? "over cap" : "disabled");
                        EditorGUILayout.LabelField(status, GUILayout.Width(70f));
                    }
                }
            }

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Actions", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope()) {
                if (GUILayout.Button("Force Push Globals", GUILayout.Height(24f))) {
                    BakedLightOcclusionManager.PushGlobals();
                    SceneView.RepaintAll();
                }
                if (GUILayout.Button("Bake All in Active Scene", GUILayout.Height(24f))) {
                    AdaptiveLightVolumesMenu.BakeAllLightsInActiveScene();
                }
            }
        }
    }
}
