using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using NUnit.Framework;
using Voltage.Sprites;
using Voltage.Textures;

namespace Voltage.Effects.Tests;

[TestFixture]
public class PixelArtTests
{
    [Test]
    public void Asset_backed_animators_share_textures_preserve_origins_and_release_on_scene_end()
    {
        if (!OperatingSystem.IsWindows() && Environment.GetEnvironmentVariable("VOLTAGE_TEST_GPU") != "1")
            Assert.Ignore("Requires a native desktop graphics session.");
        using var game = new AnimatorLifetimeGame();
        game.Run();
    }

    private sealed class AnimatorLifetimeGame : Core
    {
        public AnimatorLifetimeGame() : base(32, 32, windowTitle: "Animator lifetime check") { }
        protected override void Initialize()
        {
            base.Initialize();
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (root != null && !File.Exists(Path.Combine(root.FullName, "VoltageEngine.sln"))) root = root.Parent;
            Assert.That(root, Is.Not.Null);
            var scene = new Scene();
            Core.Scene = scene;
            var entity = scene.SimpleCreateEntity("Animator", Entity.InstanceType.NonSerialized);
            var animator = entity.AddComponent(new SpriteAnimator
            {
                Data = new SpriteAnimator.SpriteAnimatorComponentData
                {
                    TextureFilePath = Path.Combine(root.FullName, "Voltage.Editor", "DefaultContent", "UI", "Custom", "CursorSelection-UI.aseprite"),
                    Origin = new Vector2(8, 12)
                }
            });
            var source = animator.TextureFilePath;
            var other = scene.SimpleCreateEntity("Other", Entity.InstanceType.NonSerialized).AddComponent(new SpriteAnimator
            {
                Data = new SpriteAnimator.SpriteAnimatorComponentData { TextureFilePath = source, Origin = new Vector2(2, 3) }
            });
            scene.Update();
            Assert.That(other.Sprite.Texture2D, Is.SameAs(animator.Sprite.Texture2D));
            Assert.That(other.Origin, Is.EqualTo(new Vector2(2, 3)));
            animator.LoadAllAnimations();
            Assert.That(other.Sprite.Texture2D, Is.SameAs(animator.Sprite.Texture2D));
            Assert.That(animator.Origin, Is.EqualTo(new Vector2(8, 12)));
            animator.NextFrame();
            Assert.That(animator.Origin, Is.EqualTo(new Vector2(8, 12)));
            Assert.That(other.CurrentFrame, Is.Zero);
            var texture = animator.Sprite.Texture2D;
            var file = scene.Content.LoadAsepriteFile(source);
            var defaultOrigin = other.Animations["Default"].Sprites[0].Origin;
            var defaultRate = other.Animations["Default"].FrameRates[0];
            animator.Animations["Default"].Sprites[0].Origin = new Vector2(31, 37);
            animator.Animations["Default"].FrameRates[0] *= 0.5f;
            Assert.That(other.Animations["Default"].Sprites[0].Origin, Is.EqualTo(defaultOrigin));
            Assert.That(other.Animations["Default"].FrameRates[0], Is.EqualTo(defaultRate));
            Assert.That(scene.Content.LoadAsepriteAtlas(source).Sprites[0].Origin, Is.EqualTo(defaultOrigin));
            var layer = file.Layers[0].Name;
            var selected = scene.Content.LoadAsepriteAtlas(source, new[] { layer, "missing-layer" });
            Assert.That(selected, Is.SameAs(scene.Content.LoadAsepriteAtlas(source, new[] { "missing-layer", layer })));
            Assert.That(selected.Sprites[0].Texture2D, Is.Not.SameAs(texture));
            for (var frame = 0; frame < file.Frames.Count; frame++)
            {
                var sprite = selected.Sprites[frame];
                var pixels = new Color[file.CanvasWidth * file.CanvasHeight];
                sprite.Texture2D.GetData(0, sprite.SourceRect, pixels, 0, pixels.Length);
                Assert.That(pixels, Is.EqualTo(file.Frames[frame].FlattenFrameOnLayers(true, false, new[] { layer })));
            }
            var helperScene = new Scene();
            helperScene.Begin();
            var helperEntities = Enumerable.Range(0, 4).Select(index =>
                helperScene.SimpleCreateEntity("Helper " + index, Entity.InstanceType.NonSerialized)).ToArray();
            var helperAnimators = helperEntities.Select((item, index) =>
                item.AddComponent(new SpriteAnimator { Origin = new Vector2(13 + index, 17 + index) })).ToArray();
            helperScene.Update();
            var helperFile = helperScene.Content.LoadAsepriteFile(source);
            const string tag = "Helper";
            foreach (var name in new[] { tag, "Overlap" })
                helperFile.Tags.Add((Voltage.Aseprite.AsepriteTag)Activator.CreateInstance(typeof(Voltage.Aseprite.AsepriteTag),
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic, null,
                    new object[] { 0, helperFile.Frames.Count - 1, Voltage.Aseprite.AsepriteLoopDirection.Forward, Color.White, name }, null)!);
            var helperAnimations = new[]
            {
                Voltage.Utils.AsepriteUtils.LoadAsepriteAnimation(helperEntities[0], source, tag, "EntityClip"),
                Voltage.Utils.AsepriteUtils.LoadAsepriteAnimation(helperAnimators[1], source, tag, "AnimatorClip"),
                Voltage.Utils.AsepriteUtils.LoadAsepriteAnimationWithLayers(helperEntities[2], source, tag, "EntityLayersClip", layer, "missing-layer"),
                Voltage.Utils.AsepriteUtils.LoadAsepriteAnimationWithLayers(helperAnimators[3], source, tag, "AnimatorLayersClip", "missing-layer", layer)
            };
            var helperTexture = helperAnimations[0].Sprites[0].Texture2D;
            var helperLayerTexture = helperAnimations[2].Sprites[0].Texture2D;
            Assert.That(helperTexture, Is.Not.SameAs(texture));
            Assert.That(helperAnimations[1].Sprites[0].Texture2D, Is.SameAs(helperTexture));
            Assert.That(helperAnimations[3].Sprites[0].Texture2D, Is.SameAs(helperLayerTexture));
            Assert.That(helperLayerTexture, Is.Not.SameAs(helperTexture));
            Assert.That(helperAnimators[0].Animations["EntityClip"], Is.SameAs(helperAnimations[0]));
            Assert.That(helperAnimators[3].Animations["AnimatorLayersClip"], Is.SameAs(helperAnimations[3]));
            var sharedHelperAnimation = helperScene.Content.LoadAsepriteAtlas(source).GetAnimation(tag);
            var originalHelperOrigin = sharedHelperAnimation.Sprites[0].Origin;
            var originalHelperRate = sharedHelperAnimation.FrameRates[0];
            helperAnimations[0].Sprites[0].Origin = new Vector2(31, 37);
            helperAnimations[0].FrameRates[0] *= 0.5f;
            Assert.That(helperAnimations[1].Sprites[0].Origin, Is.EqualTo(originalHelperOrigin));
            Assert.That(sharedHelperAnimation.Sprites[0].Origin, Is.EqualTo(originalHelperOrigin));
            Assert.That(helperAnimations[1].FrameRates[0], Is.EqualTo(originalHelperRate));
            Assert.That(sharedHelperAnimation.FrameRates[0], Is.EqualTo(originalHelperRate));
            var automaticAnimators = Enumerable.Range(0, 2).Select(index =>
                helperScene.SimpleCreateEntity("Automatic " + index, Entity.InstanceType.NonSerialized)
                    .AddComponent(new SpriteAnimator { TextureFilePath = source })).ToArray();
            helperScene.Update();
            Assert.That(automaticAnimators[0].Sprite.Texture2D, Is.SameAs(helperTexture));
            Assert.That(automaticAnimators[1].Sprite.Texture2D, Is.SameAs(helperTexture));
            var automaticClip = automaticAnimators[0].Animations[tag];
            var automaticOtherClip = automaticAnimators[1].Animations[tag];
            Assert.That(automaticClip.Sprites[0], Is.SameAs(automaticAnimators[0].Animations["Overlap"].Sprites[0]));
            automaticClip.Sprites[0].Origin = new Vector2(41, 43);
            automaticClip.FrameRates[0] *= 0.5f;
            Assert.That(automaticOtherClip.Sprites[0].Origin, Is.EqualTo(originalHelperOrigin));
            Assert.That(automaticOtherClip.FrameRates[0], Is.EqualTo(originalHelperRate));
            Assert.That(sharedHelperAnimation.Sprites[0].Origin, Is.EqualTo(originalHelperOrigin));
            Assert.That(sharedHelperAnimation.FrameRates[0], Is.EqualTo(originalHelperRate));
            Assert.That(automaticAnimators[0].Animations["Overlap"].FrameRates[0], Is.EqualTo(originalHelperRate));
            foreach (var (helper, index) in helperAnimators.Select((item, index) => (item, index)))
                Assert.That(helper.Origin, Is.EqualTo(new Vector2(13 + index, 17 + index)));
            helperAnimators[0].Play("EntityClip");
            helperAnimators[0].NextFrame();
            Assert.That(helperAnimators[0].Origin, Is.EqualTo(new Vector2(13, 17)));
            Assert.That(helperAnimators[1].CurrentFrame, Is.Zero);
            helperEntities[0].RemoveComponent(helperAnimators[0]);
            helperScene.Update();
            Assert.That(helperTexture.IsDisposed, Is.False);
            helperAnimators[1].Play("AnimatorClip");
            helperAnimators[1].NextFrame();
            helperScene.End();
            Assert.That(helperTexture.IsDisposed, Is.True);
            Assert.That(helperLayerTexture.IsDisposed, Is.True);
            Assert.That(texture.IsDisposed, Is.False);
            var privateAtlas = file.ToSpriteAtlas();
            var privateTexture = privateAtlas.Sprites[0].Texture2D;
            Assert.That(privateTexture, Is.Not.SameAs(texture));
            ((IDisposable)privateAtlas).Dispose();
            Assert.That(privateTexture.IsDisposed, Is.True);
            Assert.That(texture.IsDisposed, Is.False);
            Assert.That(scene.Content.EvictCachedAsset(source), Is.True);
            var refreshed = scene.Content.LoadAsepriteAtlas(source);
            Assert.That(refreshed.Sprites[0].Texture2D, Is.Not.SameAs(texture));
            Assert.That(texture.IsDisposed, Is.False);
            var automatic = new SpriteAnimator();
            var first = new Sprite(texture, new Rectangle(0, 0, 2, 2));
            var second = new Sprite(texture, new Rectangle(0, 0, 4, 4));
            automatic.AddAnimation("Frames", new[] { first, second });
            automatic.Play("Frames");
            Assert.That(automatic.Origin, Is.EqualTo(first.Origin));
            automatic.NextFrame();
            Assert.That(automatic.Origin, Is.EqualTo(second.Origin));
            entity.RemoveComponent(animator);
            scene.Update();
            Assert.That(texture.IsDisposed, Is.False);
            other.NextFrame();
            Assert.That(other.Origin, Is.EqualTo(new Vector2(2, 3)));
            var refreshedTexture = refreshed.Sprites[0].Texture2D;
            var selectedTexture = selected.Sprites[0].Texture2D;
            scene.End();
            Assert.That(texture.IsDisposed, Is.True);
            Assert.That(refreshedTexture.IsDisposed, Is.True);
            Assert.That(selectedTexture.IsDisposed, Is.True);
            ((Game)this).Exit();
        }
    }

