using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Voltage.Textures;

namespace Voltage.DeferredLighting;

public partial class RoomLight : DeferredLight
{
    [Tooltip("Optional second fill hue. Color is the primary fill tint; white keeps the fill neutral.")]
    public Color SecondaryColor
    {
        get => _secondaryColor;
        set => _secondaryColor = value == default ? new Color(255, 207, 158) : value;
    }
    [Range(0, 1)]
    [Tooltip("0 keeps the primary Color throughout the room. 1 blends fully to SecondaryColor at the far side.")]
    public float SecondaryColorStrength;
    [Tooltip("Local blend direction: (1, 0) left to right, (0, 1) top to bottom; negate to reverse.")]
    public Vector2 ColorBlendDirection
    {
        get => _colorBlendDirection;
        set => _colorBlendDirection = value == Vector2.Zero ? Vector2.UnitX : value;
    }
    private Color _secondaryColor = new(255, 207, 158);
    private Vector2 _colorBlendDirection = Vector2.UnitX;
    public List<Vector2> Vertices { get; set; } = new() { new(-160, -80), new(160, -80), new(160, 80), new(-160, 80) };
    public float Intensity = 0.35f;
    public float AmbientFill = 0.3f;
    public Vector2 LightOrigin = new(0, -60);
    public float Range = 240;
    public float Falloff = 2;
    public float Scattering = 0.7f;
    public float EdgeSoftness = 12;
    [Tooltip("Additional tint near the polygon edges, mixed with the room fill.")]
    public Color BounceColor = new(255, 207, 158);
    public float BounceStrength = 0.25f;
    public PointLight SourceLight;
    public float SourceReferenceIntensity = 1;
    public float SourceInfluence = 1;
    public bool UseSourceColor;
    [Tooltip("Strength of shadows cast by solid colliders inside this room; zero disables occlusion.")]
    [Range(0, 1)] public float ShadowStrength;
    public int ShadowLayerMask = Physics.AllLayers;
    private Sprite _field;
    private int _fieldHash;
    private RectangleF _localBounds;

    public override RectangleF Bounds
    {
        get
        {
            if (!TryGetLocalBounds(out var bounds)) return new RectangleF();
            var minimum = new Vector2(float.MaxValue);
            var maximum = new Vector2(float.MinValue);
            foreach (var vertex in Vertices)
            {
                var world = ToWorld(vertex);
                minimum = Vector2.Min(minimum, world);
                maximum = Vector2.Max(maximum, world);
            }
            return new RectangleF(minimum.X, minimum.Y, maximum.X - minimum.X, maximum.Y - minimum.Y);
        }
    }

    private Vector2 ToWorld(Vector2 point)
    {
        var scaled = (point + LocalOffset) * Entity.Scale;
        float cosine = MathF.Cos(Entity.Rotation), sine = MathF.Sin(Entity.Rotation);
        return Entity.Position + new Vector2(scaled.X * cosine - scaled.Y * sine, scaled.X * sine + scaled.Y * cosine);
    }

    public bool TryGetLocalBounds(out RectangleF bounds)
    {
        bounds = new RectangleF();
        if (Vertices == null || Vertices.Count < 3 || Vertices.Count > 128) return false;
        var minimum = new Vector2(float.MaxValue);
        var maximum = new Vector2(float.MinValue);
        float area = 0;
        for (int index = 0; index < Vertices.Count; index++)
        {
            var point = Vertices[index];
            if (!float.IsFinite(point.X) || !float.IsFinite(point.Y)) return false;
            var next = Vertices[(index + 1) % Vertices.Count];
            area += point.X * next.Y - next.X * point.Y;
            minimum = Vector2.Min(minimum, point);
            maximum = Vector2.Max(maximum, point);
        }
        var size = maximum - minimum;
        if (!float.IsFinite(area) || MathF.Abs(area) < 0.001f || size.X <= 0 || size.Y <= 0 || size.X > 16384 || size.Y > 16384) return false;
        bounds = new RectangleF(minimum.X, minimum.Y, size.X, size.Y);
        return true;
    }

