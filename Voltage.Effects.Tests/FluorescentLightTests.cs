using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using NUnit.Framework;
using Voltage.DeferredLighting;
using Voltage.Editor.Gizmos;
using Voltage.Serialization;

namespace Voltage.Effects.Tests;

[TestFixture]
[NonParallelizable]
public class FluorescentLightTests
{
    [Test]
    public void Native_shader_respects_surface_normals_emission_side_and_zero_intensity()
    {
        if (!OperatingSystem.IsWindows() && Environment.GetEnvironmentVariable("VOLTAGE_TEST_GPU") != "1")
            Assert.Ignore("Requires a native desktop graphics session.");
        using var game = new FluorescentGame();
        game.Run();
    }

    private sealed class FluorescentGame : Core
    {
        public FluorescentGame() : base(256, 144, windowTitle: "Fluorescent light check") { }
        protected override void Initialize()
        {
            base.Initialize();
            var scene = new Scene();
            scene.SetPixelArtViewportSize(new Point(256, 144));
            scene.SetDesignResolution(256, 144, Scene.SceneResolutionPolicy.ShowAll);
            var entity = scene.SimpleCreateEntity("Bar", Entity.InstanceType.NonSerialized);
            entity.Position = scene.Camera.Bounds.Location + new Vector2(128, 24);
            var light = entity.AddComponent(new FluorescentLight { SourceLength = 120, Range = 100, ZPosition = 32, Intensity = .8f });
            using var normals = new Texture2D(GraphicsDevice, 256, 144);
            using var target = new RenderTarget2D(GraphicsDevice, 256, 144);
            using var mesh = Voltage.DeferredLighting.PolygonMesh.CreateRectangle();
            using var effect = new DeferredLightEffect();
            var pixels = new Color[256 * 144];
            for (int y = 0; y < 144; y++) for (int x = 0; x < 256; x++)
                pixels[y * 256 + x] = x < 128 ? new Color(128, 220, 218) : new Color(128, 36, 218);
            normals.SetData(pixels);
            void Render()
            {
                GraphicsDevice.SetRenderTarget(target);
                GraphicsDevice.Clear(Color.Black);
                GraphicsDevice.BlendState = BlendState.Additive;
                GraphicsDevice.DepthStencilState = DepthStencilState.None;
                GraphicsDevice.RasterizerState = RasterizerState.CullNone;
                effect.SetNormalMap(normals);
                effect.UpdateForCamera(scene.Camera);
                effect.UpdateForLight(light);
                mesh.Render();
                GraphicsDevice.SetRenderTarget(null);
                target.GetData(pixels);
            }
            Render();
            Assert.That(pixels[64 * 256 + 100].R, Is.GreaterThan(40));
            Assert.That(pixels[64 * 256 + 156].R, Is.LessThan(pixels[64 * 256 + 100].R / 3));
            Assert.That(pixels[12 * 256 + 100].R, Is.Zero, "Light must not leak above the bar");
            Assert.That(pixels[40 * 256 + 10].R, Is.Zero, "Light must stay inside its envelope");
            Array.Fill(pixels, new Color(128, 128, 255));
            normals.SetData(pixels);
            Render();
            Assert.That(pixels[104 * 256 + 205].R, Is.Zero, "The lower corner must lie outside the curved footprint");
            Assert.That(pixels[104 * 256 + 128].R, Is.GreaterThan(0));
            for (int x = 129; x < 218; x++)
                Assert.That(pixels[64 * 256 + x].R, Is.LessThanOrEqualTo(pixels[64 * 256 + x - 1].R), "Falloff must fade smoothly toward the side");
            light.Intensity = 0;
            Render();
            Assert.That(pixels[64 * 256 + 100].R, Is.Zero);
            light.Intensity = .8f;
            entity.Scale = new Vector2(-1, -1);
            Array.Fill(pixels, new Color(128, 128, 255));
            normals.SetData(pixels);
            Render();
            Assert.That(pixels[12 * 256 + 100].R, Is.GreaterThan(40), "Mirroring Y must aim the bar upward");
            Assert.That(pixels[64 * 256 + 100].R, Is.Zero);
            scene.SceneRenderTarget.Dispose();
            scene.Content.Dispose();
            ((Game)this).Exit();
        }
    }

