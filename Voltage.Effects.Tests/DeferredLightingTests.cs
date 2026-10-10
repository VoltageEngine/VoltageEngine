using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using NUnit.Framework;
using Voltage.DeferredLighting;
using Voltage.Serialization;
using Voltage.Sprites;

namespace Voltage.Effects.Tests;

[TestFixture]
public class DeferredLightingTests
{
    [Test]
    public void Sprite_emission_follows_the_mask_and_transparent_frames_emit_nothing()
    {
        var blank = SpriteLight.BuildField(new Color[9], 1, 9, 12);
        Assert.That(blank.All(c => c.R == 0), Is.True);
        var line = SpriteLight.BuildField(Enumerable.Repeat(Color.White, 9).ToArray(), 1, 9, 12);
        const int width = 25;
        Assert.That(line[16 * width + 12].R, Is.EqualTo(255));
        Assert.That(line[16 * width + 14].R, Is.GreaterThan(line[16 * width + 20].R));
        Assert.That(line[25 * width + 12].R, Is.GreaterThan(line[16 * width + 21].R), "Long emitters must keep their silhouette instead of a circular volume");
        Assert.That(line[0].R, Is.EqualTo(0));
    }

    [Test]
    public void Tight_sprite_light_preserves_emission_and_feathers_the_near_edge()
    {
        var mask = Enumerable.Repeat(Color.White, 9).ToArray();
        var soft = SpriteLight.BuildField(mask, 1, 9, 12);
        var explicitDefault = SpriteLight.BuildField(mask, 1, 9, 12, 1f);
        var tight = SpriteLight.BuildField(mask, 1, 9, 12, 2f);
        Assert.That(explicitDefault, Is.EqualTo(soft), "Existing lights must retain their exact default field");
        const int center = 16 * 25 + 12;
        Assert.That(tight[center].R, Is.EqualTo(255));
        Assert.That(tight[center + 1].R, Is.InRange(16, 96), "The edge must retain local feathering");
        Assert.That(tight[center + 4].R, Is.LessThan(soft[center + 4].R / 2));
        Assert.That(tight.Select((color, i) => color.R <= soft[i].R).All(value => value), Is.True);
        var translucent = SpriteLight.BuildField(Enumerable.Repeat(new Color(255, 255, 255, 128), 9).ToArray(), 1, 9, 12, 8f);
        Assert.That(translucent[center].R, Is.GreaterThanOrEqualTo(128), "Falloff must not dim source alpha");
        Assert.That(SpriteLight.BuildField(new Color[9], 1, 9, 12, 2f).All(color => color.R == 0), Is.True);
    }

    [TestCase(float.NaN)]
    [TestCase(float.PositiveInfinity)]
    [TestCase(float.NegativeInfinity)]
    [TestCase(-1f)]
    [TestCase(0f)]
    public void Invalid_sprite_light_falloff_uses_the_legacy_profile(float falloff)
    {
        var mask = new[] { Color.White };
        Assert.That(SpriteLight.BuildField(mask, 1, 1, 12, falloff), Is.EqualTo(SpriteLight.BuildField(mask, 1, 1, 12)));
    }

