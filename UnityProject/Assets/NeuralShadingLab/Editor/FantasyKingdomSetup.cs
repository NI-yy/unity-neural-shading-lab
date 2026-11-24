using System;
using System.IO;
using System.Linq;
using NeuralShadingLab;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

public static class FantasyKingdomSetup
{
    public const string ScenePath = "Assets/Scenes/Demo.unity";
    const string LocalRoot = "Assets/NeuralShadingLab/LocalSample";
    public const string ConfigPath = "Assets/NeuralShadingLab/Resources/NeuralLabBaseline.asset";
    public static readonly string[] DisabledTypes =
    {
        "Unity.FantasyKingdom.RTSCameraController", "Unity.FantasyKingdom.QualitySettingsController",
        "Unity.FantasyKingdom.StatsCanvasController", "Unity.FantasyKingdom.FogController",
        "Unity.FantasyKingdom.InputProvider", "Unity.Cinemachine.CinemachineBrain"
    };

    [MenuItem("Neural Shading Lab/Prepare Fantasy Kingdom Baseline")]
    public static void Prepare()
    {
        if (EditorSceneManager.GetActiveScene().isDirty) throw new InvalidOperationException("Save current scene changes first.");
        if (!File.Exists(ScenePath)) throw new FileNotFoundException("Import Fantasy Kingdom from My Assets first.", ScenePath);
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        int desktop = Array.IndexOf(QualitySettings.names, "Desktop");
        if (desktop < 0) throw new InvalidOperationException("Fantasy Kingdom Desktop quality preset was not imported.");
        QualitySettings.SetQualityLevel(desktop, true);
        var original = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/URP/Settings/Desktop.asset");
        if (original == null) throw new InvalidOperationException("Fantasy Kingdom Desktop pipeline was not found.");
        Directory.CreateDirectory(LocalRoot);
        AssetDatabase.Refresh();
        string pipelinePath = LocalRoot + "/BaselineDesktop.asset";
        if (!File.Exists(pipelinePath) && !AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(original), pipelinePath))
            throw new IOException("Could not copy Desktop pipeline.");
        var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(pipelinePath);
        string rendererPath = LocalRoot + "/BaselineDesktopRenderer.asset";
        if (!File.Exists(rendererPath) && !AssetDatabase.CopyAsset("Assets/URP/Settings/Desktop Renderer.asset", rendererPath))
            throw new IOException("Could not copy Desktop renderer.");
        var serializedPipeline = new SerializedObject(pipeline);
        serializedPipeline.FindProperty("m_RendererDataList").GetArrayElementAtIndex(0).objectReferenceValue = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(rendererPath);
        serializedPipeline.ApplyModifiedPropertiesWithoutUndo();
        pipeline.renderScale = 1;
        pipeline.upscalingFilter = UpscalingFilterSelection.Linear;
        pipeline.useAdaptivePerformance = false;
        QualitySettings.renderPipeline = pipeline;
        GraphicsSettings.defaultRenderPipeline = pipeline;
        QualitySettings.vSyncCount = 0;
        PlayerSettings.companyName = "NI-yy";
        PlayerSettings.productName = "Unity Neural Shading Lab";
        PlayerSettings.enableFrameTimingStats = true;
        PlayerSettings.graphicsJobs = false;
        PlayerSettings.runInBackground = true;
        PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, false);
        PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64, new[] { GraphicsDeviceType.Direct3D11 });
        PlayerSettings.defaultScreenWidth = 1920;
        PlayerSettings.defaultScreenHeight = 1080;
        PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
        var camera = Camera.main;
        if (camera == null) throw new InvalidOperationException("Sample MainCamera was not found.");
        var config = AssetDatabase.LoadAssetAtPath<BaselineConfig>(ConfigPath);
        config.scenePath = ScenePath;
        config.experiment = "fantasy-kingdom-desktop-native-reference";
        config.smokeTest = false;
        config.qualityLevel = desktop;
        config.pipelineOverride = pipeline;
        config.disableComponentTypes = DisabledTypes;
        config.cameraPoses = new[] { new CameraPose { position = camera.transform.position, rotation = camera.transform.rotation, fieldOfView = camera.fieldOfView } };
        EditorUtility.SetDirty(config);
        EditorUtility.SetDirty(pipeline);
        AssetDatabase.SaveAssets();
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        Debug.Log("NEURAL_LAB_FANTASY_READY " + ScenePath + "; Desktop, native 1920x1080, stationary original camera; original scene and APV preserved.");
    }
}

// Mutates only the transient scene being built. Original sample scene stays intact.
public sealed class FantasyKingdomBuildProcessor : IProcessSceneWithReport
{
    public int callbackOrder => 1000;
    public void OnProcessScene(Scene scene, BuildReport report)
    {
        var config = AssetDatabase.LoadAssetAtPath<BaselineConfig>(FantasyKingdomSetup.ConfigPath);
        if (config == null || config.smokeTest || scene.path != config.scenePath) return;
        foreach (var root in scene.GetRootGameObjects())
        {
            foreach (var component in root.GetComponentsInChildren<MonoBehaviour>(true))
                if (component != null && config.disableComponentTypes.Contains(component.GetType().FullName)) component.enabled = false;
            foreach (var canvas in root.GetComponentsInChildren<Canvas>(true)) canvas.gameObject.SetActive(false);
            foreach (var camera in root.GetComponentsInChildren<Camera>(true))
            {
                camera.allowDynamicResolution = false;
                camera.GetUniversalAdditionalCameraData().antialiasing = AntialiasingMode.None;
            }
        }
    }
}
