using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Voltage.BitmapFonts;
using Voltage.Project;

namespace Voltage;

/// <summary>Completed pipeline phases and cooperative main-thread loading frames.</summary>
public static class SceneLoading
{
    public static bool IsActive { get; private set; }
    public static string SceneName { get; private set; }
    public static string Phase { get; private set; }
    public static string Error { get; private set; }
    public static int Percent => _progress.Percent;
    public static int FramesPresented { get; private set; }
    public static double LongestFrameGapMs { get; private set; }
    internal static bool CanPump = true;
    private static SceneLoadProgress _progress = new();
    private static long _lastFrame;
    private static bool _pumping;
    private static bool _windowPumpAvailable = true;
    internal static int MainThreadId;
    private static Batcher _batcher;
    private static BitmapFont _font;
    private static string _fontPath;
    private static Scene _target;
    private static bool _updated;
    private static readonly string[] Phases = { "Reading scene", "Deserializing scene", "Scene settings", "Previous scene teardown", "Constructing entities", "Preparing scene", "Activating components", "First frame" };

    internal static void Begin(string scene)
    {
        SceneName = scene; Error = null; Phase = Phases[0];
        _progress = new SceneLoadProgress(); _target = null; _updated = false;
        FramesPresented = 0; LongestFrameGapMs = 0; _lastFrame = 0;
        IsActive = true;
        Pulse(true);
    }

    internal static void Target(Scene scene) => _target = scene;
    internal static bool IsTarget(Scene scene) => IsActive && scene == _target;
    internal static void CompletePhase(int phase)
    {
        if (!IsActive) return;
        _progress.CompletePhase(phase);
        Phase = Phases[_progress.CompletedPhases];
        Pulse();
    }
    internal static void Items(int phase, int completed, int total)
    {
        if (!IsActive || total <= 0) return;
        _progress.Items(phase, completed, total);
        Phase = Phases[Math.Clamp(phase, 0, 7)];
        Pulse();
    }
    internal static void Updated(Scene scene)
    {
        if (!IsTarget(scene)) return;
        _updated = true;
        _progress.MarkUpdated();
        CompletePhase(6);
    }
    internal static void Drawn(Scene scene)
    {
        if (!IsTarget(scene) || !_updated) return;
        _progress.MarkPresented();
        Phase = Error == null ? "Ready" : "Failed"; IsActive = false;
        scene.Content.ReleaseLoadingSources();
        SceneLoadDiagnostics.Drawn(scene);
    }
    internal static void Fail(string error)
    {
        if (!IsActive) return;
        error = Error ?? error;
        _progress.Fail(error);
        Error = error; Phase = "Failed"; IsActive = false;
        _target?.Content.ReleaseLoadingSources();
        SceneLoadDiagnostics.Fail(error);
    }

    internal static void ReportError(string error)
    {
        if (!IsActive) return;
        Error ??= error;
        _progress.Fail(error);
        SceneLoadDiagnostics.RecordError(error);
    }

    public static System.Text.Json.Nodes.JsonObject Status() => new()
    {
        ["active"] = IsActive, ["scene"] = SceneName, ["stage"] = Phase,
        ["percent"] = Percent, ["completedPhases"] = _progress.CompletedPhases,
        ["totalPhases"] = SceneLoadProgress.PhaseCount, ["ready"] = _progress.Ready,
        ["pendingEntities"] = _target?.Entities.EntitiesToAdd.Count ?? 0,
        ["pendingComponents"] = _target?.Entities.HasPendingStartupComponents ?? false,
        ["error"] = Error, ["framesPresented"] = FramesPresented, ["longestFrameGapMs"] = LongestFrameGapMs
    };

