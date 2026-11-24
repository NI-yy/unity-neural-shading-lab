using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Unity.Profiling;
using UnityEngine.Profiling;

namespace NeuralShadingLab
{
    // Explicitly opt-in in a built Player: -neuralLabBenchmark.
    [DefaultExecutionOrder(32000)]
    public sealed class BaselineRunner : MonoBehaviour
    {
        [Serializable]
        public sealed class Report
        {
            public string runId, status, scene, experiment, unityVersion, os, gpu, graphicsDeviceVersion, graphicsApi;
            public string quality, pipeline, colorSpace, buildInfo, timingSource;
            public string primaryGpuTimingSource;
            public bool smokeTest, developmentBuild, frameTimingEnabled;
            public int width, height, warmupFrames, requestedSamples, collectedSamples, validGpuSamples;
            public int simulationRate, seed, vSyncCount, msaa, qualityLevel;
            public int timingDrainFrames;
            public int gpuRecorderSamples, validGpuRecorderSamples;
            public bool gpuRecorderAvailable;
            public double gpuRecorderMedianMs, gpuRecorderP95Ms;
            public float renderScale;
            public double gpuMedianMs, gpuP95Ms, cpuMedianMs, cpuP95Ms;
            public BaselineConfigSnapshot configuration;
        }

        [Serializable]
        public sealed class BaselineConfigSnapshot
        {
            public CameraPose[] cameraPoses;
            public string[] disabledComponentTypes;
        }

        [Serializable]
        public sealed class CaptureState
        {
            public string method, colorSpace;
            public int frameCount, warmupFrames, width, height;
            public double simulationTime;
            public float captureDeltaTime, fieldOfView;
            public Vector3 cameraPosition;
            public Quaternion cameraRotation;
        }

