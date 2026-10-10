using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using NUnit.Framework;
using Voltage.DeferredLighting;
using Voltage.Serialization;
using Voltage.Serialization.Registries;

namespace Voltage.Effects.Tests;

[TestFixture]
public class RoomLightTests
{
    [Test]
    public void Room_fill_renders_and_tracks_driver_edits_without_leaking_outside()
    {
        if (!OperatingSystem.IsWindows() && Environment.GetEnvironmentVariable("VOLTAGE_TEST_GPU") != "1")
            Assert.Ignore("Requires a native desktop graphics session.");
        using var game = new RoomGame();
        game.Run();
    }

    private sealed class RoomGame : Core
    {
        public RoomGame() : base(64, 64, windowTitle: "Room lighting check") { }

        protected override void Initialize()
        {
            base.Initialize();
            using var batcher = new Batcher(GraphicsDevice);
            Graphics.Instance = new Graphics { Batcher = batcher };
            Core.DebugRenderEnabled = false;
            var scene = new Scene();
            scene.SetDesignResolution(64, 64, Scene.SceneResolutionPolicy.ShowAll);
            var renderer = scene.AddRenderer(new DeferredLightingRenderer(0, 100, SamplerState.PointClamp, 0));
            Core.Scene = scene;
            var entity = scene.SimpleCreateEntity("Room", Entity.InstanceType.NonSerialized);
            entity.Position = scene.Camera.Bounds.Center;
            var light = entity.AddComponent(new RoomLight
            {
                RenderLayer = 100, AmbientFill = 1, BounceStrength = 0, Intensity = 0.25f,
                Color = Color.Red, EdgeSoftness = 4,
                Vertices = new() { new(-20,-20), new(20,-20), new(20,20), new(-20,20) }
            });
            var lamp = scene.SimpleCreateEntity("Driver", Entity.InstanceType.NonSerialized).AddComponent(new PointLight { Intensity = 1, RenderLayer = 101 });
            light.SourceLight = lamp;
            scene.Update();
            var pixels = new Color[renderer.LightRT.RenderTarget.Width * renderer.LightRT.RenderTarget.Height];
            Color Sample()
            {
                renderer.Render(scene);
                GraphicsDevice.SetRenderTarget(null);
                renderer.LightRT.RenderTarget.GetData(pixels);
                int width = renderer.LightRT.RenderTarget.Width;
                int height = renderer.LightRT.RenderTarget.Height;
                Assert.That(pixels[0].R, Is.EqualTo(0));
                return pixels[(height / 2) * width + width / 2];
            }
            var full = Sample();
            scene.ClearColor = Color.Black;
            Sample();
            Assert.That(renderer.ClearColor, Is.EqualTo(Color.Black), "Deferred rendering must follow the scene background after loading settings");
            Assert.That(full.R, Is.InRange(60, 66));
            Assert.That(full.G, Is.EqualTo(0));
            lamp.Intensity = 0.5f;
            Assert.That((int)Sample().R, Is.EqualTo(full.R / 2).Within(2));
            lamp.Enabled = false;
            Assert.That(Sample().R, Is.EqualTo(0));
            light.SourceInfluence = 0;
            light.Color = Color.Blue;
            Assert.That(Sample().B, Is.InRange(60, 66));
            light.Vertices = new() { new(-20,-20), new(20,-20), new(20,-10), new(-20,-10) };
            Assert.That(Sample().B, Is.EqualTo(0), "Polygon edits must invalidate the cached field");
            scene.End();
            ((Game)this).Exit();
        }
    }

    [Test]
    public void Rectangle_has_soft_edges_and_no_exterior_fill()
    {
        var light = new RoomLight { BounceStrength = 0 };
        Assert.That(light.TryGetLocalBounds(out var bounds), Is.True);
        Assert.That(bounds.Width, Is.EqualTo(320));
        Assert.That(light.SampleIllumination(new Vector2(161, 0)), Is.EqualTo(Vector3.Zero));
        Assert.That(light.SampleIllumination(new Vector2(159, 0)).X, Is.LessThan(light.SampleIllumination(Vector2.Zero).X));
    }

    [Test]
    public void Concave_polygon_keeps_its_notch_dark_in_both_windings()
    {
        var light = new RoomLight { Vertices = new() { new(0,0), new(100,0), new(100,40), new(40,40), new(40,100), new(0,100) } };
        for (int winding = 0; winding < 2; winding++)
        {
            Assert.That(light.SampleIllumination(new Vector2(70,70)), Is.EqualTo(Vector3.Zero));
            Assert.That(light.SampleIllumination(new Vector2(20,70)).X, Is.GreaterThan(0));
            light.Vertices.Reverse();
        }
    }

    [Test]
    public void Scattering_spreads_light_and_invalid_shapes_are_safe()
    {
        var light = new RoomLight { AmbientFill = 0, Scattering = 0, BounceStrength = 0, Range = 20 };
        Assert.That(light.SampleIllumination(Vector2.Zero), Is.EqualTo(Vector3.Zero));
        light.Scattering = 1;
        Assert.That(light.SampleIllumination(Vector2.Zero).X, Is.EqualTo(1));
        light.Vertices[0] = new Vector2(float.NaN, 0);
        Assert.That(light.TryGetLocalBounds(out _), Is.False);
        Assert.That(light.SampleIllumination(Vector2.Zero), Is.EqualTo(Vector3.Zero));
    }

    [Test]
    public void Room_light_has_an_aot_factory_and_round_trips_polygon_and_controls()
    {
        ComponentDataSerializationBootstrap.EnsureInitialized();
        const string type = "Voltage.DeferredLighting.RoomLight";
        var original = new RoomLight { Intensity = 0.2f, Scattering = 0.4f, BounceStrength = 0.7f, RenderLayer = 100 };
        original.Vertices[0] = new Vector2(-50, -30);
        var copy = (RoomLight)ComponentAotFactory.Create(type);
        var data = ComponentDataAotDeserializer.TryDeserialize(type + "+RoomLightGeneratedData", Voltage.Persistence.Json.ToJson(original.Data));
        Assert.That(data, Is.Not.Null);
        copy.Data = data;
        Assert.That(copy.Vertices, Is.EqualTo(original.Vertices));
        Assert.That(copy.Intensity, Is.EqualTo(0.2f));
        Assert.That(copy.Scattering, Is.EqualTo(0.4f));
        Assert.That(copy.BounceStrength, Is.EqualTo(0.7f));
        Assert.That(copy.RenderLayer, Is.EqualTo(100));
    }
}
