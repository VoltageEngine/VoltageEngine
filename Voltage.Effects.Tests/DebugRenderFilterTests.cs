using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using NUnit.Framework;
using Voltage.DeferredLighting;
using Voltage.Sprites;

namespace Voltage.Effects.Tests;

[TestFixture, NonParallelizable]
public class DebugRenderFilterTests
{
    private DebugRenderCategory _categories;
    private bool _master;
    private SamplerState _sampler;
    private Material _material;
    [SetUp] public void SetUp()
    {
        _categories = DebugRenderFilter.EnabledCategories;
        _master = Core.DebugRenderEnabled;
        _sampler = Core.DefaultSamplerState;
        _material = Material.DefaultMaterial;
        DebugRenderFilter.EnabledCategories = DebugRenderCategory.All;
    }
    [TearDown] public void TearDown()
    {
        DebugRenderFilter.EnabledCategories = _categories;
        Core.DebugRenderEnabled = _master;
        Core.DefaultSamplerState = _sampler;
        Material.DefaultMaterial = _material;
    }

    [Test]
    public void Builtin_families_and_derived_components_use_independent_categories()
    {
        var components = new (Component Component, DebugRenderCategory Category)[]
        {
            (new BoxCollider(), DebugRenderCategory.Colliders),
            (new CircleCollider(), DebugRenderCategory.Colliders),
            (new PolygonCollider(new[] { Vector2.Zero, Vector2.UnitX, Vector2.UnitY }), DebugRenderCategory.Colliders),
            (new SpriteRenderer(), DebugRenderCategory.Sprites),
            (new SpriteAnimator(), DebugRenderCategory.Sprites),
            (new NinePatchSpriteRenderer(), DebugRenderCategory.Sprites),
            (new TilemapRenderer(), DebugRenderCategory.Tilemaps),
            (new TiledMapRenderer(null), DebugRenderCategory.Tilemaps),
            (new PointLight(), DebugRenderCategory.Lights),
            (new SpotLight(), DebugRenderCategory.Lights),
            (new RoomLight(), DebugRenderCategory.Lights),
            (new UICanvas(), DebugRenderCategory.UI),
            (new Probe(), DebugRenderCategory.Other)
        };
        foreach (var (component, category) in components)
        {
            Assert.That(DebugRenderFilter.CategoryOf(component), Is.EqualTo(category), component.GetType().Name);
            DebugRenderFilter.EnabledCategories = category;
            Assert.That(DebugRenderFilter.ShouldRender(component), Is.True, component.GetType().Name);
            DebugRenderFilter.EnabledCategories = DebugRenderCategory.None;
            Assert.That(DebugRenderFilter.ShouldRender(component), Is.False, component.GetType().Name);
        }
    }

    [Test]
    public void Category_filter_preserves_entity_flags_disabled_collider_policy_and_reparenting()
    {
        var hidden = new Entity("hidden") { DebugRenderEnabled = false };
        var visible = new Entity("visible") { DebugRenderEnabled = true };
        var leaf = new Entity("leaf") { Parent = visible.Transform, DebugRenderEnabled = true };
        var probe = leaf.AddComponent(new Probe());
        var collider = leaf.AddComponent(new ColliderProbe { IsVisibleEvenDisabled = true, Enabled = false });
        leaf.Update();
        DebugRenderFilter.EnabledCategories = DebugRenderCategory.Colliders;
        leaf.DebugRender(null);
        Assert.That(collider.Calls, Is.EqualTo(1));
        Assert.That(probe.Calls, Is.Zero);
        Assert.That(collider.Enabled, Is.False);
        Assert.That(leaf.DebugRenderEnabled, Is.True);
        leaf.Parent = hidden.Transform;
        leaf.DebugRender(null);
        Assert.That(collider.Calls, Is.EqualTo(1));
        leaf.Parent = visible.Transform;
        DebugRenderFilter.EnabledCategories = DebugRenderCategory.Other;
        leaf.DebugRender(null);
        Assert.That(probe.Calls, Is.EqualTo(1));
        Assert.That(collider.Calls, Is.EqualTo(1));
        DebugRenderFilter.EnabledCategories = DebugRenderCategory.All;
        leaf.DebugRender(null);
        Assert.That(probe.Calls, Is.EqualTo(2));
        Assert.That(collider.Calls, Is.EqualTo(2));
    }

