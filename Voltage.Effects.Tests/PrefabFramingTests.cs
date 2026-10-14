using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using NUnit.Framework;
using Voltage.Editor.Utils;
using Voltage.Sprites;

namespace Voltage.Effects.Tests;

[TestFixture]
public class PrefabFramingTests
{
    [Test, NonParallelizable]
    public void Duplicate_prefab_basenames_are_name_collisions_in_the_inspector()
    {
        var inspectorType = Type.GetType("Voltage.Editor.Windows.EntityInspectorWindow, Voltage.Editor", false);
        if (inspectorType == null) Assert.Ignore("Requires the editor build configuration.");
        var originalRoot = Scene.ProjectDirectory;
        var originalPrefabs = Scene.PrefabsDirectory;
        var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "prefab-name-tests", Guid.NewGuid().ToString("N"));
        try
        {
            foreach (var folder in new[] { "Actors", "Props" })
            {
                System.IO.Directory.CreateDirectory(System.IO.Path.Combine(root, folder));
                System.IO.File.WriteAllText(System.IO.Path.Combine(root, folder, "Shared.vprefab"), "{}");
            }
            Scene.ProjectDirectory = root; Scene.PrefabsDirectory = null;
            Assert.That(() => Scene.ResolvePrefabFile("Shared"), Throws.InvalidOperationException);
            var inspector = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(inspectorType);
            var check = inspectorType.GetMethod("CheckPrefabExists", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.That(check.Invoke(inspector, new object[] { "Shared" }), Is.True);
            Assert.That(check.Invoke(inspector, new object[] { "Actors/Shared" }), Is.True);
            Assert.That(check.Invoke(inspector, new object[] { "Unused" }), Is.False);
        }
        finally
        {
            Scene.ProjectDirectory = originalRoot; Scene.PrefabsDirectory = originalPrefabs;
            Assert.That(System.IO.Path.GetFullPath(root).StartsWith(System.IO.Path.GetFullPath(System.IO.Path.GetTempPath()), StringComparison.OrdinalIgnoreCase), Is.True);
            if (System.IO.Directory.Exists(root)) System.IO.Directory.Delete(root, true);
        }
    }

    private static Entity Authored(string name, Entity parent = null)
    {
        var e = new Entity(name) { Type = Entity.InstanceType.Serialized };
        if (parent != null) e.Parent = parent.Transform;
        return e;
    }

    private sealed class Shape(float width, float height) : RenderableComponent
    {
        public override float Width => width;
        public override float Height => height;
        public override void Render(Batcher batcher, Camera camera) { }
    }

    [TestCase(0, 0, 1, 1)]
    [TestCase(-800, -500, 4000, 3000)]
    [TestCase(9000, 12000, 16, 32)]
    public void Single_content_bounds_ignore_pivot_and_support_tiny_large_and_negative_content(float x, float y, float width, float height)
    {
        var e = Authored("content"); e.Position = new Vector2(x, y);
        var shape = e.AddComponent(new Shape(width, height)); shape.SetSerialized(true);
        Assert.That(EntityFocusBounds.PrefabBounds(new[] { e }), Is.EqualTo(shape.Bounds));
    }

    [TestCase(1, 1, 0)]
    [TestCase(-2, 3, .7f)]
    [TestCase(5, -4, 1.5707964f)]
    public void Nested_mixed_content_uses_transformed_union_and_refreshes_on_reopen(float sx, float sy, float rotation)
    {
        var root = Authored("root"); root.Position = new Vector2(300, -400); root.Scale = new Vector2(sx, sy); root.Rotation = rotation;
        var group = Authored("group", root); group.LocalPosition = new Vector2(50, -80);
        var child = Authored("child", group); child.LocalPosition = new Vector2(-20, 90);
        var shape = child.AddComponent(new Shape(300, 17)); shape.SetSerialized(true);
        var layer = Authored("layer", root);
        var map = layer.AddComponent(new TilemapRenderer { AutoBuildColliders = false });
        map.SetTile(-10, 13, 0); map.SetTile(2, 19, 0);
        var expected = RectangleF.Union(shape.Bounds, map.Bounds);
        Assert.That(EntityFocusBounds.PrefabBounds(new[] { root, child }), Is.EqualTo(expected));
        map.SetTile(30, 30, 0); root.Position += new Vector2(700, 800);
        Assert.That(EntityFocusBounds.PrefabBounds(new[] { root }), Is.EqualTo(RectangleF.Union(shape.Bounds, map.Bounds)));
    }

    [Test]
    public void Visuals_exclude_collision_envelopes_runtime_helpers_empty_maps_disabled_and_light_volumes()
    {
        var root = Authored("root");
        var shape = root.AddComponent(new Shape(16, 32)); shape.SetSerialized(true);
        root.AddComponent(new BoxCollider(-9000, -9000, 18000, 18000));
        root.AddComponent(new StencilLight());
        var helper = new Entity("helper") { Type = Entity.InstanceType.NonSerialized, Position = new Vector2(90000, 90000) };
        helper.Parent = root.Transform;
        var outlier = helper.AddComponent(new Shape(10000, 10000)); outlier.SetSerialized(true);
        var empty = Authored("empty", root); empty.Position = new Vector2(-90000, -90000);
        empty.AddComponent(new TilemapRenderer { AutoBuildColliders = false });
        var disabled = Authored("disabled", root); disabled.Enabled = false;
        var disabledShape = disabled.AddComponent(new Shape(99999, 99999)); disabledShape.SetSerialized(true);
        Assert.That(EntityFocusBounds.PrefabBounds(new[] { root }), Is.EqualTo(shape.Bounds));
    }

