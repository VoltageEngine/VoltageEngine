using System.Collections.Generic;
using ImGuiNET;
using Voltage.DeferredLighting;
using Voltage.Tilesets;
using Num = System.Numerics;

namespace Voltage.Editor.Tools.Tilemap
{
	/// <summary>Collects what is misconfigured about a tileset or a tilemap layer and renders it inline in the tile windows.</summary>
	public static class TileValidation
	{
		public readonly struct Issue
		{
			public readonly bool IsError;
			public readonly string Message;

			public Issue(bool isError, string message)
			{
				IsError = isError;
				Message = message;
			}

			public static Issue Error(string message) => new(true, message);
			public static Issue Warning(string message) => new(false, message);
		}

		private static readonly Num.Vector4 ErrorColor = new(1f, 0.35f, 0.35f, 1f);
		private static readonly Num.Vector4 WarningColor = new(1f, 0.75f, 0.3f, 1f);

		public static List<Issue> ValidateTileset(TilesetAsset asset, TilesetRuntime runtime)
		{
			var issues = new List<Issue>();

			if (asset == null)
				return issues;

			if (!asset.Texture.IsValid)
			{
				issues.Add(Issue.Error("No source image assigned. Pick a .png or .aseprite to slice into tiles."));
				return issues;
			}

			if (asset.TileWidth <= 0 || asset.TileHeight <= 0)
				issues.Add(Issue.Error("Tile width and height must both be greater than zero."));

			if (runtime?.Texture == null)
			{
				issues.Add(Issue.Error(
					$"The source image could not be loaded ({asset.Texture.AssetName ?? asset.Texture.AssetPath}). " +
					"It may have been deleted, moved outside the project, or be an unsupported format."));

				return issues;
			}

			foreach (var issue in runtime.Issues)
				issues.Add(Issue.Error(issue));

			if (runtime.TileCount == 0 && runtime.Issues.Count == 0)
				issues.Add(Issue.Error("This tileset slices into zero tiles. Check the tile size, spacing and margin."));

			return issues;
		}

		public static List<Issue> ValidatePainting(TilemapRenderer target, TilesetRuntime tileset, bool hasSelection)
		{
			var issues = new List<Issue>();

			if (target == null)
			{
				// Not an error: painting auto-creates the layer.
				return issues;
			}

			if (!target.Tileset.IsValid)
			{
				issues.Add(Issue.Error(
					$"The layer '{target.Entity?.Name}' has no tileset assigned. Pick one above before painting."));

				return issues;
			}

			if (tileset == null)
			{
				issues.Add(Issue.Error(
					"The tileset assigned to this layer could not be loaded. It may have been deleted or moved."));

				return issues;
			}

			if (tileset.TileCount > 0 && !hasSelection)
				issues.Add(Issue.Warning("No tile selected - click a tile in the atlas below to load the brush."));

			if (tileset.HasNormalMap && Core.Scene?.GetRenderer<DeferredLightingRenderer>() == null)
			{
				issues.Add(Issue.Warning(
					"This tileset has a normal map, but the scene has no DeferredLightingRenderer, so the tiles " +
					"will not be lit by it."));
			}

			return issues;
		}

		public static void Draw(List<Issue> issues)
		{
			if (issues == null || issues.Count == 0)
				return;

			foreach (var issue in issues)
			{
				ImGui.PushStyleColor(ImGuiCol.Text, issue.IsError ? ErrorColor : WarningColor);
				ImGui.TextWrapped((issue.IsError ? "[error] " : "[warning] ") + issue.Message);
				ImGui.PopStyleColor();
			}

			ImGui.Separator();
		}
	}
}