    [Test]
    public void Bar_lights_its_whole_length_only_downward_with_bounded_soft_falloff()
    {
        var light = new FluorescentLight { SourceLength = 100, Range = 80, Spread = 0.25f, EdgeSoftness = 8 };
        float center = light.SampleAttenuation(new(0, 20));
        Assert.That(center, Is.GreaterThan(0));
        Assert.That(light.SampleAttenuation(new(-45, 20)), Is.InRange(.001f, center));
        Assert.That(light.SampleAttenuation(new(45, 20)), Is.EqualTo(light.SampleAttenuation(new(-45, 20))));
        Assert.That(light.SampleAttenuation(new(0, -1)), Is.Zero);
        Assert.That(light.SampleAttenuation(new(0, 80)), Is.Zero);
        Assert.That(light.SampleAttenuation(new(70, 60)), Is.Zero);
        Assert.That(light.SampleAttenuation(new(59, 20)), Is.InRange(0.001f, center - 0.001f));
        Assert.That(light.SampleAttenuation(new(0, 60)), Is.LessThan(center));
        Assert.That(light.SampleAttenuation(new(float.NaN, 0)), Is.Zero);
    }

    [Test]
    public void Zero_spread_stays_inside_rounded_bounds_and_invalid_tuning_stays_finite()
    {
        var light = new FluorescentLight { SourceLength = 20, Spread = 0, EdgeSoftness = 2 };
        Assert.That(light.SampleAttenuation(new(13, 5)), Is.Zero);
        Assert.That(light.SampleAttenuation(new(13, 100)), Is.Zero);
        light.SourceLength = float.NaN; light.Range = float.PositiveInfinity;
        light.Spread = float.NegativeInfinity; light.EdgeSoftness = 0; light.Falloff = -5;
        light.Intensity = float.NaN;
        Assert.That(float.IsFinite(light.SampleAttenuation(new(0, 5))), Is.True);
        Assert.That(light.EffectiveIntensity, Is.Zero);
    }

    [Test]
    public void Curved_perimeter_has_no_rectangular_corner_or_intensity_jump()
    {
        var light = new FluorescentLight { SourceLength = 100, Range = 80, Spread = .25f, EdgeSoftness = 8 };
        Assert.That(light.SampleAttenuation(new(60, 65)), Is.Zero);
        Assert.That(light.SampleAttenuation(new(0, 65)), Is.GreaterThan(0));
        float previous = light.SampleAttenuation(new(0, 40));
        for (int i = 1; i <= 800; i++)
        {
            float value = light.SampleAttenuation(new(i * .1f, 40));
            Assert.That(value, Is.InRange(0, previous + .000001f));
            Assert.That(previous - value, Is.LessThan(.003f));
            previous = value;
        }
    }

    [Test]
    public void Flicker_has_brief_uneven_dips_with_steady_gaps_and_varies_between_bursts()
    {
        var light = new FluorescentLight { FlickerDepth = .24f, FlickerDuration = 1.5f, FlickerInterval = 20 };
        Assert.That(light.SampleFlicker(18.5), Is.EqualTo(1));
        int dips = 0, dimSamples = 0;
        bool dim = false, differs = false;
        float minimum = 1;
        for (int i = 0; i < 1500; i++)
        {
            double time = 18.5 + i / 1000d;
            float value = light.SampleFlicker(time);
            bool next = value < .99999f;
            if (next && !dim) dips++;
            if (next) dimSamples++;
            dim = next;
            minimum = Math.Min(minimum, value);
            differs |= Math.Abs(value - light.SampleFlicker(time + 20)) > .01f;
        }
        Assert.That(dips, Is.EqualTo(3));
        Assert.That(dimSamples, Is.InRange(350, 550), "Most of the burst should remain steady");
        Assert.That(minimum, Is.EqualTo(.76f).Within(.001));
        Assert.That(differs, Is.True, "Consecutive bursts must not repeat the same pattern");
        Assert.That(light.SampleFlicker(20), Is.EqualTo(1));
        Assert.That(light.SampleFlicker(25), Is.EqualTo(1));
        for (double t = 0; t < 100; t += .013)
            Assert.That(light.SampleFlicker(t), Is.InRange(.76f, 1));
        light.FlickerDepth = float.PositiveInfinity;
        Assert.That(light.SampleFlicker(19), Is.EqualTo(1));
        light.FlickerDepth = 4; light.FlickerDuration = 100; light.FlickerInterval = 2;
        Assert.That(light.EffectiveFlickerDuration, Is.EqualTo(1));
        Assert.That(light.SampleFlicker(double.NaN), Is.EqualTo(1));
        Assert.That(light.SampleFlicker(double.MaxValue), Is.InRange(.5f, 1));
    }

