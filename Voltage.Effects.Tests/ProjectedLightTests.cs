using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using NUnit.Framework;
using Voltage.DeferredLighting;
using Voltage.Serialization;
using Voltage.Serialization.Registries;
using Voltage.Tilesets;

namespace Voltage.Effects.Tests;

[TestFixture]
public class ProjectedLightTests
{
    [Test]
    public void Alpha_blocks_transmits_and_softens_instead_of_drawing_lines()
    {
        byte[] alpha = { 255, 0, 128, 255 };
        Assert.That(ProjectedLight.SampleTransmission(alpha, 4, 1, 0, 0), Is.Zero);
        Assert.That(ProjectedLight.SampleTransmission(alpha, 4, 1, 1, 0), Is.EqualTo(1));
        Assert.That(ProjectedLight.SampleTransmission(alpha, 4, 1, 2, 0), Is.EqualTo(127 / 255f).Within(0.001));
        Assert.That(ProjectedLight.SampleTransmission(alpha, 4, 1, 0.5f, 0), Is.EqualTo(0.5));
        Assert.That(ProjectedLight.SampleTransmission(alpha, 4, 1, -1, 0), Is.Zero);
        var window = new RectangleF(20, 10, 40, 20);
        Assert.That(ProjectedLight.ProjectToMask(window.Center + new Vector2(15, 30), window, new Vector2(0.5f, 1), 30, 2),
            Is.EqualTo(window.Center));
    }

    [Test]
    public void Projected_light_round_trips_through_the_aot_registry()
    {
        ComponentDataSerializationBootstrap.EnsureInitialized();
        const string type = "Voltage.DeferredLighting.ProjectedLight";
        var original = new ProjectedLight
        {
            Windows = new() { new(12, 24, 32, 48) }, RoomArea = new(0, 0, 200, 100),
            ProjectionDistance = 50, Magnification = 2, Softness = 3, Intensity = 0.8f,
            Direction = new(-0.5f, 1), Color = Color.CornflowerBlue, RenderLayer = 100, OcclusionLayerMask = 3
        };
        var copy = (ProjectedLight)ComponentAotFactory.Create(type);
        copy.Data = ComponentDataAotDeserializer.TryDeserialize(type + "+ProjectedLightGeneratedData", Voltage.Persistence.Json.ToJson(original.Data));
        Assert.That(copy.Windows, Is.EqualTo(original.Windows));
        Assert.That(copy.RoomArea, Is.EqualTo(original.RoomArea));
        Assert.That(copy.ProjectionDistance, Is.EqualTo(50));
        Assert.That(copy.Magnification, Is.EqualTo(2));
        Assert.That(copy.Softness, Is.EqualTo(3));
        Assert.That(copy.Intensity, Is.EqualTo(0.8f));
        Assert.That(copy.Direction, Is.EqualTo(original.Direction));
        Assert.That(copy.OcclusionLayerMask, Is.EqualTo(3));
    }

    [Test]
    public void Board_gaps_reach_the_deferred_buffer_and_follow_tile_edits()
    {
        if (!OperatingSystem.IsWindows() && Environment.GetEnvironmentVariable("VOLTAGE_TEST_GPU") != "1")
            Assert.Ignore("Requires a native desktop graphics session.");
        using var game = new ProjectionGame();
        game.Run();
    }

    private sealed class ProjectionGame : Core
    {
        public ProjectionGame() : base(64, 64, windowTitle: "Projected lighting check") { }
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
            string directory = Path.Combine(Path.GetTempPath(), "VoltageProjectedLight-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string imagePath = Path.Combine(directory, "mask.png"), tilesetPath = Path.Combine(directory, "mask.vtileset");
            using var texture = new Texture2D(GraphicsDevice, 16, 16);
            var alpha = Enumerable.Repeat(Color.White, 256).ToArray();
            for (int y = 0; y < 16; y++) for (int x = 6; x < 10; x++) alpha[y * 16 + x] = Color.Transparent;
            texture.SetData(alpha);
            using (var output = File.Create(imagePath)) texture.SaveAsPng(output, 16, 16);
            TilesetAssetIO.Save(new TilesetAsset { Texture = new AssetReference { AssetPath = imagePath }, TileWidth = 16, TileHeight = 16 }, tilesetPath);
            var center = scene.Camera.Bounds.Center;
            var boardEntity = scene.SimpleCreateEntity("Board", Entity.InstanceType.NonSerialized);
            boardEntity.Position = center - new Vector2(8);
            var boards = boardEntity.AddComponent(new TilemapRenderer { Tileset = new AssetReference { AssetPath = tilesetPath }, AutoBuildColliders = false });
            boards.SetTile(0, 0, 0);
            var light = scene.SimpleCreateEntity("Projection", Entity.InstanceType.NonSerialized).AddComponent(new ProjectedLight
            {
                Boards = boards, Windows = new() { new(0, 0, 16, 16) },
                RoomArea = new(center.X - 8, center.Y - 8, 16, 32), RenderLayer = 100,
                ProjectionDistance = 8, Magnification = 1, Softness = 0, Direction = Vector2.UnitY, Intensity = 1
            });
            scene.Update();
            var pixels = new Color[renderer.LightRT.RenderTarget.Width * renderer.LightRT.RenderTarget.Height];
            Color Sample(int offsetX = 0)
            {
                renderer.Render(scene); GraphicsDevice.SetRenderTarget(null);
                renderer.LightRT.RenderTarget.GetData(pixels);
                int width = renderer.LightRT.RenderTarget.Width, height = renderer.LightRT.RenderTarget.Height;
                Assert.That(pixels[0].R, Is.Zero, "Light escaped its receiving area");
                return pixels[height / 2 * width + width / 2 + offsetX];
            }
            Assert.That(Sample().R, Is.GreaterThan(150), "Transparent board gaps did not illuminate the light buffer");
            Assert.That(Sample(-6).R, Is.LessThan(5), "Opaque wood transmitted light");
            light.NormalZ = 0.1f;
            Assert.That(Sample().R, Is.LessThan(40), "Projected light ignored the receiving surface normal");
            light.NormalZ = 1.5f;
            var wall = scene.SimpleCreateEntity("Occluder", Entity.InstanceType.NonSerialized);
            wall.Position = center;
            var collider = wall.AddComponent(new BoxCollider(-1, -5, 2, 2));
            scene.Update();
            Assert.That(Sample().R, Is.LessThan(5), "A solid object did not occlude the projected light");
            collider.IsTrigger = true;
            Assert.That(Sample().R, Is.GreaterThan(150), "A trigger occluded the projected light");
            boards.EraseTile(0, 0);
            Assert.That(Sample(-6).R, Is.GreaterThan(150), "Removing boards did not invalidate the projected shadow");
            boards.Enabled = false;
            Assert.That(Sample().R, Is.Zero, "Disabled masks still emitted light");
            boards.Enabled = true;
            light.Intensity = 0;
            Assert.That(Sample().R, Is.Zero);
            scene.End();
            TilesetRuntime.Invalidate(tilesetPath);
            Directory.Delete(directory, true);
            ((Game)this).Exit();
        }
    }
}
