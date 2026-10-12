using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using ImGuiNET;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Voltage.Aseprite;
using Voltage.Assets;
using Voltage.Editor.ImGuiCore;
using Voltage.Editor.ProjectFile;

namespace Voltage.Editor.Assets;

internal sealed class AssetThumbnailCache : IDisposable
{
    private sealed class Entry
    {
        public Texture2D Texture;
        public IntPtr Id;
        public int Used, Checked;
        public readonly Dictionary<string, long> Dependencies = new(StringComparer.OrdinalIgnoreCase);
    }
    internal sealed record SpriteSource(string Path, Matrix Transform, Vector2 Origin, Vector2 Offset,
        float Rotation, Color Color, SpriteEffects Effects, int Layer, int Frame, string Tag, string[] Layers);

    private readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private int _builtFrame = -1;
    private const int CacheLimit = 96;

    public IntPtr Get(string path)
    {
        var frame = ImGui.GetFrameCount();
        if (_entries.TryGetValue(path, out var entry))
        {
            entry.Used = frame;
            if (frame - entry.Checked < 60) return entry.Id;
            entry.Checked = frame;
            if (entry.Dependencies.All(d => Stamp(d.Key) == d.Value)) return entry.Id;
            // Defer disposal until a new frame so previously submitted draw commands retain their texture.
            if (_builtFrame == frame) return entry.Id;
            Free(entry);
            _entries.Remove(path);
        }
        if (_builtFrame == frame) return IntPtr.Zero;
        _builtFrame = frame;
        entry = new Entry { Used = frame, Checked = frame };
        entry.Dependencies[path] = Stamp(path);
        try { entry.Texture = Build(path, entry.Dependencies); }
        catch (Exception ex) { Debug.Warn($"Asset preview '{Path.GetFileName(path)}': {ex.Message}", "AssetBrowser"); }
        if (entry.Texture != null) entry.Id = Core.GetGlobalManager<ImGuiManager>().BindTexture(entry.Texture);
        _entries[path] = entry;
        while (_entries.Count > CacheLimit)
        {
            var oldest = _entries.Where(e => e.Value.Used < frame - 1).OrderBy(e => e.Value.Used).FirstOrDefault();
            if (oldest.Key == null) break;
            Free(oldest.Value);
            _entries.Remove(oldest.Key);
        }
        return entry.Id;
    }

    private static long Stamp(string path) => File.Exists(path) ? File.GetLastWriteTimeUtc(path).Ticks : 0;
    private static void Free(Entry entry)
    {
        if (entry.Id != IntPtr.Zero) Core.GetGlobalManager<ImGuiManager>()?.UnbindTexture(entry.Id);
        entry.Texture?.Dispose();
    }
    public void Dispose()
    {
        foreach (var entry in _entries.Values) Free(entry);
        _entries.Clear();
    }

    internal static string ResolveSource(JsonElement value, string project)
    {
        string path = value.ValueKind == JsonValueKind.String ? value.GetString() : String(value, "AssetPath");
        if (value.ValueKind == JsonValueKind.Object && Guid.TryParse(String(value, "AssetGuid"), out var guid)
            && AssetManifest.TryGetAbsolutePath(guid, out var resolved)) path = resolved;
        if (string.IsNullOrEmpty(path)) return null;
        var absolute = Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(project, path.Replace('/', Path.DirectorySeparatorChar)));
        if (!absolute.StartsWith(Path.GetFullPath(project).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) return null;
        return absolute;
    }