    [TestCase("Default"), TestCase("Layers"), TestCase("Exclude"), TestCase("Screen"), TestCase("Deferred")]
    public void Builtin_renderers_filter_overlays_and_keep_normal_rendering(string renderer)
    {
        if (!OperatingSystem.IsWindows() && Environment.GetEnvironmentVariable("VOLTAGE_TEST_GPU") != "1")
            Assert.Ignore("Requires a native desktop graphics session.");
        using var game = new FilterGame(renderer);
        game.Run();
    }

    private sealed class Probe : Component
    {
        public int Calls;
        public override void DebugRender(Batcher batcher) => Calls++;
    }
    private sealed class ColliderProbe : BoxCollider
    {
        public int Calls;
        public override void DebugRender(Batcher batcher) => Calls++;
    }
    private sealed class SpriteProbe : SpriteRenderer
    {
        public int Draws, DebugDraws;
        public override RectangleF Bounds => new(12, 12, 20, 20);
        public override void Render(Batcher batcher, Camera camera) { Draws++; batcher.DrawRect(Bounds, Color.White); }
        public override void DebugRender(Batcher batcher) => DebugDraws++;
    }
    private sealed class FilterGame(string rendererName) : Core(64, 64, windowTitle: "Debug category checks")
    {
        protected override void Initialize()
        {
            base.Initialize();
            Core.DefaultSamplerState = new SamplerState { Filter = TextureFilter.Point };
            Material.DefaultMaterial = new Material();
            using var batcher = new Batcher(GraphicsDevice);
            using var pixel = new Texture2D(GraphicsDevice, 1, 1);
            pixel.SetData(new[] { Color.White });
            Graphics.Instance = new Graphics { Batcher = batcher, PixelTexture = new Voltage.Textures.Sprite(pixel) };
            var scene = new Scene();
            scene.SetDesignResolution(64, 64, Scene.SceneResolutionPolicy.ShowAll);
            Core.Scene = scene;
            var root = scene.AddEntity(new Entity("root") { DebugRenderEnabled = true });
            var leaf = scene.AddEntity(new Entity("leaf") { Parent = root.Transform, DebugRenderEnabled = true });
            var sprite = leaf.AddComponent(new SpriteProbe());
            scene.Update();
            Renderer renderer = rendererName switch
            {
                "Layers" => new RenderLayerRenderer(0, 0),
                "Exclude" => new RenderLayerExcludeRenderer(0, 99),
                "Screen" => new ScreenSpaceRenderer(0, 0) { Camera = scene.Camera },
                "Deferred" => new DeferredLightingRenderer(0, 100, SamplerState.PointClamp, 0),
                _ => new DefaultRenderer()
            };
            scene.AddRenderer(renderer);
            using var target = new RenderTarget2D(GraphicsDevice, 64, 64);
            Core.DebugRenderEnabled = true;
            void Draw()
            {
                GraphicsDevice.SetRenderTarget(target);
                renderer.Render(scene);
                GraphicsDevice.SetRenderTarget(null);
            }
            DebugRenderFilter.EnabledCategories = DebugRenderCategory.All & ~DebugRenderCategory.Sprites;
            Draw();
            Assert.That(sprite.Draws, Is.GreaterThan(0), "Filter disabled normal sprite rendering");
            Assert.That(sprite.DebugDraws, Is.Zero, "Renderer bypassed the category filter");
            var draws = sprite.Draws;
            DebugRenderFilter.EnabledCategories = DebugRenderCategory.All;
            Draw();
            Assert.That(sprite.Draws, Is.GreaterThan(draws));
            Assert.That(sprite.DebugDraws, Is.GreaterThan(0));
            sprite.DebugDraws = 0;
            root.DebugRenderEnabled = false;
            Draw();
            Assert.That(sprite.DebugDraws, Is.Zero, "Renderer bypassed the ancestor debug setting");
            Assert.That(leaf.DebugRenderEnabled, Is.True);
            Exit();
        }
    }
}
