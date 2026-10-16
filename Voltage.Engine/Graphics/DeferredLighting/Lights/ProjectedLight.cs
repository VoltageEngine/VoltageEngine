using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Voltage.Textures;

namespace Voltage.DeferredLighting;

/// <summary>Projects light through a tilemap's alpha into the normal-mapped deferred light buffer.</summary>
public partial class ProjectedLight : DeferredLight
{
    public TilemapRenderer Boards;
    [Tooltip("Openings in the board tilemap's local pixel coordinates.")]
    public List<RectangleF> Windows = new();
    [Tooltip("World-space receiving area; illumination stays inside this rectangle.")]
    public RectangleF RoomArea;
    public Vector2 Direction = new(0.6f, 1);
    public float ProjectionDistance = 44;
    [Range(1, 4)] public float Magnification = 1.5f;
    public float Softness = 2;
    public float Intensity = 0.65f;
    public float NormalZ = 1.5f;
    [PhysicsLayerMask] public int OcclusionLayerMask = Physics.AllLayers;
    private Sprite _field;
    private int _fieldHash;
    private Texture2D _maskTexture;
    private Color[] _maskPixels;
    private Effect _effect;
    public override RectangleF Bounds => RoomArea;

    public static Vector2 ProjectToMask(Vector2 point, RectangleF opening, Vector2 direction, float distance, float magnification) =>
        opening.Center + (point - opening.Center - direction * distance) / magnification;

    public static float SampleTransmission(byte[] alpha, int width, int height, float x, float y)
    {
        int column = (int)MathF.Floor(x), row = (int)MathF.Floor(y);
        float Openness(int cx, int cy) => cx >= 0 && cy >= 0 && cx < width && cy < height
            ? 1 - alpha[cy * width + cx] / 255f : 0;
        float horizontal = x - column, vertical = y - row;
        return MathHelper.Lerp(MathHelper.Lerp(Openness(column, row), Openness(column + 1, row), horizontal),
            MathHelper.Lerp(Openness(column, row + 1), Openness(column + 1, row + 1), horizontal), vertical);
    }

    private Vector2 ToBoardSpace(Vector2 world)
    {
        var value = world - Boards.Entity.Position;
        float cosine = MathF.Cos(-Boards.Entity.Rotation), sine = MathF.Sin(-Boards.Entity.Rotation);
        value = new Vector2(value.X * cosine - value.Y * sine, value.X * sine + value.Y * cosine) - Boards.LocalOffset;
        return value / Boards.Entity.Scale;
    }

    private byte AlphaAt(int x, int y)
    {
        var tileset = Boards.ResolvedTileset;
        int column = (int)MathF.Floor(x / (float)tileset.TileWidth), row = (int)MathF.Floor(y / (float)tileset.TileHeight);
        float px = x - column * tileset.TileWidth + 0.5f, py = y - row * tileset.TileHeight + 0.5f;
        byte orientation = Boards.GetOrientation(column, row);
        var point = new Vector2(px - tileset.TileWidth * 0.5f, py - tileset.TileHeight * 0.5f);
        for (int turn = 0; turn < (orientation & TilemapRenderer.OrientRotationMask); turn++)
            point = new Vector2(point.Y, -point.X);
        point += new Vector2(tileset.TileWidth * 0.5f, tileset.TileHeight * 0.5f);
        if ((orientation & TilemapRenderer.OrientFlipX) != 0) point.X = tileset.TileWidth - point.X;
        if ((orientation & TilemapRenderer.OrientFlipY) != 0) point.Y = tileset.TileHeight - point.Y;
        float transmission = 1;
        foreach (int tile in Boards.GetCellStack(column, row))
        {
            if (tile < 0 || tile >= tileset.SourceRects.Length) continue;
            var source = tileset.SourceRects[tileset.ResolveFrame(tile, Voltage.Utils.Time.TotalTime)];
            int sx = source.X + Math.Clamp((int)point.X, 0, source.Width - 1);
            int sy = source.Y + Math.Clamp((int)point.Y, 0, source.Height - 1);
            transmission *= 1 - _maskPixels[sy * _maskTexture.Width + sx].A / 255f;
        }
        return (byte)Math.Clamp((1 - transmission) * Boards.Color.A, 0, 255);
    }

