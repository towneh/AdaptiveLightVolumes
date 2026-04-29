# Changelog

All notable changes to this package are documented in this file.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.1.0] - 2026-04-29

Initial release.

### Added
- `BakedShadowedLight` component supporting Point, Spot, and Area light types with per-light baked occlusion Texture3D, type-aware gizmos, and an `[ExecuteAlways]` lifecycle so registration works in the editor without entering Play mode.
- `BakedLightOcclusionManager` scene singleton that pushes the `_ALV_*` shader global arrays and per-light Texture3D bindings every `LateUpdate`.
- CPU bake path using `RaycastCommand.ScheduleBatch` against scene Colliders with cancellable, chunked dispatch. Soft shadows via jittered multi-sample baking, deterministic per-light via instance-ID seed.
- Hardware ray-tracing bake path using inline RT in a compute shader against a `RayTracingAccelerationStructure` (Unity 6+, D3D12, DXR-capable GPU). Auto-fallback to CPU on unsupported hardware with the failure reason logged.
- `Tools → Adaptive Light Volumes → Bake All Lights in Active Scene` menu item with overall progress + cancel.
- Custom inspector for `BakedShadowedLight`: type-aware field hiding, in-place Bake / Clear buttons, baked-asset status box, multi-edit support.
- Custom inspector for `BakedLightOcclusionManager`: registered-light list with bind status (`bound`, `over cap`, `disabled`), max-cap warning, Force Push Globals button.
- HLSL evaluator (`Runtime/Shaders/AdaptiveLightVolumes.hlsl`) for use from Shader Graph Custom Function Nodes or hand-written URP shaders. Implements:
  - Point: distance falloff × NoL × baked occlusion.
  - Spot: cone falloff with smooth inner/outer transition + projected cookie texture mapped onto the cone footprint.
  - Area: rectangular Lambert via Linearly Transformed Cosines polygon integration with the production-tested LTCGI 16-case clip table.
- Demo shader `AdaptiveLightVolumes/Demo Unlit` for end-to-end visual verification.
- `TwoSided` toggle on area lights for free double-sided emissive panels.

### Known limitations
- Specular LTC for area lights is not implemented (diffuse Lambert only).
- One-sided area lights show a hard cutoff line where their plane intersects receiver geometry; enable `TwoSided` to bypass.
- Shader supports up to 8 simultaneously active lights (`ALV_MAX_LIGHTS = 8`).

[Unreleased]: https://github.com/towneh/AdaptiveLightVolumes/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/towneh/AdaptiveLightVolumes/releases/tag/v0.1.0
