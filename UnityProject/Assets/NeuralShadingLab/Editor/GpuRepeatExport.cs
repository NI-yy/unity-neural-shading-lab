using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.Profiling;
using UnityEditorInternal;
using UnityEngine;

public static class GpuRepeatExport
{
    static readonly string[] Markers = {
        "PlayerLoop", "ExecuteRenderGraph", "Draw Main Light Shadowmap", "DrawDepthNormalPrepass",
        "DrawOpaqueObjects", "DrawTransparentObjects", "SSAO", "Bloom", "RG_UberPost",
        "Blit Color Full Screen", "CopyDepth", "Blit Color LUT", "Draw Depth Only",
        "CopyColor", "UpdateOccluders", "InstanceOcclusionTest", "GPUResidentInstanceBatcher.UpdateSpeedTreeWindAndUploadWindParamsToGPU"
    };

    [MenuItem("Neural Shading Lab/Export Three GPU Repeats")]
    public static void Export()
    {
        var root = Path.GetFullPath(Path.Combine(Application.dataPath, "../.."));
        var inputDir = Path.Combine(root, "LocalData", "ProfilerTrials");
        var args = Environment.GetCommandLineArgs();
        int inputArg = Array.IndexOf(args, "-neuralLabIssue3Input");
        if (inputArg >= 0 && inputArg + 1 < args.Length)
            inputDir = Path.GetFullPath(args[inputArg + 1]);
        int singleArg = Array.IndexOf(args, "-neuralLabIssue3SingleFile");
        bool singleFile = singleArg >= 0 && singleArg + 1 < args.Length;
        bool allFrames = Array.IndexOf(args, "-neuralLabIssue3AllFrames") >= 0;
        int outputArg = Array.IndexOf(args, "-neuralLabIssue3Output");
        var outputDir = outputArg >= 0 && outputArg + 1 < args.Length
            ? Path.GetFullPath(args[outputArg + 1]) : Path.Combine(root, "Benchmarks", "GpuProfiler");
        Directory.CreateDirectory(outputDir);
        var output = new StringBuilder("run,frame,state,frame_start_ms");
        foreach (var marker in Markers) output.Append(',').Append(Quote(marker));
        output.Append('\n');
        int gpuColumn = GetGpuColumn();
        for (int run = 1; run <= (singleFile ? 1 : 3); run++)
        {
            var input = singleFile ? Path.GetFullPath(args[singleArg + 1]) : Path.Combine(inputDir, "attached-repeat-" + run + ".raw");
            if (!ProfilerDriver.LoadProfile(input, false)) throw new IOException("Unable to load " + input);
            int first = ProfilerDriver.firstFrameIndex;
            int last = ProfilerDriver.lastFrameIndex;
            if (!allFrames && last < first + 1299) throw new InvalidOperationException("Capture is shorter than 1300 frames: " + input + " first=" + first + " last=" + last);
            int gathered = 0;
            for (int frame = allFrames ? first : first + 300; frame < (allFrames ? last + 1 : first + 1300); frame++)
            {
                var state = ProfilerDriver.GetGpuStatisticsAvailabilityState(frame);
                bool valid = (state & GpuProfilingStatisticsAvailabilityStates.Gathered) != 0;
                var times = new Dictionary<string, float>();
                if (valid)
                {
                    gathered++;
                    using (var view = ProfilerDriver.GetHierarchyFrameDataView(frame, 0, HierarchyFrameDataView.ViewModes.Default, HierarchyFrameDataView.columnDontSort, false))
                    {
                        if (!view.valid) throw new InvalidOperationException("GPU hierarchy unavailable: " + input + " frame=" + frame);
                        var pending = new Stack<int>();
                        var children = new List<int>();
                        pending.Push(view.GetRootItemID());
                        while (pending.Count > 0)
                        {
                            int id = pending.Pop();
                            string name = view.GetItemName(id);
                            int depth = view.GetItemDepth(id);
                            foreach (var marker in Markers)
                            {
                                if (name != marker || (marker == "PlayerLoop" ? depth != 0 : marker == "ExecuteRenderGraph" ? depth != 7 : depth != 8)) continue;
                                float ms = view.GetItemColumnDataAsSingle(id, gpuColumn);
                                times[marker] = times.TryGetValue(marker, out var old) ? old + ms : ms;
                            }
                            children.Clear();
                            view.GetItemChildren(id, children);
                            for (int i = children.Count - 1; i >= 0; i--) pending.Push(children[i]);
                        }
                    }
                }
                double frameStartMs;
                using (var raw = ProfilerDriver.GetRawFrameDataView(frame, 0))
                    frameStartMs = raw.valid ? raw.frameStartTimeMs : 0;
                output.Append(run).Append(',').Append(frame - first).Append(',').Append(Quote(state.ToString())).Append(',');
                if (frameStartMs > 0) output.Append(frameStartMs.ToString("R", CultureInfo.InvariantCulture));
                foreach (var marker in Markers)
                {
                    output.Append(',');
                    if (valid && times.TryGetValue(marker, out var ms)) output.Append(ms.ToString("R", CultureInfo.InvariantCulture));
                }
                output.Append('\n');
            }
            Debug.Log("NEURAL_LAB_REPEAT_EXPORT run=" + run + " first=" + first + " last=" + last + " gathered=" + gathered);
        }
        var path = Path.Combine(outputDir, "repeat-frame-times.csv");
        File.WriteAllText(path, output.ToString(), new UTF8Encoding(false));
        Debug.Log("NEURAL_LAB_REPEAT_EXPORT_COMPLETE " + path);
    }

    static int GetGpuColumn()
    {
        var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.IgnoreCase;
        var field = typeof(HierarchyFrameDataView).GetField("columnTotalGpuTime", flags);
        if (field != null) return (int)field.GetValue(null);
        var property = typeof(HierarchyFrameDataView).GetProperty("columnTotalGpuTime", flags);
        if (property != null) return (int)property.GetValue(null);
        throw new InvalidOperationException("GPU time column unavailable");
    }

    static string Quote(string value) => "\"" + (value ?? "").Replace("\"", "\"\"") + "\"";
}
