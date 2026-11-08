using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Voltage.Sprites;
using Voltage.Textures;

namespace Voltage.Aseprite;

public sealed partial class AsepriteFile
{
    /// <summary>The Glow layers that pair with the given Normals layers, as the file spells them.</summary>
    internal string[] GlowLayersFor(string[] normalLayers)
    {
        var glow = new List<string>();
        foreach (var layer in Layers)
            if (layer.Name.StartsWith("Glow-", StringComparison.OrdinalIgnoreCase) &&
                Array.Exists(normalLayers, name => name.Equals("Normals-" + layer.Name.Substring(5), StringComparison.OrdinalIgnoreCase)))
                glow.Add(layer.Name);
        return glow.ToArray();
    }

    /// <summary>Normal RGB with the glow's strength (brightest channel times alpha) in alpha; glow off the normal map gets a front-facing normal.</summary>
    internal static Color PackGlow(Color normal, Color glow)
    {
        var strength = (byte)(Math.Max(glow.R, Math.Max(glow.G, glow.B)) * glow.A / 255);
        if (normal.A == 0) return strength == 0 ? normal : new Color(128, 128, 255, (int)strength);
        return new Color((int)normal.R, normal.G, normal.B, strength);
    }

    internal SpriteAtlas ToSharedSpriteAtlas(bool onlyVisibleLayers, string[] layers, string[] glowLayers = null)
    {
        using var timing = SceneLoadDiagnostics.Measure("asset.aseprite.prepare", $"{CanvasWidth}x{CanvasHeight}, {Frames.Count} frames");
        if (Frames.Count == 0) throw new InvalidOperationException("An Aseprite atlas needs at least one frame.");
        var unique = new List<int>();
        var buckets = new Dictionary<ulong, List<int>>();
        var indices = new int[Frames.Count];
        var pixels = new Color[CanvasWidth * CanvasHeight];
        var comparison = new Color[pixels.Length];
        var glow = glowLayers is { Length: > 0 } ? new Color[pixels.Length] : null;
        void Flatten(int frame, Color[] into)
        {
            Frames[frame].FlattenFrameOnLayersInto(into, onlyVisibleLayers, false, layers.Length > 0 ? layers : null);
            if (glow == null) return;
            Frames[frame].FlattenFrameOnLayersInto(glow, false, false, glowLayers);
            for (int i = 0; i < into.Length; i++) into[i] = PackGlow(into[i], glow[i]);
        }
        int comparisonIndex = -1;
        for (int frame = 0; frame < Frames.Count; frame++)
        {
            SceneLoading.Pulse();
            Flatten(frame, pixels);
            ulong hash = 14695981039346656037;
            foreach (var pixel in pixels) hash = unchecked((hash ^ pixel.PackedValue) * 1099511628211);
            if (!buckets.TryGetValue(hash, out var candidates)) buckets[hash] = candidates = new List<int>();
            int match = -1;
            foreach (int candidate in candidates)
            {
                if (comparisonIndex != candidate)
                {
                    Flatten(unique[candidate], comparison);
                    comparisonIndex = candidate;
                }
                if (pixels.AsSpan().SequenceEqual(comparison)) { match = candidate; break; }
            }
            if (match < 0)
            {
                match = unique.Count;
                unique.Add(frame);
                candidates.Add(match);
            }
            indices[frame] = match;
        }
        int columns = (int)Math.Ceiling(Math.Sqrt(unique.Count));
        int rows = (unique.Count + columns - 1) / columns;
        int width = columns * CanvasWidth, height = rows * CanvasHeight;
        var sheet = new Color[width * height];
        for (int index = 0; index < unique.Count; index++)
        {
            SceneLoading.Pulse();
            Flatten(unique[index], pixels);
            for (int row = 0; row < CanvasHeight; row++)
                Array.Copy(pixels, row * CanvasWidth, sheet,
                    (index / columns * CanvasHeight + row) * width + index % columns * CanvasWidth, CanvasWidth);
        }
        var texture = new Texture2D(Core.GraphicsDevice, width, height);
        try { using (SceneLoadDiagnostics.Measure("asset.gpu.upload", $"{width}x{height}")) texture.SetData(sheet); }
        catch { texture.Dispose(); throw; }
        var atlas = new SpriteAtlas
        {
            HasGlow = glow != null,
            Names = new string[Frames.Count], Sprites = new Sprite[Frames.Count],
            AnimationNames = new string[Tags.Count], SpriteAnimations = new SpriteAnimation[Tags.Count]
        };
        for (int frame = 0; frame < Frames.Count; frame++)
        {
            int index = indices[frame];
            var region = new Rectangle(index % columns * CanvasWidth, index / columns * CanvasHeight, CanvasWidth, CanvasHeight);
            atlas.Sprites[frame] = new Sprite(texture, region, new Vector2(CanvasWidth, CanvasHeight) / 2);
        }
        for (int index = 0; index < Tags.Count; index++)
        {
            var tag = Tags[index];
            var sprites = new Sprite[tag.To - tag.From + 1];
            var rates = new float[sprites.Length];
            for (int frame = 0; frame < sprites.Length; frame++)
            {
                sprites[frame] = atlas.Sprites[tag.From + frame];
                rates[frame] = 1.0f / (Frames[tag.From + frame].Duration / 1000.0f);
            }
            atlas.AnimationNames[index] = tag.Name;
            atlas.SpriteAnimations[index] = new SpriteAnimation(sprites, rates) { HasGlow = glow != null };
        }
        return atlas;
    }
}
