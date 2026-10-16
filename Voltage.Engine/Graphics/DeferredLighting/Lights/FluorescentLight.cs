using System;
using Microsoft.Xna.Framework;

namespace Voltage.DeferredLighting;

/// <summary>A finite bar emitting normal-mapped light toward its local positive Y axis.</summary>
public partial class FluorescentLight : DeferredLight, IUpdatable
{
    [Range(1, 16384)] public float SourceLength = 96;
    [Range(1, 16384)] public float Range = 140;
    [Tooltip("Extra horizontal radius per pixel of downward reach.")]
    [Range(0, 2)] public float Spread = 0.2f;
    [Range(0.1f, 128)] public float EdgeSoftness = 8;
    [Range(0.1f, 8)] public float Falloff = 1.5f;
    [Range(0.1f, 16384)] public float ZPosition = 48;
    [Range(0, 8)] public float Intensity = 0.5f;
    [Range(0, 0.5f)] public float FlickerDepth;
    [Range(0.1f, 10)] public float FlickerDuration = 1.5f;
    [Range(2, 120)] public float FlickerInterval = 20;
    [Range(0, 120)] public float FlickerPhase;

    private double _flickerTime;

    private static float Safe(float value, float min, float max, float fallback) =>
        float.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;

    public float EffectiveLength => Safe(SourceLength, 1, 16384, 96);
    public float EffectiveRange => Safe(Range, 1, 16384, 140);
    public float EffectiveSpread => Safe(Spread, 0, 2, 0.2f);
    public float EffectiveSoftness => Safe(EdgeSoftness, 0.1f, 128, 8);
    public float EffectiveFalloff => Safe(Falloff, 0.1f, 8, 1.5f);
    public float EffectiveFlickerInterval => Safe(FlickerInterval, 2, 120, 20);
    public float EffectiveFlickerDuration => Math.Min(Safe(FlickerDuration, 0.1f, 10, 1.5f), EffectiveFlickerInterval * 0.5f);
    public float FlickerMultiplier => Enabled ? SampleFlicker(_flickerTime) : 1;
    public float EffectiveIntensity => Safe(Intensity, 0, 8, 0) * FlickerMultiplier;

    public void Update()
    {
        float delta = Voltage.Utils.Time.UnscaledDeltaTime;
        if (Core.IsEditMode || Core.IsPauseMode || !Enabled || !float.IsFinite(delta) || delta <= 0) return;
        _flickerTime = (_flickerTime + delta) % (EffectiveFlickerInterval * 256d);
    }

    public float SampleFlicker(double elapsed)
    {
        float depth = Safe(FlickerDepth, 0, 0.5f, 0);
        if (depth == 0 || !double.IsFinite(elapsed) || elapsed < 0) return 1;
        double time = elapsed + Safe(FlickerPhase, 0, 120, 0);
        double cycle = time % EffectiveFlickerInterval;
        double progress = (cycle - (EffectiveFlickerInterval - EffectiveFlickerDuration)) / EffectiveFlickerDuration;
        if (progress <= 0) return 1;
        int burst = (int)(Math.Floor(time / EffectiveFlickerInterval) % 256);
        double first = Dip(progress, .06 + Noise(burst, 1) * .04, .08 + Noise(burst, 2) * .04) * (.55 + Noise(burst, 3) * .25);
        double second = Dip(progress, .35 + Noise(burst, 4) * .10, .055 + Noise(burst, 5) * .025) * (.35 + Noise(burst, 6) * .25);
        double third = Dip(progress, .72 + Noise(burst, 7) * .05, .10 + Noise(burst, 8) * .04);
        return 1 - depth * (float)Math.Max(first, Math.Max(second, third));
    }

    private static double Dip(double progress, double start, double duration)
    {
        double t = (progress - start) / duration;
        if (t <= 0 || t >= 1) return 0;
        double edge = t < .3 ? t / .3 : (1 - t) / .7;
        return edge * edge * (3 - 2 * edge);
    }

    private static double Noise(int burst, uint salt)
    {
        uint value = unchecked((uint)burst * 747796405u + salt * 2891336453u);
        value = unchecked((value ^ (value >> 16)) * 2246822519u);
        return (value ^ (value >> 13)) / (double)uint.MaxValue;
    }

