using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using NeuralShadingLab;
using UnityEditor;
using UnityEditor.Profiling;
using UnityEditor.SceneManagement;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Rendering;

public static class CandidateAudit
{
    static readonly CultureInfo CI = CultureInfo.InvariantCulture;
    static string Root => Path.GetFullPath(Path.Combine(Application.dataPath, "../.."));
    static string Out => Path.Combine(Root, "Benchmarks", "Candidates");
    sealed class Series { public int depth; public string name; public double[] gpu = Enumerable.Repeat(double.NaN, 1000).ToArray(); public double[] draws = new double[1000]; }
    static string Q(string s) => "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";
    static string N(double x) => x.ToString("R", CI);
    static double P(IEnumerable<double> input, double p)
    { var x = input.OrderBy(v => v).ToArray(); if (x.Length == 0) return double.NaN; double i = (x.Length - 1) * p; return x[(int)Math.Floor(i)] + (x[(int)Math.Ceiling(i)] - x[(int)Math.Floor(i)]) * (i - Math.Floor(i)); }
    static int Column(string name)
    { var f = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static; var field = typeof(HierarchyFrameDataView).GetField(name, f); if (field != null) return (int)field.GetValue(null); return (int)typeof(HierarchyFrameDataView).GetProperty(name, f).GetValue(null); }

    public static void Export()
    {
        var args = Environment.GetCommandLineArgs();
        Func<string, string, string> option = (key, fallback) => { int i = Array.IndexOf(args, key); return i >= 0 && i + 1 < args.Length ? Path.GetFullPath(args[i + 1]) : fallback; };
        string dataDir = option("-candidateHierarchyData", Path.Combine(Root, "Benchmarks/Issue3/binary/reference"));
        string rawDir = option("-candidateHierarchyRaw", Path.Combine(Root, "LocalData/Issue3Binary/reference"));
        string outputDir = option("-candidateHierarchyOutput", Out);
        Directory.CreateDirectory(outputDir);
        int gpuColumn = Column("columnTotalGpuTime"), drawColumn = Column("columnDrawCalls");
        var output = new StringBuilder("run,depth,path,name,requested,valid,missing,median_ms,p95_ms,mean_ms,median_draw_calls\n");
        for (int run = 1; run <= 3; run++)
        {
            var lines = File.ReadAllLines(Path.Combine(dataDir, "run-" + run + "/matched-frames.csv"));
            var header = lines[0].Split(','); int timeColumn = Array.IndexOf(header, "profiler_qpc_ms");
            if (timeColumn < 0) timeColumn = Array.IndexOf(header, "qpc_ms");
            if (timeColumn < 0) throw new InvalidOperationException("Missing QPC column");
            var selected = lines.Skip(1).Select(line => double.Parse(line.Split(',')[timeColumn], CI)).ToArray();
            if (selected.Length != 1000) throw new InvalidOperationException("Expected 1000 selected frames");
            var input = Path.Combine(rawDir, "attached-repeat-" + run + ".raw");
            if (!ProfilerDriver.LoadProfile(input, false)) throw new IOException(input);
            var data = new Dictionary<string, Series>();
            var seen = new HashSet<int>();
            var sample = new StringBuilder("depth,path,name,gpu_ms,draw_calls\n");
            for (int frame = ProfilerDriver.firstFrameIndex; frame <= ProfilerDriver.lastFrameIndex; frame++)
            {
                double timestamp;
                using (var raw = ProfilerDriver.GetRawFrameDataView(frame, 0)) { if (!raw.valid) continue; timestamp = raw.frameStartTimeMs; }
                int index = Array.BinarySearch(selected, timestamp);
                if (index < 0) { int insertion = ~index; index = insertion == selected.Length ? insertion - 1 : insertion; if (insertion > 0 && Math.Abs(selected[insertion - 1] - timestamp) < Math.Abs(selected[index] - timestamp)) index = insertion - 1; }
                if (Math.Abs(selected[index] - timestamp) > .01) continue;
                if (!seen.Add(index)) throw new InvalidOperationException("Duplicate selected frame " + index);
                var state = ProfilerDriver.GetGpuStatisticsAvailabilityState(frame);
                if ((state & GpuProfilingStatisticsAvailabilityStates.Gathered) == 0) throw new InvalidOperationException("Not gathered");
                using (var view = ProfilerDriver.GetHierarchyFrameDataView(frame, 0, HierarchyFrameDataView.ViewModes.Default, HierarchyFrameDataView.columnDontSort, false))
                {
                    if (!view.valid) throw new InvalidOperationException("Invalid GPU hierarchy");
                    var pending = new Stack<(int, string)>(); pending.Push((view.GetRootItemID(), ""));
                    var children = new List<int>();
                    while (pending.Count > 0)
                    {
                        var item = pending.Pop(); int id = item.Item1;
                        string name = view.GetItemName(id), path = item.Item2 + "/" + name; int depth = view.GetItemDepth(id);
                        double ms = view.GetItemColumnDataAsSingle(id, gpuColumn), draws = view.GetItemColumnDataAsSingle(id, drawColumn);
                        if (!data.TryGetValue(path, out var series)) data[path] = series = new Series { depth = depth, name = name };
                        series.gpu[index] = double.IsNaN(series.gpu[index]) ? ms : series.gpu[index] + ms; series.draws[index] += draws;
                        if (index == 100) sample.Append(depth).Append(',').Append(Q(path)).Append(',').Append(Q(name)).Append(',').Append(N(ms)).Append(',').Append(N(draws)).Append('\n');
                        children.Clear(); view.GetItemChildren(id, children); for (int j = children.Count - 1; j >= 0; j--) pending.Push((children[j], path));
                    }
                }
            }
            if (seen.Count != 1000) throw new InvalidOperationException("Missing selected frames " + seen.Count);
            foreach (var pair in data.OrderBy(p => p.Value.depth).ThenByDescending(p => P(p.Value.gpu.Where(v => v > 0), .5)))
            {
                var s = pair.Value; var valid = s.gpu.Where(v => v > 0 && !double.IsInfinity(v)).ToArray();
                output.Append(run).Append(',').Append(s.depth).Append(',').Append(Q(pair.Key)).Append(',').Append(Q(s.name)).Append(",1000,").Append(valid.Length).Append(',').Append(1000 - valid.Length).Append(',').Append(N(P(valid, .5))).Append(',').Append(N(P(valid, .95))).Append(',').Append(N(valid.Length > 0 ? valid.Average() : double.NaN)).Append(',').Append(N(P(s.draws, .5))).Append('\n');
            }
            File.WriteAllText(Path.Combine(outputDir, "sample-hierarchy-run-" + run + ".csv"), sample.ToString());
            Debug.Log("CANDIDATE_AUDIT_HIERARCHY run=" + run + " frames=" + seen.Count + " paths=" + data.Count);
        }
        File.WriteAllText(Path.Combine(outputDir, "baseline-hierarchy-summary.csv"), output.ToString());
        if (!args.Contains("-candidateSkipInventory")) Inventory();
    }

