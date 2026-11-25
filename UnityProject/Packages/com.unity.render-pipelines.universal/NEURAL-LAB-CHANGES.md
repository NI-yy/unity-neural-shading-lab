# Project-local URP 17.3.0 changes

The main directional light uses PCSS when soft shadows are enabled.

- `ShaderLibrary/NeuralLabPCSS.hlsl`: blocker search, directional penumbra estimation, variable-radius filtering, receiver-plane depth correction, and defaults.
- `ShaderLibrary/Shadows.hlsl`: includes the PCSS implementation and routes the main light's soft-shadow evaluation to it.
- `Runtime/Passes/MainLightShadowCasterPass.cs`: uploads the active cascade atlas rectangles and world/depth scales.

Additional-light shadows, shadow-map generation, native shadow strength/fade, and the hard-shadow path retain their URP implementations. Set `NEURAL_LAB_MAIN_LIGHT_PCSS` to `0` and rebuild to select the original main-light PCF evaluation path.

The upstream package version and license files are retained. This embedded package is maintained by the project; Unity will not upgrade it automatically. See the repository's `Docs/pcss-implementation.md` for parameters and the saved PCF baseline.