    [Test]
    public void New_parented_transform_keeps_world_positions_finite()
    {
        var root = new Entity("Root", Entity.InstanceType.NonSerialized);
        var lamp = new Entity("Lamp", Entity.InstanceType.NonSerialized) { Parent = root.Transform };
        var burst = new Entity("Burst", Entity.InstanceType.NonSerialized) { Parent = lamp.Transform };
        var position = new Vector2(314.965f, 213.93439f);
        burst.Position = position;
        Assert.That(burst.Position, Is.EqualTo(position));
        root.Position = new Vector2(7, 11);
        Assert.That(burst.Position, Is.EqualTo(position + root.Position));
    }

    [Test]
    public void Tilemap_tint_and_cells_survive_runtime_deserialization()
    {
        Voltage.Serialization.ComponentDataSerializationBootstrap.EnsureInitialized();
        var data = (TilemapRenderer.TilemapRendererComponentData)Voltage.Serialization.AotDeserializers.DeserializeTilemapRenderer(
            """{"Color":{"B":205,"G":195,"R":185,"A":255,"PackedValue":4291675065},"ChunkCoords":[0,-1],"ChunkData":["0*1017,318,319,0*5"],"OrientationValues":[3],"LocalOffset":{"X":2,"Y":-3},"RenderLayer":100,"AutoBuildColliders":false} """);
        Assert.That(data.Color, Is.EqualTo(new Color(185, 195, 205, 255)));
        Assert.That(data.ChunkCoords, Is.EqualTo(new[] { 0, -1 }));
        Assert.That(data.ChunkData, Is.EqualTo(new[] { "0*1017,318,319,0*5" }));
        Assert.That(data.OrientationValues, Is.EqualTo(new byte[] { 3 }));
        Assert.That(data.LocalOffset, Is.EqualTo(new Vector2(2, -3)));
        Assert.That(data.RenderLayer, Is.EqualTo(100));
        Assert.That(data.AutoBuildColliders, Is.False);
    }

