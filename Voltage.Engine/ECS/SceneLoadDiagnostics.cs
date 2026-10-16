using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace Voltage;

/// <summary>Bounded scene-load timing records, enabled in Debug or with VOLTAGE_LOADING_TRACE=1.</summary>
public static class SceneLoadDiagnostics
{
#if DEBUG
    public static bool Enabled { get; set; } = true;
#else
    public static bool Enabled { get; set; } = Environment.GetEnvironmentVariable("VOLTAGE_LOADING_TRACE") == "1";
#endif
    private static readonly Queue<JsonObject> History = new();
    private static readonly Queue<JsonObject> Actions = new();
    private static readonly DateTime StartedUtc = System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime();
    private static readonly string BuildId = ReadBuildId();
    private static Trace _active;
    private static int _sequence;
    internal sealed class Trace
    {
        public int Id;
        public long Start;
        public string Scene, Previous, Stage = "request", Error, SceneHash;
        public Scene Target;
        public bool Updated;
        public readonly JsonArray Events = new(), Slow = new(), Preceding = new();
        public readonly JsonArray Errors = new();
        public readonly Dictionary<string, (int count, double ms)> Totals = new();
        public int Dropped;
    }

    public static void RecordAction(string action)
    {
        if (!Enabled) return;
        if (action.Length > 512) action = action.Substring(0, 512);
        Actions.Enqueue(new JsonObject { ["action"] = action, ["sinceStartupMs"] = (DateTime.UtcNow - StartedUtc).TotalMilliseconds });
        while (Actions.Count > 32) Actions.Dequeue();
    }

    internal static void Begin(string scene, string previous)
    {
        if (!Enabled) return;
        if (_active != null) Fail("Superseded by another scene request");
        _active = new Trace { Id = ++_sequence, Start = Stopwatch.GetTimestamp(), Scene = scene, Previous = previous };
        foreach (var action in Actions) _active.Preceding.Add(action.DeepClone());
        RecordAction("load " + scene);
    }

