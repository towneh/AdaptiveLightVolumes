# Adaptive Light Volumes

Movable point, spot, and area lights with pre-baked 3D occlusion volumes for Unity 6.4+.

ALV is the dynamic-light counterpart to Unity's Adaptive Probe Volumes. APVs handle the static, indirect side of a scene's lighting; ALV handles the small-radius dynamic lights that need to move, blink, or follow gameplay state — without the per-frame cost of a real-time shadow map per light.

A baker traces rays from a 3D voxel grid around each light against your static scene (CPU `RaycastCommand` against Colliders, or hardware ray tracing against MeshRenderers on D3D12 + a DXR-capable GPU). The resulting visibility data is stored as an R8 Texture3D that the shader samples at runtime, multiplying each light's contribution by the cached occlusion. Many small lights with believable shadows for the cost of a few texture fetches per fragment.

## Features

- **Three light types**: omnidirectional Point, cone Spot with optional projected cookie texture, and rectangular Area integrated via Linearly Transformed Cosines.
- **Per-light baked occlusion volume** — soft shadows from static scenery without realtime shadow maps. Soft penumbras via jittered multi-sample baking (configurable `ShadowSamples` × `ShadowRadius`).
- **CPU and hardware ray-tracing bakers**. The HW path uses inline RT in a compute shader against a `RayTracingAccelerationStructure` and bakes a 32³×16-sample volume in milliseconds; the CPU path uses `RaycastCommand` and is the safe default everywhere.

## Requirements

