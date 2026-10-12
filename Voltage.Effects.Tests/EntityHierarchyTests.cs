using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using NUnit.Framework;
using Voltage.Editor.Utils;

namespace Voltage.Effects.Tests;

[TestFixture]
public class EntityHierarchyTests
{
    private static Entity Child(Entity parent, string name = "child")
    {
        var entity = new Entity(name) { DebugRenderEnabled = true };
        entity.Parent = parent.Transform;
        return entity;
    }

    private static TilemapRenderer Map(Entity entity, params Point[] cells)
    {
        var map = entity.AddComponent(new TilemapRenderer { AutoBuildColliders = false });
        foreach (var cell in cells) map.SetTile(cell.X, cell.Y, 0);
        return map;
    }

    private static void CenterIs(IEnumerable<Entity> entities, Vector2 expected)
    {
        var actual = EntityFocusBounds.Center(entities);
        Assert.That(actual.X, Is.EqualTo(expected.X).Within(.01));
        Assert.That(actual.Y, Is.EqualTo(expected.Y).Within(.01));
    }

    [TestCase(1)]
    [TestCase(3)]
    [TestCase(64)]
    public void Ancestor_debug_gate_suppresses_component_calls_and_preserves_local_flags(int depth)
    {
        var root = new Entity("root") { DebugRenderEnabled = true };
        var leaf = root;
        for (int i = 0; i < depth; i++) leaf = Child(leaf);
        var sibling = Child(root, "sibling");
        var probe = leaf.AddComponent(new DebugProbe());
        leaf.Update();
        leaf.DebugRender(null);
        Assert.That(probe.Calls, Is.EqualTo(1));
        root.DebugRenderEnabled = false;
        leaf.DebugRender(null);
        Assert.That(probe.Calls, Is.EqualTo(1));
        Assert.That(leaf.DebugRenderEnabled, Is.True);
        Assert.That(leaf.DebugRenderEnabledInHierarchy, Is.False);
        root.DebugRenderEnabled = true;
        leaf.DebugRender(null);
        Assert.That(probe.Calls, Is.EqualTo(2));
        leaf.DebugRenderEnabled = false;
        root.DebugRenderEnabled = false;
        root.DebugRenderEnabled = true;
        Assert.That(leaf.DebugRenderEnabledInHierarchy, Is.False);
        Assert.That(sibling.DebugRenderEnabledInHierarchy, Is.True);
    }

    [Test]
    public void Debug_gate_tracks_reparenting_and_new_children()
    {
        var hidden = new Entity("hidden") { DebugRenderEnabled = false };
        var visible = new Entity("visible") { DebugRenderEnabled = true };
        var child = Child(hidden);
        Assert.That(child.DebugRenderEnabledInHierarchy, Is.False);
        child.Parent = visible.Transform;
        Assert.That(child.DebugRenderEnabledInHierarchy, Is.True);
        child.Parent = hidden.Transform;
        Assert.That(child.DebugRenderEnabledInHierarchy, Is.False);
        child.Parent = null;
        Assert.That(child.DebugRenderEnabledInHierarchy, Is.True);
    }

    [Test]
    public void Painted_cells_frame_the_layer_and_ancestors_without_empty_origins()
    {
        var root = new Entity("root") { Position = new Vector2(9000, 9000) };
        var group = Child(root);
        var layer = Child(group);
        _ = layer.Position;
        layer.Position = Vector2.Zero;
        Map(layer, new Point(100, -20), new Point(103, -18));
        var empty = Child(root);
        empty.Position = new Vector2(-5000, -5000);
        Map(empty);
        var expected = new Vector2(1632, -296);
        CenterIs(new[] { layer }, expected);
        CenterIs(new[] { group }, expected);
        CenterIs(new[] { root }, expected);
        CenterIs(new[] { root, group, layer }, expected);
    }

