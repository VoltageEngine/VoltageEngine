using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using NUnit.Framework;
using Voltage.DeferredLighting;
using Voltage.Materials;
using Voltage.Sprites;
using Voltage.Textures;

namespace Voltage.Effects.Tests;

[TestFixture]
[NonParallelizable]
public class CustomLightAndGlowTests
{
    private sealed class TwoLobeLight : DeferredLight
    {
        public int Calls;
        public override bool RenderCustom(DeferredLightContext context)
        {
            Calls++;
            context.DrawPoint(Entity.Position, 24, 120, 2, Color.White);
            context.DrawPoint(Entity.Position + new Vector2(0, 2), 24, 120, 2, Color.White);
            return true;
        }
    }

    [Test]
    public void Glow_layers_pack_into_alpha_custom_lights_render_and_scenes_render_offscreen()
    {
        if (!OperatingSystem.IsWindows() && Environment.GetEnvironmentVariable("VOLTAGE_TEST_GPU") != "1")
            Assert.Ignore("Requires a native desktop graphics session.");
        using var game = new CheckGame();
        game.Run();
    }

    private static string Fixture()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "VoltageEngine.sln"))) root = root.Parent;
        Assert.That(root, Is.Not.Null);
        return Path.Combine(root!.FullName, "Voltage.Effects.Tests", "Fixtures", "LightFixture.aseprite");
    }

    private static Color Pixel(Sprite sprite, int x, int y)
    {
        var sheet = new Color[sprite.Texture2D.Width * sprite.Texture2D.Height];
        sprite.Texture2D.GetData(sheet);
        return sheet[(sprite.SourceRect.Y + y) * sprite.Texture2D.Width + sprite.SourceRect.X + x];
    }

    private sealed class CheckGame : Core
    {
        public CheckGame() : base(32, 32, windowTitle: "Custom light and glow check") { }

        protected override void Initialize()
        {
            base.Initialize();
            using var batcher = new Batcher(GraphicsDevice);
            Graphics.Instance = new Graphics { Batcher = batcher };
            Core.DebugRenderEnabled = false;
            // A scene that is never made active, as the editor's Light Preview uses.
            var scene = new Scene();
            var renderer = scene.AddRenderer(new DeferredLightingRenderer(0, 100, SamplerState.PointClamp, 0));
            var source = Fixture();

            var plain = scene.Content.LoadAsepriteNormalAtlas(source, new[] { "Normals-Other" });
            Assert.That(plain.HasGlow, Is.False);
            Assert.That(plain, Is.SameAs(scene.Content.LoadAsepriteAtlas(source, new[] { "Normals-Other" }, false)), "Without Glow the load is the ordinary atlas");
            Assert.That(Pixel(plain.Sprites[0], 3, 3), Is.EqualTo(new Color(128, 128, 255, 255)));

            var atlas = scene.Content.LoadAsepriteNormalAtlas(source, new[] { "Normals-Art" });
            Assert.That(atlas.HasGlow, Is.True);
            Assert.That(atlas.GetAnimation("Idle").HasGlow, Is.True);
            Assert.That(Pixel(atlas.Sprites[0], 3, 3), Is.EqualTo(new Color(128, 198, 234, 128)), "Glow strength is the brightest channel, normal RGB stays intact");
            Assert.That(Pixel(atlas.Sprites[0], 12, 3), Is.EqualTo(new Color(128, 198, 234, 0)), "Unpainted pixels keep the RGB and no glow");
            Assert.That(Pixel(atlas.Sprites[1], 3, 3).A, Is.Zero, "The second frame has no glow");

            var entity = scene.SimpleCreateEntity("Actor", Entity.InstanceType.NonSerialized);
            var diffuse = scene.Content.LoadAsepriteAtlas(source, new[] { "Art" }, false);
            var animator = entity.AddComponent(new SpriteAnimator { RenderLayer = 0 });
            animator.AddAnimation("Idle", diffuse.GetAnimation("Idle"));
            animator.AddAnimation("Plain", new SpriteAnimation(diffuse.Sprites, 10));
            using var material = new DeferredAnimatedMaterial(animator, new Dictionary<string, SpriteAnimation>
            {
                ["Idle"] = atlas.GetAnimation("Idle"), ["Plain"] = new(plain.Sprites, 10)
            });
            animator.SetMaterial(material);
            var lightEntity = scene.SimpleCreateEntity("Light", Entity.InstanceType.NonSerialized);
            var custom = lightEntity.AddComponent(new TwoLobeLight { RenderLayer = 100, Enabled = false });
            scene.Update();
            animator.Play("Idle");
            animator.Pause();
            scene.Camera.Position = Vector2.Zero;
            renderer.SetAmbientColor(Color.Black);
            using var target = new RenderTarget2D(GraphicsDevice, 32, 32);
            var pixels = new Color[32 * 32];
            Color Render(int x, int y)
            {
                renderer.RenderTo(scene, target);
                GraphicsDevice.SetRenderTarget(null);
                target.GetData(pixels);
                return pixels[y * 32 + x];
            }

            Assert.That(Render(12, 16).R, Is.InRange(95, 105), "Half glow lights the 200 grey to about 100 with no lights at all");
            Assert.That(Render(20, 16).R, Is.Zero, "No glow and no light stays black");
            animator.Play("Plain");
            animator.Pause();
            Assert.That(Render(12, 16).R, Is.Zero, "A clip without glow must not self-illuminate through its opaque normal alpha");
            animator.Play("Idle");
            animator.Pause();

            custom.Enabled = true;
            scene.Update();
            Assert.That(Render(20, 16).R, Is.GreaterThan(0), "A custom light draws through RenderCustom");
            Assert.That(custom.Calls, Is.GreaterThan(0));
            GraphicsDevice.SetRenderTarget(null);
            renderer.Unload();
            scene.Content.Dispose();
            Exit();
        }
    }
}
