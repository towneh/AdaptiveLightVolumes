using UnityEditor;
using UnityEngine;

namespace AdaptiveLightVolumes.Editor {

    [CustomEditor(typeof(BakedShadowedLight))]
    [CanEditMultipleObjects]
    public class BakedShadowedLightEditor : UnityEditor.Editor {

        private SerializedProperty _type;
        private SerializedProperty _color;
        private SerializedProperty _intensity;
        private SerializedProperty _range;
        private SerializedProperty _falloffExponent;

        private SerializedProperty _spotOuterAngle;
        private SerializedProperty _spotInnerAngle;
        private SerializedProperty _cookie;

        private SerializedProperty _areaSize;
        private SerializedProperty _twoSided;

        private SerializedProperty _bakeExtents;
        private SerializedProperty _bakeResolution;
        private SerializedProperty _occluderLayers;
        private SerializedProperty _shadowSamples;
        private SerializedProperty _shadowRadius;
        private SerializedProperty _useHardwareRT;
        private SerializedProperty _bakedOcclusion;

        private void OnEnable() {
            _type            = serializedObject.FindProperty(nameof(BakedShadowedLight.Type));
            _color           = serializedObject.FindProperty(nameof(BakedShadowedLight.Color));
            _intensity       = serializedObject.FindProperty(nameof(BakedShadowedLight.Intensity));
            _range           = serializedObject.FindProperty(nameof(BakedShadowedLight.Range));
            _falloffExponent = serializedObject.FindProperty(nameof(BakedShadowedLight.FalloffExponent));

            _spotOuterAngle  = serializedObject.FindProperty(nameof(BakedShadowedLight.SpotOuterAngle));
            _spotInnerAngle  = serializedObject.FindProperty(nameof(BakedShadowedLight.SpotInnerAngle));
            _cookie          = serializedObject.FindProperty(nameof(BakedShadowedLight.Cookie));

            _areaSize        = serializedObject.FindProperty(nameof(BakedShadowedLight.AreaSize));
            _twoSided        = serializedObject.FindProperty(nameof(BakedShadowedLight.TwoSided));

            _bakeExtents     = serializedObject.FindProperty(nameof(BakedShadowedLight.BakeExtents));
            _bakeResolution  = serializedObject.FindProperty(nameof(BakedShadowedLight.BakeResolution));
            _occluderLayers  = serializedObject.FindProperty(nameof(BakedShadowedLight.OccluderLayers));
            _shadowSamples   = serializedObject.FindProperty(nameof(BakedShadowedLight.ShadowSamples));
            _shadowRadius    = serializedObject.FindProperty(nameof(BakedShadowedLight.ShadowRadius));
            _useHardwareRT   = serializedObject.FindProperty(nameof(BakedShadowedLight.UseHardwareRT));
            _bakedOcclusion  = serializedObject.FindProperty(nameof(BakedShadowedLight.BakedOcclusion));
        }

        public override void OnInspectorGUI() {
            serializedObject.Update();

            DrawSectionHeader("Light");
            EditorGUILayout.PropertyField(_type);
            EditorGUILayout.PropertyField(_color);
            EditorGUILayout.PropertyField(_intensity);
            EditorGUILayout.PropertyField(_range);
            EditorGUILayout.PropertyField(_falloffExponent);

            bool mixedType = _type.hasMultipleDifferentValues;
            LightType displayedType = (LightType)_type.enumValueIndex;

            if (mixedType || displayedType == LightType.Spot) {
                DrawSectionHeader("Spot");
                EditorGUILayout.PropertyField(_spotOuterAngle);
                EditorGUILayout.PropertyField(_spotInnerAngle);
                EditorGUILayout.PropertyField(_cookie);
            }

            if (mixedType || displayedType == LightType.Area) {
                DrawSectionHeader("Area");
                EditorGUILayout.PropertyField(_areaSize);
                EditorGUILayout.PropertyField(_twoSided);
            }

            DrawSectionHeader("Occlusion Bake");
            EditorGUILayout.PropertyField(_bakeExtents);
            EditorGUILayout.PropertyField(_bakeResolution);
            EditorGUILayout.PropertyField(_occluderLayers);
            EditorGUILayout.PropertyField(_shadowSamples);
            EditorGUILayout.PropertyField(_shadowRadius);
            EditorGUILayout.PropertyField(_useHardwareRT);
            if (_useHardwareRT.boolValue && !HardwareRTOcclusionBaker.IsSupported) {
                EditorGUILayout.HelpBox(
                    "Hardware ray tracing is not available in this editor session. Bake will fall back to CPU. Set Graphics API to D3D12 (or Vulkan with RT extensions) and ensure the GPU supports DXR / VK_KHR_ray_tracing.",
                    MessageType.Info);
            }

            DrawSectionHeader("Result");
            using (new EditorGUI.DisabledScope(true)) {
                EditorGUILayout.PropertyField(_bakedOcclusion);
            }
            DrawBakeStatus();

            EditorGUILayout.Space(6);
            DrawBakeActions();

            serializedObject.ApplyModifiedProperties();
        }

        private static void DrawSectionHeader(string label) {
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
        }

        private void DrawBakeStatus() {
            if (targets.Length > 1) {
                EditorGUILayout.HelpBox($"{targets.Length} lights selected. Bake will run on each in sequence.", MessageType.Info);
                return;
            }
            var light = (BakedShadowedLight)target;
            if (light.BakedOcclusion == null) {
                EditorGUILayout.HelpBox("No occlusion volume baked yet. Press Bake to generate one.", MessageType.Warning);
            } else {
                var tex = light.BakedOcclusion;
                EditorGUILayout.HelpBox(
                    $"Baked: {tex.width}×{tex.height}×{tex.depth}  format: {tex.format}",
                    MessageType.Info);
            }
        }

        private void DrawBakeActions() {
            using (new EditorGUILayout.HorizontalScope()) {
                if (GUILayout.Button("Bake Occlusion Volume", GUILayout.Height(28f))) {
                    BakeSelected();
                }
                using (new EditorGUI.DisabledScope(!AnyTargetHasBakedTexture())) {
                    if (GUILayout.Button("Clear", GUILayout.Width(80f), GUILayout.Height(28f))) {
                        foreach (var t in targets) {
                            if (t is BakedShadowedLight l && l.BakedOcclusion != null) {
                                Undo.RecordObject(l, "Clear Baked Occlusion");
                                l.BakedOcclusion = null;
                                EditorUtility.SetDirty(l);
                            }
                        }
                    }
                }
            }
        }

        private void BakeSelected() {
            try {
                int total = targets.Length;
                int idx = 0;
                foreach (var t in targets) {
                    if (!(t is BakedShadowedLight l)) { idx++; continue; }
                    int captured = idx;
                    bool completed = OcclusionVolumeBaker.Bake(l, (sub, status) => {
                        float overall = total > 0 ? (captured + sub) / total : sub;
                        return EditorUtility.DisplayCancelableProgressBar(
                            "Baking Adaptive Light Volumes",
                            total > 1 ? $"Light {captured + 1}/{total}: {status}" : status,
                            overall);
                    });
                    if (!completed) break;
                    idx++;
                }
            } finally {
                EditorUtility.ClearProgressBar();
            }
        }

        private bool AnyTargetHasBakedTexture() {
            foreach (var t in targets) {
                if (t is BakedShadowedLight l && l.BakedOcclusion != null) return true;
            }
            return false;
        }
    }
}
