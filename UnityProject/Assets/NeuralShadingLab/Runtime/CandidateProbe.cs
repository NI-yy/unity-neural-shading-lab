using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace NeuralShadingLab
{
    // Explicit diagnostic ablations, never enabled by the normal baseline command.
    public static class CandidateProbe
    {
        [Serializable] sealed class Info
        {
            public string variant, view, sunShadows, sunSoftQuality, selectedObject;
            public int disabledRenderers;
            public string[] disabledFeatures;
            public Vector3 cameraPosition;
            public Quaternion cameraRotation;
            public float fieldOfView;
        }
        static bool overridePose;
        static Vector3 position;
        static Quaternion rotation;
        static float fov;
        static string Arg(string[] args, string key, string fallback)
        { int i = Array.IndexOf(args, key); return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback; }

        public static void Apply(string[] args, Camera camera, CameraPose initial, string output)
        {
            if (!args.Contains("-candidate")) return;
            var info = new Info { variant = Arg(args, "-candidate", "reference"), view = Arg(args, "-candidateView", "initial"), disabledFeatures = Array.Empty<string>() };
            var sun = RenderSettings.sun;
            if (sun == null) sun = UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None).FirstOrDefault(l => l.enabled && l.type == LightType.Directional);
            if (sun == null) throw new InvalidOperationException("Diagnostic requires the main directional light");
            position = initial.position; rotation = initial.rotation; fov = initial.fieldOfView;
            switch (info.view)
            {
                case "initial": break;
                case "alternate": rotation = Quaternion.AngleAxis(7, Vector3.up) * rotation; sun.transform.Rotate(Vector3.up, 25, Space.Self); break;
                case "shadow-stress":
                    // Fixed pose discovered by screening; independent of object enumeration order.
                    position = new Vector3(-129.9334717f, 12.7908611f, 409.6483459f);
                    rotation = new Quaternion(.0644175485f, -.3694965839f, .0256864037f, .9266406298f); fov = 50; break;
                case "lake":
                case "waterfall":
                    string shader = info.view == "lake" ? "Shader Graphs/WaterLake" : "Shader Graphs/SG_Waterfall";
                    var selected = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None)
                        .Where(r => r.enabled && r.gameObject.activeInHierarchy && r.sharedMaterials.Any(m => m != null && m.shader.name == shader))
                        .OrderByDescending(r => r.bounds.size.sqrMagnitude).FirstOrDefault();
                    if (selected == null) throw new InvalidOperationException("Missing shader view " + shader);
                    var bounds = selected.bounds; var direction = (initial.position - bounds.center).normalized;
                    float radius = Mathf.Max(.5f, bounds.extents.magnitude); fov = 50;
                    position = bounds.center + direction * (radius / Mathf.Tan(fov * Mathf.Deg2Rad / 2) * 1.1f);
                    rotation = Quaternion.LookRotation(bounds.center - position, Vector3.up); info.selectedObject = selected.name;
                    break;
                default: throw new ArgumentException("Unknown diagnostic view " + info.view);
            }
            overridePose = true;
            switch (info.variant)
            {
                case "reference": break;
                case "no-shadows": sun.shadows = LightShadows.None; break;
                case "hard-shadows": sun.shadows = LightShadows.Hard; break;
                case "low-shadows": sun.GetUniversalAdditionalLightData().softShadowQuality = SoftShadowQuality.Low; break;
                case "no-foliage":
                    foreach (var renderer in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                        if (renderer.enabled && renderer.sharedMaterials.Any(m => m != null && m.shader.name == "Shader Graphs/CustomTree")) { renderer.enabled = false; info.disabledRenderers++; }
                    break;
                case "no-post": camera.GetUniversalAdditionalCameraData().renderPostProcessing = false; break;
                case "no-fog":
                case "no-ssao":
                    var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
                    var data = (ScriptableRendererData[])typeof(UniversalRenderPipelineAsset).GetField("m_RendererDataList", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(pipeline);
                    var disabled = new System.Collections.Generic.List<string>();
                    foreach (var renderer in data.Where(r => r != null)) foreach (var feature in renderer.rendererFeatures)
                        if (feature != null && (info.variant == "no-fog" ? feature.name == "HeightFog" || feature.name == "CubeMapFog" : feature.name == "ScreenSpaceAmbientOcclusion")) { feature.SetActive(false); disabled.Add(feature.name); }
                    if (disabled.Count == 0) throw new InvalidOperationException("No diagnostic renderer features matched");
                    info.disabledFeatures = disabled.ToArray(); break;
                default: throw new ArgumentException("Unknown diagnostic variant " + info.variant);
            }
            info.sunShadows = sun.shadows.ToString(); info.sunSoftQuality = sun.GetUniversalAdditionalLightData().softShadowQuality.ToString();
            info.cameraPosition = position; info.cameraRotation = rotation; info.fieldOfView = fov;
            File.WriteAllText(Path.Combine(output, "candidate-info.json"), JsonUtility.ToJson(info, true));
            Debug.Log("CANDIDATE_PROBE " + JsonUtility.ToJson(info));
        }

        public static void ApplyPose(Camera camera)
        { if (!overridePose) return; camera.transform.SetPositionAndRotation(position, rotation); camera.fieldOfView = fov; }
    }
}
