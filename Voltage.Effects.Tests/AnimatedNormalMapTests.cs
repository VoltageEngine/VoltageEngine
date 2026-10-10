using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using NUnit.Framework;
using Voltage.DeferredLighting;
using Voltage.Materials;
using Voltage.Serialization;
using Voltage.Serialization.Registries;
using Voltage.Sprites;
using Voltage.Textures;

namespace Voltage.Effects.Tests;

[TestFixture]
public class AnimatedNormalMapTests
{
    [Test]
    public void Material_disposal_releases_owned_states_and_preserves_shared_states()
    {
        var sharedBlend = BlendState.Opaque;
        var sharedDepth = DepthStencilState.Default;
        var sharedSampler = SamplerState.PointWrap;
        new Material { BlendState = sharedBlend, DepthStencilState = sharedDepth, SamplerState = sharedSampler }.Dispose();
        Assert.That(sharedBlend.IsDisposed, Is.False);
        Assert.That(sharedDepth.IsDisposed, Is.False);
        Assert.That(sharedSampler.IsDisposed, Is.False);
        var blend = new BlendState();
        var depth = new DepthStencilState();
        var sampler = new SamplerState();
        new Material { BlendState = blend, DepthStencilState = depth, SamplerState = sampler }.Dispose();
        Assert.That(blend.IsDisposed, Is.True);
        Assert.That(depth.IsDisposed, Is.True);
        Assert.That(sampler.IsDisposed, Is.True);
    }

    [Test]
    public void Normal_source_and_hidden_layer_bindings_round_trip_without_reflection()
    {
        ComponentDataSerializationBootstrap.EnsureInitialized();
        const string type = "Voltage.DeferredLighting.AnimatedNormalMap";
        var original = new AnimatedNormalMap
        {
            Source = new AssetReference { AssetPath = "Content/Actor.aseprite" },
            Layers = new() { "Normals-Body", "Normals-Knife" },
            Clips = new()
            {
                new() { Animation = "Run", Source = new AssetReference { AssetPath = "Content/Run.aseprite" }, Tag = "Running", Layers = new() { "Normals-Body" } }
            }
        };
        var copy = (AnimatedNormalMap)ComponentAotFactory.Create(type);
        copy.Data = ComponentDataAotDeserializer.TryDeserialize(type + "+AnimatedNormalMapGeneratedData", Voltage.Persistence.Json.ToJson(original.Data));
        Assert.That(copy.Source.AssetPath, Is.EqualTo(original.Source.AssetPath));
        Assert.That(copy.Layers, Is.EqualTo(original.Layers));
        Assert.That(copy.Clips, Has.Count.EqualTo(1));
        Assert.That(copy.Clips[0].Animation, Is.EqualTo("Run"));
        Assert.That(copy.Clips[0].Source.AssetPath, Is.EqualTo("Content/Run.aseprite"));
        Assert.That(copy.Clips[0].Tag, Is.EqualTo("Running"));
        Assert.That(copy.Clips[0].Layers, Is.EqualTo(new[] { "Normals-Body" }));
        var legacy = (AnimatedNormalMap)ComponentAotFactory.Create(type);
        legacy.Data = ComponentDataAotDeserializer.TryDeserialize(type + "+AnimatedNormalMapGeneratedData", "{\"Layers\":[\"Normals-Body\"]}");
        Assert.That(legacy.Clips, Is.Empty);
    }

    [Test]
    public void Animated_normals_track_atlas_frames_flips_rotation_and_unmapped_clips()
    {
        if (!OperatingSystem.IsWindows() && Environment.GetEnvironmentVariable("VOLTAGE_TEST_GPU") != "1")
            Assert.Ignore("Requires a native desktop graphics session.");
        using var game = new NormalGame();
        game.Run();
    }

    private sealed class NormalGame : Core
    {
        public NormalGame() : base(32, 32, windowTitle: "Animated normal map check") { }