- Unity 6000.4 (Unity 6.4) or newer
- URP (the bundled demo shader and the HLSL include reference URP's `Core.hlsl`)
- *Optional:* Direct3D 12 graphics API + a DXR / VK_KHR_ray_tracing-capable GPU for the hardware ray-tracing bake path. Without these the baker transparently falls back to CPU `RaycastCommand`.

## Installation

Add to your project's `Packages/manifest.json`:

```json
"com.towneh.adaptivelightvolumes": "https://github.com/towneh/AdaptiveLightVolumes.git?path=Packages/com.towneh.adaptivelightvolumes"
```

Or, for local development, a `file:` reference to a clone:

```json
"com.towneh.adaptivelightvolumes": "file:C:/path/to/AdaptiveLightVolumes/Packages/com.towneh.adaptivelightvolumes"
```

## Quick start

1. **Add a manager.** Create an empty GameObject in your scene and attach `Lighting → Adaptive Light Volumes → Baked Light Occlusion Manager`. One per scene.
2. **Add a light.** Create another empty GameObject inside the area you want lit, attach `Lighting → Adaptive Light Volumes → Baked Shadowed Light`. Choose `Type` (Point / Spot / Area), set Color, Intensity, Range. Size `BakeExtents` to wrap the volume you want occlusion data for; `BakeResolution` controls voxel density.
3. **Bake.** Click **Bake Occlusion Volume** in the BakedShadowedLight inspector, or use **Tools → Adaptive Light Volumes → Bake All Lights in Active Scene** for the whole scene. The baked Texture3D lands in `Assets/ALV-Generated/`.
4. **Apply the demo shader.** Create a material, set its shader to `AdaptiveLightVolumes/Demo Unlit`. Drop it on any mesh inside the bake bounds — you should see it lit by the registered lights with baked shadows.
5. **(Optional) Wire into your own shaders.** Add a Shader Graph Custom Function Node, point it at `Packages/com.towneh.adaptivelightvolumes/Runtime/Shaders/AdaptiveLightVolumes.hlsl`, name `EvaluateAdaptiveLights_float`, with inputs `PositionWS` (Vector3) and `NormalWS` (Vector3) and output `LightContribution` (Vector3). Add the result to your existing diffuse term.

## Components

### `BakedShadowedLight`

| Field | Purpose |
| --- | --- |
| `Type` | Point, Spot, or Area. |
| `Color`, `Intensity`, `Range` | Standard light parameters. |
| `FalloffExponent` | Distance attenuation curve: `pow(1 - dist/range, FalloffExponent)`. 1 ≈ linear, 2 ≈ inverse-square-ish. |
| `SpotOuterAngle`, `SpotInnerAngle` | Cone outer / inner half-angles in degrees. *(Spot only.)* |
| `Cookie` | Optional projected texture for spot lights. Set Wrap Mode = Clamp on the texture for clean cone-edge cutoffs. |
| `AreaSize` | Width × height of the rectangular area light, in meters. *(Area only.)* |
| `TwoSided` | If on, the rectangle emits from both faces — useful for hanging signs or ceiling fixtures. *(Area only.)* |
| `BakeExtents` | World-space size of the occlusion volume baked around the light. |
| `BakeResolution` | Voxel resolution of the occlusion Texture3D. 32³ is a good default; 64³ for sharper shadows on detailed scenes. |
| `OccluderLayers` | Layer mask for what counts as an occluder. |
| `ShadowSamples` | Number of jittered rays per voxel for soft-shadow penumbra. 1 = hard binary shadows. |
| `ShadowRadius` | Light-source radius driving penumbra softness. 0 forces hard shadows regardless of `ShadowSamples`. |
| `UseHardwareRT` | Use hardware ray tracing for baking when available; CPU fallback otherwise. |
| `BakedOcclusion` | Output Texture3D, assigned by the baker (read-only in the inspector). |

### `BakedLightOcclusionManager`

One per scene. Holds the registry of active `BakedShadowedLight`s and pushes the `_ALV_*` shader globals every `LateUpdate` (in edit mode too via `[ExecuteAlways]`). The custom inspector lists every registered light with a bind status (`bound`, `over cap`, `disabled`), warns when more lights are active than the shader's `MaxLights = 8`, and exposes a **Force Push Globals** button for cases where the shader hasn't picked up a fresh bake.

## Shader integration

`Runtime/Shaders/AdaptiveLightVolumes.hlsl` declares the `_ALV_*` globals and the entry point:

```hlsl
void EvaluateAdaptiveLights_float(float3 PositionWS, float3 NormalWS,
                                  out float3 LightContribution);
```

You can:

- Reference it from a **Shader Graph Custom Function Node** (PositionWS / NormalWS in, LightContribution out), then add the result to the diffuse output of your graph. Most ergonomic for production materials.
- `#include` it directly from a **hand-written URP shader** — see `ALV_DemoUnlit.shader` in the same folder for the minimum forward-pass setup. The shader expects the consumer to have included URP's `Core.hlsl` first; the bundled `sampler_LinearClamp` is then in scope.

The shader supports up to 8 simultaneous active lights (`ALV_MAX_LIGHTS`). Excess lights are surfaced by the manager inspector with a Warning HelpBox; either reduce active lights or split your scene into bake regions.

## Hardware ray tracing

When the editor's graphics API is set to **Direct3D 12** and the GPU supports DXR (any modern NVIDIA / AMD / Intel Arc), the baker dispatches inline ray tracing in a compute shader against a freshly built `RayTracingAccelerationStructure` over the scene's MeshRenderers. Voxel counts that take seconds on the CPU path complete in single-digit milliseconds.

To enable: **Edit → Project Settings → Player → Graphics APIs for Windows** → put **Direct3D12** at the top of the list, restart the editor. The BakedShadowedLight inspector's `Use Hardware RT` checkbox surfaces an info-box when RT support is missing in the current session.

If a HW bake fails (driver issue, scene contains a non-RT-compatible renderer, etc.), the per-light bake automatically falls back to the CPU path with the failure reason logged. Nothing to toggle.

A note on layer semantics: the CPU path filters Colliders by `OccluderLayers`; the HW path filters MeshRenderers by their GameObject layer. Scenes that use the same layer for occluder colliders and renderers (the common case) get identical results.

## Known limitations

- **Specular LTC for area lights is not implemented.** The current area evaluator handles diffuse Lambert only via the LTC polygon integral with the matrix collapsed to identity. Adding glossy/GGX response would need the LTC matrix LUT (~16 KB) and the LTC2 amplitude texture, plus a tangent-frame transform.
- **Hard cull at the rectangle's plane** for one-sided area lights. Receiver geometry that straddles the plane shows a visible discontinuity. Workaround: enable **TwoSided** on the area light.
- **`MaxLights = 8`** hard cap on the shader-side light array. Scenes that need more should split into regions or upgrade to a `StructuredBuffer`-based future variant.
- **Single-shot CPU bake.** Very large bakes (e.g. `64³ × 32 samples ≈ 8 M rays`) hold ~600 MB of NativeArray data during dispatch. The HW RT path is the recommended escape hatch for large scenes.

## Credits

- Inspired by the static-light-volume design of [VRC Light Volumes](https://github.com/REDSIM/VRCLightVolumes).
- Linearly Transformed Cosines for area-light integration: Heitz, Dupuy, Hill, Iwasaki, *"Real-Time Polygonal-Light Shading with Linearly Transformed Cosines"*, ACM SIGGRAPH 2016.
- The polygon clip table and rational `θ/sin(θ)` approximation follow the production-tested implementation in [Pi's LTCGI shader](https://github.com/PiMaker/ltcgi) (case ordering, `[forcecase]` performance note, `abs(sum)` integration sign).

## License

[MIT](LICENSE.md).