    /// <summary>Refreshes the small loading display without running gameplay or moving GPU work off thread.</summary>
    public static void Pulse(bool force = false)
    {
        if (!IsActive || !CanPump || _pumping || Core.IsEditorMode || Graphics.Instance == null || Environment.CurrentManagedThreadId != MainThreadId) return;
        long now = Stopwatch.GetTimestamp();
        double gap = _lastFrame == 0 ? 0 : (now - _lastFrame) * 1000.0 / Stopwatch.Frequency;
        if (!force && _lastFrame != 0 && gap < 80) return;
        _pumping = true;
        try
        {
            var device = Core.GraphicsDevice;
            var targets = device.GetRenderTargets();
            var viewport = device.Viewport;
            try
            {
                using var presentationTiming = SceneLoadDiagnostics.Measure("loading.display");
                if (_windowPumpAvailable)
                {
                    try { PumpWindowEvents(); }
                    catch (DllNotFoundException) { _windowPumpAvailable = false; }
                    catch (EntryPointNotFoundException) { _windowPumpAvailable = false; }
                }
                device.SetRenderTarget(null);
                device.Clear(ProjectSettings.Instance.Rendering.BackgroundClearColor);
                if (TransitionPresentation != null) TransitionPresentation.Render(Graphics.Instance.Batcher);
                else Draw();
                Core.Instance.PresentLoadingFrame();
                device.Present();
                FramesPresented++;
                long presented = Stopwatch.GetTimestamp();
                if (_lastFrame != 0) LongestFrameGapMs = Math.Max(LongestFrameGapMs, (presented - _lastFrame) * 1000.0 / Stopwatch.Frequency);
                _lastFrame = presented;
            }
            finally
            {
                device.SetRenderTargets(targets);
                device.Viewport = viewport;
            }
        }
        finally { _pumping = false; }
    }

    internal static void Draw()
    {
        if (TransitionPresentation != null) return;
        var settings = ProjectSettings.Instance.Loading;
        if (!settings.Enabled || Graphics.Instance == null || Core.IsEditorMode) return;
        if (_font == null || _fontPath != settings.Font)
        {
            _fontPath = settings.Font;
            try { _font = string.IsNullOrWhiteSpace(_fontPath) ? Graphics.Instance.BitmapFont : Core.Content.LoadBitmapFont(_fontPath); }
            catch (Exception ex) { Debug.Warn("Loading font: " + ex.Message); _font = Graphics.Instance.BitmapFont; }
        }
        _batcher ??= new Batcher(Core.GraphicsDevice);
        var size = Core.GraphicsDevice.PresentationParameters;
        Core.GraphicsDevice.Viewport = new Viewport(0, 0, size.BackBufferWidth, size.BackBufferHeight);
        float scale = Math.Max(1, (float)Math.Floor(size.BackBufferHeight / (float)Math.Max(1, ProjectSettings.Instance.DesignResolution.Height)));
        string label = Error == null ? settings.Label + " " + Percent + "%" : "Load failed: " + SceneName + " (" + Error.Substring(0, Math.Min(Error.Length, 100)) + ")";
        label = label.Replace('\r', ' ').Replace('\n', ' ');
        var characters = label.ToCharArray();
        for (int i = 0; i < characters.Length; i++) if (!_font.ContainsCharacter(characters[i])) characters[i] = _font.ContainsCharacter('?') ? '?' : ' ';
        label = new string(characters);
        while (label.Length > 3 && _font.MeasureString(label).X * scale > size.BackBufferWidth - settings.Margin * scale * 2) label = label.Substring(0, label.Length - 1);
        var measure = _font.MeasureString(label) * scale;
        var position = new Vector2(size.BackBufferWidth - measure.X - settings.Margin * scale, size.BackBufferHeight - measure.Y - settings.Margin * scale);
        position.X = Math.Max(settings.Margin * scale, position.X);
        _batcher.Begin(BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullNone);
        _font.DrawInto(_batcher, label, position + new Vector2(scale), Color.Black, 0, Vector2.Zero, new Vector2(scale), SpriteEffects.None, 0);
        _font.DrawInto(_batcher, label, position, settings.TextColor, 0, Vector2.Zero, new Vector2(scale), SpriteEffects.None, 0);
        double seconds = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
        for (int i = 0; Error == null && i < Math.Clamp(settings.Sparks, 0, 8); i++)
        {
            float life = (float)((seconds * settings.SparkSpeed + i * 0.31) % 1);
            var spark = position + new Vector2(-8 - life * 10, 5 - life * 7 + i * 2) * scale;
            _batcher.DrawRect((float)Math.Round(spark.X), (float)Math.Round(spark.Y), scale, scale, settings.SparkColor * (1 - life));
        }
        _batcher.End();
    }

    private static SceneTransition TransitionPresentation => Error == null &&
        Core.Instance?.SceneTransition is { HideLoadingScreen: true, PreviousSceneRender: { IsDisposed: false } } transition
            ? transition : null;

    [DllImport("SDL2", EntryPoint = "SDL_PumpEvents", CallingConvention = CallingConvention.Cdecl)]
    private static extern void PumpWindowEvents();

}
