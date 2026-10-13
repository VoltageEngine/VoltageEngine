using System;
using System.Linq;
using Microsoft.Xna.Framework;
using Voltage.DeferredLighting;
using Voltage.Shadows;

namespace Voltage.Editor.Gizmos;

/// <summary>Captures editable light geometry, without changing its entity or illumination source.</summary>
public sealed class LightBoundsResizeState
{
    private readonly RenderableComponent _light;
    private readonly RectangleF _bounds;
    private readonly Vector2 _offset;
    private readonly Vector2[] _vertices;
    private readonly float _width, _height, _radius;
    private readonly Vector3 _direction;

    private static Vector2 Abs(Vector2 value) => new(Math.Abs(value.X), Math.Abs(value.Y));

    public static bool CanResize(RenderableComponent light)
    {
        if (light?.Entity == null) return false;
        if (light is DirLight direction)
            return float.IsFinite(direction.Direction.X) && float.IsFinite(direction.Direction.Y) &&
                float.IsFinite(direction.Direction.Z) && float.IsFinite(light.Entity.Position.X) &&
                float.IsFinite(light.Entity.Position.Y);
        if (light is not (RoomLight or AreaLight or PointLight or SpriteLight or ProjectedLight or PolyLight or StencilLight)) return false;
        if (light is SpriteLight sprite && sprite.Source?.Sprite == null) return false;
        var scale = light.Entity.Scale;
        var bounds = light.Bounds;
        return float.IsFinite(scale.X) && float.IsFinite(scale.Y) &&
            (light is PolyLight || Math.Abs(scale.X) > .0001f && Math.Abs(scale.Y) > .0001f)
            && float.IsFinite(bounds.X) && float.IsFinite(bounds.Y) && float.IsFinite(bounds.Width) && float.IsFinite(bounds.Height)
            && bounds.Width > 0 && bounds.Height > 0;
    }

    public LightBoundsResizeState(RenderableComponent light)
    {
        _light = light;
        _bounds = light.Bounds;
        _offset = light.LocalOffset;
        if (light is RoomLight room) _vertices = room.Vertices.ToArray();
        if (light is AreaLight area) { _width = area.RectangleWidth; _height = area.RectangleHeight; }
        if (light is PointLight point) _radius = point.Radius;
        if (light is SpriteLight sprite) _radius = sprite.Radius;
        if (light is DirLight directional) _direction = directional.Direction;
        if (light is PolyLight polygon) _radius = polygon.Radius;
        if (light is StencilLight stencil) _radius = stencil.Radius;
    }

    public void Restore()
    {
        _light.LocalOffset = _offset;
        if (_light is RoomLight room) room.Vertices = _vertices.ToList();
        if (_light is AreaLight area) area.SetWidth(_width).SetHeight(_height);
        if (_light is PointLight point) point.SetRadius(_radius);
        if (_light is SpriteLight sprite) sprite.Radius = (int)_radius;
        if (_light is ProjectedLight projected) projected.RoomArea = _bounds;
        if (_light is DirLight directional) directional.Direction = _direction;
        if (_light is PolyLight polygon) polygon.SetRadius(_radius);
        if (_light is StencilLight stencil) stencil.SetRadius(_radius);
    }