    [Test]
    public void Groups_use_union_of_content_instead_of_average_layer_pivots()
    {
        var root = new Entity("root");
        var a = Child(root);
        var b = Child(Child(root));
        Map(a, new Point(10, 20));
        Map(b, new Point(100, 80), new Point(110, 90));
        CenterIs(new[] { root }, new Vector2(968, 888));
        CenterIs(new[] { a, b }, new Vector2(968, 888));
        CenterIs(new[] { a }, new Vector2(168, 328));
    }

    [TestCase(0f, 1f, 1f)]
    [TestCase(0f, -2f, 3f)]
    [TestCase(1.5707964f, 2f, 3f)]
    [TestCase(.7f, -2f, -3f)]
    public void Tile_center_tracks_nested_translation_rotation_scale_and_local_offset(float rotation, float sx, float sy)
    {
        var root = new Entity("root") { Position = new Vector2(200, -80), Rotation = rotation, Scale = new Vector2(sx, sy) };
        var layer = Child(Child(root));
        layer.LocalPosition = new Vector2(30, 50);
        var map = Map(layer, new Point(-12, 30), new Point(-9, 32));
        map.LocalOffset = new Vector2(7, -11);
        var localCenter = new Vector2(-160, 504) * layer.Scale;
        var cos = MathF.Cos(layer.Rotation);
        var sin = MathF.Sin(layer.Rotation);
        var expected = layer.Position + map.LocalOffset + new Vector2(localCenter.X * cos - localCenter.Y * sin, localCenter.X * sin + localCenter.Y * cos);
        CenterIs(new[] { layer }, expected);
        CenterIs(new[] { root }, expected);
    }

    [Test]
    public void Framing_refreshes_after_paint_erase_transform_and_reparent()
    {
        var root = new Entity("root");
        var other = new Entity("other") { Position = new Vector2(100, 200) };
        var layer = Child(root);
        var map = Map(layer, new Point(20, 30));
        CenterIs(new[] { root }, new Vector2(328, 488));
        map.SetTile(40, 50, 0);
        CenterIs(new[] { root }, new Vector2(488, 648));
        map.EraseTile(20, 30);
        CenterIs(new[] { root }, new Vector2(648, 808));
        layer.Parent = other.Transform;
        CenterIs(new[] { other }, new Vector2(748, 1008));
        other.Position += new Vector2(40, -80);
        CenterIs(new[] { other }, new Vector2(788, 928));
        map.ClearAllTiles();
        CenterIs(new[] { other }, layer.Position);
        CenterIs(new[] { root }, root.Position);
    }

    [Test]
    public void Mixed_component_hierarchy_includes_all_bounds_and_hidden_content()
    {
        var root = new Entity("root");
        var layer = Child(root);
        Map(layer, new Point(10, 20));
        var collider = layer.AddComponent(new BoxCollider(900, 500, 100, 50));
        collider.SetEnabled(false);
        layer.DebugRenderEnabled = false;
        layer.AddComponent(new FixedBounds(new RectangleF(-100, -200, 50, 40)));
        CenterIs(new[] { root }, new Vector2(450, 175));
    }

    [Test]
    public void Contentless_hierarchy_uses_leaf_positions_and_ignores_invalid_bounds()
    {
        var root = new Entity("root") { Position = new Vector2(1000, 1000) };
        var a = Child(root); a.Position = new Vector2(-80, 40);
        var b = Child(Child(root)); b.Position = new Vector2(120, 80);
        b.AddComponent(new FixedBounds(new RectangleF(float.NaN, 0, 10, 20)));
        b.AddComponent(new TiledMapRenderer(null));
        CenterIs(new[] { root, null }, new Vector2(20, 60));
        CenterIs(Array.Empty<Entity>(), Vector2.Zero);
        CenterIs(new[] { new Entity("empty") { Position = new Vector2(3, 7) } }, new Vector2(3, 7));
    }

    [Test]
    public void Debug_gate_hides_generated_tile_colliders_in_rendered_pixels_and_camera_centers_at_any_zoom()
    {
        var sampler = Core.DefaultSamplerState;
        var material = Material.DefaultMaterial;
        try
        {
            using var game = new HierarchyGame();
            game.Run();
        }
        finally { Core.DefaultSamplerState = sampler; Material.DefaultMaterial = material; }
    }