    [Test]
    public void Collider_only_and_empty_prefabs_have_stable_fallback_bounds()
    {
        var e = Authored("collider"); e.Position = new Vector2(-80, 120);
        var collider = e.AddComponent(new BoxCollider(10, 20, 100, 50));
        Assert.That(EntityFocusBounds.PrefabBounds(new[] { e }), Is.EqualTo(collider.Bounds));
        var empty = Authored("empty"); empty.Position = new Vector2(300, -200);
        Assert.That(EntityFocusBounds.PrefabBounds(new[] { empty }).Center, Is.EqualTo(empty.Position));
        Assert.That(EntityFocusBounds.PrefabBounds(Array.Empty<Entity>()).Center, Is.EqualTo(Vector2.Zero));
    }

    [TestCase(1, 1, 640, 360)]
    [TestCase(16, 32, 360, 640)]
    [TestCase(5000, 17, 640, 360)]
    [TestCase(17, 5000, 640, 360)]
    [TestCase(10000, 10000, 1600, 1200)]
    [TestCase(10000, 10000, 1200, 1600)]
    [TestCase(0, 0, 640, 360)]
    public void Zoom_fits_both_axes_with_padding_independent_of_previous_zoom(float w, float h, float viewportWidth, float viewportHeight)
    {
        var bounds = new RectangleF(-120, 900, w, h);
        var first = EntityFocusBounds.FitZoom(bounds, new Vector2(viewportWidth, viewportHeight), 1);
        foreach (var previous in new[] { .01f, .5f, 1f, 4f, 8f })
        {
            var fit = EntityFocusBounds.FitZoom(bounds, new Vector2(viewportWidth, viewportHeight) / previous, previous);
            Assert.That(fit, Is.EqualTo(first).Within(.00001));
            Assert.That(Math.Max(32, w) * fit, Is.LessThan(viewportWidth));
            Assert.That(Math.Max(32, h) * fit, Is.LessThan(viewportHeight));
        }
    }

    [Test, NonParallelizable]
    public void Actual_sprite_origin_tilemap_and_nested_transforms_project_inside_the_render_target()
    {
        using var game = new FramingGame();
        game.Run();
    }

    private sealed class FramingGame : Core
    {
        public FramingGame() : base(640, 360, windowTitle: "Prefab framing checks") { }
        protected override void Initialize()
        {
            base.Initialize();
            using var texture = new Texture2D(GraphicsDevice, 32, 16);
            var scene = new Scene();
            Core.Scene = scene;
            var root = scene.AddEntity(Authored("root"));
            root.Position = new Vector2(-700, 1200);
            var child = scene.AddEntity(Authored("sprite", root));
            child.LocalPosition = new Vector2(-200, 90);
            var sprite = child.AddComponent(new SpriteRenderer(new Voltage.Textures.Sprite(texture)));
            sprite.SetOrigin(new Vector2(3, 14)); sprite.LocalOffset = new Vector2(11, -7);
            var layer = scene.AddEntity(Authored("tiles", root));
            layer.LocalPosition = new Vector2(80, -240);
            var map = layer.AddComponent(new TilemapRenderer { AutoBuildColliders = false });
            map.SetTile(-40, 10, 0); map.SetTile(50, 14, 0);
            scene.Update();
            foreach (var scale in new[] { new Vector2(1, 1), new Vector2(-3, 2), new Vector2(4, -5) })
            foreach (var rotation in new[] { 0f, .9f, 1.5707964f })
            {
                root.Scale = scale; root.Rotation = rotation;
                RectangleF Normalize(RectangleF b) => new(Math.Min(b.X, b.Right), Math.Min(b.Y, b.Bottom), Math.Abs(b.Width), Math.Abs(b.Height));
                var bounds = RectangleF.Union(Normalize(sprite.Bounds), Normalize(map.Bounds));
                Assert.That(EntityFocusBounds.PrefabBounds(new[] { root }), Is.EqualTo(bounds), $"{scale} {rotation}: sprite={sprite.Entity.Type}, tiles={map.Entity.Type}");
                var target = EntityFocusBounds.FramePrefab(scene.Camera, new[] { root });
                var midpoint = scene.Camera.WorldToScreenPoint(bounds.Center);
                var size = scene.SceneRenderTargetSize;
                Assert.That(midpoint.X, Is.EqualTo(size.X / 2f).Within(.01));
                Assert.That(midpoint.Y, Is.EqualTo(size.Y / 2f).Within(.01));
                var a = scene.Camera.WorldToScreenPoint(bounds.Location);
                var b = scene.Camera.WorldToScreenPoint(new Vector2(bounds.Right, bounds.Bottom));
                Assert.That(a.X, Is.GreaterThan(0)); Assert.That(a.Y, Is.GreaterThan(0));
                Assert.That(b.X, Is.LessThan(size.X)); Assert.That(b.Y, Is.LessThan(size.Y));
                Assert.That(target, Is.EqualTo(scene.Camera.Position));
            }
            ((Game)this).Exit();
        }
    }
}