        struct Row { public int queryFrame; public FrameTiming timing; }
        struct CounterRow { public int queryFrame; public double milliseconds; }
        readonly List<Row> rows = new List<Row>();
        readonly List<CounterRow> counterRows = new List<CounterRow>();
        ProfilerRecorder gpuRecorder;
        readonly FrameTiming[] recent = new FrameTiming[4];
        readonly WaitForEndOfFrame endOfFrame = new WaitForEndOfFrame();
        static readonly IComparer<FrameTiming> TimingComparer = Comparer<FrameTiming>.Create((a, b) => a.frameStartTimestamp.CompareTo(b.frameStartTimestamp));
        BaselineConfig config;
        Camera target;
        ulong lastTimestamp;
        int queryFrame;
        string output;
        string runId;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSplashScreen)]
        static void EnableGpuProfilingAtStartup()
        {
            var args = Environment.GetCommandLineArgs();
            if (!Debug.isDebugBuild || !args.Contains("-neuralLabGpuProfile")) return;
            int binaryArg = Array.IndexOf(args, "-neuralLabBinaryProfile");
            if (binaryArg >= 0 && binaryArg + 1 < args.Length)
            {
                var path = Path.GetFullPath(args[binaryArg + 1]);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                Profiler.logFile = path;
                Profiler.enableBinaryLog = true;
                Debug.Log("NEURAL_LAB_BINARY_PROFILER " + path);
            }
            Profiler.enabled = true;
            Profiler.SetAreaEnabled(ProfilerArea.GPU, true);
            Debug.Log("NEURAL_LAB_GPU_PROFILER_ENABLED " + Profiler.GetAreaEnabled(ProfilerArea.GPU));
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            var args = Environment.GetCommandLineArgs();
            if (Application.isEditor || (!args.Contains("-neuralLabBenchmark") && !args.Contains("-neuralLabCapture") && !args.Contains("-neuralLabProfile"))) return;
            new GameObject("Neural Lab Baseline Runner").AddComponent<BaselineRunner>();
        }

        IEnumerator Start()
        {
            config = Resources.Load<BaselineConfig>("NeuralLabBaseline");
            if (config == null || config.cameraPoses.Length == 0 || config.sampleCount < 1 ||
                config.warmupFrames < 1 || config.simulationRate < 1)
            { Fail("Missing or invalid baseline configuration."); yield break; }
            if (SceneManager.GetActiveScene().path != config.scenePath)
            { Fail("The built scene does not match the baseline configuration. Rebuild the Player."); yield break; }
            target = Camera.main;
            if (target == null) { Fail("The scene needs one enabled camera tagged MainCamera."); yield break; }
            var mainCameras = FindObjectsByType<Camera>(FindObjectsSortMode.None)
                .Count(c => c.enabled && c.CompareTag("MainCamera"));
            if (mainCameras != 1) { Fail("Expected exactly one active MainCamera."); yield break; }
            foreach (var component in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
                if (component != null && config.disableComponentTypes.Contains(component.GetType().FullName))
                    component.enabled = false;
            if (config.qualityLevel >= 0) QualitySettings.SetQualityLevel(config.qualityLevel, true);
            if (config.pipelineOverride != null)
            {
                GraphicsSettings.defaultRenderPipeline = config.pipelineOverride;
                QualitySettings.renderPipeline = config.pipelineOverride;
            }
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;
            Application.runInBackground = true;
            Time.captureDeltaTime = 1f / config.simulationRate;
            UnityEngine.Random.InitState(config.randomSeed);
            target.allowDynamicResolution = false;
            var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (urp != null && !Mathf.Approximately(urp.renderScale, 1f))
            { Fail("Set the selected URP asset Render Scale to 1 before benchmarking."); yield break; }
            if (target.GetUniversalAdditionalCameraData().antialiasing == AntialiasingMode.TemporalAntiAliasing)
            { Fail("Disable TAA for the initial baseline or implement a documented history reset protocol."); yield break; }
            Screen.SetResolution(config.width, config.height, FullScreenMode.Windowed);
            runId = DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ", CultureInfo.InvariantCulture);
            var args = Environment.GetCommandLineArgs();
            var outputIndex = Array.IndexOf(args, "-neuralLabOutput");
            var root = outputIndex >= 0 && outputIndex + 1 < args.Length
                ? args[outputIndex + 1] : Path.Combine(Application.persistentDataPath, "Benchmarks");
            output = Path.GetFullPath(Path.Combine(root, runId));
            Directory.CreateDirectory(output);
            CandidateProbe.Apply(args, target, config.cameraPoses[0], output);
            if (!FrameTimingManager.IsFeatureEnabled())
            { Fail("Frame Timing Stats is not enabled. Rebuild with the provided editor menu."); yield break; }
            rows.Capacity = config.sampleCount;
            counterRows.Capacity = config.sampleCount;
            gpuRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Render, "GPU Frame Time");
            var sampleIndices = new Dictionary<ulong, int>(config.sampleCount);
            // Resolution and startup work settle before the documented warmup begins.
            for (var i = 0; i < 10; i++) yield return null;
            if (Screen.width != config.width || Screen.height != config.height)
            { Fail("The actual render resolution differs from the configured resolution."); yield break; }

            if (args.Contains("-neuralLabProfile"))
            {
                gpuRecorder.Dispose();
                File.WriteAllText(Path.Combine(output, "profile-info.txt"), config.scenePath + "\n" + config.experiment + "\n" + Screen.width + "x" + Screen.height + "\n" + SystemInfo.graphicsDeviceType);
                Debug.Log("NEURAL_LAB_PROFILE_READY Static baseline is running. Attach Unity Profiler; this mode does not auto-exit.");
                yield break;
            }

            if (args.Contains("-neuralLabCapture"))
            {
                for (queryFrame = 0; queryFrame < config.warmupFrames; queryFrame++)
                { ApplyCameraPose(); yield return endOfFrame; }
                var screenshot = Path.Combine(output, "capture.png");
                bool synchronous = args.Contains("-neuralLabCaptureSynchronous");
                var captureState = new CaptureState
                {
                    method = synchronous ? "CaptureScreenshotAsTexture + EncodeToPNG; synchronous end-of-frame readback" : "CaptureScreenshot; asynchronous",
                    colorSpace = QualitySettings.activeColorSpace.ToString(), frameCount = Time.frameCount,
                    warmupFrames = config.warmupFrames, width = Screen.width, height = Screen.height,
                    simulationTime = Time.timeAsDouble, captureDeltaTime = Time.captureDeltaTime,
                    cameraPosition = target.transform.position, cameraRotation = target.transform.rotation, fieldOfView = target.fieldOfView
                };
                if (synchronous)
                {
                    var texture = ScreenCapture.CaptureScreenshotAsTexture();
                    try { File.WriteAllBytes(screenshot, texture.EncodeToPNG()); }
                    finally { Destroy(texture); }
                }
                else
                {
                    ScreenCapture.CaptureScreenshot(screenshot);
                    for (int i = 0; i < 120 && !File.Exists(screenshot); i++) yield return null;
                }
                File.WriteAllText(Path.Combine(output, "capture-state.json"), JsonUtility.ToJson(captureState, true));
                File.WriteAllText(Path.Combine(output, "capture-info.txt"), config.scenePath + "\n" + config.experiment + "\n" + Screen.width + "x" + Screen.height + "\nWarmup frames: " + config.warmupFrames);
                Debug.Log("NEURAL_LAB_CAPTURE " + screenshot);
                gpuRecorder.Dispose();
                // The opt-in synchronous capture waits for deferred texture destruction
                // and pending render work before shutting down the sample Player.
                if (synchronous) for (int i = 0; i < 10; i++) yield return endOfFrame;
                Application.Quit(File.Exists(screenshot) ? 0 : 2);
                yield break;
            }

            const int drainFrames = 8;
            int completedAt = -1;
            while ((completedAt < 0 || queryFrame < completedAt + drainFrames) && queryFrame < config.warmupFrames + config.sampleCount + 600)
            {
                ApplyCameraPose();
                FrameTimingManager.CaptureFrameTimings();
                yield return endOfFrame;
                if (queryFrame >= config.warmupFrames + drainFrames && counterRows.Count < config.sampleCount)
                    counterRows.Add(new CounterRow { queryFrame = queryFrame, milliseconds = gpuRecorder.Valid ? gpuRecorder.LastValue * 1e-6 : 0 });
                int count = (int)FrameTimingManager.GetLatestTimings((uint)recent.Length, recent);
                Array.Sort(recent, 0, count, TimingComparer);
                for (var i = 0; i < count; i++)
                {
                    var timing = recent[i];
                    if (timing.frameStartTimestamp == 0) continue;
                    // A timestamp can reappear with completed GPU data. Retain the original sample,
                    // and merge the newer data instead of permanently discarding a pending zero.
                    if (sampleIndices.TryGetValue(timing.frameStartTimestamp, out int index))
                    {
                        if (Valid(timing.gpuFrameTime))
                        {
                            var previous = rows[index]; previous.timing = timing; rows[index] = previous;
                        }
                        continue;
                    }
                    if (timing.frameStartTimestamp <= lastTimestamp) continue;
                    lastTimestamp = timing.frameStartTimestamp;
                    if (queryFrame >= config.warmupFrames + drainFrames && rows.Count < config.sampleCount)
                    {
                        sampleIndices.Add(timing.frameStartTimestamp, rows.Count);
                        rows.Add(new Row { queryFrame = queryFrame, timing = timing });
                        if (rows.Count == config.sampleCount) completedAt = queryFrame;
                    }
                }
                queryFrame++;
            }
            WriteResults();
            bool complete = rows.Count == config.sampleCount && (rows.All(r => Valid(r.timing.gpuFrameTime)) ||
                (counterRows.Count == config.sampleCount && counterRows.All(r => Valid(r.milliseconds))));
            gpuRecorder.Dispose();
            Application.Quit(complete ? 0 : 2);
        }

        void LateUpdate() { if (config != null && target != null) ApplyCameraPose(); }

        void ApplyCameraPose()
        {
            var poses = config.cameraPoses;
            // Replay the same simulation path independently of the measured frame rate.
            float progress = Mathf.Clamp01((queryFrame - config.warmupFrames) / (float)Math.Max(1, config.sampleCount - 1));
            float segment = progress * Math.Max(0, poses.Length - 1);
            int a = Mathf.Min(Mathf.FloorToInt(segment), poses.Length - 1);
            int b = Mathf.Min(a + 1, poses.Length - 1);
            target.transform.SetPositionAndRotation(Vector3.Lerp(poses[a].position, poses[b].position, segment - a),
                Quaternion.Slerp(poses[a].rotation, poses[b].rotation, segment - a));
            target.fieldOfView = Mathf.Lerp(poses[a].fieldOfView, poses[b].fieldOfView, segment - a);
            CandidateProbe.ApplyPose(target);
        }

        static bool Valid(double value) => value > 0 && !double.IsNaN(value) && !double.IsInfinity(value);
        static double Percentile(IEnumerable<double> values, double percentile)
        {
            var sorted = values.Where(Valid).OrderBy(x => x).ToArray();
            if (sorted.Length == 0) return 0;
            double offset = (sorted.Length - 1) * percentile;
            int a = (int)Math.Floor(offset), b = (int)Math.Ceiling(offset);
            return sorted[a] + (sorted[b] - sorted[a]) * (offset - a);
        }

        void WriteResults()
        {
            var csv = new StringBuilder("sample,query_frame,frame_start_timestamp,gpu_ms,cpu_ms,width_scale,height_scale,sync_interval\n");
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i]; var t = r.timing;
                csv.AppendFormat(CultureInfo.InvariantCulture, "{0},{1},{2},{3:R},{4:R},{5:R},{6:R},{7}\n",
                    i, r.queryFrame, t.frameStartTimestamp, t.gpuFrameTime, t.cpuFrameTime, t.widthScale, t.heightScale, t.syncInterval);
            }
            File.WriteAllText(Path.Combine(output, "frames.csv"), csv.ToString());
            var counterCsv = new StringBuilder("sample,query_frame,gpu_counter_ms\n");
            for (int i = 0; i < counterRows.Count; i++)
                counterCsv.AppendFormat(CultureInfo.InvariantCulture, "{0},{1},{2:R}\n", i, counterRows[i].queryFrame, counterRows[i].milliseconds);
            File.WriteAllText(Path.Combine(output, "gpu-counter.csv"), counterCsv.ToString());
            var validGpu = rows.Count(r => Valid(r.timing.gpuFrameTime));
            var validCounterGpu = counterRows.Count(r => Valid(r.milliseconds));
            bool useCounter = validCounterGpu == config.sampleCount && counterRows.Count == config.sampleCount;
            var gpuValues = useCounter ? counterRows.Select(r => r.milliseconds) : rows.Select(r => r.timing.gpuFrameTime);
            var report = new Report
            {
                runId = runId, status = rows.Count == config.sampleCount && (validGpu == config.sampleCount || useCounter) ? "complete" : "incomplete",
                scene = SceneManager.GetActiveScene().path, experiment = config.experiment, smokeTest = config.smokeTest,
                unityVersion = Application.unityVersion, os = SystemInfo.operatingSystem, gpu = SystemInfo.graphicsDeviceName,
                graphicsDeviceVersion = SystemInfo.graphicsDeviceVersion, graphicsApi = SystemInfo.graphicsDeviceType.ToString(),
                quality = QualitySettings.names[QualitySettings.GetQualityLevel()], qualityLevel = QualitySettings.GetQualityLevel(),
                pipeline = GraphicsSettings.currentRenderPipeline != null ? GraphicsSettings.currentRenderPipeline.name : "Built-in",
                colorSpace = QualitySettings.activeColorSpace.ToString(), developmentBuild = Debug.isDebugBuild,
                timingSource = "FrameTimingManager (milliseconds); instrumentation enabled for every variant; total frame only",
                primaryGpuTimingSource = useCounter ? "ProfilerRecorder: GPU Frame Time (nanoseconds converted to milliseconds; asynchronous aggregate samples, not aligned to FrameTiming timestamps)" : "FrameTimingManager",
                gpuRecorderAvailable = gpuRecorder.Valid, gpuRecorderSamples = counterRows.Count, validGpuRecorderSamples = validCounterGpu,
                gpuRecorderMedianMs = Percentile(counterRows.Select(r => r.milliseconds), .5),
                gpuRecorderP95Ms = Percentile(counterRows.Select(r => r.milliseconds), .95),
                frameTimingEnabled = FrameTimingManager.IsFeatureEnabled(), width = Screen.width, height = Screen.height,
                warmupFrames = config.warmupFrames, requestedSamples = config.sampleCount, collectedSamples = rows.Count,
                timingDrainFrames = 8,
                validGpuSamples = validGpu, simulationRate = config.simulationRate, seed = config.randomSeed,
                vSyncCount = QualitySettings.vSyncCount, msaa = QualitySettings.antiAliasing,
                renderScale = (GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset)?.renderScale ?? 1f,
                gpuMedianMs = Percentile(gpuValues, .5),
                gpuP95Ms = Percentile(gpuValues, .95),
                cpuMedianMs = Percentile(rows.Select(r => r.timing.cpuFrameTime), .5),
                cpuP95Ms = Percentile(rows.Select(r => r.timing.cpuFrameTime), .95),
                buildInfo = Resources.Load<TextAsset>("NeuralLabBuildInfo")?.text ?? "missing",
                configuration = new BaselineConfigSnapshot { cameraPoses = config.cameraPoses, disabledComponentTypes = config.disableComponentTypes }
            };
            File.WriteAllText(Path.Combine(output, "summary.json"), JsonUtility.ToJson(report, true));
            Debug.Log("NEURAL_LAB_RESULT " + output + " status=" + report.status);
        }

        static void Fail(string message) { Debug.LogError("NEURAL_LAB_ERROR " + message); Application.Quit(2); }
    }
}

