using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Voltage.Serialization;

namespace Voltage.Tilesets
{
	/// <summary>A <see cref="TilesetAsset"/> resolved into GPU-ready form: atlases plus per-tile source rects.</summary>
	/// <remarks>
	/// Instances are cached by absolute path: the Aseprite path allocates a new <see cref="Texture2D"/> on every
	/// flatten, so loading uncached would leak one per call.
	/// </remarks>
	public sealed class TilesetRuntime
	{
		public TilesetAsset Asset { get; private set; }
		public Texture2D Texture { get; private set; }
		public Texture2D NormalMap { get; private set; }

		public Rectangle[] SourceRects { get; private set; } = Array.Empty<Rectangle>();

		/// <summary>True for tiles that are fully transparent in the source image.</summary>
		public bool[] IsTileBlank { get; private set; } = Array.Empty<bool>();

		/// <summary>True for tiles with no transparent pixel at all.</summary>
		public bool[] IsTileOpaque { get; private set; } = Array.Empty<bool>();

		public int Columns { get; private set; }
		public int Rows { get; private set; }

		/// <summary>Problems found while loading; the editor surfaces these in the tile windows.</summary>
		public IReadOnlyList<string> Issues => _issues;

		private readonly List<string> _issues = new();

		public int TileWidth => Asset?.TileWidth ?? 0;
		public int TileHeight => Asset?.TileHeight ?? 0;
		public int TileCount => SourceRects.Length;
		public bool HasNormalMap => NormalMap != null;

		private static readonly Dictionary<string, TilesetRuntime> _cache =
			new(StringComparer.OrdinalIgnoreCase);

		/// <summary>Bumped whenever a cached tileset is dropped, so UI holding a resolved tileset notices a re-save under the same path.</summary>
		public static int Generation { get; private set; }

		public static TilesetRuntime Get(AssetReference tilesetRef)
		{
			if (!tilesetRef.IsValid)
				return null;

			var path = tilesetRef.ResolvePath();
			if (string.IsNullOrEmpty(path))
			{
				Debug.Warn($"TilesetRuntime: could not resolve tileset reference {tilesetRef}.");
				return null;
			}

			return Get(path);
		}

		public static TilesetRuntime Get(string absolutePath)
		{
			if (string.IsNullOrEmpty(absolutePath))
				return null;

			if (_cache.TryGetValue(absolutePath, out var cached))
				return cached;

			var asset = TilesetAssetIO.Load(absolutePath);
			if (asset == null)
			{
				Debug.Warn($"TilesetRuntime: no tileset file at '{absolutePath}'.");
				return null;
			}

			var runtime = Build(asset);
			if (runtime == null)
				return null;

			_cache[absolutePath] = runtime;
			return runtime;
		}

		public static void Invalidate(string absolutePath)
		{
			if (string.IsNullOrEmpty(absolutePath))
				return;

			_cache.Remove(absolutePath);
			Generation++;
		}

		public static void InvalidateAll()
		{
			_cache.Clear();
			Generation++;
		}

		/// <summary>Builds a runtime from an in-memory asset without touching the cache.</summary>
		public static TilesetRuntime Build(TilesetAsset asset)
		{
			if (asset == null)
				return null;

			var texture = LoadImage(asset.Texture, asset.TextureSource, asset.TextureLayers, asset.TextureFrame);
			if (texture == null)
			{
				Debug.Warn($"TilesetRuntime: tileset '{asset.Name}' has no loadable source image.");
				return null;
			}

			var runtime = new TilesetRuntime { Asset = asset, Texture = texture };

			if (asset.NormalMap.IsValid)
			{
				var normal = LoadImage(asset.NormalMap, asset.NormalMapSource, asset.NormalMapLayers, asset.NormalMapFrame);

				if (normal == null)
				{
					runtime._issues.Add("The normal map is set but could not be loaded. Check the asset reference.");
				}
				// The shader samples the normal map with the diffuse's UVs, so a differently sized atlas would
				// sample the wrong tile. Reject it.
				else if (normal.Width != texture.Width || normal.Height != texture.Height)
				{
					var message =
						$"Normal map is {normal.Width}x{normal.Height} but the source image is " +
						$"{texture.Width}x{texture.Height}. They must match exactly — the normal map is ignored.";

					runtime._issues.Add(message);
					Debug.Warn($"TilesetRuntime: tileset '{asset.Name}': {message}");
					normal = null;
				}

				runtime.NormalMap = normal;
			}

			runtime.Slice();
			runtime.ClassifyTiles();
			return runtime;
		}

