using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Voltage.Sprites;
using Voltage.Textures;

namespace Voltage.Aseprite;

public sealed partial class AsepriteFile
{
    internal SpriteAtlas ToSharedSpriteAtlas(bool onlyVisibleLayers, string[] layers)
    {
        if (Frames.Count == 0) throw new InvalidOperationException("An Aseprite atlas needs at least one frame.");
        var unique = new List<int>();
        var buckets = new Dictionary<ulong, List<int>>();
        var indices = new int[Frames.Count];
        var pixels = new Color[CanvasWidth * CanvasHeight];
        var comparison = new Color[pixels.Length];
        int comparisonIndex = -1;
        for (int frame = 0; frame < Frames.Count; frame++)
        {
            Frames[frame].FlattenFrameOnLayersInto(pixels, onlyVisibleLayers, false, layers.Length > 0 ? layers : null);
            ulong hash = 14695981039346656037;
            foreach (var pixel in pixels) hash = unchecked((hash ^ pixel.PackedValue) * 1099511628211);
            if (!buckets.TryGetValue(hash, out var candidates)) buckets[hash] = candidates = new List<int>();
            int match = -1;
            foreach (int candidate in candidates)
            {
                if (comparisonIndex != candidate)
                {
                    Frames[unique[candidate]].FlattenFrameOnLayersInto(comparison, onlyVisibleLayers, false, layers.Length > 0 ? layers : null);
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
            Frames[unique[index]].FlattenFrameOnLayersInto(pixels, onlyVisibleLayers, false, layers.Length > 0 ? layers : null);
            for (int row = 0; row < CanvasHeight; row++)
                Array.Copy(pixels, row * CanvasWidth, sheet,
                    (index / columns * CanvasHeight + row) * width + index % columns * CanvasWidth, CanvasWidth);
        }
        var texture = new Texture2D(Core.GraphicsDevice, width, height);
        try { texture.SetData(sheet); }
        catch { texture.Dispose(); throw; }
        var atlas = new SpriteAtlas
        {
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
            atlas.SpriteAnimations[index] = new SpriteAnimation(sprites, rates);
        }
        return atlas;
    }
}
