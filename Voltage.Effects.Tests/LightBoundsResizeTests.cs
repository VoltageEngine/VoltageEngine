using Microsoft.Xna.Framework;
using NUnit.Framework;
using Voltage.DeferredLighting;
using Voltage.Editor.Gizmos;
using Voltage.Shadows;

namespace Voltage.Effects.Tests;

[TestFixture]
public class LightBoundsResizeTests
{
    private static Entity NestedEntity()
    {
        var root = new Entity("root") { Position = new(70, -30), Rotation = .3f, Scale = new(-2, 3) };
        var child = new Entity("light") { Parent = root.Transform, LocalPosition = new(12, 8), LocalRotation = .4f, LocalScale = new(.6f, 1.2f) };
        return child;
    }

    private static void EqualBounds(RectangleF actual, RectangleF expected)
    {
        Assert.That(actual.X, Is.EqualTo(expected.X).Within(.002));
        Assert.That(actual.Y, Is.EqualTo(expected.Y).Within(.002));
        Assert.That(actual.Width, Is.EqualTo(expected.Width).Within(.002));
        Assert.That(actual.Height, Is.EqualTo(expected.Height).Within(.002));
    }

    [TestCase(RectangleHandleType.Left)]
    [TestCase(RectangleHandleType.Right)]
    [TestCase(RectangleHandleType.Top)]
    [TestCase(RectangleHandleType.Bottom)]
    [TestCase(RectangleHandleType.TopLeft)]
    [TestCase(RectangleHandleType.TopRight)]
    [TestCase(RectangleHandleType.BottomLeft)]
    [TestCase(RectangleHandleType.BottomRight)]
    public void Room_handles_preserve_entity_source_and_opposite_edges_under_transformed_parents(RectangleHandleType handle)
    {
        var entity = NestedEntity();
        var light = entity.AddComponent(new RoomLight { LocalOffset = new(5, -7) });
        var before = new LightBoundsResizeState(light);
        var bounds = light.Bounds;
        var position = entity.Position;
        var origin = light.LightOrigin;
        var vertices = light.Vertices.ToArray();
        bool left = handle.ToString().Contains("Left"), right = handle.ToString().Contains("Right");
        bool top = handle.ToString().Contains("Top"), bottom = handle.ToString().Contains("Bottom");
        var mouse = new Vector2(left ? bounds.Left - 40 : right ? bounds.Right + 40 : bounds.Center.X,
            top ? bounds.Top - 20 : bottom ? bounds.Bottom + 20 : bounds.Center.Y);
        before.Drag(mouse, handle, false);
        var expected = new RectangleF(bounds.X - (left ? 40 : 0), bounds.Y - (top ? 20 : 0),
            bounds.Width + (left || right ? 40 : 0), bounds.Height + (top || bottom ? 20 : 0));
        EqualBounds(light.Bounds, expected);
        Assert.That(entity.Position, Is.EqualTo(position));
        Assert.That(light.LightOrigin, Is.EqualTo(origin));
        Assert.That(light.LocalOffset, Is.EqualTo(new Vector2(5, -7)));
        var after = new LightBoundsResizeState(light);
        before.Restore();
        Assert.That(light.Vertices, Is.EqualTo(vertices));
        EqualBounds(light.Bounds, bounds);
        after.Restore();
        EqualBounds(light.Bounds, expected);
        before.Drag(mouse, handle, true);
        EqualBounds(light.Bounds, new RectangleF(bounds.X - (left || right ? 40 : 0), bounds.Y - (top || bottom ? 20 : 0),
            bounds.Width + (left || right ? 80 : 0), bounds.Height + (top || bottom ? 40 : 0)));
        Assert.That(entity.Position, Is.EqualTo(position));
    }