        protected override void Initialize()
        {
            base.Initialize();
            using var batcher = new Batcher(GraphicsDevice);
            Graphics.Instance = new Graphics { Batcher = batcher };
            Core.DebugRenderEnabled = false;
            using var diffuse = new Texture2D(GraphicsDevice, 16, 8);
            diffuse.SetData(Enumerable.Repeat(Color.White, 128).ToArray());
            using var normal = new Texture2D(GraphicsDevice, 8, 16);
            normal.SetData(Enumerable.Repeat(new Color(255, 128, 128), 64)
                .Concat(Enumerable.Repeat(new Color(128, 255, 128), 64)).ToArray());
            var scene = new Scene();
            scene.SetDesignResolution(32, 32, Scene.SceneResolutionPolicy.ShowAll);
            var renderer = scene.AddRenderer(new DeferredLightingRenderer(0, 100, SamplerState.PointClamp, 0));
            Core.Scene = scene;
            var entity = scene.SimpleCreateEntity("Actor", Entity.InstanceType.NonSerialized);
            entity.Position = scene.Camera.Bounds.Center;
            var animator = entity.AddComponent(new SpriteAnimator { RenderLayer = 0 });
            animator.AddAnimation("Mapped", new[] { new Sprite(diffuse, new Rectangle(0, 0, 8, 8)), new Sprite(diffuse, new Rectangle(8, 0, 8, 8)) }, 10);
            animator.AddAnimation("Unmapped", new[] { new Sprite(diffuse, new Rectangle(0, 0, 8, 8)) }, 10);
            var mappings = new Dictionary<string, SpriteAnimation>
            {
                ["Mapped"] = new(new[] { new Sprite(normal, new Rectangle(0, 0, 8, 8)), new Sprite(normal, new Rectangle(0, 8, 8, 8)) }, 10)
            };
            using var material = new DeferredAnimatedMaterial(animator, mappings);
            animator.SetMaterial(material);
            scene.Update();
            animator.Play("Mapped");
            animator.Pause();
            var pixels = new Color[renderer.NormalRT.RenderTarget.Width * renderer.NormalRT.RenderTarget.Height];
            Color Sample()
            {
                renderer.Render(scene);
                GraphicsDevice.SetRenderTarget(null);
                renderer.NormalRT.RenderTarget.GetData(pixels);
                return pixels[(renderer.NormalRT.RenderTarget.Height / 2) * renderer.NormalRT.RenderTarget.Width + renderer.NormalRT.RenderTarget.Width / 2];
            }
            Assert.That(Sample().R, Is.EqualTo(255), "The first packed frame must sample its own normal region");
            animator.SetFrame(1);
            Assert.That(Sample().G, Is.EqualTo(255), "Independent diffuse/normal atlas layouts must stay aligned");
            animator.SetFrame(0);
            animator.FlipX = true;
            Assert.That(Sample().R, Is.EqualTo(0), "Mirroring must reverse the normal direction");
            animator.FlipX = false;
            animator.RotationOffset = MathHelper.PiOver2;
            Assert.That(Sample().G, Is.EqualTo(0).Within(1), "Sprite rotation must rotate the normal in screen coordinates");
            animator.RotationOffset = 0;
            animator.Play("Unmapped");
            animator.Pause();
            var flat = Sample();
            Assert.That(flat.R, Is.InRange(127, 128));
            Assert.That(flat.G, Is.InRange(127, 128));
            Assert.That(flat.B, Is.EqualTo(255));
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (root != null && !File.Exists(Path.Combine(root.FullName, "VoltageEngine.sln"))) root = root.Parent;
            Assert.That(root, Is.Not.Null);
            var source = Path.Combine(root!.FullName, "Voltage.Editor", "DefaultContent", "UI", "Custom", "CursorSelection-UI.aseprite");
            var file = scene.Content.LoadAsepriteFile(source);
            var layer = file.Layers[0].Name;
            var normalAtlas = scene.Content.LoadAsepriteAtlas(source, new[] { layer }, false);
            var originalMaterial = animator.Material;
            animator.AddAnimation("AssetClip", new[] { new Sprite(diffuse, new Rectangle(0, 0, 8, 8)) }, 10);
            var binding = entity.AddComponent(new AnimatedNormalMap
            {
                Clips = new()
                {
                    new() { Animation = "AssetClip", Source = new AssetReference { AssetPath = source }, Layers = new() { layer } }
                }
            });
            scene.Update();
            Assert.That(animator.Material, Is.TypeOf<DeferredAnimatedMaterial>());
            animator.Play("AssetClip");
            animator.Pause();
            var mappedMaterial = (DeferredAnimatedMaterial)animator.Material;
            mappedMaterial.OnPreRender(scene.Camera);
            Assert.That(mappedMaterial.Effect.Parameters["_normalMap"].GetValueTexture2D(), Is.SameAs(normalAtlas.Sprites[0].Texture2D));
            binding.Enabled = false;
            Assert.That(animator.Material, Is.SameAs(originalMaterial));
            Assert.That(normalAtlas.Sprites[0].Texture2D.IsDisposed, Is.False);
            binding.Enabled = true;
            Assert.That(animator.Material, Is.TypeOf<DeferredAnimatedMaterial>());
            entity.RemoveComponent(binding);
            scene.Update();
            Assert.That(animator.Material, Is.SameAs(originalMaterial));
            scene.End();
            ((Game)this).Exit();
        }
    }
}