    [Test]
    public void Partially_loaded_font_can_be_disposed()
    {
        using var font = new Voltage.BitmapFonts.BitmapFont { Textures = new Texture2D[1] };
        Assert.DoesNotThrow(font.Dispose);
    }

    [Test]
    public void Integer_pixels_keep_their_size_during_fractional_camera_motion()
    {
        if (!OperatingSystem.IsWindows() && Environment.GetEnvironmentVariable("VOLTAGE_TEST_GPU") != "1")
            Assert.Ignore("Requires a native desktop graphics session.");
        using var game = new PixelGame();
        game.Run();
        Assert.That(game.FramesChecked, Is.EqualTo(24));
    }

    private sealed class PixelGame : Core
    {
        public int FramesChecked;
        public PixelGame() : base(64, 32, windowTitle: "Pixel rendering check") { }
        protected override void Initialize()
        {
            base.Initialize();
            var scene = new Scene();
            scene.SetPixelArtViewportSize(new Point(50, 26));
            scene.SetDesignResolution(16, 8, Scene.SceneResolutionPolicy.SmoothPixelPerfect);
            Assert.That(scene.PixelArtRenderScale, Is.EqualTo(3));
            Assert.That(scene.SceneRenderTargetSize, Is.EqualTo(new Point(50, 26)));
            Assert.That(scene.Camera.RawZoom, Is.EqualTo(1));
            Assert.That(scene.Camera.Position, Is.EqualTo(Vector2.Zero));
            Assert.That(scene.Camera.Bounds.Width, Is.EqualTo(50f / 3).Within(0.001));
            Assert.That(scene.Camera.Bounds.Height, Is.EqualTo(26f / 3).Within(0.001));
            Assert.That(scene.Camera.ProjectionMatrix.M11, Is.EqualTo(2f / 50).Within(0.00001), "Lighting projection must use the game panel, not the host window");
            Assert.That(scene.Camera.ProjectionMatrix.M22, Is.EqualTo(-2f / 26).Within(0.00001));
            using var texture = new Texture2D(GraphicsDevice, 4, 1);
            texture.SetData(new[] { Color.Red, Color.Green, Color.Blue, Color.White });
            using var batch = new SpriteBatch(GraphicsDevice);
            using var voltageBatch = new Batcher(GraphicsDevice);
            var mover = scene.SimpleCreateEntity("Moving sprite", Entity.InstanceType.NonSerialized);
            var renderer = mover.AddComponent(new SpriteRenderer(new Sprite(texture)));
            var samples = new Color[50 * 26];
            int previousStart = -1, advances = 0;
            for (int frame = 0; frame < 24; frame++)
            {
                scene.Camera.Position = new Vector2(frame / 12f, 0.25f);
                GraphicsDevice.SetRenderTarget(scene.SceneRenderTarget);
                GraphicsDevice.Clear(Color.Black);
                batch.Begin(samplerState: SamplerState.PointClamp, transformMatrix: scene.Camera.TransformMatrix);
                batch.Draw(texture, new Vector2(4, 2), Color.White);
                batch.End();
                scene.SceneRenderTarget.GetData(samples);
                var row = samples.Skip(5 * 50).Take(50).ToArray();
                foreach (var color in new[] { Color.Red, Color.Green, Color.Blue, Color.White })
                    Assert.That(row.Count(pixel => pixel == color), Is.EqualTo(3), $"frame {frame}, color {color}");
                int start = Array.IndexOf(row, Color.Red);
                if (previousStart >= 0) { Assert.That(Math.Abs(start - previousStart), Is.LessThanOrEqualTo(1)); if (start != previousStart) advances++; }
                previousStart = start;
                var point = new Vector2(4.25f, 2.75f);
                var roundTrip = scene.Camera.ScreenToWorldPoint(scene.Camera.WorldToScreenPoint(point));
                Assert.That(Vector2.Distance(point, roundTrip), Is.LessThan(0.0001f));
                mover.Position = new Vector2(5 + frame / 8f, 2.25f);
                var authoredPosition = mover.Position;
                GraphicsDevice.SetRenderTarget(scene.SceneRenderTarget);
                GraphicsDevice.Clear(Color.Black);
                voltageBatch.Begin(scene.Camera.TransformMatrix);
                renderer.Render(voltageBatch, scene.Camera);
                voltageBatch.End();
                scene.SceneRenderTarget.GetData(samples);
                int spriteRow = Array.IndexOf(samples, Color.Red) / 50 + 1;
                row = samples.Skip(spriteRow * 50).Take(50).ToArray();
                foreach (var color in new[] { Color.Red, Color.Green, Color.Blue, Color.White })
                {
                    Assert.That(row.Count(pixel => pixel == color), Is.EqualTo(3), $"moving sprite frame {frame}, color {color}");
                    Assert.That(samples.Count(pixel => pixel == color), Is.EqualTo(9), $"moving sprite pixel area frame {frame}");
                }
                Assert.That(mover.Position, Is.EqualTo(authoredPosition));
                FramesChecked++;
            }
            Assert.That(advances, Is.GreaterThan(3));
            GraphicsDevice.SetRenderTarget(null);
            scene.AddRenderer(new ResizeRenderer());
            scene.SetPixelArtViewportSize(new Point(79, 40));
            Render(scene);
            Assert.That(scene.SceneRenderTargetSize, Is.EqualTo(new Point(79, 40)));
            Assert.That(scene.PixelArtRenderScale, Is.EqualTo(5));
            Assert.That(scene.Camera.RawZoom, Is.EqualTo(1));
            var canvas = scene.SimpleCreateEntity("UI", Entity.InstanceType.NonSerialized).AddComponent(new UICanvas());
            canvas.Stage.Entity = canvas.Entity;
            Assert.That(canvas.Stage.GetWidth(), Is.EqualTo(79f / 5));
            Assert.That(canvas.Stage.GetHeight(), Is.EqualTo(8));
            var cameraPosition = scene.Camera.Position;
            foreach (var size in new[] { new Point(31, 17), new Point(55, 29), new Point(54, 28), new Point(12, 6) })
            {
                GraphicsDevice.SetRenderTarget(null);
                scene.SetPixelArtViewportSize(size);
                Render(scene);
                Assert.That(scene.SceneRenderTargetSize, Is.EqualTo(size), "Preview must follow both growing and shrinking panels");
                Assert.That(scene.PixelArtRenderScale, Is.EqualTo(Math.Max(1, (int)Math.Floor(Math.Min(size.X / 16f, size.Y / 8f) + 0.5f))));
                Assert.That(scene.Camera.Position, Is.EqualTo(cameraPosition));
                Assert.That(scene.Camera.RawZoom, Is.EqualTo(1));
            }
            GraphicsDevice.SetRenderTarget(null);
            scene.SetPixelArtViewportSize(null);
            Render(scene);
            var runtimeSize = scene.SceneRenderTargetSize;
            var runtimeScale = scene.PixelArtRenderScale;
            Color[] Capture()
            {
                GraphicsDevice.SetRenderTarget(scene.SceneRenderTarget);
                GraphicsDevice.Clear(Color.Black);
                batch.Begin(samplerState: SamplerState.PointClamp, transformMatrix: scene.Camera.TransformMatrix);
                batch.Draw(texture, new Vector2(4, 2), Color.White);
                batch.End();
                var pixels = new Color[runtimeSize.X * runtimeSize.Y];
                scene.SceneRenderTarget.GetData(pixels);
                GraphicsDevice.SetRenderTarget(null);
                return pixels;
            }
            var runtimePixels = Capture();
            Assert.That(runtimePixels.Contains(Color.Red), Is.True);
            scene.SetPixelArtViewportSize(runtimeSize);
            Render(scene);
            Assert.That(scene.SceneRenderTargetSize, Is.EqualTo(runtimeSize));
            Assert.That(scene.PixelArtRenderScale, Is.EqualTo(runtimeScale));
            Assert.That(Capture(), Is.EqualTo(runtimePixels), "Editor preview and runtime must produce identical pixels at the same viewport size");
            foreach (var (size, scale) in new[] {
                (new Point(1258, 607), 2), (new Point(1920, 1080), 3),
                (new Point(3840, 2160), 6), (new Point(1280, 800), 2) })
            {
                scene.SetPixelArtViewportSize(size);
                scene.SetDesignResolution(640, 360, Scene.SceneResolutionPolicy.SmoothPixelPerfect);
                Assert.That(scene.SceneRenderTargetSize, Is.EqualTo(size));
                Assert.That(scene.PixelArtRenderScale, Is.EqualTo(scale));
                Assert.That(scene.Camera.RawZoom, Is.EqualTo(1));
            }
            var rendering = Voltage.Project.ProjectSettings.Instance.Rendering;
            bool originalFixedFrame = rendering.SmoothPixelArtFixedFrame;
            try
            {
                rendering.SmoothPixelArtFixedFrame = true;
                foreach (var (size, scale) in new[] {
                    (new Point(954, 514), 2), (new Point(1258, 607), 2),
                    (new Point(1258, 473), 2), (new Point(500, 250), 1),
                    (new Point(1920, 1080), 3), (new Point(1280, 800), 2) })
                {
                    scene.SetPixelArtViewportSize(size);
                    scene.SetDesignResolution(640, 360, Scene.SceneResolutionPolicy.SmoothPixelPerfect);
                    Assert.That(scene.SceneRenderTargetSize, Is.EqualTo(new Point(640 * scale, 360 * scale)));
                    Assert.That(scene.PixelArtRenderScale, Is.EqualTo(scale));
                    Assert.That(scene.Camera.Bounds.Width, Is.EqualTo(640).Within(0.001));
                    Assert.That(scene.Camera.Bounds.Height, Is.EqualTo(360).Within(0.001));
                    var displaySize = new Vector2(scene.SceneRenderTargetSize.X / Input.ResolutionScale.X,
                        scene.SceneRenderTargetSize.Y / Input.ResolutionScale.Y);
                    Assert.That(displaySize.X, Is.LessThanOrEqualTo(size.X + 0.001f));
                    Assert.That(displaySize.Y, Is.LessThanOrEqualTo(size.Y + 0.001f));
                    Assert.That(Math.Min(size.X - displaySize.X, size.Y - displaySize.Y), Is.LessThanOrEqualTo(1));
                    Assert.That(displaySize.X / displaySize.Y, Is.EqualTo(640f / 360).Within(0.005));
                }
            }
            finally { rendering.SmoothPixelArtFixedFrame = originalFixedFrame; }
            scene.SetDesignResolution(16, 8, Scene.SceneResolutionPolicy.ShowAllPixelPerfect);
            Assert.That(scene.PixelArtRenderScale, Is.EqualTo(1));
            scene.SceneRenderTarget.Dispose();
            scene.Content.Dispose();
            ((Game)this).Exit();
        }