    [TestCase(30)]
    [TestCase(60)]
    [TestCase(144)]
    public void Flicker_timing_is_frame_rate_independent_and_authored_intensity_is_preserved(int fps)
    {
        using (var mode = new SimulationMode())
        {
            var light = new FluorescentLight { Intensity = .5f, FlickerDepth = .24f };
            Voltage.Utils.Time.UnscaledDeltaTime = 1f / fps;
            for (int i = 0; i < 19 * fps; i++)
            {
                light.Update();
                Assert.That(light.EffectiveIntensity, Is.InRange(.38f, .5f));
            }
            Assert.That(light.FlickerMultiplier, Is.EqualTo(light.SampleFlicker(19)).Within(.00002));
            Assert.That(light.Intensity, Is.EqualTo(.5f));
            Voltage.Utils.Time.UnscaledDeltaTime = 71;
            light.Update();
            Assert.That(light.FlickerMultiplier, Is.EqualTo(light.SampleFlicker(90)).Within(.00002));
        }
    }

    [Test]
    public void Hiding_pause_and_edit_freeze_flicker_and_removal_resets_it()
    {
        using (var mode = new SimulationMode())
        {
            var entity = new Entity("lamp");
            var light = entity.AddComponent(new FluorescentLight { FlickerDepth = .24f });
            Voltage.Utils.Time.UnscaledDeltaTime = 19.66f;
            light.Update();
            float dip = light.FlickerMultiplier;
            Assert.That(dip, Is.LessThan(1));
            entity.Enabled = false; light.Update();
            Assert.That(light.FlickerMultiplier, Is.EqualTo(1));
            entity.Enabled = true;
            Assert.That(light.FlickerMultiplier, Is.EqualTo(dip));
            mode.Paused(true); light.Update(); mode.Paused(false);
            mode.Edit(true); light.Update(); mode.Edit(false);
            Assert.That(light.FlickerMultiplier, Is.EqualTo(dip));
            Voltage.Utils.Time.UnscaledDeltaTime = float.NaN; light.Update();
            Assert.That(light.FlickerMultiplier, Is.EqualTo(dip));
            light.OnRemovedFromEntity();
            Assert.That(light.FlickerMultiplier, Is.EqualTo(1));
        }
    }

