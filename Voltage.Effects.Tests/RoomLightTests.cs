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
            light.SecondaryColor = Color.Red;
            light.SecondaryColorStrength = 1;
            var mixed = Sample();
            Assert.That(mixed.R, Is.InRange(28, 36), "Secondary hue edits must rebuild the cached field");
            Assert.That(mixed.B, Is.InRange(28, 36));
            light.SecondaryColor = Color.Green;
            Assert.That(Sample().G, Is.InRange(14, 20), "Changing the second color must invalidate the cached field");
            light.ColorBlendDirection = Vector2.UnitY;
            Assert.That(Sample().G, Is.InRange(14, 20));
            light.Color = Color.White;
            light.SecondaryColorStrength = 0;
            light.EdgeSoftness = 0;
            var anchoredPosition = entity.Position;
            Sample();
            int bufferWidth = renderer.LightRT.RenderTarget.Width;
            int doorPixel = (renderer.LightRT.RenderTarget.Height / 2) * bufferWidth + bufferWidth / 2 + 24;
            Assert.That(pixels[doorPixel].R, Is.Zero, "Doorway starts outside the room polygon");
            light.Vertices[1] = new(28, -20);
            light.Vertices[2] = new(28, 20);
            Assert.That(Sample().R, Is.InRange(60, 66));
            Assert.That(pixels[doorPixel].R, Is.InRange(60, 66), "Editing X must regenerate the expanded field without a bright seam");
            Assert.That(entity.Position, Is.EqualTo(anchoredPosition));
            light.Vertices[1] = new(20, -20);
            light.Vertices[2] = new(20, 20);
            Sample();
            Assert.That(pixels[doorPixel].R, Is.Zero, "Restoring vertices must remove the expanded field");
            entity.Scale = new Vector2(1.4f, .8f);
            Sample();
            Assert.That(pixels[doorPixel].R, Is.InRange(60, 66), "Manual scale must keep the rendered field aligned with its bounds");
            Assert.That(entity.Position, Is.EqualTo(anchoredPosition));
            entity.Scale = Vector2.One;
            light.LightOrigin = new(-15, 0);
            light.ShadowStrength = 0.75f;
            light.ShadowLayerMask = 1;
            var wallEntity = scene.SimpleCreateEntity("Occluder", Entity.InstanceType.NonSerialized);
            wallEntity.Position = entity.Position + new Vector2(-8, 0);
            var wall = wallEntity.AddComponent(new BoxCollider(-2, -12, 4, 24));
            scene.Update();
            var shadowed = Sample();
            Assert.That(shadowed.R, Is.InRange(12, 20), "Solid geometry must cast a shadow into the room fill");
            wall.IsTrigger = true;
            Assert.That(Sample().R, Is.InRange(60, 66), "Triggers must not cast shadows");
            wall.IsTrigger = false;
            Assert.That(Sample().R, Is.InRange(12, 20));
            wall.Enabled = false;
            Assert.That(Sample().R, Is.InRange(60, 66), "Opening an occluder must invalidate the shadow field");
            light.Vertices = new() { new(-20,-20), new(20,-20), new(20,-10), new(-20,-10) };
            Assert.That(Sample().B, Is.EqualTo(0), "Polygon edits must invalidate the cached field");
            using var spriteTexture = new Texture2D(GraphicsDevice, 8, 4);
            var spriteEntity = scene.SimpleCreateEntity("Sprite source", Entity.InstanceType.NonSerialized);
            spriteEntity.Position = entity.Position;
            spriteEntity.Scale = new(-2, 3);
            spriteEntity.Rotation = .4f;
            var sprite = spriteEntity.AddComponent(new Voltage.Sprites.SpriteRenderer(spriteTexture));
            var glow = scene.SimpleCreateEntity("Sprite glow", Entity.InstanceType.NonSerialized).AddComponent(new SpriteLight { Source = sprite, Radius = 3 });
            var glowBefore = new Voltage.Editor.Gizmos.LightBoundsResizeState(glow);
            var sourceBounds = sprite.Bounds;
            float paddingScale = MathF.Cos(.4f) * 2 + MathF.Sin(.4f) * 3;
            glowBefore.Drag(new(sourceBounds.Right + 10 * paddingScale, sourceBounds.Center.Y), Voltage.Editor.Gizmos.RectangleHandleType.Right, false);
            Assert.That(glow.Radius, Is.EqualTo(10));
            Assert.That(glow.Bounds.Right, Is.EqualTo(sourceBounds.Right + 10 * paddingScale).Within(.001));
            Assert.That(spriteEntity.Position, Is.EqualTo(anchoredPosition));
            glowBefore.Restore();
            Assert.That(glow.Radius, Is.EqualTo(3));
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
    public void Second_hue_is_opt_in_directional_and_keeps_edge_bounce_independent()
    {
        var light = new RoomLight { AmbientFill = 1, EdgeSoftness = 0, BounceStrength = 0 };
        Assert.That(light.Color, Is.EqualTo(Color.White));
        Assert.That(light.SecondaryColorStrength, Is.Zero);
        Assert.That(light.SampleIllumination(Vector2.Zero), Is.EqualTo(Vector3.One));
        light.Color = Color.Red;
        light.SecondaryColor = Color.Blue;
        Assert.That(light.SampleIllumination(new Vector2(120, 0)), Is.EqualTo(Vector3.UnitX));
        light.SecondaryColorStrength = 1;
        Assert.That(light.SampleIllumination(new Vector2(-120, 0)), Is.EqualTo(new Vector3(0.875f, 0, 0.125f)));
        Assert.That(light.SampleIllumination(new Vector2(120, 0)), Is.EqualTo(new Vector3(0.125f, 0, 0.875f)));
        light.ColorBlendDirection = -Vector2.UnitX;
        Assert.That(light.SampleIllumination(new Vector2(120, 0)), Is.EqualTo(new Vector3(0.875f, 0, 0.125f)));
        light.ColorBlendDirection = Vector2.UnitY;
        Assert.That(light.SampleIllumination(new Vector2(0, -60)), Is.EqualTo(new Vector3(0.875f, 0, 0.125f)));
        light.BounceColor = Color.Green;
        light.BounceStrength = 1;
        Assert.That(light.SampleIllumination(new Vector2(0, -79)).Y, Is.GreaterThan(0));
        Assert.That(light.SampleIllumination(new Vector2(0, -81)), Is.EqualTo(Vector3.Zero));
        light.ColorBlendDirection = new Vector2(float.NaN, 0);
        Assert.That(light.SampleIllumination(Vector2.Zero).X, Is.EqualTo(0.5f));
    }

    [Test]
    public void Room_light_has_an_aot_factory_and_round_trips_polygon_and_controls()
    {
        ComponentDataSerializationBootstrap.EnsureInitialized();
        const string type = "Voltage.DeferredLighting.RoomLight";
        var original = new RoomLight
        {
            Intensity = 0.2f, Scattering = 0.4f, BounceStrength = 0.7f, RenderLayer = 100,
            Color = Color.CornflowerBlue, SecondaryColor = Color.OrangeRed,
            SecondaryColorStrength = 0.6f, ColorBlendDirection = new Vector2(-1, 1)
        };
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
        Assert.That(copy.Color, Is.EqualTo(original.Color));
        Assert.That(copy.SecondaryColor, Is.EqualTo(original.SecondaryColor));
        Assert.That(copy.SecondaryColorStrength, Is.EqualTo(0.6f));
        Assert.That(copy.ColorBlendDirection, Is.EqualTo(original.ColorBlendDirection));
        var legacy = System.Text.Json.Nodes.JsonNode.Parse(Voltage.Persistence.Json.ToJson(original.Data));
        foreach (var member in new[] { "SecondaryColor", "SecondaryColorStrength", "ColorBlendDirection" })
            legacy.AsObject().Remove(member);
        copy.Data = ComponentDataAotDeserializer.TryDeserialize(type + "+RoomLightGeneratedData", legacy.ToJsonString());
        Assert.That(copy.SecondaryColorStrength, Is.Zero, "Existing scenes must retain their single-color appearance");
        Assert.That(copy.SecondaryColor, Is.EqualTo(new Color(255, 207, 158)));
        Assert.That(copy.ColorBlendDirection, Is.EqualTo(Vector2.UnitX));
    }
}