    public override void OnRemovedFromEntity()
    {
        _flickerTime = 0;
        base.OnRemovedFromEntity();
    }

    public bool HasValidTransform => Entity != null && float.IsFinite(Entity.Position.X) && float.IsFinite(Entity.Position.Y) &&
        float.IsFinite(Entity.Rotation) && float.IsFinite(Entity.Scale.X) && float.IsFinite(Entity.Scale.Y) &&
        Math.Abs(Entity.Scale.X) > 0.0001f && Math.Abs(Entity.Scale.Y) > 0.0001f &&
        float.IsFinite(LocalOffset.X) && float.IsFinite(LocalOffset.Y);

    public Vector2 ToWorld(Vector2 point)
    {
        var scaled = (point + LocalOffset) * Entity.Scale;
        float cosine = MathF.Cos(Entity.Rotation), sine = MathF.Sin(Entity.Rotation);
        return Entity.Position + new Vector2(scaled.X * cosine - scaled.Y * sine, scaled.X * sine + scaled.Y * cosine);
    }

    public Vector2 ToLocal(Vector2 world)
    {
        var delta = world - Entity.Position;
        float cosine = MathF.Cos(Entity.Rotation), sine = MathF.Sin(Entity.Rotation);
        return new Vector2(delta.X * cosine + delta.Y * sine, -delta.X * sine + delta.Y * cosine) / Entity.Scale - LocalOffset;
    }

    public Vector2 EmissionDirection => new Vector2(-MathF.Sin(Entity.Rotation), MathF.Cos(Entity.Rotation)) * Math.Sign(Entity.Scale.Y);
    public float WorldHeight => Safe(ZPosition, 0.1f, 16384, 48) * Math.Abs(Entity.Scale.Y);

    public override RectangleF Bounds
    {
        get
        {
            if (!HasValidTransform) return new RectangleF();
            float half = EffectiveLength * 0.5f + EffectiveRange * EffectiveSpread + EffectiveSoftness;
            var a = ToWorld(new Vector2(-half, 0));
            var b = ToWorld(new Vector2(half, 0));
            var c = ToWorld(new Vector2(-half, EffectiveRange));
            var d = ToWorld(new Vector2(half, EffectiveRange));
            var min = Vector2.Min(Vector2.Min(a, b), Vector2.Min(c, d));
            var max = Vector2.Max(Vector2.Max(a, b), Vector2.Max(c, d));
            if (!float.IsFinite(min.X) || !float.IsFinite(min.Y) || !float.IsFinite(max.X - min.X) || !float.IsFinite(max.Y - min.Y)) return new RectangleF();
            return new RectangleF(min.X, min.Y, max.X - min.X, max.Y - min.Y);
        }
    }

    public float SampleAttenuation(Vector2 localPoint)
    {
        if (!float.IsFinite(localPoint.X) || !float.IsFinite(localPoint.Y) || localPoint.Y < 0 || localPoint.Y >= EffectiveRange) return 0;
        float radius = EffectiveLength * 0.5f + EffectiveRange * EffectiveSpread + EffectiveSoftness;
        float distance = new Vector2(localPoint.X / radius, localPoint.Y / EffectiveRange).Length();
        float remaining = Math.Max(0, 1 - distance);
        float edge = Math.Clamp(remaining * radius / EffectiveSoftness, 0, 1);
        edge *= edge * (3 - 2 * edge);
        return edge * MathF.Pow(remaining, EffectiveFalloff);
    }

    public override void DebugRender(Batcher batcher)
    {
        if (Bounds.Width <= 0 || Bounds.Height <= 0) return;
        float half = EffectiveLength * 0.5f, radius = half + EffectiveRange * EffectiveSpread + EffectiveSoftness;
        batcher.DrawLine(ToWorld(new(-half, 0)), ToWorld(new(half, 0)), Color, 2);
        var previous = ToWorld(new(radius, 0));
        for (int i = 1; i <= 32; i++)
        {
            float angle = MathF.PI * i / 32;
            var next = ToWorld(new(radius * MathF.Cos(angle), EffectiveRange * MathF.Sin(angle)));
            batcher.DrawLine(previous, next, Color);
            previous = next;
        }
    }
}