    [Test]
    public void Sprite_visual_rotation_preserves_entity_and_child_transforms()
    {
        var entity = new Entity("Sprite", Entity.InstanceType.NonSerialized) { Position = new Vector2(7, 13), Rotation = 0.25f };
        var child = new Entity("Child", Entity.InstanceType.NonSerialized);
        child.Parent = entity.Transform;
        child.Transform.LocalPosition = new Vector2(3, 4);
        var renderer = entity.AddComponent(new SpriteRenderer());
        var childPosition = child.Position;
        renderer.RotationOffset = 0.75f;
        Assert.That(renderer.RenderRotation, Is.EqualTo(1f));
        Assert.That(entity.Rotation, Is.EqualTo(0.25f));
        Assert.That(child.Position, Is.EqualTo(childPosition));
        renderer.RotationOffset = float.NaN;
        Assert.That(renderer.RotationOffset, Is.Zero);
        Assert.That(renderer.RenderRotation, Is.EqualTo(0.25f));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Sprite_visual_rotation_round_trips_component_data(bool animated)
    {
        ComponentDataSerializationBootstrap.EnsureInitialized();
        var type = animated ? "Voltage.Sprites.SpriteAnimator" : "Voltage.Sprites.SpriteRenderer";
        var dataType = type + (animated ? "+SpriteAnimatorComponentData" : "+SpriteRendererComponentData");
        ComponentData Decode(string json) => animated
            ? Voltage.Persistence.Json.FromJson<SpriteAnimator.SpriteAnimatorComponentData>(json)
            : ComponentDataAotDeserializer.TryDeserialize(dataType, json);
        var renderer = (SpriteRenderer)Voltage.Serialization.Registries.ComponentAotFactory.Create(type);
        renderer.Data = Decode("""{"RotationOffset":0.75}""");
        Assert.That(renderer.RotationOffset, Is.EqualTo(0.75f));
        var restored = (SpriteRenderer)Voltage.Serialization.Registries.ComponentAotFactory.Create(type);
        restored.Data = Decode(Voltage.Persistence.Json.ToJson(renderer.Data));
        Assert.That(restored.RotationOffset, Is.EqualTo(0.75f));
        if (animated)
            Assert.That(new SpriteAnimator.SpriteAnimatorComponentData((SpriteAnimator)renderer).RotationOffset, Is.EqualTo(0.75f));
        else
        {
            Assert.That(new SpriteRenderer.SpriteRendererComponentData(renderer).RotationOffset, Is.EqualTo(0.75f));
            Assert.That(((SpriteRenderer)renderer.Clone()).RotationOffset, Is.EqualTo(0.75f));
        }
        restored.Data = Decode("""{"Enabled":true}""");
        Assert.That(restored.RotationOffset, Is.Zero, "Scenes authored before visual rotation keep their existing behavior");
    }

    [Test]
    public void Spotlight_factory_and_data_preserve_cone_and_render_layer()
    {
        ComponentDataSerializationBootstrap.EnsureInitialized();
        const string type = "Voltage.DeferredLighting.SpotLight";
        var light = (SpotLight)Voltage.Serialization.Registries.ComponentAotFactory.Create(type);
        light.Data = ComponentDataAotDeserializer.TryDeserialize(type + "+SpotLightComponentData",
            """{"ConeAngle":65,"Radius":120,"Intensity":2.5,"ZPosition":24,"RenderLayer":100,"LayerDepth":0.4,"LocalOffset":{"X":3,"Y":4},"ColorR":255,"ColorG":205,"ColorB":139}""");
        Assert.That(light.ConeAngle, Is.EqualTo(65));
        Assert.That(light.Radius, Is.EqualTo(120));
        Assert.That(light.Intensity, Is.EqualTo(2.5));
        Assert.That(light.RenderLayer, Is.EqualTo(100));
        Assert.That(light.LocalOffset, Is.EqualTo(new Vector2(3, 4)));
        Assert.That(((SpotLight.SpotLightComponentData)light.Data).Color, Is.EqualTo(new Color(255,205,139)));
    }

    [Test]
    public void Ambient_map_darkens_only_its_regions_and_lights_still_add()
    {
        if (!OperatingSystem.IsWindows() && Environment.GetEnvironmentVariable("VOLTAGE_TEST_GPU") != "1")
            Assert.Ignore("Requires a native desktop graphics session.");
        using var game = new AmbientGame();
        game.Run();
    }

    private sealed class AmbientGame : Core
    {
        public AmbientGame() : base(32, 16, windowTitle: "Deferred lighting check") { }
        protected override void Initialize()
        {
            base.Initialize();
            using var diffuse = new Texture2D(GraphicsDevice, 2, 1);
            using var normals = new Texture2D(GraphicsDevice, 2, 1);
            using var lights = new Texture2D(GraphicsDevice, 2, 1);
            using var ambient = new Texture2D(GraphicsDevice, 2, 1);
            diffuse.SetData(new[] {Color.White, Color.White});
            normals.SetData(new[] {new Color(128,128,255,0),new Color(128,128,255,0)});
            lights.SetData(new[] {new Color(32,16,0,255),Color.Black});
            ambient.SetData(new[] {new Color(10,12,18),new Color(235,238,245)});
            using var target = new RenderTarget2D(GraphicsDevice, 2, 1);
            using var effect = new DeferredLightEffect();
            var quad = new QuadMesh(GraphicsDevice);
            GraphicsDevice.SetRenderTarget(target);
            GraphicsDevice.BlendState = BlendState.Opaque;
            GraphicsDevice.DepthStencilState = DepthStencilState.None;
            GraphicsDevice.RasterizerState = RasterizerState.CullNone;
            effect.SetAmbientColor(new Color(235,238,245));
            effect.PrepareForFinalCombine(diffuse, lights, normals, ambient);
            quad.Render();
            GraphicsDevice.SetRenderTarget(null);
            var pixels = new Color[2];
            target.GetData(pixels);
            Assert.That(pixels[0], Is.EqualTo(new Color(42,28,18)), "Light must illuminate a dark region without a black overlay");
            Assert.That(pixels[1], Is.EqualTo(new Color(235,238,245)), "Exterior ambient must stay unchanged");
            var scene = new Scene();
            scene.SetPixelArtViewportSize(new Point(32, 16));
            scene.SetDesignResolution(32, 16, Scene.SceneResolutionPolicy.ShowAll);
            var entity = scene.SimpleCreateEntity("Test light", Entity.InstanceType.NonSerialized);
            entity.Position = scene.Camera.Bounds.Center;
            var lamp = entity.AddComponent(new PointLight(Color.Red, 12) { ZPosition = 2, Intensity = 0.5f });
            using var lightTarget = new RenderTarget2D(GraphicsDevice, 32, 16);
            using var mesh = Voltage.DeferredLighting.PolygonMesh.CreateSymmetricalPolygon(10);
            var samples = new Color[32 * 16];
            Color Sample(float intensity)
            {
                lamp.Intensity = intensity;
                GraphicsDevice.SetRenderTarget(lightTarget);
                GraphicsDevice.Clear(Color.Black);
                GraphicsDevice.BlendState = BlendState.Additive;
                effect.SetNormalMap(normals);
                effect.UpdateForCamera(scene.Camera);
                effect.UpdateForLight(lamp);
                mesh.Render();
                GraphicsDevice.SetRenderTarget(null);
                lightTarget.GetData(samples);
                return samples[8 * 32 + 16];
            }
            var half = Sample(0.5f);
            var full = Sample(1f);
            Assert.That(half.R, Is.GreaterThan(50), "Real red illumination must replace debug grayscale");
            Assert.That(half.G, Is.EqualTo(0));
            Assert.That(half.B, Is.EqualTo(0));
            Assert.That((int)full.R, Is.EqualTo(half.R * 2).Within(2), "Intensity must be linear, not multiplied by alpha again");
            scene.SceneRenderTarget.Dispose();
            scene.Content.Dispose();
            ((Game)this).Exit();
        }
    }
}
