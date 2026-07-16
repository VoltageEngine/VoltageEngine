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

		// True for textures we created (`new Texture2D`, Aseprite) rather than content-loaded ones: we dispose these
		// ourselves and they outlive a scene reload, so their cache entry is kept rather than rebuilt.
		private bool _ownsTexture;
		private bool _ownsNormalMap;

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

			if (_cache.TryGetValue(absolutePath, out var runtime))
			{
				runtime.Dispose();
				_cache.Remove(absolutePath);
			}

			Generation++;
		}

		public static void InvalidateAll()
		{
			foreach (var runtime in _cache.Values)
				runtime.Dispose();

			_cache.Clear();
			Generation++;
		}

		/// <summary>
		/// Drops entries whose textures were content-loaded (and so just disposed by <c>Scene.End</c> - reusing them
		/// samples black). Entries that own their textures survive the reload and are kept, avoiding a rebuild.
		/// </summary>
		public static void DropSceneOwned()
		{
			List<string> stale = null;

			foreach (var pair in _cache)
			{
				var runtime = pair.Value;
				var survivesReload = runtime._ownsTexture && (runtime.NormalMap == null || runtime._ownsNormalMap);
				if (!survivesReload)
					(stale ??= new List<string>()).Add(pair.Key);
			}

			if (stale == null)
				return;

			// Their textures were the ContentManager's to dispose; we just drop the dangling references.
			foreach (var path in stale)
				_cache.Remove(path);

			Generation++;
		}

		/// <summary>Disposes only the textures this runtime created itself.</summary>
		public void Dispose()
		{
			if (_ownsTexture)
				Texture?.Dispose();

			if (_ownsNormalMap)
				NormalMap?.Dispose();

			Texture = null;
			NormalMap = null;
		}

		/// <summary>Builds a runtime from an in-memory asset without touching the cache.</summary>
		public static TilesetRuntime Build(TilesetAsset asset)
		{
			if (asset == null)
				return null;

			var texture = asset.TextureIsAsepriteAnimation
				? LoadAsepriteAnimationStrip(asset)
				: LoadImage(asset.Texture, asset.TextureSource, asset.TextureLayers, asset.TextureFrame);

			if (texture == null)
			{
				Debug.Warn($"TilesetRuntime: tileset '{asset.Name}' has no loadable source image.");
				return null;
			}

			// Aseprite frames/strips are `new Texture2D` we own; a plain image comes from the ContentManager.
			var ownsTexture = asset.TextureIsAsepriteAnimation || asset.TextureSource == TilesetImageSource.Aseprite;
			var runtime = new TilesetRuntime { Asset = asset, Texture = texture, _ownsTexture = ownsTexture };

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
				runtime._ownsNormalMap = normal != null && asset.NormalMapSource == TilesetImageSource.Aseprite;
			}

			runtime.Slice();
			runtime.ClassifyTiles();
			runtime.BuildAnimations();
			runtime.BuildTerrains();
			return runtime;
		}

		// Per-terrain member tiles with their neighbour signatures, for autotile matching.
		private readonly Dictionary<int, List<(int Tile, byte Mask)>> _terrains = new();

		public bool HasTerrains => _terrains.Count > 0;

		private void BuildTerrains()
		{
			if (Asset?.Tiles == null)
				return;

			foreach (var info in Asset.Tiles)
			{
				if (info.TerrainId < 0 || !IsValidIndex(info.Index))
					continue;

				if (!_terrains.TryGetValue(info.TerrainId, out var list))
				{
					list = new List<(int, byte)>();
					_terrains[info.TerrainId] = list;
				}

				list.Add((info.Index, info.TerrainMask));
			}
		}

		public bool TerrainHasTiles(int terrainId) => _terrains.ContainsKey(terrainId);

		public bool IsTerrainMember(int terrainId, int tileIndex)
		{
			if (!_terrains.TryGetValue(terrainId, out var list))
				return false;

			foreach (var (tile, _) in list)
			{
				if (tile == tileIndex)
					return true;
			}

			return false;
		}

		/// <summary>
		/// The best terrain tile for a given 8-bit neighbour mask: an exact signature match if one exists,
		/// otherwise the member whose signature is closest (fewest differing bits), so partial tilesets still
		/// resolve to something sensible.
		/// </summary>
		public int ResolveAutotile(int terrainId, byte neighbourMask)
		{
			if (!_terrains.TryGetValue(terrainId, out var list) || list.Count == 0)
				return -1;

			var best = list[0].Tile;
			var bestDist = int.MaxValue;

			foreach (var (tile, mask) in list)
			{
				if (mask == neighbourMask)
					return tile;

				var dist = PopCount((byte)(mask ^ neighbourMask));
				if (dist < bestDist)
				{
					bestDist = dist;
					best = tile;
				}
			}

			return best;
		}

		private static int PopCount(byte b)
		{
			var count = 0;
			while (b != 0)
			{
				count += b & 1;
				b >>= 1;
			}

			return count;
		}

		// Precomputed per-tile animation, indexed by tile. null = static tile. Built once at load so rendering
		// resolves the current frame in O(1) with no per-frame allocation.
		private sealed class TileAnimation
		{
			public int[] Frames;
			public float FrameDuration;
			public float TotalDuration;
		}

		private TileAnimation[] _animations = Array.Empty<TileAnimation>();

		public bool HasAnimations { get; private set; }

		private void BuildAnimations()
		{
			_animations = new TileAnimation[SourceRects.Length];
			if (Asset?.Tiles == null)
				return;

			foreach (var info in Asset.Tiles)
			{
				if (!info.IsAnimated || !IsValidIndex(info.Index))
					continue;

				var duration = Math.Max(0.001f, info.AnimationFrameDuration);
				var frames = info.AnimationFrames.ToArray();

				_animations[info.Index] = new TileAnimation
				{
					Frames = frames,
					FrameDuration = duration,
					TotalDuration = duration * frames.Length,
				};

				HasAnimations = true;
			}
		}

		/// <summary>The source-rect index to draw for a placed tile at a given time — the animation frame for an
		/// animated tile, or the tile itself otherwise.</summary>
		public int ResolveFrame(int tileIndex, float time)
		{
			if (!HasAnimations || (uint)tileIndex >= (uint)_animations.Length)
				return tileIndex;

			var anim = _animations[tileIndex];
			if (anim == null)
				return tileIndex;

			var t = time % anim.TotalDuration;
			var frame = (int)(t / anim.FrameDuration);
			if (frame >= anim.Frames.Length)
				frame = anim.Frames.Length - 1;

			return anim.Frames[frame];
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

		/// <summary>
		/// Packs Aseprite frames [<c>TextureAnimStart</c>..<c>TextureAnimEnd</c>] left-to-right into one strip
		/// texture (one frame per tile), so the animated tile can cycle through them as ordinary tile indices.
		/// </summary>
		private static Texture2D LoadAsepriteAnimationStrip(TilesetAsset asset)
		{
			var path = asset.Texture.ResolvePath();
			var content = Core.Scene?.Content ?? Core.Content;
			if (string.IsNullOrEmpty(path) || content == null)
				return null;

			var file = content.LoadAsepriteFile(path);
			var layers = asset.TextureLayers is { Count: > 0 } ? asset.TextureLayers.ToArray() : null;
			return BuildAsepriteStrip(file, layers, asset.TextureAnimStart, asset.TextureAnimEnd, out _, out _, out _, out _);
		}

		/// <summary>
		/// Packs Aseprite frames [start..end] left-to-right into one strip texture (one frame per column). Shared
		/// by the runtime and by the editor's animation preview so both draw the exact same pixels.
		/// </summary>
		public static Texture2D BuildAsepriteStrip(Voltage.Aseprite.AsepriteFile file, string[] layers,
			int start, int end, out int frameCount, out int frameWidth, out int frameHeight, out float firstFrameSeconds)
		{
			frameCount = 0;
			frameWidth = 0;
			frameHeight = 0;
			firstFrameSeconds = 0.1f;

			if (file == null || file.Frames.Count == 0)
				return null;

			try
			{
				var last = file.Frames.Count - 1;
				var lo = Math.Clamp(Math.Min(start, end), 0, last);
				var hi = Math.Clamp(Math.Max(start, end), 0, last);
				var count = hi - lo + 1;

				var fw = file.Frames[lo].Width;
				var fh = file.Frames[lo].Height;

				var strip = new Color[fw * count * fh];
				for (var f = 0; f < count; f++)
				{
					var frame = file.Frames[lo + f];
					var pixels = layers == null || layers.Length == 0
						? frame.FlattenFrame()
						: frame.FlattenFrameOnLayers(true, false, layers);

					for (var y = 0; y < fh; y++)
					{
						for (var x = 0; x < fw; x++)
							strip[y * (fw * count) + f * fw + x] = pixels[y * fw + x];
					}
				}

				var texture = new Texture2D(Core.GraphicsDevice, fw * count, fh);
				texture.SetData(strip);

				frameCount = count;
				frameWidth = fw;
				frameHeight = fh;
				firstFrameSeconds = Math.Max(0.01f, file.Frames[lo].Duration / 1000f);
				return texture;
			}
			catch (Exception e)
			{
				Debug.Error($"TilesetRuntime: failed to build Aseprite animation strip: {e.Message}");
				return null;
			}
		}
	}
}