    [Test]
    public void Room_repeated_drag_uses_start_geometry_and_rejects_invalid_or_collapsed_edits()
    {
        var light = NestedEntity().AddComponent(new RoomLight());
        var before = new LightBoundsResizeState(light);
        var bounds = light.Bounds;
        before.Drag(new(bounds.Right + 10, bounds.Center.Y), RectangleHandleType.Right, false);
        before.Drag(new(bounds.Right + 30, bounds.Center.Y), RectangleHandleType.Right, false);
        Assert.That(light.Bounds.Width, Is.EqualTo(bounds.Width + 30).Within(.002));
        var valid = light.Vertices.ToArray();
        before.Drag(new(float.NaN, 0), RectangleHandleType.Right, false);
        Assert.That(light.Vertices, Is.EqualTo(valid));
        before.Drag(new(bounds.Left - 20, 0), RectangleHandleType.Right, false);
        Assert.That(light.Bounds.Width, Is.EqualTo(1).Within(.002));
        Assert.That(light.TryGetLocalBounds(out _), Is.True);
    }

    [Test]
    public void Area_resize_uses_local_dimensions_keeps_entity_fixed_and_serializes_offset()
    {
        var entity = NestedEntity();
        var light = entity.AddComponent(new AreaLight(40, 30) { LocalOffset = new(10, 20) });
        var before = new LightBoundsResizeState(light);
        var bounds = light.Bounds;
        var position = entity.Position;
        before.Drag(new(bounds.Right + 24, bounds.Bottom + 36), RectangleHandleType.BottomRight, false);
        EqualBounds(light.Bounds, new(bounds.X, bounds.Y, bounds.Width + 24, bounds.Height + 36));
        Assert.That(entity.Position, Is.EqualTo(position));
        Assert.That(Vector2.Distance(light.LocalOffset, new Vector2(22, 38)), Is.LessThan(.001));
        var copy = NestedEntity().AddComponent(new AreaLight());
        copy.Data = light.Data;
        EqualBounds(copy.Bounds, light.Bounds);
        before.Restore();
        EqualBounds(light.Bounds, bounds);
        Assert.That(entity.Position, Is.EqualTo(position));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Radial_lights_resize_their_radius_without_moving_center(bool spot)
    {
        var entity = NestedEntity();
        var light = entity.AddComponent<PointLight>(spot ? new SpotLight() : new PointLight());
        light.Radius = 30; light.LocalOffset = new(5, 8);
        var bounds = light.Bounds;
        var position = entity.Position;
        Assert.That(bounds.Center, Is.EqualTo(position + light.LocalOffset));
        var before = new LightBoundsResizeState(light);
        before.Drag(bounds.Center + new Vector2(120, 0), RectangleHandleType.Right, false);
        Assert.That(light.Bounds.Width, Is.EqualTo(240).Within(.001));
        Assert.That(light.Bounds.Height, Is.EqualTo(240).Within(.001));
        Assert.That(Vector2.Distance(light.Bounds.Center, bounds.Center), Is.LessThan(.001));
        Assert.That(entity.Position, Is.EqualTo(position));
        before.Restore();
        EqualBounds(light.Bounds, bounds);
    }

    [Test]
    public void Missing_source_and_zero_scale_lights_have_no_resize_handles()
    {
        var entity = new Entity("lights");
        Assert.That(LightBoundsResizeState.CanResize(entity.AddComponent(new SpriteLight())), Is.False);
        var room = entity.AddComponent(new RoomLight());
        entity.Scale = new(0, 1);
        Assert.That(LightBoundsResizeState.CanResize(room), Is.False);
    }

    [Test]
    public void Directional_light_handle_turns_direction_without_changing_z_or_entity()
    {
        var entity = new Entity("sun") { Position = new(20, 30) };
        var light = entity.AddComponent(new DirLight { Direction = new(30, 40, 100), LocalOffset = new(5, -5) });
        Assert.That(LightBoundsResizeState.CanResize(light), Is.True);
        var before = new LightBoundsResizeState(light);
        before.Drag(new(75, 25), RectangleHandleType.Right, false);
        Assert.That(light.Direction.X, Is.EqualTo(50).Within(.001));
        Assert.That(light.Direction.Y, Is.EqualTo(0).Within(.001));
        Assert.That(light.Direction.Z, Is.EqualTo(100));
        Assert.That(entity.Position, Is.EqualTo(new Vector2(20, 30)));
        before.Restore();
        Assert.That(light.Direction, Is.EqualTo(new Vector3(30, 40, 100)));
    }

    [Test]
    public void Polygon_light_radius_matches_bounds_and_round_trips()
    {
        var entity = new Entity("polygon") { Position = new(20, 30) };
        var light = entity.AddComponent(new PolyLight(30) { LocalOffset = new(5, 7) });
        EqualBounds(light.Bounds, new(-5, 7, 60, 60));
        Assert.That(LightBoundsResizeState.CanResize(light), Is.True);
        var before = new LightBoundsResizeState(light);
        before.Drag(new(75, 37), RectangleHandleType.Right, false);
        EqualBounds(light.Bounds, new(-25, -13, 100, 100));
        before.Restore();
        EqualBounds(light.Bounds, new(-5, 7, 60, 60));
        Assert.That(entity.Position, Is.EqualTo(new Vector2(20, 30)));
    }

    [Test]
    public void Polygon_spotlight_has_resize_handles_before_scene_start()
    {
        var entity = new Entity("polygon spot") { Position = new(20, 30) };
        var light = entity.AddComponent(new PolySpotLight(30));
        Assert.That(LightBoundsResizeState.CanResize(light), Is.True);
        var before = new LightBoundsResizeState(light);
        before.Drag(new(70, 30), RectangleHandleType.Right, false);
        Assert.That(light.Radius, Is.EqualTo(50).Within(.001));
        before.Restore();
        Assert.That(light.Radius, Is.EqualTo(30).Within(.001));
    }

    [Test]
    public void Stencil_light_radius_uses_absolute_largest_scale_and_round_trips()
    {
        var entity = new Entity("stencil") { Position = new(20, 30), Scale = new(-2, 3) };
        var light = entity.AddComponent(new StencilLight(30) { LocalOffset = new(5, 7) });
        EqualBounds(light.Bounds, new(-65, -53, 180, 180));
        Assert.That(LightBoundsResizeState.CanResize(light), Is.True);
        var before = new LightBoundsResizeState(light);
        before.Drag(new(145, 37), RectangleHandleType.Right, false);
        Assert.That(light.Radius, Is.EqualTo(40).Within(.001));
        EqualBounds(light.Bounds, new(-95, -83, 240, 240));
        before.Restore();
        EqualBounds(light.Bounds, new(-65, -53, 180, 180));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Polygon_vertex_drag_keeps_other_world_vertices_and_entity_position(bool room)
    {
        var entity = NestedEntity();
        Component target = room ? entity.AddComponent(new RoomLight { LocalOffset = new(7, 8) })
            : entity.AddComponent(new PolygonCollider { LocalOffset = new(7, 8) });
        var original = Enumerable.Range(0, PolygonBoundsEditor.Points(target).Count).Select(i => PolygonBoundsEditor.WorldPoint(target, i)).ToArray();
        var position = entity.Position;
        var destination = original[1] + new Vector2(20, 10);
        Assert.That(PolygonBoundsEditor.MovePoint(target, 1, destination), Is.True);
        for (int i = 0; i < original.Length; i++)
            Assert.That(Vector2.Distance(PolygonBoundsEditor.WorldPoint(target, i), i == 1 ? destination : original[i]), Is.LessThan(.002), $"vertex {i}");
        Assert.That(entity.Position, Is.EqualTo(position));
    }

    [Test]
    public void Projected_light_resizes_receiving_area_without_moving_projector()
    {
        var entity = NestedEntity();
        var light = entity.AddComponent(new ProjectedLight { RoomArea = new(100, 200, 80, 60) });
        var before = new LightBoundsResizeState(light);
        var position = entity.Position;
        before.Drag(new(220, 280), RectangleHandleType.BottomRight, false);
        EqualBounds(light.Bounds, new(100, 200, 120, 80));
        Assert.That(entity.Position, Is.EqualTo(position));
        before.Restore();
        EqualBounds(light.Bounds, new(100, 200, 80, 60));
    }
}
