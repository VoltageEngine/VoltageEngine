using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Voltage.Aseprite;
using Voltage.DeferredLighting;
using Voltage.Editor.Gateway;
using Voltage.Editor.Inspectors.TypeInspectors;
using Voltage.Materials;
using Voltage.Sprites;

namespace Voltage.Editor.Lighting;

/// <summary>A light with its private entity and undo-free inspectors.</summary>
internal sealed class PreviewLight
{
    public DeferredLight Light;
    public Entity Entity;
    public List<AbstractTypeInspector> Inspectors;
    public string Name;
}

/// <summary>A game material offered with [LightPreviewMaterial].</summary>
internal sealed record PreviewMaterial(string Name, Func<SpriteAnimator, IReadOnlyDictionary<string, SpriteAnimation>, DeferredAnimatedMaterial> Build);

/// <summary>Renders one Aseprite sprite through the real DeferredLightingRenderer in a scene that never becomes active.</summary>
internal sealed class LightPreviewSession : IDisposable
{
    private const int SpriteLayer = 0, LightLayer = 100, Padding = 96, MaxCanvas = 4096;
    private const string WholeFile = "All frames";

    private readonly Scene _scene = new();
    private readonly DeferredLightingRenderer _renderer;
    private Entity _actor;
    private SpriteAnimator _animator;
    private DeferredAnimatedMaterial _material;
    private DateTime _stamp;
    private Assembly _scripts = Core.LatestScriptAssembly;
    private int _canvasWidth = 128, _canvasHeight = 128;

    public readonly List<PreviewLight> Lights = new();
    public readonly List<string> Layers = new(), NormalChoices = new(), GlowChoices = new(), Tags = new();
    public readonly HashSet<string> Diffuse = new(), Normals = new(), Glow = new();
    public string Path { get; private set; }
    public string Tag { get; set; } = WholeFile;
    public int Frame { get; set; }
    public int FrameCount => _animator?.Animations != null && _animator.Animations.TryGetValue(Tag, out var a) ? a.Sprites.Length : 0;
    public float FrameRate => FrameCount > 0 ? _animator.Animations[Tag].FrameRates[Math.Clamp(Frame, 0, FrameCount - 1)] : 10;
    public Color Ambient = new(51, 51, 51);
    public Color Background = new(24, 26, 32);
    public PreviewMaterial Material { get; set; }
    public string Error { get; private set; }
    public RenderTarget2D Target { get; private set; }

    public LightPreviewSession()
    {
        _renderer = new DeferredLightingRenderer(0, LightLayer, SamplerState.PointClamp, SpriteLayer) { ShouldDebugRender = false };
        _scene.AddRenderer(_renderer);
    }

    private static Assembly _scannedScripts;
    private static bool _scanned;
    private static List<(Type Type, string Name)> _lightTypes;
    private static List<PreviewMaterial> _materials;

    /// <summary>Every light the engine and the loaded game offer, with its list name.</summary>
    public static IReadOnlyList<(Type Type, string Name)> LightTypes() { Scan(); return _lightTypes; }

    /// <summary>Static methods marked [LightPreviewMaterial] in the engine and the current scripts.</summary>
    public static IReadOnlyList<PreviewMaterial> Materials() { Scan(); return _materials; }

    private static void Scan()
    {
        if (_scanned && _scannedScripts == Core.LatestScriptAssembly) return;
        _scanned = true;
        _scannedScripts = Core.LatestScriptAssembly;
        _lightTypes = GatewayValues.ConcreteTypes(typeof(DeferredLight))
            .Select(type => (Type: type, Info: type.GetCustomAttribute<LightPreviewAttribute>(false)))
            .Where(item => item.Info?.Hidden != true)
            .Select(item => (item.Type, item.Info?.DisplayName ?? item.Type.Name))
            .OrderBy(item => item.Item2, StringComparer.OrdinalIgnoreCase).ToList();
        _materials = FindMaterials();
    }