    internal static List<SpriteSource> ReadPrefab(string path, string project, Dictionary<string, long> dependencies)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var sources = new List<SpriteSource>();
        var nodes = new List<JsonElement> { doc.RootElement };
        if (doc.RootElement.TryGetProperty("ChildEntities", out var children)) nodes.AddRange(children.EnumerateArray());
        var byId = nodes.Where(n => n.TryGetProperty("Id", out _)).ToDictionary(n => String(n, "Id"));
        var byName = nodes.Where(n => n.TryGetProperty("Name", out _)).GroupBy(n => String(n, "Name")).ToDictionary(g => g.Key, g => g.First());
        Matrix Transform(JsonElement node, HashSet<string> visited)
        {
            var id = String(node, "Id");
            if (!visited.Add(id)) return Matrix.Identity;
            var local = Matrix.CreateScale(new Vector3(Vector(node, "Scale", Vector2.One), 1))
                * Matrix.CreateRotationZ(Number(node, "Rotation")) * Matrix.CreateTranslation(new Vector3(Vector(node, "Position", Vector2.Zero), 0));
            if (byId.TryGetValue(String(node, "ParentId"), out var parent)
                || byName.TryGetValue(String(node, "ParentEntityName"), out parent)) return local * Transform(parent, visited);
            return local;
        }
        foreach (var node in nodes)
        {
            if (!Enabled(node) || !node.TryGetProperty("EntityData", out var entityData)
                || !entityData.TryGetProperty("ComponentDataList", out var components)) continue;
            var fallback = FindAuthoredSprite(components, project, dependencies);
            foreach (var component in components.EnumerateArray())
            {
                var type = String(component, "ComponentTypeName");
                if (type is not ("Voltage.Sprites.SpriteRenderer" or "Voltage.Sprites.SpriteAnimator")) continue;
                var json = String(component, "Json");
                if (string.IsNullOrWhiteSpace(json)) continue;
                using var body = JsonDocument.Parse(json);
                var data = body.RootElement;
                if (!Enabled(data)) continue;
                var source = data.TryGetProperty("AsepriteAsset", out var asset) ? ResolveSource(asset, project) : null;
                source ??= data.TryGetProperty("TextureFilePath", out var texture) ? ResolveSource(texture, project) : null;
                source ??= fallback.Path;
                if (source == null) continue;
                int frame = 1;
                string tag = String(data, "LoadedTag");
                var layers = Strings(data, "LoadedLayers");
                if (data.TryGetProperty("AsepriteData", out var ase) && ase.ValueKind == JsonValueKind.Object)
                {
                    frame = Math.Max(1, (int)Number(ase, "FrameNumber") + 1);
                    layers = Strings(ase, "LayerNames");
                    if (layers.Length == 0 && String(ase, "LayerName") != "") layers = new[] { String(ase, "LayerName") };
                }
                if (source == fallback.Path && tag == "") tag = fallback.Tag;
                if (source == fallback.Path && layers.Length == 0) layers = fallback.Layers;
                Enum.TryParse<SpriteEffects>(String(data, "SpriteEffects"), out var effects);
                sources.Add(new SpriteSource(source, Transform(node, new HashSet<string>()), Vector(data, "Origin", Vector2.Zero),
                    Vector(data, "LocalOffset", Vector2.Zero), Number(data, "RotationOffset"),
                    new Color((byte)Number(data, "ColorR", 255), (byte)Number(data, "ColorG", 255), (byte)Number(data, "ColorB", 255),
                        (byte)Number(data, "ColorA", 255)), effects, (int)Number(data, "RenderLayer"), frame, tag, layers));
            }
        }
        return sources.OrderByDescending(s => s.Layer).ToList();
    }

    private static (string Path, string Tag, string[] Layers) FindAuthoredSprite(JsonElement components, string project, Dictionary<string, long> dependencies)
    {
        foreach (var component in components.EnumerateArray())
        {
            var json = String(component, "Json");
            if (string.IsNullOrWhiteSpace(json)) continue;
            using var body = JsonDocument.Parse(json);
            foreach (var field in body.RootElement.EnumerateObject())
            {
                if (field.Value.ValueKind != JsonValueKind.Object) continue;
                var path = ResolveSource(field.Value, project);
                if (path == null || !path.EndsWith(".vasset", StringComparison.OrdinalIgnoreCase) || !File.Exists(path)) continue;
                dependencies[path] = Stamp(path);
                using var asset = JsonDocument.Parse(File.ReadAllText(path));
                var clips = new List<JsonElement>();
                void Find(JsonElement value)
                {
                    if (value.ValueKind == JsonValueKind.Object)
                    {
                        if (value.TryGetProperty("Source", out var source) && ResolveSource(source, project) is { } image
                            && Path.GetExtension(image).ToLowerInvariant() is ".aseprite" or ".ase" or ".png") clips.Add(value);
                        else foreach (var child in value.EnumerateObject()) Find(child.Value);
                    }
                    else if (value.ValueKind == JsonValueKind.Array) foreach (var child in value.EnumerateArray()) Find(child);
                }
                Find(asset.RootElement);
                var clip = clips.FirstOrDefault(c => String(c, "Key").Contains("Idle", StringComparison.OrdinalIgnoreCase));
                if (clip.ValueKind == JsonValueKind.Undefined && clips.Count > 0) clip = clips[0];
                if (clip.ValueKind != JsonValueKind.Undefined) return (ResolveSource(clip.GetProperty("Source"), project), String(clip, "Tag"), Strings(clip, "Layers"));
            }
        }
        return (null, "", Array.Empty<string>());
    }

    private static Texture2D Build(string path, Dictionary<string, long> dependencies)
    {
        var project = ProjectManager.Instance.CurrentProject.ProjectPath;
        var extension = Path.GetExtension(path).ToLowerInvariant();
        var sources = extension is ".vprefab" or ".prefab" ? ReadPrefab(path, project, dependencies)
            : extension is ".png" or ".aseprite" or ".ase"
                ? new List<SpriteSource> { new(path, Matrix.Identity, Vector2.Zero, Vector2.Zero, 0, Color.White, SpriteEffects.None, 0, 1, "", Array.Empty<string>()) }
                : new List<SpriteSource>();
        if (sources.Count == 0) return null;
        var loaded = new List<(SpriteSource Source, Texture2D Texture, Rectangle Crop)>();
        var min = new Vector2(float.MaxValue); var max = new Vector2(float.MinValue);
        RenderTarget2D target = null;
        try
        {
            foreach (var source in sources.Take(64))
            {
                dependencies[source.Path] = Stamp(source.Path);
                if (!File.Exists(source.Path) || new FileInfo(source.Path).Length > 32 * 1024 * 1024) continue;
                Texture2D texture;
                if (Path.GetExtension(source.Path).ToLowerInvariant() is ".aseprite" or ".ase")
                {
                    if (!AsepriteFileLoader.TryLoad(source.Path, true, out var file) || file.Frames.Count == 0) continue;
                    var tag = file.Tags.FirstOrDefault(t => t.Name == source.Tag);
                    var frame = tag != null ? tag.From + 1 : Math.Clamp(source.Frame, 1, file.Frames.Count);
                    texture = file.GetTextureFromLayers(frame, source.Layers.Length == 0, false, source.Layers);
                }
                else
                {
                    using var stream = File.OpenRead(source.Path);
                    texture = Texture2D.FromStream(Core.GraphicsDevice, stream);
                }
                loaded.Add((source, texture, default));
                var pixels = new Color[texture.Width * texture.Height];
                texture.GetData(pixels);
                if (Path.GetExtension(source.Path).Equals(".png", StringComparison.OrdinalIgnoreCase))
                {
                    for (var p = 0; p < pixels.Length; p++)
                        pixels[p] = Color.FromNonPremultiplied(pixels[p].ToVector4());
                    texture.SetData(pixels);
                }
                var cropMin = new Point(texture.Width, texture.Height); var cropMax = new Point(-1, -1);
                for (var y = 0; y < texture.Height; y++) for (var x = 0; x < texture.Width; x++)
                    if (pixels[y * texture.Width + x].A > 0)
                    { cropMin = new Point(Math.Min(cropMin.X, x), Math.Min(cropMin.Y, y)); cropMax = new Point(Math.Max(cropMax.X, x), Math.Max(cropMax.Y, y)); }
                if (cropMax.X < 0) { texture.Dispose(); loaded.RemoveAt(loaded.Count - 1); continue; }
                var crop = new Rectangle(cropMin.X, cropMin.Y, cropMax.X - cropMin.X + 1, cropMax.Y - cropMin.Y + 1);
                loaded[^1] = (source, texture, crop);
                var rotation = Matrix.CreateRotationZ(source.Rotation);
                foreach (var corner in new[] { new Vector2(crop.Left, crop.Top), new Vector2(crop.Right, crop.Top), new Vector2(crop.Left, crop.Bottom), new Vector2(crop.Right, crop.Bottom) })
                {
                    var visibleCorner = new Vector2(
                        source.Effects.HasFlag(SpriteEffects.FlipHorizontally) ? texture.Width - corner.X : corner.X,
                        source.Effects.HasFlag(SpriteEffects.FlipVertically) ? texture.Height - corner.Y : corner.Y);
                    var at = Vector2.Transform(Vector2.Transform(visibleCorner - source.Origin, rotation) + source.Offset, source.Transform);
                    min = Vector2.Min(min, at); max = Vector2.Max(max, at);
                }
            }
            if (loaded.Count == 0) return null;
            const int size = 192;
            var scale = (size - 16) / Math.Max(1, Math.Max(max.X - min.X, max.Y - min.Y));
            var view = Matrix.CreateTranslation(new Vector3(-(min + max) / 2, 0)) * Matrix.CreateScale(scale)
                * Matrix.CreateTranslation(size / 2f, size / 2f, 0);
            var device = Core.GraphicsDevice;
            var previous = device.GetRenderTargets();
            var viewport = device.Viewport;
            target = new RenderTarget2D(device, size, size);
            try
            {
                device.SetRenderTarget(target); device.Clear(Color.Transparent);
                using var batch = new SpriteBatch(device);
                foreach (var part in loaded)
                {
                    batch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None,
                        RasterizerState.CullNone, null, part.Source.Transform * view);
                    batch.Draw(part.Texture, part.Source.Offset, null, part.Source.Color, part.Source.Rotation,
                        part.Source.Origin, 1, part.Source.Effects, 0);
                    batch.End();
                }
            }
            finally { device.SetRenderTargets(previous); device.Viewport = viewport; }
            var result = target; target = null;
            return result;
        }
        finally
        {
            target?.Dispose();
            foreach (var part in loaded) part.Texture.Dispose();
        }
    }

    private static bool Enabled(JsonElement value) => !value.TryGetProperty("Enabled", out var enabled) || enabled.ValueKind != JsonValueKind.False;
    private static string String(JsonElement value, string name) => value.ValueKind == JsonValueKind.Object
        && value.TryGetProperty(name, out var field) && field.ValueKind == JsonValueKind.String ? field.GetString() : "";
    private static float Number(JsonElement value, string name, float fallback = 0) => value.TryGetProperty(name, out var field)
        && field.ValueKind == JsonValueKind.Number ? field.GetSingle() : fallback;
    private static Vector2 Vector(JsonElement value, string name, Vector2 fallback) => value.TryGetProperty(name, out var field)
        && field.ValueKind == JsonValueKind.Object ? new Vector2(Number(field, "X", fallback.X), Number(field, "Y", fallback.Y)) : fallback;
    private static string[] Strings(JsonElement value, string name) => value.TryGetProperty(name, out var field)
        && field.ValueKind == JsonValueKind.Array ? field.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.String).Select(v => v.GetString()).ToArray() : Array.Empty<string>();
}