    public void Drag(Vector2 mouse, RectangleHandleType handle, bool mirrored)
    {
        if (!CanResize(_light) || !float.IsFinite(mouse.X) || !float.IsFinite(mouse.Y)) return;
        if (_light is DirLight directional)
        {
            var delta = mouse - (directional.Entity.Position + _offset);
            if (delta.LengthSquared() < .0001f) return;
            var length = new Vector2(_direction.X, _direction.Y).Length();
            delta.Normalize();
            directional.Direction = new Vector3(delta * (length > .001f ? length : 50f), _direction.Z);
            return;
        }
        bool left = handle is RectangleHandleType.Left or RectangleHandleType.TopLeft or RectangleHandleType.BottomLeft;
        bool right = handle is RectangleHandleType.Right or RectangleHandleType.TopRight or RectangleHandleType.BottomRight;
        bool top = handle is RectangleHandleType.Top or RectangleHandleType.TopLeft or RectangleHandleType.TopRight;
        bool bottom = handle is RectangleHandleType.Bottom or RectangleHandleType.BottomLeft or RectangleHandleType.BottomRight;
        if (!(left || right || top || bottom)) return;

        // Radial lights keep their center; every handle changes the same radius.
        if (_light is PointLight point)
        {
            var delta = Abs(mouse - _bounds.Center);
            float radius = Math.Max(left || right ? delta.X : 0, top || bottom ? delta.Y : 0);
            point.SetRadius(Math.Clamp(radius / Math.Abs(point.Entity.Scale.X), 1, 16384));
            return;
        }
        if (_light is PolyLight polygon)
        {
            var delta = Abs(mouse - polygon.Entity.Position - polygon.LocalOffset);
            var radius = polygon is PolySpotLight ? delta.Length() :
                Math.Max(left || right ? delta.X : 0, top || bottom ? delta.Y : 0);
            polygon.SetRadius(Math.Clamp(radius, 1, 2000));
            return;
        }
        if (_light is StencilLight stencil)
        {
            var delta = Abs(mouse - _bounds.Center);
            var radius = Math.Max(left || right ? delta.X : 0, top || bottom ? delta.Y : 0);
            var scale = stencil.Entity.Scale;
            stencil.SetRadius(Math.Clamp(radius / Math.Max(Math.Abs(scale.X), Math.Abs(scale.Y)), 1, 2000));
            return;
        }
        if (_light is SpriteLight sprite)
        {
            var source = sprite.Source.Bounds;
            var scale = sprite.Source.Entity.Scale;
            float cosine = Math.Abs(MathF.Cos(sprite.Source.RenderRotation)), sine = Math.Abs(MathF.Sin(sprite.Source.RenderRotation));
            var paddingScale = new Vector2(cosine * Math.Abs(scale.X) + sine * Math.Abs(scale.Y), sine * Math.Abs(scale.X) + cosine * Math.Abs(scale.Y));
            var delta = Abs(mouse - source.Center) - new Vector2(source.Width, source.Height) / 2;
            float radius = Math.Max(left || right ? delta.X / Math.Max(.0001f, paddingScale.X) : 0,
                top || bottom ? delta.Y / Math.Max(.0001f, paddingScale.Y) : 0);
            sprite.Radius = (int)Math.Clamp(Math.Round(radius), 1, 128);
            return;
        }

        float l = _bounds.Left, r = _bounds.Right, t = _bounds.Top, b = _bounds.Bottom;
        if (mirrored)
        {
            var half = Vector2.Max(Abs(mouse - _bounds.Center), new Vector2(.5f));
            if (left || right) { l = _bounds.Center.X - half.X; r = _bounds.Center.X + half.X; }
            if (top || bottom) { t = _bounds.Center.Y - half.Y; b = _bounds.Center.Y + half.Y; }
        }
        else
        {
            if (left) l = Math.Min(mouse.X, r - 1);
            if (right) r = Math.Max(mouse.X, l + 1);
            if (top) t = Math.Min(mouse.Y, b - 1);
            if (bottom) b = Math.Max(mouse.Y, t + 1);
        }
        ApplyBounds(new RectangleF(l, t, r - l, b - t));
    }

    private void ApplyBounds(RectangleF target)
    {
        var entity = _light.Entity;
        if (_light is ProjectedLight projected) projected.RoomArea = target;
        else if (_light is AreaLight area)
        {
            area.SetWidth(Math.Clamp(target.Width / Math.Abs(entity.Scale.X), 1, 16384));
            area.SetHeight(Math.Clamp(target.Height / Math.Abs(entity.Scale.Y), 1, 16384));
            area.LocalOffset = target.Center - entity.Position;
        }
        else if (_light is RoomLight room)
        {
            float cosine = MathF.Cos(entity.Rotation), sine = MathF.Sin(entity.Rotation);
            var vertices = new Vector2[_vertices.Length];
            for (int i = 0; i < vertices.Length; i++)
            {
                var local = (_vertices[i] + _offset) * entity.Scale;
                var world = entity.Position + new Vector2(local.X * cosine - local.Y * sine, local.X * sine + local.Y * cosine);
                world = new Vector2(target.X + (world.X - _bounds.X) * target.Width / _bounds.Width,
                    target.Y + (world.Y - _bounds.Y) * target.Height / _bounds.Height) - entity.Position;
                vertices[i] = new Vector2(world.X * cosine + world.Y * sine, -world.X * sine + world.Y * cosine) / entity.Scale - _offset;
            }
            var previous = room.Vertices;
            room.Vertices = vertices.ToList();
            if (!room.TryGetLocalBounds(out _)) room.Vertices = previous;
        }
    }
}