    private static List<PreviewMaterial> FindMaterials()
    {
        var found = new List<PreviewMaterial>();
        foreach (var assembly in new[] { typeof(DeferredLight).Assembly, Core.LatestScriptAssembly }.Where(a => a != null).Distinct())
        {
            Type[] types;
            try { types = assembly.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t != null).ToArray(); }
            foreach (var type in types)
                foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly))
                {
                    var marker = method.GetCustomAttribute<LightPreviewMaterialAttribute>();
                    if (marker == null) continue;
                    var build = Delegate.CreateDelegate(typeof(Func<SpriteAnimator, IReadOnlyDictionary<string, SpriteAnimation>, DeferredAnimatedMaterial>), method, false);
                    if (build != null) found.Add(new PreviewMaterial(marker.DisplayName, (Func<SpriteAnimator, IReadOnlyDictionary<string, SpriteAnimation>, DeferredAnimatedMaterial>)build));
                    else Debug.Warn($"[Light Preview] {type.Name}.{method.Name} has the wrong signature for [LightPreviewMaterial]");
                }
        }
        return found;
    }

    /// <summary>Opens a file and picks every layer the conventions name.</summary>
    public void Open(string path)
    {
        Path = System.IO.Path.GetFullPath(path);
        _scene.Content.EvictCachedAsset(Path);
        Error = null;
        Layers.Clear(); NormalChoices.Clear(); GlowChoices.Clear(); Tags.Clear();
        Diffuse.Clear(); Normals.Clear(); Glow.Clear();
        try
        {
            var file = _scene.Content.LoadAsepriteFileForRendering(Path);
            foreach (var layer in file.Layers.Where(l => l is not AsepriteGroupLayer))
            {
                if (layer.Name.StartsWith("Normals-", StringComparison.OrdinalIgnoreCase) || layer.Name.Equals("Normals", StringComparison.OrdinalIgnoreCase))
                {
                    NormalChoices.Add(layer.Name);
                    if (!System.Text.RegularExpressions.Regex.IsMatch(layer.Name, "-gen\\d*$")) Normals.Add(layer.Name);
                }
                else if (layer.Name.StartsWith("Glow-", StringComparison.OrdinalIgnoreCase)) { GlowChoices.Add(layer.Name); Glow.Add(layer.Name); }
                else
                {
                    Layers.Add(layer.Name);
                    if (layer.IsVisible) Diffuse.Add(layer.Name);
                }
            }
            Tags.Add(WholeFile);
            Tags.AddRange(file.Tags.Select(t => t.Name));
            if (!Tags.Contains(Tag)) Tag = WholeFile;
        }
        catch (Exception ex) { Error = ex.Message; return; }
        Rebuild();
    }

    /// <summary>Reloads when the file changed on disk; a save still in progress fails here and is retried next call.</summary>
    public void ReloadIfChanged()
    {
        if (Path == null || !File.Exists(Path)) return;
        var stamp = File.GetLastWriteTimeUtc(Path);
        if (stamp == _stamp) return;
        var frame = Frame;
        _scene.Content.EvictCachedAsset(Path);
        Rebuild();
        Frame = frame;
    }

    /// <summary>Rebuilds the animator and material from the current layer picks.</summary>
    public void Rebuild()
    {
        if (Path == null) return;
        Error = null;
        var stamp = File.Exists(Path) ? File.GetLastWriteTimeUtc(Path) : default;
        try
        {
            _material?.Dispose();
            _material = null;
            _actor?.Destroy();
            _actor = null;
            _animator = null;
            if (Diffuse.Count == 0) { Error = "Pick at least one diffuse layer."; _stamp = stamp; return; }
            var file = _scene.Content.LoadAsepriteFileForRendering(Path);
            if (file.CanvasWidth + Padding > MaxCanvas || file.CanvasHeight + Padding > MaxCanvas) throw new InvalidDataException("The canvas is too large to preview.");
            var diffuse = _scene.Content.LoadAsepriteAtlas(Path, Diffuse, false);
            var normals = new Dictionary<string, SpriteAnimation>();
            _actor = _scene.SimpleCreateEntity("Sprite", Entity.InstanceType.NonSerialized);
            var animator = _actor.AddComponent(new SpriteAnimator { RenderLayer = SpriteLayer });
            animator.AddAnimation(WholeFile, new SpriteAnimation(diffuse.Sprites, 10));
            for (int i = 0; i < diffuse.AnimationNames.Length; i++) animator.AddAnimation(diffuse.AnimationNames[i], diffuse.SpriteAnimations[i]);
            if (Normals.Count > 0)
            {
                var glow = file.GlowLayersFor(Normals.ToArray()).Where(Glow.Contains).ToArray();
                var atlas = _scene.Content.LoadAsepriteAtlas(Path, Normals, out _, false, glow);
                normals[WholeFile] = new SpriteAnimation(atlas.Sprites, 10) { HasGlow = atlas.HasGlow };
                for (int i = 0; i < atlas.AnimationNames.Length; i++) normals[atlas.AnimationNames[i]] = atlas.SpriteAnimations[i];
            }
            _material = Material?.Build(animator, normals) ?? new DeferredAnimatedMaterial(animator, normals);
            animator.SetMaterial(_material);
            _animator = animator;
            _canvasWidth = file.CanvasWidth + Padding;
            _canvasHeight = file.CanvasHeight + Padding;
            _stamp = stamp;
            Play();
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            if (ex is not IOException) _stamp = stamp;
            _actor?.Destroy();
            _actor = null;
            _animator = null;
        }
    }

    private void Play()
    {
        if (_animator == null || !_animator.Animations.ContainsKey(Tag)) return;
        _animator.Play(Tag);
        _animator.Pause();
        Frame = Math.Clamp(Frame, 0, Math.Max(0, FrameCount - 1));
        _animator.SetFrame(Frame);
    }

    public PreviewLight AddLight(Type type)
    {
        var entity = _scene.SimpleCreateEntity(type.Name, Entity.InstanceType.NonSerialized);
        var light = (DeferredLight)Activator.CreateInstance(type);
        light.RenderLayer = LightLayer;
        entity.AddComponent(light);
        entity.Position = new Vector2(0, -_canvasHeight / 2f + Padding / 4f);
        var inspectors = TypeInspectorUtils.GetInspectableProperties(light);
        foreach (var inspector in inspectors) inspector.IsUndoDisabled = true;
        var preview = new PreviewLight { Light = light, Entity = entity, Inspectors = inspectors, Name = type.GetCustomAttribute<LightPreviewAttribute>(false)?.DisplayName ?? type.Name };
        Lights.Add(preview);
        return preview;
    }

    /// <summary>After a recompile, rebuilds script lights and the game material from the new assembly so old code stops running.</summary>
    private void FollowScripts()
    {
        if (_scripts == Core.LatestScriptAssembly) return;
        _scripts = Core.LatestScriptAssembly;
        var engine = typeof(DeferredLight).Assembly;
        foreach (var old in Lights.Where(l => l.Light.GetType().Assembly != engine).ToArray())
        {
            var position = old.Entity.Position;
            var type = old.Light.GetType().FullName;
            RemoveLight(old);
            var replacement = LightTypes().FirstOrDefault(t => t.Type.FullName == type).Type;
            if (replacement != null) AddLight(replacement).Entity.Position = position;
        }
        if (Material == null) return;
        Material = Materials().FirstOrDefault(m => m.Name == Material.Name);
        Rebuild();
    }

    public void RemoveLight(PreviewLight light)
    {
        Lights.Remove(light);
        light.Entity.Destroy();
    }

    /// <summary>Draws the current frame into Target. Safe during layout: the previous render target is restored.</summary>
    public void Render()
    {
        FollowScripts();
        var device = Core.GraphicsDevice;
        var previous = device.GetRenderTargets();
        var viewport = device.Viewport;
        try
        {
            if (Target == null || Target.Width != _canvasWidth || Target.Height != _canvasHeight)
            {
                Target?.Dispose();
                Target = null;
                Target = new RenderTarget2D(device, _canvasWidth, _canvasHeight);
            }
            _scene.Update();
            Play();
            _scene.ClearColor = Background;
            _renderer.SetAmbientColor(Ambient);
            _scene.Camera.Position = Vector2.Zero;
            _renderer.RenderTo(_scene, Target);
        }
        catch (Exception ex)
        {
            // A game light or material throwing must not unwind the editor's ImGui frame.
            Error = ex.Message;
        }
        finally
        {
            device.SetRenderTargets(previous);
            device.Viewport = viewport;
        }
    }

    public byte[] Png()
    {
        using var stream = new MemoryStream();
        Target.SaveAsPng(stream, Target.Width, Target.Height);
        return stream.ToArray();
    }

    public void Dispose()
    {
        _material?.Dispose();
        _renderer.Unload();
        _scene.Content.Dispose();
        Target?.Dispose();
    }
}