    public static void Inventory()
    {
        Directory.CreateDirectory(Out);
        var config = Resources.Load<BaselineConfig>("NeuralLabBaseline");
        EditorSceneManager.OpenScene(config.scenePath);
        var camera = Camera.main; var pose = config.cameraPoses[0]; camera.transform.SetPositionAndRotation(pose.position, pose.rotation); camera.fieldOfView = pose.fieldOfView;
        var planes = GeometryUtility.CalculateFrustumPlanes(camera);
        var csv = new StringBuilder("object_path,active,frustum_candidate,renderer_type,triangles,distance,shader,material\n");
        foreach (var r in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
        {
            string path = r.name; for (var parent = r.transform.parent; parent != null; parent = parent.parent) path = parent.name + "/" + path;
            var filter = r.GetComponent<MeshFilter>();
            var mesh = r is SkinnedMeshRenderer skinned ? skinned.sharedMesh : filter != null ? filter.sharedMesh : null;
            ulong triangles = 0; if (mesh != null) for (int s = 0; s < mesh.subMeshCount; s++) triangles += mesh.GetIndexCount(s) / 3;
            bool active = r.enabled && r.gameObject.activeInHierarchy, inView = active && GeometryUtility.TestPlanesAABB(planes, r.bounds);
            foreach (var m in r.sharedMaterials)
                csv.Append(Q(path)).Append(',').Append(active).Append(',').Append(inView).Append(',').Append(Q(r.GetType().Name)).Append(',').Append(triangles).Append(',').Append(N(Vector3.Distance(camera.transform.position, r.bounds.center))).Append(',').Append(Q(m?.shader?.name)).Append(',').Append(Q(m?.name)).Append('\n');
        }
        File.WriteAllText(Path.Combine(Out, "renderer-inventory.csv"), csv.ToString());
        Debug.Log("CANDIDATE_AUDIT_COMPLETE " + Out);
    }
}
