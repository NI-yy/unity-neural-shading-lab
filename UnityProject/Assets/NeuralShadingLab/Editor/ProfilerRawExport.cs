using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Profiling;
using UnityEditorInternal;
using UnityEngine;

public static class ProfilerRawExport
{
    [MenuItem("Neural Shading Lab/Export GPU Profiler Trial")]
    public static void Export()
    {
        var root = Path.GetFullPath(Path.Combine(Application.dataPath, "../.."));
        var dir = Path.Combine(root, "LocalData", "ProfilerTrials");
        var args = Environment.GetCommandLineArgs();
        var argIndex = Array.IndexOf(args, "-neuralLabRaw");
        var name = argIndex >= 0 && argIndex + 1 < args.Length ? args[argIndex + 1] : "attached-player";
        var input = Path.Combine(dir, name + ".raw");
        if (!ProfilerDriver.LoadProfile(input, false)) throw new IOException("Unable to load " + input);
        var threads = new StringBuilder("frame,thread_index,thread_name,thread_group,samples,frame_gpu_ms,frame_cpu_ms\n");
        var gpu = new StringBuilder("frame,thread_index,sample_index,name,gpu_ms\n");
        int last = ProfilerDriver.lastFrameIndex;
        int gpuThreads = 0;
        for (int frame = 0; frame <= last; frame++)
        {
            for (int thread = 0; thread < 128; thread++)
            {
                using (var view = ProfilerDriver.GetRawFrameDataView(frame, thread))
                {
                    if (!view.valid) break;
                    var isGpu = (view.threadName ?? "").IndexOf("GPU", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        (view.threadGroupName ?? "").IndexOf("GPU", StringComparison.OrdinalIgnoreCase) >= 0;
                    threads.Append(frame).Append(',').Append(thread).Append(',').Append(Quote(view.threadName)).Append(',')
                        .Append(Quote(view.threadGroupName)).Append(',').Append(view.sampleCount).Append(',')
                        .Append(view.frameGpuTimeMs.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append(',')
                        .Append(view.frameTimeMs.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append('\n');
                    if (!isGpu) continue;
                    gpuThreads++;
                    for (int i = 0; i < view.sampleCount; i++)
                        gpu.Append(frame).Append(',').Append(thread).Append(',').Append(i).Append(',')
                            .Append(Quote(view.GetSampleName(i))).Append(',')
                            .Append(view.GetSampleTimeMs(i).ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append('\n');
                }
            }
        }
        File.WriteAllText(Path.Combine(dir, name + "-threads.csv"), threads.ToString());
        File.WriteAllText(Path.Combine(dir, name + "-gpu-samples.csv"), gpu.ToString());
        Debug.Log("NEURAL_LAB_RAW_EXPORT frames=" + (last + 1) + " gpu_threads=" + gpuThreads);
    }

    [MenuItem("Neural Shading Lab/Connect Development Player Profiler")]
    public static void ConnectDevelopmentPlayer()
    {
        var ids = ProfilerDriver.GetAvailableProfilers();
        Debug.Log("NEURAL_LAB_PROFILER_AVAILABLE " + string.Join(",", ids));
        foreach (var id in ids)
        {
            if (id <= 0) continue;
            ProfilerDriver.connectedProfiler = id;
            ProfilerDriver.profileGPU = true;
            ProfilerDriver.enabled = true;
            Debug.Log("NEURAL_LAB_PROFILER_CONNECTED " + ProfilerDriver.connectedProfiler + " gpu=" + ProfilerDriver.profileGPU);
            return;
        }
        throw new InvalidOperationException("No Development Player profiler found");
    }
    [MenuItem("Neural Shading Lab/Save Connected Profiler Capture")]
    public static void SaveConnectedCapture()
    {
        var root = Path.GetFullPath(Path.Combine(Application.dataPath, "../.."));
        var dir = Path.Combine(root, "LocalData", "ProfilerTrials");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "attached-player.raw");
        int first = ProfilerDriver.firstFrameIndex;
        int last = ProfilerDriver.lastFrameIndex;
        var states = new System.Collections.Generic.Dictionary<GpuProfilingStatisticsAvailabilityStates, int>();
        for (int frame = first; frame <= last; frame++)
        {
            var state = ProfilerDriver.GetGpuStatisticsAvailabilityState(frame);
            states[state] = states.TryGetValue(state, out var n) ? n + 1 : 1;
        }
        var lines = new StringBuilder();
        foreach (var pair in states) lines.Append(pair.Key).Append('=').Append(pair.Value).Append(' ');
        Debug.Log("NEURAL_LAB_GPU_AVAILABILITY first=" + first + " last=" + last + " " + lines);
        if (!ProfilerDriver.SaveProfile(path)) throw new IOException("Unable to save " + path);
        Debug.Log("NEURAL_LAB_CONNECTED_CAPTURE " + path);
    }
    [MenuItem("Neural Shading Lab/Export GPU Hierarchy")]
    public static void ExportGpuHierarchy()
    {
        var root = Path.GetFullPath(Path.Combine(Application.dataPath, "../.."));
        var dir = Path.Combine(root, "LocalData", "ProfilerTrials");
        var input = Path.Combine(dir, "attached-player.raw");
        if (!ProfilerDriver.LoadProfile(input, false)) throw new IOException("Unable to load " + input);
        var output = new StringBuilder("frame,depth,name,gpu_ms,draw_calls,state\n");
        int rows = 0;
        for (int frame = ProfilerDriver.firstFrameIndex; frame <= ProfilerDriver.lastFrameIndex; frame++)
        {
            var state = ProfilerDriver.GetGpuStatisticsAvailabilityState(frame);
            if ((state & GpuProfilingStatisticsAvailabilityStates.Gathered) == 0) continue;
            using (var view = ProfilerDriver.GetHierarchyFrameDataView(frame, 0, HierarchyFrameDataView.ViewModes.Default, HierarchyFrameDataView.columnDontSort, false))
            {
                if (!view.valid) continue;
                var pending = new System.Collections.Generic.Stack<int>();
                pending.Push(view.GetRootItemID());
                var children = new System.Collections.Generic.List<int>();
                while (pending.Count > 0)
                {
                    int id = pending.Pop();
                    output.Append(frame).Append(',').Append(view.GetItemDepth(id)).Append(',').Append(Quote(view.GetItemName(id))).Append(',')
                        .Append(view.GetItemColumnDataAsSingle(id, GpuColumn("columnTotalGpuTime")).ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append(',')
                        .Append(view.GetItemColumnDataAsSingle(id, GpuColumn("columnDrawCalls")).ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append(',')
                        .Append(Quote(state.ToString())).Append('\n');
                    rows++;
                    children.Clear();
                    view.GetItemChildren(id, children);
                    for (int i = children.Count - 1; i >= 0; i--) pending.Push(children[i]);
                }
            }
        }
        File.WriteAllText(Path.Combine(dir, "attached-player-gpu-hierarchy.csv"), output.ToString());
        Debug.Log("NEURAL_LAB_GPU_HIERARCHY rows=" + rows);
    }
    [MenuItem("Neural Shading Lab/Enable GPU Usage Module")]
    public static void EnableGpuUsageModule()
    {
        var window = EditorWindow.GetWindow<ProfilerWindow>();
        var module = window.GetFrameTimeViewSampleSelectionController("GPU Usage");
        System.Reflection.PropertyInfo property = null;
        for (var type = module.GetType(); type != null && property == null; type = type.BaseType)
            property = type.GetProperty("active", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly);
        if (property == null) throw new InvalidOperationException("GPU module active property unavailable");
        property.SetValue(module, true);
        ProfilerDriver.profileGPU = true;
        Debug.Log("NEURAL_LAB_GPU_MODULE_ACTIVE " + property.GetValue(module) + " gpu=" + ProfilerDriver.profileGPU);
    }
    static int repeatRun;
    static int repeatFrames;
    static int mismatchedFrames;
    static bool issue3Batch;
    static string issue3RawDir;
    static DateTime batchWaitStarted;
    const int RepeatWarmup = 300;
    const int RepeatSamples = 1000;

    [MenuItem("Neural Shading Lab/Capture Three GPU Repeats")]
    public static void CaptureThreeRepeats()
    {
        var args = Environment.GetCommandLineArgs();
        issue3Batch = Array.IndexOf(args, "-neuralLabIssue3Batch") >= 0;
        int rawArg = Array.IndexOf(args, "-neuralLabIssue3RawDir");
        issue3RawDir = rawArg >= 0 && rawArg + 1 < args.Length ? Path.GetFullPath(args[rawArg + 1]) : null;
        int id = -1;
        foreach (var candidate in ProfilerDriver.GetAvailableProfilers())
            if (candidate > 0) { id = candidate; break; }
        if (id < 0)
        {
            if (!issue3Batch) throw new InvalidOperationException("Development Player is unavailable");
            batchWaitStarted = DateTime.UtcNow;
            EditorApplication.update -= WaitForBatchPlayer;
            EditorApplication.update += WaitForBatchPlayer;
            Debug.Log("NEURAL_LAB_ISSUE3_WAIT_FOR_PLAYER");
            return;
        }
        ProfilerDriver.connectedProfiler = id;
        try { EnableGpuUsageModule(); }
        catch (Exception error)
        {
            if (!issue3Batch) throw;
            Debug.LogWarning("NEURAL_LAB_GPU_MODULE_BATCH_UNAVAILABLE " + error);
        }
        ProfilerDriver.profileGPU = true;
        repeatRun = 1;
        BeginRepeat();
    }

    static void WaitForBatchPlayer()
    {
        foreach (var id in ProfilerDriver.GetAvailableProfilers())
        {
            if (id <= 0) continue;
            EditorApplication.update -= WaitForBatchPlayer;
            Debug.Log("NEURAL_LAB_ISSUE3_PLAYER_FOUND " + id);
            CaptureThreeRepeats();
            return;
        }
        if ((DateTime.UtcNow - batchWaitStarted).TotalSeconds < 90) return;
        EditorApplication.update -= WaitForBatchPlayer;
        Debug.LogError("NEURAL_LAB_ISSUE3_PLAYER_TIMEOUT");
        EditorApplication.Exit(1);
    }

    static void BeginRepeat()
    {
        ProfilerDriver.enabled = false;
        ProfilerDriver.ClearAllFrames();
        ProfilerDriver.profileGPU = true;
        repeatFrames = 0;
        mismatchedFrames = 0;
        ProfilerDriver.NewProfilerFrameRecorded -= OnRepeatFrame;
        ProfilerDriver.NewProfilerFrameRecorded += OnRepeatFrame;
        ProfilerDriver.enabled = true;
        Debug.Log("NEURAL_LAB_REPEAT_START run=" + repeatRun + " connected=" + ProfilerDriver.connectedProfiler + " gpu=" + ProfilerDriver.profileGPU);
    }

    static void OnRepeatFrame(int connectionId, int frameIndex)
    {
        if (connectionId != ProfilerDriver.connectedProfiler)
        {
            mismatchedFrames++;
            if (mismatchedFrames == 1) Debug.Log("NEURAL_LAB_REPEAT_CONNECTION_MISMATCH event=" + connectionId + " selected=" + ProfilerDriver.connectedProfiler);
            return;
        }
        repeatFrames++;
        if (repeatFrames < RepeatWarmup + RepeatSamples) return;
        ProfilerDriver.NewProfilerFrameRecorded -= OnRepeatFrame;
        EditorApplication.delayCall += FinishRepeat;
    }

    static void FinishRepeat()
    {
        ProfilerDriver.enabled = false;
        var root = Path.GetFullPath(Path.Combine(Application.dataPath, "../.."));
        var dir = issue3RawDir ?? Path.Combine(root, "LocalData", "ProfilerTrials");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "attached-repeat-" + repeatRun + ".raw");
        int first = ProfilerDriver.firstFrameIndex;
        int last = ProfilerDriver.lastFrameIndex;
        int gathered = 0, enabled = 0, missing = 0;
        for (int frame = first + RepeatWarmup; frame < first + RepeatWarmup + RepeatSamples && frame <= last; frame++)
        {
            var state = ProfilerDriver.GetGpuStatisticsAvailabilityState(frame);
            if ((state & GpuProfilingStatisticsAvailabilityStates.Gathered) != 0) gathered++;
            else if ((state & GpuProfilingStatisticsAvailabilityStates.Enabled) != 0) enabled++;
            else missing++;
        }
        if (!ProfilerDriver.SaveProfile(path)) throw new IOException("Unable to save " + path);
        Debug.Log("NEURAL_LAB_REPEAT_RESULT run=" + repeatRun + " first=" + first + " last=" + last + " gathered=" + gathered + " enabled_ungathered=" + enabled + " disabled=" + missing + " path=" + path);
        repeatRun++;
        if (repeatRun <= 3) BeginRepeat();
        else if (issue3Batch) EditorApplication.Exit(0);
    }
    static int GpuColumn(string name)
    {
        var flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.IgnoreCase;
        var type = typeof(HierarchyFrameDataView);
        var field = type.GetField(name, flags);
        if (field != null) return (int)field.GetValue(null);
        var property = type.GetProperty(name, flags);
        if (property != null) return (int)property.GetValue(null);
        var available = new StringBuilder();
        foreach (var item in type.GetFields(flags)) available.Append(item.Name).Append(',');
        foreach (var item in type.GetProperties(flags)) available.Append(item.Name).Append(',');
        throw new InvalidOperationException("GPU column unavailable: " + name + " available=" + available);
    }
    static string Quote(string value) => "\"" + (value ?? "").Replace("\"", "\"\"") + "\"";
}