    private sealed class SimulationMode : IDisposable
    {
        private static readonly System.Reflection.FieldInfo EditFlag = typeof(Core).GetField("_isEditMode", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        private static readonly System.Reflection.FieldInfo PauseFlag = typeof(Core).GetField("_isPauseMode", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        private readonly bool _edit = Core.IsEditMode, _paused = Core.IsPauseMode;
        private readonly float _delta = Voltage.Utils.Time.UnscaledDeltaTime;
        public SimulationMode() { Edit(false); Paused(false); }
        // Clock unit tests must not invoke listeners retained by earlier disposed native games.
        public void Edit(bool value) => EditFlag.SetValue(null, value);
        public void Paused(bool value) => PauseFlag.SetValue(null, value);
        public void Dispose()
        {
            Edit(_edit); Paused(_paused);
            Voltage.Utils.Time.UnscaledDeltaTime = _delta;
        }
    }

    [Test]
    public void Generated_factory_and_serialization_preserve_authored_geometry_and_light_fields()
    {
        ComponentDataSerializationBootstrap.EnsureInitialized();
        const string type = "Voltage.DeferredLighting.FluorescentLight";
        var light = (FluorescentLight)Voltage.Serialization.Registries.ComponentAotFactory.Create(type);
        light.SourceLength = 123; light.Range = 84; light.Spread = .3f; light.EdgeSoftness = 5;
        light.Falloff = 2; light.ZPosition = 31; light.Intensity = .4f;
        light.FlickerDepth = .2f; light.FlickerDuration = 1.5f; light.FlickerInterval = 16; light.FlickerPhase = 4;
        light.Color = new Color(120, 190, 240); light.RenderLayer = 100; light.LocalOffset = new(4, -8);
        var copy = (FluorescentLight)Voltage.Serialization.Registries.ComponentAotFactory.Create(type);
        copy.Data = ComponentDataAotDeserializer.TryDeserialize(type + "+FluorescentLightGeneratedData", Voltage.Persistence.Json.ToJson(light.Data));
        Assert.That(copy.SourceLength, Is.EqualTo(123));
        Assert.That(copy.Range, Is.EqualTo(84));
        Assert.That(copy.Spread, Is.EqualTo(.3f));
        Assert.That(copy.EdgeSoftness, Is.EqualTo(5));
        Assert.That(copy.Falloff, Is.EqualTo(2));
        Assert.That(copy.ZPosition, Is.EqualTo(31));
        Assert.That(copy.Intensity, Is.EqualTo(.4f));
        Assert.That(copy.FlickerDepth, Is.EqualTo(.2f));
        Assert.That(copy.FlickerDuration, Is.EqualTo(1.5f));
        Assert.That(copy.FlickerInterval, Is.EqualTo(16));
        Assert.That(copy.FlickerPhase, Is.EqualTo(4));
        Assert.That(copy.Color, Is.EqualTo(light.Color));
        Assert.That(copy.LocalOffset, Is.EqualTo(light.LocalOffset));
        Assert.That(copy.RenderLayer, Is.EqualTo(100));
    }

    [TestCase(0f, 1f, 1f)]
    [TestCase(.7f, -2f, 3f)]
    [TestCase(-1.2f, .5f, -2f)]
    public void Transformed_bounds_cover_source_and_envelope_and_resize_undo_keeps_origin(float rotation, float sx, float sy)
    {
        var parent = new Entity("parent") { Position = new(40, 20), Rotation = .2f, Scale = new(1.5f) };
        var entity = new Entity("bar") { Parent = parent.Transform, LocalPosition = new(8, -4), LocalRotation = rotation, LocalScale = new(sx, sy) };
        var light = entity.AddComponent(new FluorescentLight { SourceLength = 100, Range = 80, Spread = .25f, EdgeSoftness = 8, LocalOffset = new(3, 6) });
        var position = entity.Position;
        var origin = light.ToWorld(Vector2.Zero);
        var bounds = light.Bounds;
        foreach (var local in new[] { new Vector2(-78, 0), new(78, 0), new(-78, 80), new(78, 80) })
        {
            var point = light.ToWorld(local);
            Assert.That(point.X, Is.InRange(bounds.Left - .001f, bounds.Right + .001f));
            Assert.That(point.Y, Is.InRange(bounds.Top - .001f, bounds.Bottom + .001f));
            Assert.That(Vector2.Distance(light.ToLocal(point), local), Is.LessThan(.001f));
        }
        var before = new LightBoundsResizeState(light);
        before.Drag(light.ToWorld(new(100, 100)), RectangleHandleType.BottomRight, false);
        Assert.That(light.Range, Is.EqualTo(100).Within(.001));
        Assert.That(light.SourceLength, Is.EqualTo(134).Within(.001));
        Assert.That(light.ToWorld(Vector2.Zero), Is.EqualTo(origin));
        Assert.That(entity.Position, Is.EqualTo(position));
        var after = new LightBoundsResizeState(light);
        before.Restore();
        Assert.That(light.SourceLength, Is.EqualTo(100));
        Assert.That(light.Range, Is.EqualTo(80));
        after.Restore();
        Assert.That(light.Range, Is.EqualTo(100).Within(.001));
        Assert.That(LightBoundsResizeState.CanResize(light), Is.True);
        entity.Scale = Vector2.Zero;
        Assert.That(light.Bounds, Is.EqualTo(new RectangleF()));
        Assert.That(LightBoundsResizeState.CanResize(light), Is.False);
        entity.Scale = new Vector2(float.MaxValue);
        Assert.That(light.Bounds, Is.EqualTo(new RectangleF()));
        Assert.That(LightBoundsResizeState.CanResize(light), Is.False);
    }
}