		private void Slice()
		{
			var tw = Math.Max(1, Asset.TileWidth);
			var th = Math.Max(1, Asset.TileHeight);
			var spacing = Math.Max(0, Asset.Spacing);
			var margin = Math.Max(0, Asset.Margin);

			Columns = Math.Max(0, (Texture.Width - 2 * margin + spacing) / (tw + spacing));
			Rows = Math.Max(0, (Texture.Height - 2 * margin + spacing) / (th + spacing));

			Asset.Columns = Columns;
			Asset.Rows = Rows;

			if (Columns == 0 || Rows == 0)
			{
				_issues.Add(
					$"A {tw}x{th} tile size with margin {margin} does not fit inside the " +
					$"{Texture.Width}x{Texture.Height} image — the grid produces no tiles.");
			}
			else
			{
				var usedWidth = margin * 2 + Columns * (tw + spacing) - spacing;
				var usedHeight = margin * 2 + Rows * (th + spacing) - spacing;

				if (usedWidth != Texture.Width || usedHeight != Texture.Height)
				{
					_issues.Add(
						$"The grid covers {usedWidth}x{usedHeight} of the {Texture.Width}x{Texture.Height} image, " +
						$"leaving {Texture.Width - usedWidth}x{Texture.Height - usedHeight} px unused. " +
						"Check the tile size, spacing and margin.");
				}
			}

			SourceRects = new Rectangle[Columns * Rows];
			for (var row = 0; row < Rows; row++)
			{
				for (var col = 0; col < Columns; col++)
				{
					SourceRects[row * Columns + col] = new Rectangle(
						margin + col * (tw + spacing),
						margin + row * (th + spacing),
						tw, th);
				}
			}
		}

		public bool IsValidIndex(int tileIndex) => tileIndex >= 0 && tileIndex < SourceRects.Length;

		public Rectangle GetSourceRect(int tileIndex) =>
			IsValidIndex(tileIndex) ? SourceRects[tileIndex] : Rectangle.Empty;

		/// <summary>True when the tile has no visible pixels.</summary>
		public bool IsBlank(int tileIndex) =>
			tileIndex >= 0 && tileIndex < IsTileBlank.Length && IsTileBlank[tileIndex];

		/// <summary>True when the tile is fully opaque. Unknown tiles report true.</summary>
		public bool IsOpaque(int tileIndex) =>
			tileIndex < 0 || tileIndex >= IsTileOpaque.Length || IsTileOpaque[tileIndex];

		private void ClassifyTiles()
		{
			IsTileBlank = new bool[SourceRects.Length];
			IsTileOpaque = new bool[SourceRects.Length];

			if (SourceRects.Length == 0)
				return;

			Color[] pixels;
			try
			{
				pixels = new Color[Texture.Width * Texture.Height];
				Texture.GetData(pixels);
			}
			catch (Exception e)
			{
				// Readback can fail on some backends/formats; degrade to "everything is opaque".
				for (var i = 0; i < IsTileOpaque.Length; i++)
					IsTileOpaque[i] = true;

				Debug.Warn($"TilesetRuntime: could not read tileset pixels to classify tiles: {e.Message}");
				return;
			}

			var width = Texture.Width;

			for (var i = 0; i < SourceRects.Length; i++)
			{
				var rect = SourceRects[i];
				var anyVisible = false;
				var anyTransparent = false;

				for (var y = rect.Y; y < rect.Bottom; y++)
				{
					var row = y * width;
					for (var x = rect.X; x < rect.Right; x++)
					{
						if (pixels[row + x].A == 0)
							anyTransparent = true;
						else
							anyVisible = true;

						if (anyVisible && anyTransparent)
							break;
					}

					if (anyVisible && anyTransparent)
						break;
				}

				IsTileBlank[i] = !anyVisible;
				IsTileOpaque[i] = anyVisible && !anyTransparent;
			}
		}

		private static Texture2D LoadImage(AssetReference reference, TilesetImageSource source,
			List<string> layers, int frame)
		{
			if (!reference.IsValid)
				return null;

			var path = reference.ResolvePath();
			if (string.IsNullOrEmpty(path))
			{
				Debug.Warn($"TilesetRuntime: could not resolve image reference {reference}.");
				return null;
			}

			var content = Core.Scene?.Content ?? Core.Content;
			if (content == null)
				return null;

			try
			{
				if (source == TilesetImageSource.Aseprite)
				{
					var file = content.LoadAsepriteFile(path);
					if (file == null)
						return null;

					// The Aseprite API numbers frames from 1; we store them from 0.
					if (layers == null || layers.Count == 0)
						return file.GetTextureFromFrameNumber(frame + 1);

					return file.GetTextureFromLayers(frame + 1, true, false, layers.ToArray());
				}

				return content.LoadTexture(path);
			}
			catch (Exception e)
			{
				Debug.Error($"TilesetRuntime: failed to load image '{path}': {e.Message}");
				return null;
			}
		}
	}
}