    internal static void Target(Scene scene) { if (_active != null) _active.Target = scene; }
    internal static void Identity(string json) { if (_active != null) _active.SceneHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))); }
    internal static void RecordError(string error)
    {
        if (_active == null) return;
        if (error.Length > 1024) error = error.Substring(0, 1024);
        _active.Error ??= error;
        if (_active.Errors.Count < 32) _active.Errors.Add((JsonNode)JsonValue.Create(error));
    }
    internal static void Updated(Scene scene) { if (_active?.Target == scene) _active.Updated = true; }
    internal static void Drawn(Scene scene)
    {
        if (_active?.Target != scene || !_active.Updated) return;
        _active.Stage = _active.Error == null ? "ready" : "failed";
        Finish();
    }
    internal static void Fail(string error)
    {
        if (_active == null) return;
        _active.Stage = "failed";
        _active.Error = error;
        Finish();
    }

    public static Scope Measure(string stage, string operation = null) => _active == null ? default : new Scope(_active, stage, operation);
    internal static Scope MeasureComponent(string stage, Entity entity, Component component) =>
        _active == null ? default : Measure(stage, entity.Name + "/" + component.GetType().Name);

    public readonly struct Scope : IDisposable
    {
        private readonly Trace _trace;
        private readonly long _start;
        private readonly string _stage, _operation;
        internal Scope(Trace trace, string stage, string operation)
        {
            _trace = trace; _start = Stopwatch.GetTimestamp(); _stage = stage; _operation = operation;
            trace.Stage = stage;
        }
        public void Dispose()
        {
            if (_trace == null) return;
            double ms = Ms(_start);
            var total = _trace.Totals.GetValueOrDefault(_stage);
            _trace.Totals[_stage] = (total.count + 1, total.ms + ms);
            var entry = new JsonObject { ["stage"] = _stage, ["operation"] = _operation,
                ["startMs"] = (_start - _trace.Start) * 1000.0 / Stopwatch.Frequency, ["elapsedMs"] = ms };
            if (_trace.Events.Count < 2048) _trace.Events.Add((JsonNode)entry); else _trace.Dropped++;
            if (ms >= 5)
            {
                _trace.Slow.Add(entry.DeepClone());
                if (_trace.Slow.Count > 64)
                {
                    int smallest = 0;
                    for (int i = 1; i < _trace.Slow.Count; i++)
                        if ((double)_trace.Slow[i]["elapsedMs"] < (double)_trace.Slow[smallest]["elapsedMs"]) smallest = i;
                    _trace.Slow.RemoveAt(smallest);
                }
            }
        }
    }

    private static double Ms(long since) => (Stopwatch.GetTimestamp() - since) * 1000.0 / Stopwatch.Frequency;
    private static string ReadBuildId()
    {
        if (!Enabled || string.IsNullOrEmpty(Environment.ProcessPath)) return null;
        try
        {
            var assembly = Path.ChangeExtension(Environment.ProcessPath, ".dll");
            using var stream = File.OpenRead(File.Exists(assembly) ? assembly : Environment.ProcessPath);
            var identity = Convert.ToHexString(SHA256.HashData(stream));
            var engine = Path.Combine(Path.GetDirectoryName(Environment.ProcessPath), "Voltage.dll");
            if (!File.Exists(engine)) return identity;
            using var engineStream = File.OpenRead(engine);
            return identity + ":" + Convert.ToHexString(SHA256.HashData(engineStream));
        }
        catch (IOException) { return null; }
    }
    private static JsonObject Snapshot(Trace trace, bool details = true)
    {
        var totals = new JsonArray();
        foreach (var pair in trace.Totals)
            totals.Add((JsonNode)new JsonObject { ["stage"] = pair.Key, ["count"] = pair.Value.count, ["inclusiveMs"] = pair.Value.ms });
        return new JsonObject { ["id"] = trace.Id, ["scene"] = trace.Scene, ["previousScene"] = trace.Previous,
            ["stage"] = trace.Stage, ["error"] = trace.Error, ["elapsedMs"] = Ms(trace.Start),
            ["sinceStartupMs"] = (DateTime.UtcNow - StartedUtc).TotalMilliseconds, ["configuration"] = Configuration,
            ["engineVersion"] = VoltageVersion.Engine, ["buildId"] = BuildId,
            ["engineAssemblyVersion"] = typeof(Core).Assembly.GetName().Version?.ToString(),
            ["game"] = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name,
            ["gameVersion"] = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version?.ToString(),
            ["nativeAot"] = !System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported,
            ["sceneSha256"] = trace.SceneHash, ["progress"] = SceneLoading.Status(),
            ["sceneKey"] = trace.Target?.LevelName, ["compiledAssets"] = Assets.CompiledContentIndex.Count > 0,
            ["stages"] = totals, ["precedingActions"] = trace.Preceding.DeepClone(),
            ["events"] = details ? OrderedEvents(trace.Events) : null, ["errors"] = trace.Errors.DeepClone(),
            ["slowOperations"] = trace.Slow.DeepClone(), ["droppedEvents"] = trace.Dropped };
    }
    private static JsonArray OrderedEvents(JsonArray events)
    {
        var result = new JsonArray();
        foreach (var entry in events.OrderBy(e => (double)e["startMs"])) result.Add(entry.DeepClone());
        return result;
    }
#if EDITOR
    private const string Configuration = "Editor";
#elif DEBUG
    private const string Configuration = "Debug";
#else
    private const string Configuration = "Release";
#endif
    private static void Finish()
    {
        var snapshot = Snapshot(_active);
        History.Enqueue(snapshot);
        while (History.Count > 8) History.Dequeue();
        _active = null;
        var directory = Environment.GetEnvironmentVariable("VOLTAGE_LOADING_TRACE_DIRECTORY");
        if (string.IsNullOrWhiteSpace(directory)) return;
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, $"load-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}-{(int)snapshot["id"]}.json"), snapshot.ToJsonString());
        }
        catch (Exception ex) { Voltage.Debug.Warn("Could not write loading trace: " + ex.Message); }
    }

    public static JsonObject Status()
    {
        var last = History.Count == 0 ? null : (JsonObject)History.Last().DeepClone();
        last?.Remove("events");
        return new JsonObject { ["enabled"] = Enabled, ["progress"] = SceneLoading.Status(),
            ["active"] = _active == null ? null : Snapshot(_active, false), ["last"] = last };
    }
    public static JsonArray GetHistory()
    {
        var result = new JsonArray();
        foreach (var trace in History) result.Add(trace.DeepClone());
        return result;
    }
}