    private bool EnsureField()
    {
        var tileset = Boards?.ResolvedTileset;
        if (tileset?.Texture == null || Boards.Entity == null || Windows == null || !Boards.Entity.Enabled || !Boards.Enabled ||
            RoomArea.Width <= 0 || RoomArea.Height <= 0 || !float.IsFinite(RoomArea.X) || !float.IsFinite(RoomArea.Y) ||
            !float.IsFinite(RoomArea.Width) || !float.IsFinite(RoomArea.Height) ||
            Boards.Entity.Scale.X == 0 || Boards.Entity.Scale.Y == 0 ||
            !float.IsFinite(Direction.X) || !float.IsFinite(Direction.Y) || Direction.LengthSquared() < 0.001f) return false;
        var hash = new HashCode();
        hash.Add(tileset.Texture); hash.Add(Boards.Entity.Position); hash.Add(Boards.Entity.Scale);
        hash.Add(Boards.Entity.Rotation); hash.Add(Boards.LocalOffset); hash.Add(Boards.Color.A);
        hash.Add(RoomArea); hash.Add(Direction); hash.Add(ProjectionDistance); hash.Add(Magnification); hash.Add(Softness);
        hash.Add(OcclusionLayerMask);
        foreach (var opening in Windows) hash.Add(opening);
        foreach (var tile in Boards.EnumerateTiles())
        {
            hash.Add(tile); hash.Add(Boards.GetOrientation(tile.X, tile.Y));
            foreach (int stacked in Boards.GetCellStack(tile.X, tile.Y))
                hash.Add(tileset.ResolveFrame(stacked, Voltage.Utils.Time.TotalTime));
        }
        var occluders = Physics.BoxcastBroadphase(RoomArea, OcclusionLayerMask);
        foreach (var collider in occluders)
            if (collider.Enabled && !collider.IsTrigger) { hash.Add(collider.Bounds); hash.Add(collider.Rotation); }
        int value = hash.ToHashCode();
        if (_field != null && _fieldHash == value && !_field.Texture2D.IsDisposed) return true;
        if (_maskTexture != tileset.Texture)
        {
            _maskTexture = tileset.Texture;
            _maskPixels = new Color[_maskTexture.Width * _maskTexture.Height];
            _maskTexture.GetData(_maskPixels);
        }
        var masks = new List<(RectangleF Area, byte[] Alpha, int Width, int Height)>();
        foreach (var opening in Windows)
        {
            if (!float.IsFinite(opening.Width) || !float.IsFinite(opening.Height) ||
                opening.Width <= 0 || opening.Height <= 0 || opening.Width > 512 || opening.Height > 512 ||
                !float.IsFinite(opening.X) || !float.IsFinite(opening.Y)) continue;
            int mw = (int)MathF.Ceiling(opening.Width), mh = (int)MathF.Ceiling(opening.Height);
            var alpha = new byte[mw * mh];
            for (int row = 0; row < mh; row++)
                for (int column = 0; column < mw; column++)
                    alpha[row * mw + column] = AlphaAt((int)opening.X + column, (int)opening.Y + row);
            masks.Add((opening, alpha, mw, mh));
        }
        int width = Math.Clamp((int)MathF.Ceiling(RoomArea.Width), 2, 512);
        int height = Math.Clamp((int)MathF.Ceiling(RoomArea.Height), 2, 512);
        var pixels = new Color[(width + 2) * (height + 2)];
        float distance = float.IsFinite(ProjectionDistance) ? Math.Clamp(ProjectionDistance, 0, 512) : 44;
        float magnification = float.IsFinite(Magnification) ? Math.Clamp(Magnification, 1, 4) : 1.5f;
        float softness = float.IsFinite(Softness) ? Math.Clamp(Softness, 0, 16) : 2;
        for (int row = 0; row < height; row++)
            for (int column = 0; column < width; column++)
            {
                var world = new Vector2(RoomArea.X + (column + 0.5f) * RoomArea.Width / width,
                    RoomArea.Y + (row + 0.5f) * RoomArea.Height / height);
                var local = ToBoardSpace(world);
                float illumination = 0;
                foreach (var mask in masks)
                {
                    var point = ProjectToMask(local, mask.Area, Direction, distance, magnification);
                    float transmission = 0;
                    for (int y = -1; y <= 1; y++)
                        for (int x = -1; x <= 1; x++)
                            transmission += SampleTransmission(mask.Alpha, mask.Width, mask.Height,
                                point.X - mask.Area.X + x * softness, point.Y - mask.Area.Y + y * softness) *
                                (x == 0 ? 2 : 1) * (y == 0 ? 2 : 1) / 16f;
                    if (transmission <= 0) continue;
                    var origin = Boards.LocalTileSpaceToWorld(point);
                    foreach (var collider in occluders)
                        if (collider.Enabled && !collider.IsTrigger && !collider.Shape.ContainsPoint(origin) &&
                            collider.Shape.CollidesWithLine(origin, world, out var hit) && hit.Fraction < 0.99f)
                        { transmission = 0; break; }
                    illumination = Math.Min(1, illumination + transmission);
                }
                pixels[(row + 1) * (width + 2) + column + 1] = new Color(illumination, illumination, illumination, 1);
            }
        var texture = new Texture2D(Core.GraphicsDevice, width + 2, height + 2);
        texture.SetData(pixels);
        _field?.Texture2D.Dispose(); _field = new Sprite(texture); _fieldHash = value;
        return true;
    }