    private sealed class DebugProbe : Component
    {
        public int Calls;
        public override void DebugRender(Batcher batcher) => Calls++;
    }

    private sealed class FixedBounds(RectangleF bounds) : RenderableComponent
    {
        public override RectangleF Bounds => bounds;
        public override void Render(Batcher batcher, Camera camera) { }
    }

    private sealed class HierarchyGame : Core
    {
        public HierarchyGame() : base(64, 64, windowTitle: "Entity hierarchy checks") { }
        protected override void Initialize()
        {
            base.Initialize();
            Core.DefaultSamplerState = new SamplerState { Filter = TextureFilter.Point };
            Material.DefaultMaterial = new Material();
            var scene = new Scene();
            Core.Scene = scene;
            scene.SetDesignResolution(64, 64, Scene.SceneResolutionPolicy.ShowAll);
            var root = scene.AddEntity(new Entity("root") { DebugRenderEnabled = true });
            var group = scene.AddEntity(Child(root));
            var entity = scene.AddEntity(Child(group));
            var map = Map(entity, new Point(10, 10));
            map.SetCollision(10, 10, true);
            map.RebuildColliders();
            foreach (var collider in Physics.GetAllColliders().Where(c => c.Entity == entity)) collider.DebugRenderEnabled = true;
            scene.Update();
            var center = EntityFocusBounds.Center(new[] { root });
            foreach (var zoom in new[] { .5f, 1f, 2f })
            {
                scene.Camera.RawZoom = zoom;
                scene.Camera.Position = EntityFocusBounds.CameraPosition(scene.Camera, center);
                var screen = scene.Camera.WorldToScreenPoint(center);
                Assert.That(screen.X, Is.EqualTo(scene.SceneRenderTargetSize.X * .5f).Within(.01));
                Assert.That(screen.Y, Is.EqualTo(scene.SceneRenderTargetSize.Y * .5f).Within(.01));
            }
            using var batcher = new Batcher(GraphicsDevice);
            using var pixel = new Texture2D(GraphicsDevice, 1, 1);
            pixel.SetData(new[] { Color.White });
            Graphics.Instance = new Graphics { Batcher = batcher, PixelTexture = new Voltage.Textures.Sprite(pixel) };
            using var target = new RenderTarget2D(GraphicsDevice, 64, 64);
            var pixels = new Color[64 * 64];
            int Draw()
            {
                GraphicsDevice.SetRenderTarget(target);
                GraphicsDevice.Clear(Color.Transparent);
                batcher.Begin(scene.Camera.TransformMatrix);
                entity.DebugRender(batcher);
                batcher.End();
                GraphicsDevice.SetRenderTarget(null);
                target.GetData(pixels);
                return pixels.Count(p => p.A > 0);
            }
            Core.DebugRenderEnabled = true;
            var visible = Draw();
            Assert.That(visible, Is.GreaterThan(0));
            var categories = DebugRenderFilter.EnabledCategories;
            try
            {
                DebugRenderFilter.EnabledCategories = DebugRenderCategory.None;
                Assert.That(Draw(), Is.Zero);
                DebugRenderFilter.EnabledCategories = DebugRenderCategory.Colliders;
                Assert.That(Draw(), Is.GreaterThan(0), "Tilemap bounds filter hid generated colliders");
                DebugRenderFilter.EnabledCategories = DebugRenderCategory.Tilemaps;
                Assert.That(Draw(), Is.GreaterThan(0), "Collider filter hid tilemap bounds");
            }
            finally { DebugRenderFilter.EnabledCategories = categories; }
            root.DebugRenderEnabled = false;
            Assert.That(Draw(), Is.Zero);
            root.DebugRenderEnabled = true;
            group.DebugRenderEnabled = false;
            Assert.That(Draw(), Is.Zero);
            group.DebugRenderEnabled = true;
            Assert.That(Draw(), Is.EqualTo(visible));
            entity.DebugRenderEnabled = false;
            Assert.That(Draw(), Is.Zero);
            map.RemoveColliders();
            ((Game)this).Exit();
        }
    }
}
