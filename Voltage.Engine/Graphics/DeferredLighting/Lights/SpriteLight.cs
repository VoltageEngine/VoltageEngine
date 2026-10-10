using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Voltage.Sprites;
using Voltage.Textures;

namespace Voltage.DeferredLighting;

/// <summary>Animated sprite silhouettes with a soft light field added to the deferred light buffer.</summary>
public partial class SpriteLight : DeferredLight
{
    public SpriteRenderer Source;
    public int Radius = 24;
    public float Intensity = 1.4f;
    public bool UseSourceSprite = true;
    private readonly Dictionary<Sprite, Sprite> _masks = new();
    private readonly Dictionary<(Sprite, int), Sprite> _fields = new();
    public override RectangleF Bounds
    {
        get
        {
            var b = Source?.Bounds ?? new RectangleF();
            float pad = Radius * Math.Max(Source?.Entity.Scale.X ?? 1, Source?.Entity.Scale.Y ?? 1);
            return new RectangleF(b.X - pad, b.Y - pad, b.Width + pad * 2, b.Height + pad * 2);
        }
    }
    public void SetFrameMask(Sprite frame, Sprite mask)
    {
        _masks[frame] = mask;
        GetField(mask);
    }
    private Sprite GetField(Sprite mask)
    {
        int radius = Math.Clamp(Radius, 1, 128);
        var key = (mask, radius);
        if (_fields.TryGetValue(key, out var cached)) return cached;
        var pixels = new Color[mask.SourceRect.Width * mask.SourceRect.Height];
        mask.Texture2D.GetData(0, mask.SourceRect, pixels, 0, pixels.Length);
        var field = BuildField(pixels, mask.SourceRect.Width, mask.SourceRect.Height, radius);
        var texture = new Texture2D(Core.GraphicsDevice, mask.SourceRect.Width + radius * 2, mask.SourceRect.Height + radius * 2);
        texture.SetData(field);
        var sprite = new Sprite(texture);
        _fields.Add(key, sprite);
        return sprite;
    }
    public static Color[] BuildField(Color[] mask, int width, int height, int radius)
    {
        if (width < 1 || height < 1 || radius < 1 || mask.Length != width * height) throw new ArgumentException("Invalid sprite light mask dimensions.");
        int w = width + radius * 2, h = height + radius * 2;
        var emission = new float[w * h];
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
            emission[(y + radius) * w + x + radius] = mask[y * width + x].A / 255f;
        var near = (float[])emission.Clone();
        var far = (float[])emission.Clone();
        for (int pass = 0; pass < 3; pass++)
        {
            near = Blur(near, w, h, Math.Min(2, Math.Max(1, radius / 3)));
            far = Blur(far, w, h, Math.Max(1, radius / 3));
        }
        var result = new Color[w * h];
        for (int i = 0; i < result.Length; i++)
        {
            float value = Math.Min(1, emission[i] + near[i] * 2.5f + far[i] * 1.5f);
            result[i] = new Color(value, value, value, 1f);
        }
        return result;
    }
    private static float[] Blur(float[] source, int width, int height, int radius)
    {
        var horizontal = new float[source.Length];
        var result = new float[source.Length];
        float divisor = radius * 2 + 1;
        for (int y = 0; y < height; y++)
        {
            float sum = 0;
            int row = y * width;
            for (int x = 0; x <= radius && x < width; x++) sum += source[row + x];
            for (int x = 0; x < width; x++)
            {
                horizontal[row + x] = sum / divisor;
                if (x - radius >= 0) sum -= source[row + x - radius];
                if (x + radius + 1 < width) sum += source[row + x + radius + 1];
            }
        }
        for (int x = 0; x < width; x++)
        {
            float sum = 0;
            for (int y = 0; y <= radius && y < height; y++) sum += horizontal[y * width + x];
            for (int y = 0; y < height; y++)
            {
                result[y * width + x] = Math.Max(0, sum / divisor);
                if (y - radius >= 0) sum -= horizontal[(y - radius) * width + x];
                if (y + radius + 1 < height) sum += horizontal[(y + radius + 1) * width + x];
            }
        }
        return result;
    }
    internal void RenderLight(Batcher batcher, Camera camera)
    {
        if (Source?.Sprite == null || !Source.Enabled || !Source.Entity.Enabled) return;
        if (!_masks.TryGetValue(Source.Sprite, out var mask))
        {
            if (!UseSourceSprite) return;
            mask = Source.Sprite;
        }
        var field = GetField(mask);
        int radius = Math.Clamp(Radius, 1, 128);
        var position = Source.Entity.Position + Source.LocalOffset;
        var origin = Source.Origin + new Vector2(radius);
        var scale = Source.Entity.Scale;
        if (Source.Entity.Scene.UsesSmoothPixelArt && camera.Rotation == 0 && Source.Entity.Rotation == 0)
        {
            var corner = camera.WorldToScreenPoint(position - Source.Origin * scale);
            corner.X = (float)Math.Round(corner.X); corner.Y = (float)Math.Round(corner.Y);
            position = camera.ScreenToWorldPoint(corner) + Source.Origin * scale;
        }
        float strength = Math.Clamp(Intensity, 0, 8) * Source.Color.A / 255f;
        while (strength > 0)
        {
            float amount = Math.Min(1, strength);
            batcher.Draw(field, position, new Color(Color.ToVector3() * amount), Source.Entity.Rotation, origin, scale, Source.SpriteEffects, 0);
            strength -= amount;
        }
    }
    public override void OnRemovedFromEntity()
    {
        foreach (var sprite in _fields.Values) sprite.Texture2D.Dispose();
        _fields.Clear(); _masks.Clear();
    }
}