    private static float Safe(float value, float minimum, float maximum, float fallback) =>
        float.IsFinite(value) ? Math.Clamp(value, minimum, maximum) : fallback;

    public Vector3 SampleIllumination(Vector2 point)
    {
        if (!TryGetLocalBounds(out var bounds) || !float.IsFinite(point.X) || !float.IsFinite(point.Y)) return Vector3.Zero;
        bool inside = false;
        float distanceSquared = float.MaxValue;
        for (int index = 0; index < Vertices.Count; index++)
        {
            var start = Vertices[index];
            var end = Vertices[(index + 1) % Vertices.Count];
            if ((start.Y > point.Y) != (end.Y > point.Y) &&
                point.X < (end.X - start.X) * (point.Y - start.Y) / (end.Y - start.Y) + start.X) inside = !inside;
            var edge = end - start;
            float fraction = edge.LengthSquared() > 0 ? Math.Clamp(Vector2.Dot(point - start, edge) / edge.LengthSquared(), 0, 1) : 0;
            distanceSquared = Math.Min(distanceSquared, Vector2.DistanceSquared(point, start + edge * fraction));
        }
        if (!inside) return Vector3.Zero;
        float distance = MathF.Sqrt(distanceSquared);
        float softness = Safe(EdgeSoftness, 0, 4096, 12);
        float coverage = softness > 0 ? Math.Clamp(distance / softness, 0, 1) : 1;
        coverage = coverage * coverage * (3 - 2 * coverage);
        var origin = float.IsFinite(LightOrigin.X) && float.IsFinite(LightOrigin.Y) ? LightOrigin : Vector2.Zero;
        float attenuation = MathF.Pow(Math.Clamp(1 - Vector2.Distance(point, origin) / Safe(Range, 1, 16384, 240), 0, 1), Safe(Falloff, 0.1f, 8, 2));
        float spread = Safe(Scattering, 0, 1, 0.7f);
        float fill = Safe(AmbientFill, 0, 1, 0.3f);
        float direct = fill + (1 - fill) * (attenuation + (1 - attenuation) * spread);
        float bounce = Safe(BounceStrength, 0, 1, 0.25f) * MathF.Exp(-distance / Math.Max(1, softness * 4));
        var fillColor = Color.ToVector3();
        float colorStrength = Safe(SecondaryColorStrength, 0, 1, 0);
        if (colorStrength > 0)
        {
            var direction = ColorBlendDirection;
            float extent = MathF.Abs(direction.X) + MathF.Abs(direction.Y);
            if (!float.IsFinite(extent) || extent < 0.0001f)
                direction = Vector2.UnitX;
            else
                direction /= extent;
            var offset = new Vector2((point.X - bounds.Center.X) / bounds.Width,
                (point.Y - bounds.Center.Y) / bounds.Height);
            float blend = Math.Clamp(0.5f + Vector2.Dot(offset, direction), 0, 1) * colorStrength;
            fillColor = Vector3.Lerp(fillColor, SecondaryColor.ToVector3(), blend);
        }
        return Vector3.Clamp((fillColor * direct + BounceColor.ToVector3() * bounce) * coverage, Vector3.Zero, Vector3.One);
    }

    public float GetSourceMultiplier()
    {
        if (SourceLight == null) return 1;
        float source = SourceLight.Enabled && SourceLight.Entity?.Enabled == true && SourceLight.Entity.Scene == Entity?.Scene
            ? Safe(SourceLight.Intensity, 0, 64, 0) / Safe(SourceReferenceIntensity, 0.001f, 64, 1) : 0;
        return MathHelper.Lerp(1, Math.Clamp(source, 0, 8), Safe(SourceInfluence, 0, 1, 1));
    }