    internal void RenderLight(Batcher batcher, Camera camera, Texture2D normalMap)
    {
        if (!EnsureField()) return;
        _effect ??= new Effect(Core.GraphicsDevice, EffectResource.ProjectedLightBytes);
        _effect.Parameters["_matrixTransform"].SetValue(camera.ViewProjectionMatrix);
        _effect.Parameters["_normalMap"].SetValue(normalMap);
        var direction = Vector2.TransformNormal(Direction, Matrix.CreateRotationZ(Boards.Entity.Rotation));
        float z = float.IsFinite(NormalZ) ? Math.Clamp(NormalZ, 0.1f, 8) : 1.5f;
        _effect.Parameters["_surfaceToLight"].SetValue(Vector3.Normalize(new Vector3(-direction, z)));
        var texel = new Vector2(RoomArea.Width / (_field.Texture2D.Width - 2), RoomArea.Height / (_field.Texture2D.Height - 2));
        float strength = float.IsFinite(Intensity) ? Math.Clamp(Intensity, 0, 8) : 0;
        batcher.Begin(BlendState.Additive, SamplerState.LinearClamp, DepthStencilState.None,
            RasterizerState.CullNone, _effect, camera.TransformMatrix, false);
        while (strength > 0)
        {
            float amount = Math.Min(1, strength);
            batcher.Draw(_field, RoomArea.Location - texel, new Color(Color.ToVector3() * amount),
                0, Vector2.Zero, texel, SpriteEffects.None, 0);
            strength -= amount;
        }
        batcher.End();
    }

    public override void DebugRender(Batcher batcher) => batcher.DrawHollowRect(RoomArea, Color, 1);
    public override void OnRemovedFromEntity()
    {
        _field?.Texture2D.Dispose(); _field = null;
        _effect?.Dispose(); _effect = null;
        _maskTexture = null; _maskPixels = null;
        base.OnRemovedFromEntity();
    }
}
