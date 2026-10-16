using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Voltage.DeferredLighting;
using Voltage.PhysicsShapes;

namespace Voltage.Editor.Gizmos;

/// <summary>The same local point list and world-space handles for room lights and polygon colliders.</summary>
public static class PolygonBoundsEditor
{
    public static List<Vector2> Points(Component target) => target switch
    {
        RoomLight room => room.Vertices,
        PolygonCollider collider => collider.Points,
        _ => throw new ArgumentException("Expected a room light or polygon collider.", nameof(target))
    };

    private static Vector2 Rotate(Vector2 point, float rotation)
    {
        float cosine = MathF.Cos(rotation), sine = MathF.Sin(rotation);
        return new(point.X * cosine - point.Y * sine, point.X * sine + point.Y * cosine);
    }

    public static Vector2 WorldPoint(Component target, int index)
    {
        var point = Points(target)[index];
        var entity = target.Entity;
        if (target is RoomLight room)
            return entity.Position + Rotate((point + room.LocalOffset) * entity.Scale, entity.Rotation);
        var collider = (PolygonCollider)target;
        _ = collider.Bounds;
        var polygon = (Polygon)collider.Shape;
        return polygon.Position + polygon.Points[index];
    }

    public static bool MovePoint(Component target, int index, Vector2 world)
    {
        if (target.Entity == null || !float.IsFinite(world.X) || !float.IsFinite(world.Y)) return false;
        var points = Points(target);
        if (points == null || index < 0 || index >= points.Count) return false;
        var entity = target.Entity;
        if (target is PolygonCollider polygon)
        {
            var worldPoints = new Vector2[points.Count];
            var center = Vector2.Zero;
            for (int i = 0; i < points.Count; i++)
            {
                worldPoints[i] = i == index ? world : WorldPoint(target, i);
                center += worldPoints[i];
            }
            center /= points.Count;
            var scale = polygon.ShouldColliderScaleAndRotateWithTransform ? entity.Scale : Vector2.One;
            float rotation = polygon.ShouldColliderScaleAndRotateWithTransform ? entity.Rotation : 0;
            if (!float.IsFinite(scale.X) || !float.IsFinite(scale.Y) || Math.Abs(scale.X) < .0001f || Math.Abs(scale.Y) < .0001f) return false;
            var localCenter = center - entity.Position - Rotate(polygon.LocalOffset * scale, rotation);
            var editedPoints = new List<Vector2>(points.Count);
            foreach (var vertex in worldPoints)
                editedPoints.Add(Rotate(vertex - center, -rotation) / scale + localCenter);
            SetPoints(target, editedPoints);
            return true;
        }
        var point = world - entity.Position;
        var roomScale = entity.Scale;
        if (!float.IsFinite(roomScale.X) || !float.IsFinite(roomScale.Y) || Math.Abs(roomScale.X) < .0001f || Math.Abs(roomScale.Y) < .0001f) return false;
        point = Rotate(point, -entity.Rotation) / roomScale;
        if (target is RoomLight room) point -= room.LocalOffset;
        var edited = new List<Vector2>(points);
        edited[index] = point;
        SetPoints(target, edited);
        return true;
    }

    public static void SetPoints(Component target, List<Vector2> points)
    {
        // A new list on every write prevents later drags from mutating inspector undo snapshots.
        if (target is RoomLight room) room.Vertices = new List<Vector2>(points);
        else ((PolygonCollider)target).Points = new List<Vector2>(points);
    }
}
