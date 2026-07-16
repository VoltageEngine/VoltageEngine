using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Voltage
{
	public partial class TilemapRenderer
	{
		// Per-cell orientation as a byte: bits 0-1 = quarter turns (0-3), bit 2 = flip X, bit 3 = flip Y.
		// Only cells with a non-default orientation appear here, so an un-oriented map pays nothing.
		private readonly Dictionary<long, byte> _cellOrientation = new();

		public const byte OrientRotationMask = 0x3;
		public const byte OrientFlipX = 0x4;
		public const byte OrientFlipY = 0x8;

		public byte GetOrientation(int tileX, int tileY) =>
			_cellOrientation.TryGetValue(CellKey(tileX, tileY), out var o) ? o : (byte)0;

		public void SetOrientation(int tileX, int tileY, byte orientation)
		{
			var key = CellKey(tileX, tileY);
			if (orientation == 0)
				_cellOrientation.Remove(key);
			else
				_cellOrientation[key] = orientation;
		}

		public static byte MakeOrientation(int quarterTurns, bool flipX, bool flipY)
		{
			var o = (byte)(((quarterTurns % 4 + 4) % 4) & OrientRotationMask);
			if (flipX) o |= OrientFlipX;
			if (flipY) o |= OrientFlipY;
			return o;
		}

		private static void DecodeOrientation(byte orientation, out int quarterTurns, out SpriteEffects effects)
		{
			quarterTurns = orientation & OrientRotationMask;
			effects = SpriteEffects.None;
			if ((orientation & OrientFlipX) != 0) effects |= SpriteEffects.FlipHorizontally;
			if ((orientation & OrientFlipY) != 0) effects |= SpriteEffects.FlipVertically;
		}

		// Draws one tile. Orientation 0 keeps the plain top-left draw; an oriented tile is drawn about its centre
		// (origin at the tile's mid-point) so 90° rotations pivot in place, composing with the entity rotation.
		private void DrawTile(Batcher batcher, Texture2D texture, Rectangle source, Vector2 world,
			in TileTransform xform, int tileX, int tileY, int tw, int th, Color color, float rotation, Vector2 scale,
			byte orientation, float layerDepth)
		{
			if (orientation == 0)
			{
				batcher.Draw(texture, world, source, color, rotation, Vector2.Zero, scale,
					SpriteEffects.None, layerDepth);
				return;
			}

			DecodeOrientation(orientation, out var quarterTurns, out var effects);

			var centre = xform.ToWorld(tileX * tw + tw * 0.5f, tileY * th + th * 0.5f);
			var origin = new Vector2(source.Width * 0.5f, source.Height * 0.5f);

			batcher.Draw(texture, centre, source, color,
				rotation + quarterTurns * MathHelper.PiOver2, origin, scale, effects, layerDepth);
		}

		#region Serialization

		private void SaveOrientationTo(TilemapRendererComponentData data)
		{
			if (_cellOrientation.Count == 0)
			{
				data.OrientationCoords = Array.Empty<int>();
				data.OrientationValues = Array.Empty<byte>();
				return;
			}

			var coords = new List<int>(_cellOrientation.Count * 2);
			var values = new List<byte>(_cellOrientation.Count);

			foreach (var (key, orientation) in _cellOrientation)
			{
				coords.Add((int)(key >> 32));
				coords.Add((int)(uint)key);
				values.Add(orientation);
			}

			data.OrientationCoords = coords.ToArray();
			data.OrientationValues = values.ToArray();
		}

		private void LoadOrientationFrom(TilemapRendererComponentData data)
		{
			_cellOrientation.Clear();

			var coords = data.OrientationCoords;
			var values = data.OrientationValues;
			if (coords == null || values == null)
				return;

			var count = Math.Min(coords.Length / 2, values.Length);
			for (var i = 0; i < count; i++)
			{
				if (values[i] != 0)
					_cellOrientation[CellKey(coords[i * 2], coords[i * 2 + 1])] = values[i];
			}
		}

		#endregion
	}
}