        private static void Render(Scene scene) => typeof(Scene)
            .GetMethod("Render", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(scene, null);

        private sealed class ResizeRenderer : Renderer
        {
            public ResizeRenderer() : base(0) { }
            public override void Render(Scene scene) { }
        }
    }

    [Test]
    public void Smooth_pixel_setting_round_trips_through_the_runtime_deserializer()
    {
        var settings = Voltage.Serialization.AotDeserializers.DeserializeProjectSettings("{\"Rendering\":{\"SmoothPixelArt\":true,\"DeferredLighting\":true,\"AmbientLightColor\":{\"R\":235,\"G\":238,\"B\":245,\"A\":255}}}");
        Assert.That(settings.Rendering.SmoothPixelArt, Is.True);
        var fixedFrame = Voltage.Serialization.AotDeserializers.DeserializeProjectSettings("{\"Rendering\":{\"SmoothPixelArt\":true,\"SmoothPixelArtFixedFrame\":true}}");
        Assert.That(fixedFrame.Rendering.SmoothPixelArtFixedFrame, Is.True);
        Assert.That(settings.Rendering.DeferredLighting, Is.True);
        Assert.That(settings.Rendering.AmbientLightColor, Is.EqualTo(new Color(235, 238, 245, 255)));
    }

    [Test]
    public void Moon_light_survives_the_AOT_factory_and_saved_data_reader()
    {
        const string type = "Voltage.DeferredLighting.PointLight";
		Voltage.Serialization.ComponentDataSerializationBootstrap.EnsureInitialized();
        var light = (Voltage.DeferredLighting.PointLight)Voltage.Serialization.Registries.ComponentAotFactory.Create(type);
        light.Data = Voltage.Serialization.ComponentDataAotDeserializer.TryDeserialize(type + "+PointLightComponentData",
            "{\"Radius\":90,\"Intensity\":0.3,\"ZPosition\":35,\"RenderLayer\":100,\"LocalOffset\":{\"X\":3,\"Y\":4}}");
        Assert.That(light.Radius, Is.EqualTo(90));
        Assert.That(light.Intensity, Is.EqualTo(0.3f));
        Assert.That(light.RenderLayer, Is.EqualTo(100));
        Assert.That(light.LocalOffset, Is.EqualTo(new Vector2(3, 4)));
    }
}