    private void EnsureField()
    {
        var hash = new HashCode();
        foreach (var vertex in Vertices) hash.Add(vertex);
        hash.Add(Color); hash.Add(SecondaryColor); hash.Add(SecondaryColorStrength); hash.Add(ColorBlendDirection);
        hash.Add(AmbientFill); hash.Add(LightOrigin); hash.Add(Range);
        hash.Add(Falloff); hash.Add(Scattering); hash.Add(EdgeSoftness); hash.Add(BounceColor); hash.Add(BounceStrength);
        hash.Add(ShadowStrength); hash.Add(ShadowLayerMask);
        HashSet<Collider> occluders = null;
        if (ShadowStrength > 0 && Entity?.Scene != null)
        {
            occluders = Physics.BoxcastBroadphase(Bounds, ShadowLayerMask);
            hash.Add(Entity.Position); hash.Add(Entity.Scale); hash.Add(Entity.Rotation); hash.Add(LocalOffset);
            foreach (var collider in occluders)
                if (collider.Enabled && !collider.IsTrigger)
                {
                    hash.Add(collider.Bounds); hash.Add(collider.Rotation);
                }
        }
        int value = hash.ToHashCode();
        if (_field != null && _fieldHash == value && !_field.Texture2D.IsDisposed) return;
        TryGetLocalBounds(out _localBounds);
        int width = Math.Clamp((int)MathF.Ceiling(_localBounds.Width / 2), 2, 256);
        int height = Math.Clamp((int)MathF.Ceiling(_localBounds.Height / 2), 2, 256);
        var pixels = new Color[(width + 2) * (height + 2)];
        for (int row = 0; row < height; row++)
            for (int column = 0; column < width; column++)
            {
                var point = new Vector2(_localBounds.X + (column + 0.5f) * _localBounds.Width / width,
                    _localBounds.Y + (row + 0.5f) * _localBounds.Height / height);
                var illumination = SampleIllumination(point);
                if (occluders != null)
                {
                    var origin = ToWorld(LightOrigin);
                    var destination = ToWorld(point);
                    foreach (var collider in occluders)
                    {
                        if (!collider.Enabled || collider.IsTrigger || collider.Shape.ContainsPoint(origin)) continue;
                        if (!collider.Shape.CollidesWithLine(origin, destination, out var hit) || hit.Fraction >= 0.99f) continue;
                        illumination *= 1 - Safe(ShadowStrength, 0, 1, 0);
                        break;
                    }
                }
                pixels[(row + 1) * (width + 2) + column + 1] = new Color(illumination);
            }
        var texture = new Texture2D(Core.GraphicsDevice, width + 2, height + 2);
        texture.SetData(pixels);
        _field?.Texture2D.Dispose();
        _field = new Sprite(texture);
        _fieldHash = value;
    }

    internal void RenderLight(Batcher batcher)
    {
        if (!TryGetLocalBounds(out _)) return;
        EnsureField();
        var texel = new Vector2(_localBounds.Width / (_field.Texture2D.Width - 2), _localBounds.Height / (_field.Texture2D.Height - 2));
        var position = ToWorld(new Vector2(_localBounds.X, _localBounds.Y) - texel);
        var tint = UseSourceColor && SourceLight != null ? SourceLight.Color.ToVector3() : Vector3.One;
        float strength = Math.Clamp(Safe(Intensity, 0, 8, 0) * GetSourceMultiplier(), 0, 8);
        while (strength > 0)
        {
            float amount = Math.Min(1, strength);
            batcher.Draw(_field, position, new Color(tint * amount), Entity.Rotation, Vector2.Zero, texel * Entity.Scale, SpriteEffects.None, 0);
            strength -= amount;
        }
    }

    public override void DebugRender(Batcher batcher)
    {
        if (!TryGetLocalBounds(out _)) return;
        for (int index = 0; index < Vertices.Count; index++)
            batcher.DrawLine(ToWorld(Vertices[index]), ToWorld(Vertices[(index + 1) % Vertices.Count]), Color.Yellow, 1);
    }

    public override void OnRemovedFromEntity()
    {
        _field?.Texture2D.Dispose();
        _field = null;
        base.OnRemovedFromEntity();
    }
}
