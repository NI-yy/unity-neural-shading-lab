using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using NeuralShadingLab;

public static class NeuralLabProjectSetup
{
    const string Root = "Assets/NeuralShadingLab";
    const string ResourcesPath = Root + "/Resources";
    const string ConfigPath = ResourcesPath + "/NeuralLabBaseline.asset";
    const string SmokeScene = Root + "/Scenes/BaselineSmokeTest.unity";

    [MenuItem("Neural Shading Lab/Initialize URP Project")]
    public static void Initialize()
    {
        Directory.CreateDirectory(ResourcesPath);
        Directory.CreateDirectory(Root + "/Scenes");
        AssetDatabase.Refresh();
        PlayerSettings.companyName = "NI-yy";
        PlayerSettings.productName = "Unity Neural Shading Lab";
        PlayerSettings.colorSpace = ColorSpace.Linear;
        PlayerSettings.enableFrameTimingStats = true;
        PlayerSettings.graphicsJobs = false;
        PlayerSettings.runInBackground = true;
        PlayerSettings.defaultScreenWidth = 1920;
        PlayerSettings.defaultScreenHeight = 1080;
        PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
        PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, false);
        PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64, new[] { GraphicsDeviceType.Direct3D11 });
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
        var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
        if (pipeline == null) throw new InvalidOperationException("PC URP template asset was not found.");
        GraphicsSettings.defaultRenderPipeline = pipeline;
        QualitySettings.renderPipeline = pipeline;
        QualitySettings.vSyncCount = 0;
        pipeline.renderScale = 1;
        EditorUtility.SetDirty(pipeline);
        if (!File.Exists(SmokeScene)) CreateSmokeScene();
        if (AssetDatabase.LoadAssetAtPath<BaselineConfig>(ConfigPath) == null)
        {
            var config = ScriptableObject.CreateInstance<BaselineConfig>();
            config.scenePath = SmokeScene;
            config.cameraPoses = new[] { Pose(new Vector3(13, 10, -16), Vector3.zero), Pose(new Vector3(-13, 10, -16), Vector3.zero) };
            AssetDatabase.CreateAsset(config, ConfigPath);
        }
        AssetDatabase.SaveAssets();
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(SmokeScene, true) };
        Debug.Log("NEURAL_LAB_PROJECT_READY " + Application.unityVersion);
    }

    static CameraPose Pose(Vector3 position, Vector3 lookAt) => new CameraPose
    { position = position, rotation = Quaternion.LookRotation(lookAt - position), fieldOfView = 50 };

    static void CreateSmokeScene()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        material.color = new Color(.3f, .55f, .8f);
        material.SetFloat("_Smoothness", .65f);
        AssetDatabase.CreateAsset(material, Root + "/SmokeMaterial.mat");
        var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.transform.localScale = Vector3.one * 3;
        floor.name = "Smoke Test Floor";
        floor.GetComponent<Renderer>().sharedMaterial = material;
        for (int x = -4; x <= 4; x++) for (int z = -4; z <= 4; z++)
        {
            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.transform.position = new Vector3(x * 2, 1, z * 2);
            sphere.GetComponent<Renderer>().sharedMaterial = material;
        }
        var sun = new GameObject("Directional Light").AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.intensity = 2;
        sun.shadows = LightShadows.Soft;
        sun.transform.rotation = Quaternion.Euler(45, -35, 0);
        RenderSettings.ambientLight = Color.gray;
        var camera = new GameObject("Main Camera").AddComponent<Camera>();
        camera.tag = "MainCamera";
        var pose = Pose(new Vector3(13, 10, -16), Vector3.zero);
        camera.transform.SetPositionAndRotation(pose.position, pose.rotation);
        camera.fieldOfView = pose.fieldOfView;
        camera.GetUniversalAdditionalCameraData().antialiasing = AntialiasingMode.None;
        EditorSceneManager.SaveScene(scene, SmokeScene);
    }

    [MenuItem("Neural Shading Lab/Use Current Scene As Baseline")]
    public static void ConfigureCurrentScene()
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        var camera = Camera.main;
        if (string.IsNullOrEmpty(scene.path) || camera == null)
            throw new InvalidOperationException("Open a saved sample scene with an enabled MainCamera first.");
        var config = AssetDatabase.LoadAssetAtPath<BaselineConfig>(ConfigPath);
        Undo.RecordObject(config, "Configure Baseline Scene");
        config.scenePath = scene.path;
        config.experiment = "fantasy-kingdom-reference";
        config.smokeTest = false;
        config.cameraPoses = new[] { new CameraPose { position = camera.transform.position, rotation = camera.transform.rotation, fieldOfView = camera.fieldOfView } };
        EditorUtility.SetDirty(config);
        AssetDatabase.SaveAssets();
        Selection.activeObject = config;
        Debug.Log("Baseline now uses the current scene with a stationary camera. Add route poses through the menu if needed.");
    }

    [MenuItem("Neural Shading Lab/Append Scene View Pose To Route")]
    public static void AppendSceneViewPose()
    {
        var camera = SceneView.lastActiveSceneView?.camera;
        if (camera == null) throw new InvalidOperationException("Open a Scene view first.");
        var config = AssetDatabase.LoadAssetAtPath<BaselineConfig>(ConfigPath);
        Undo.RecordObject(config, "Append Baseline Pose");
        config.cameraPoses = config.cameraPoses.Concat(new[] { new CameraPose
        { position = camera.transform.position, rotation = camera.transform.rotation, fieldOfView = camera.fieldOfView } }).ToArray();
        EditorUtility.SetDirty(config);
        AssetDatabase.SaveAssets();
    }

    [MenuItem("Neural Shading Lab/Build Baseline Player")]
    public static void BuildBaseline()
    {
        var config = AssetDatabase.LoadAssetAtPath<BaselineConfig>(ConfigPath);
        if (config == null || !File.Exists(config.scenePath)) throw new InvalidOperationException("Configure a valid baseline scene first.");
        if (EditorSceneManager.GetActiveScene().isDirty) throw new InvalidOperationException("Save the scene before building.");
        if (config.qualityLevel >= 0) QualitySettings.SetQualityLevel(config.qualityLevel, true);
        if (config.pipelineOverride != null)
        {
            QualitySettings.renderPipeline = config.pipelineOverride;
            GraphicsSettings.defaultRenderPipeline = config.pipelineOverride;
        }
        PlayerSettings.enableFrameTimingStats = true;
        AssetDatabase.SaveAssets();
        // The imported sample defaults to IL2CPP; this machine has the Mono Windows module.
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
        PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, false);
        PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64, new[] { GraphicsDeviceType.Direct3D12, GraphicsDeviceType.Direct3D11 });
        AssetDatabase.SaveAssets();
        var root = Path.GetFullPath(Path.Combine(Application.dataPath, "../.."));
        var info = "unity=" + Application.unityVersion + "\npackage_lock_sha256=" + Hash("Packages/packages-lock.json") +
            "\nproject_settings_sha256=" + Hash("ProjectSettings/ProjectSettings.asset") +
            "\nquality_settings_sha256=" + Hash("ProjectSettings/QualitySettings.asset") +
            "\ngraphics_settings_sha256=" + Hash("ProjectSettings/GraphicsSettings.asset") +
            "\nscripting_backend=" + PlayerSettings.GetScriptingBackend(NamedBuildTarget.Standalone) +
            "\nscene_dependency_hash=" + AssetDatabase.GetAssetDependencyHash(config.scenePath) +
            "\npipeline_dependency_hash=" + AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(GraphicsSettings.currentRenderPipeline)) +
            "\nmain_light_shadow_filter=" + MainLightShadowFilter() +
            "\nmain_light_shadow_filter_source_sha256=" + Hash("Packages/com.unity.render-pipelines.universal/ShaderLibrary/NeuralLabPCSS.hlsl") +
            "\nbuilt_utc=" + DateTime.UtcNow.ToString("O");
        File.WriteAllText(ResourcesPath + "/NeuralLabBuildInfo.txt", info);
        AssetDatabase.Refresh();
        AssetDatabase.SaveAssets();
        var args = Environment.GetCommandLineArgs();
        bool development = args.Contains("-neuralLabDevelopment");
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { config.scenePath },
            locationPathName = Path.Combine(root, "Builds", development ? "WindowsDevelopment" : "Windows", "NeuralShadingLab.exe"),
            target = BuildTarget.StandaloneWindows64,
            options = development ? BuildOptions.Development : BuildOptions.None
        });
        if (report.summary.result != BuildResult.Succeeded) throw new InvalidOperationException("Baseline build failed: " + report.summary.result);
        Debug.Log("NEURAL_LAB_BUILD_READY " + report.summary.outputPath);
    }

    static string MainLightShadowFilter()
    {
        const string pcss = "Packages/com.unity.render-pipelines.universal/ShaderLibrary/NeuralLabPCSS.hlsl";
        return File.Exists(pcss) && File.ReadAllText(pcss).Contains("#define NEURAL_LAB_MAIN_LIGHT_PCSS 1") ? "PCSS" : "PCF";
    }

    static string Hash(string path)
    {
        if (!File.Exists(path)) return "missing";
        using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
    }
}
