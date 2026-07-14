using System;
using System.Collections.Generic;
using System.IO;
using ImGuiNET;
using Voltage.Editor.Assets;
using Voltage.Editor.Gizmos;
using Voltage.Editor.ImGuiCore;
using Voltage.Editor.Undo.Core;
using Voltage.Tilesets;
using EngineAssetReference = Voltage.Serialization.AssetReference;
using Num = System.Numerics;

namespace Voltage.Editor.Tools.Tilemap
{
	/// <summary>UI for tile painting: pick the layer, the tileset, the stamp and the active tool. Drives the shared <see cref="TilePaintTool"/>.</summary>
	public class TilePaletteWindow
	{
		public bool IsOpen;

		private static readonly string[] TilesetExtensions = { TilesetAssetIO.FileExtension };

		private TilePaintTool _tool;

		private TilesetRuntime _tileset;
		private string _tilesetPath;
		private IntPtr _atlasTextureId;

		// Cache generation the atlas was loaded at — a re-save under the same path moves it, which a path compare misses.
		private int _tilesetGeneration = -1;

		private float _zoom = 2f;

		private bool _openNewLayerPopup;
		private string _newLayerName = "Tilemap";

		// Atlas selection, in tile coordinates.
		private bool _isSelecting;
		private Point2 _selectionAnchor;
		private Point2 _selectionEnd;
		private bool _hasSelection;

		private readonly struct Point2
		{
			public readonly int X;
			public readonly int Y;

			public Point2(int x, int y)
			{
				X = x;
				Y = y;
			}
		}

		public void Draw(TilePaintTool tool, TilesetEditorWindow tilesetEditor, GizmoSelectionManager cursor)
		{
			_tool = tool;

			if (!IsOpen)
				return;

			ImGui.SetNextWindowSize(new Num.Vector2(420, 640), ImGuiCond.FirstUseEver);

			// "###TilePaletteWindow" pins the ImGui ID independently of the label; it keys the docking entry in the
			// layout .ini. Without it the docked position is not restored.
			var open = IsOpen;
			if (!ImGui.Begin("Tile Palette ###TilePaletteWindow", ref open))
			{
				IsOpen = open;
				ImGui.End();
				return;
			}

			IsOpen = open;

			tool.DropDeadTarget();

			EnsureTilesetLoaded(tool);

			DrawModeBanner(cursor);
			DrawIssues(tool);
			DrawTargetSelector(tool);
			ImGui.Separator();

			DrawTilesetSelector(tool, tilesetEditor);
			ImGui.Separator();

			DrawLayerOrder(tool);
			ImGui.Separator();

			DrawToolButtons(tool);
			DrawGridOptions(tool);
			ImGui.Separator();

			DrawAtlas(tool);

			ImGui.End();

			// Must be outside Begin/End to be modal.
			DrawNewLayerPopup(tool);
		}

		private void DrawNewLayerPopup(TilePaintTool tool)
		{
			if (_openNewLayerPopup)
			{
				ImGui.OpenPopup("new-tilemap-layer");
				_openNewLayerPopup = false;
			}

			var center = ImGui.GetMainViewport().GetCenter();
			ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new Num.Vector2(0.5f, 0.5f));

			// Fixed size, and deliberately NOT AlwaysAutoResize: the fill-width input sizes to the window while the
			// window auto-sizes to its content, so the two chase each other smaller every frame.
			ImGui.SetNextWindowSize(new Num.Vector2(420, 190), ImGuiCond.Always);

			var open = true;
			if (!ImGui.BeginPopupModal("new-tilemap-layer", ref open, ImGuiWindowFlags.NoResize))
				return;

			ImGui.TextUnformatted("New tilemap layer");
			ImGui.Separator();

			ImGui.TextUnformatted("Name:");
			ImGui.SetNextItemWidth(-1);

			var entered = ImGui.InputText("##newlayername", ref _newLayerName, 128,
				ImGuiInputTextFlags.EnterReturnsTrue);

			var nameValid = !string.IsNullOrWhiteSpace(_newLayerName);

			ImGui.TextDisabled(_tileset?.Asset != null
				? $"Tileset: {_tileset.Asset.Name ?? "(unnamed)"}"
				: "No tileset selected — the layer will be created without one.");

			ImGui.Separator();

			ImGui.BeginDisabled(!nameValid);
			var confirm = ImGui.Button("Create", new Num.Vector2(120, 0)) || (entered && nameValid);
			ImGui.EndDisabled();

			ImGui.SameLine();

			if (ImGui.Button("Cancel", new Num.Vector2(120, 0)))
				ImGui.CloseCurrentPopup();

			if (confirm && nameValid)
			{
				var reference = _tilesetPath != null
					? TilemapSceneUtils.ReferenceFor(_tilesetPath)
					: default;

				var created = TilemapSceneUtils.CreateTilemapLayer(reference, null, _newLayerName.Trim());
				if (created != null)
				{
					tool.Target = created;
					SyncTilesetFromTarget(tool);
				}

				ImGui.CloseCurrentPopup();
			}

			ImGui.EndPopup();
		}

		private void DrawModeBanner(GizmoSelectionManager cursor)
		{
			if (cursor.SelectionMode == CursorSelectionMode.TilePaint)
			{
				ImGui.TextColored(new Num.Vector4(0.4f, 0.9f, 0.5f, 1f), "Tile cursor active — paint in the game view.");
				return;
			}

			ImGui.TextColored(new Num.Vector4(0.9f, 0.75f, 0.35f, 1f), "Tile cursor is not active.");
			ImGui.SameLine();

			if (ImGui.SmallButton("Activate (B)"))
				cursor.SelectionMode = CursorSelectionMode.TilePaint;
		}

		private void DrawIssues(TilePaintTool tool)
		{
			var issues = TileValidation.ValidateTileset(_tileset?.Asset, _tileset);
			issues.AddRange(TileValidation.ValidatePainting(tool.Target, _tileset, tool.HasSelection));

			TileValidation.Draw(issues);
		}

		private void DrawTargetSelector(TilePaintTool tool)
		{
			var maps = TilemapSceneUtils.FindTilemaps();

			var label = tool.Target?.Entity != null
				? tool.Target.Entity.Name
				: "(no layer selected)";

			if (ImGui.BeginCombo("Layer", label))
			{
				foreach (var map in maps)
				{
					if (map.Entity == null)
						continue;

					var selected = ReferenceEquals(map, tool.Target);
					if (ImGui.Selectable(map.Entity.Name, selected))
					{
						tool.Target = map;
						SyncTilesetFromTarget(tool);
					}

					if (selected)
						ImGui.SetItemDefaultFocus();
				}

				if (maps.Count == 0)
					ImGui.TextDisabled("No tilemap layers in this scene.");

				ImGui.EndCombo();
			}

			if (ImGui.Button("New Layer"))
			{
				_newLayerName = string.IsNullOrEmpty(_tileset?.Asset?.Name)
					? "Tilemap"
					: $"{_tileset.Asset.Name} Layer";

				_openNewLayerPopup = true;
			}

			if (ImGui.IsItemHovered())
				ImGui.SetTooltip("Creates an entity with a TilemapRenderer using the selected tileset.");

			if (tool.Target != null)
			{
				ImGui.SameLine();
				ImGui.TextDisabled($"{tool.Target.TileCount} tiles");
			}
		}

		private void DrawTilesetSelector(TilePaintTool tool, TilesetEditorWindow tilesetEditor)
		{
			var reference = tool.Target?.Tileset ?? (_tilesetPath != null
				? TilemapSceneUtils.ReferenceFor(_tilesetPath)
				: default);

			if (TileAssetUtils.DrawAssetSlot("Tileset", ref reference, TilesetExtensions))
				AssignTileset(tool, reference);

			if (ImGui.Button("New Tileset..."))
				tilesetEditor.NewTileset();

			ImGui.SameLine();

			var canEdit = _tilesetPath != null;
			ImGui.BeginDisabled(!canEdit);
			if (ImGui.Button("Edit Tileset") && canEdit)
				tilesetEditor.Open(_tilesetPath);
			ImGui.EndDisabled();

			if (_tileset == null)
			{
				ImGui.TextDisabled(tool.Target == null
					? "Pick a layer, or create one with a tileset."
					: "This layer has no tileset assigned.");
			}
		}

		/// <summary>
		/// Draw order across every tilemap layer, front-most first. A LOWER RenderLayer draws IN FRONT (engine
		/// convention, inverted from intuition), and it is the same layer space sprites use.
		/// </summary>
		private void DrawLayerOrder(TilePaintTool tool)
		{
			var maps = SortedFrontToBack();

			if (!ImGui.CollapsingHeader($"Layer order ({maps.Count})"))
				return;

			if (maps.Count == 0)
			{
				ImGui.TextDisabled("No tilemap layers in this scene.");
				return;
			}

			ImGui.TextDisabled("Top of the list draws in front of the ones below it.");

			for (var i = 0; i < maps.Count; i++)
			{
				var map = maps[i];
				if (map.Entity == null)
					continue;

				ImGui.PushID(i);

				ImGui.BeginDisabled(i == 0);
				if (ImGui.ArrowButton("up", ImGuiDir.Up))
					MoveLayer(maps, i, -1);
				ImGui.EndDisabled();

				if (ImGui.IsItemHovered())
					ImGui.SetTooltip("Move in front of the layer above.");

				ImGui.SameLine();

				ImGui.BeginDisabled(i == maps.Count - 1);
				if (ImGui.ArrowButton("down", ImGuiDir.Down))
					MoveLayer(maps, i, 1);
				ImGui.EndDisabled();

				if (ImGui.IsItemHovered())
					ImGui.SetTooltip("Move behind the layer below.");

				ImGui.SameLine();

				var isTarget = ReferenceEquals(map, tool.Target);
				if (ImGui.Selectable($"{map.Entity.Name}##layer", isTarget))
				{
					tool.Target = map;
					SyncTilesetFromTarget(tool);
				}

				ImGui.SameLine();
				ImGui.TextDisabled($"(layer {map.RenderLayer}, depth {map.LayerDepth:0.00})");

				ImGui.PopID();
			}

			if (tool.Target?.Entity == null)
				return;

			ImGui.Spacing();
			ImGui.TextDisabled($"Selected: {tool.Target.Entity.Name}");

			var renderLayer = tool.Target.RenderLayer;
			ImGui.SetNextItemWidth(140f);
			if (ImGui.InputInt("Render layer", ref renderLayer))
			{
				tool.Target.RenderLayer = renderLayer;
				EditorChangeTracker.MarkChanged(tool.Target.Entity, "Change tilemap render layer");
			}

			if (ImGui.IsItemHovered())
			{
				ImGui.SetTooltip(
					"A LOWER render layer draws in FRONT (engine convention).\n" +
					"This is the same layer space sprites and other renderables use, so it is how you put a\n" +
					"tilemap in front of or behind them.");
			}

			var layerDepth = tool.Target.LayerDepth;
			ImGui.SetNextItemWidth(140f);
			if (ImGui.SliderFloat("Layer depth", ref layerDepth, 0f, 1f, "%.2f"))
			{
				tool.Target.LayerDepth = layerDepth;
				EditorChangeTracker.MarkChanged(tool.Target.Entity, "Change tilemap layer depth");
			}

			if (ImGui.IsItemHovered())
				ImGui.SetTooltip("Tie-breaker within one render layer. 0 = front, 1 = back.");
		}

		private static List<TilemapRenderer> SortedFrontToBack()
		{
			var maps = TilemapSceneUtils.FindTilemaps();

			maps.Sort((a, b) =>
			{
				var byLayer = a.RenderLayer.CompareTo(b.RenderLayer);
				return byLayer != 0 ? byLayer : a.LayerDepth.CompareTo(b.LayerDepth);
			});

			return maps;
		}

		private static void MoveLayer(List<TilemapRenderer> sorted, int index, int direction)
		{
			var other = index + direction;
			if (other < 0 || other >= sorted.Count)
				return;

			// Layers left at identical defaults have nothing to swap, so spread the depths first — then the swap
			// always changes the order.
			SpreadDepthsWithinLayers(sorted);

			var a = sorted[index];
			var b = sorted[other];

			var layer = a.RenderLayer;
			var depth = a.LayerDepth;

			a.RenderLayer = b.RenderLayer;
			a.LayerDepth = b.LayerDepth;
			b.RenderLayer = layer;
			b.LayerDepth = depth;

			EditorChangeTracker.MarkChanged(a.Entity, "Reorder tilemap layers");
			EditorChangeTracker.MarkChanged(b.Entity, "Reorder tilemap layers");
		}

		private static void SpreadDepthsWithinLayers(List<TilemapRenderer> sorted)
		{
			var i = 0;
			while (i < sorted.Count)
			{
				var layer = sorted[i].RenderLayer;

				var j = i;
				while (j < sorted.Count && sorted[j].RenderLayer == layer)
					j++;

				var count = j - i;
				if (count > 1)
				{
					for (var k = 0; k < count; k++)
						sorted[i + k].LayerDepth = k / (float)(count - 1) * 0.9f;
				}

				i = j;
			}
		}

		private void DrawToolButtons(TilePaintTool tool)
		{
			DrawToolButton(tool, TileTool.Brush, "Brush", "Stamp the selected tiles. Shift or right-drag erases.");
			ImGui.SameLine();
			DrawToolButton(tool, TileTool.Eraser, "Eraser", "Erase cells.");
			ImGui.SameLine();
			DrawToolButton(tool, TileTool.Rectangle, "Rect", "Drag a rectangle; the stamp tiles across it.");
			ImGui.SameLine();
			DrawToolButton(tool, TileTool.Fill, "Fill", "Flood fill the contiguous matching region.");
			ImGui.SameLine();
			DrawToolButton(tool, TileTool.Picker, "Pick", "Pick the tile under the cursor into the palette.");

			ImGui.Checkbox("Stack while dragging", ref tool.StackWhileDragging);

			if (ImGui.IsItemHovered())
			{
				ImGui.SetTooltip(
					"Off (default): dragging steps the stamp by its own size, so tiles are laid AROUND each\n" +
					"other, edge to edge.\n\n" +
					"On: dragging follows the cursor cell by cell, so tiles are laid ON TOP of each other and\n" +
					"overlap. A tile with transparency stacks over what is beneath it; a fully opaque one\n" +
					"replaces it.\n\n" +
					"A single click is always free-form, whichever way this is set.");
			}
		}

		private static void DrawToolButton(TilePaintTool tool, TileTool value, string label, string tooltip)
		{
			var active = tool.Tool == value;

			if (active)
				ImGui.PushStyleColor(ImGuiCol.Button, new Num.Vector4(0.2f, 0.5f, 1f, 1f));

			if (ImGui.Button(label))
				tool.Tool = value;

			if (active)
				ImGui.PopStyleColor();

			if (ImGui.IsItemHovered())
				ImGui.SetTooltip(tooltip);
		}

		private void DrawGridOptions(TilePaintTool tool)
		{
			ImGui.Checkbox("Show grid", ref tool.ShowGrid);
			ImGui.SameLine();
			ImGui.Checkbox("Always", ref tool.AlwaysShowGrid);

			if (ImGui.IsItemHovered())
				ImGui.SetTooltip("Keep drawing the grid even when the tile cursor is not active.");

			var color = new Num.Vector4(
				tool.GridColor.R / 255f, tool.GridColor.G / 255f,
				tool.GridColor.B / 255f, tool.GridColor.A / 255f);

			if (ImGui.ColorEdit4("Grid color", ref color,
				    ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.AlphaBar | ImGuiColorEditFlags.AlphaPreview))
			{
				tool.GridColor = new Microsoft.Xna.Framework.Color(color.X, color.Y, color.Z, color.W);
			}

			ImGui.SetNextItemWidth(140f);
			ImGui.SliderFloat("Grid thickness", ref tool.GridThickness, 0.25f, 4f, "%.2f px");

			if (ImGui.IsItemHovered())
				ImGui.SetTooltip("Line width in screen pixels — stays constant as you zoom.");

			ImGui.SetNextItemWidth(140f);
			ImGui.SliderFloat("Cursor thickness", ref tool.HighlightThickness, 0.5f, 6f, "%.2f px");
		}

		private void DrawAtlas(TilePaintTool tool)
		{
			if (_tileset?.Texture == null || _atlasTextureId == IntPtr.Zero)
				return;

			ImGui.SliderFloat("Zoom", ref _zoom, 1f, 8f, "%.1fx");

			if (_hasSelection)
			{
				var w = Math.Abs(_selectionEnd.X - _selectionAnchor.X) + 1;
				var h = Math.Abs(_selectionEnd.Y - _selectionAnchor.Y) + 1;
				ImGui.TextDisabled($"Selection: {w} x {h} tile{(w * h == 1 ? "" : "s")}");
			}
			else
			{
				ImGui.TextDisabled("Click a tile — or drag across several — to choose the stamp.");
			}

			ImGui.BeginChild("tile_atlas", new Num.Vector2(0, 0), true,
				ImGuiWindowFlags.HorizontalScrollbar);

			var origin = ImGui.GetCursorScreenPos();
			var size = new Num.Vector2(_tileset.Texture.Width * _zoom, _tileset.Texture.Height * _zoom);

			ImGui.Image(_atlasTextureId, size);

			var drawList = ImGui.GetWindowDrawList();
			TilePaletteDrawing.DrawSliceGrid(drawList, origin, _tileset, _zoom);

			HandleAtlasSelection(tool, origin);
			DrawSelectionOverlay(drawList, origin);

			ImGui.EndChild();
		}

		private void HandleAtlasSelection(TilePaintTool tool, Num.Vector2 origin)
		{
			if (!ImGui.IsItemHovered() && !_isSelecting)
				return;

			var mouse = ImGui.GetIO().MousePos;
			var cell = CellAt(mouse, origin);

			if (!IsInsideAtlas(cell))
			{
				if (!_isSelecting)
					return;
			}

			if (ImGui.IsMouseClicked(ImGuiMouseButton.Left) && IsInsideAtlas(cell))
			{
				_isSelecting = true;
				_selectionAnchor = cell;
				_selectionEnd = cell;
			}
			else if (_isSelecting && ImGui.IsMouseDown(ImGuiMouseButton.Left))
			{
				_selectionEnd = Clamp(cell);
			}

			if (_isSelecting && ImGui.IsMouseReleased(ImGuiMouseButton.Left))
			{
				_isSelecting = false;
				_hasSelection = true;
				CommitSelection(tool);
			}
		}

		private void CommitSelection(TilePaintTool tool)
		{
			var minX = Math.Min(_selectionAnchor.X, _selectionEnd.X);
			var minY = Math.Min(_selectionAnchor.Y, _selectionEnd.Y);
			var width = Math.Abs(_selectionEnd.X - _selectionAnchor.X) + 1;
			var height = Math.Abs(_selectionEnd.Y - _selectionAnchor.Y) + 1;

			var tiles = new int[width * height];
			for (var y = 0; y < height; y++)
			{
				for (var x = 0; x < width; x++)
					tiles[y * width + x] = (minY + y) * _tileset.Columns + (minX + x);
			}

			tool.SetSelection(tiles, width, height);

			if (tool.Tool is TileTool.Picker or TileTool.Eraser)
				tool.Tool = TileTool.Brush;
		}

		private void DrawSelectionOverlay(ImDrawListPtr drawList, Num.Vector2 origin)
		{
			if (!_hasSelection && !_isSelecting)
				return;

			var minX = Math.Min(_selectionAnchor.X, _selectionEnd.X);
			var minY = Math.Min(_selectionAnchor.Y, _selectionEnd.Y);
			var maxX = Math.Max(_selectionAnchor.X, _selectionEnd.X);
			var maxY = Math.Max(_selectionAnchor.Y, _selectionEnd.Y);

			var tw = _tileset.TileWidth;
			var th = _tileset.TileHeight;
			var spacing = _tileset.Asset.Spacing;
			var margin = _tileset.Asset.Margin;

			var topLeft = new Num.Vector2(
				origin.X + (margin + minX * (tw + spacing)) * _zoom,
				origin.Y + (margin + minY * (th + spacing)) * _zoom);

			var bottomRight = new Num.Vector2(
				origin.X + (margin + maxX * (tw + spacing) + tw) * _zoom,
				origin.Y + (margin + maxY * (th + spacing) + th) * _zoom);

			drawList.AddRectFilled(topLeft, bottomRight, ImGui.GetColorU32(new Num.Vector4(0.2f, 0.6f, 1f, 0.25f)));
			drawList.AddRect(topLeft, bottomRight, ImGui.GetColorU32(new Num.Vector4(0.3f, 0.8f, 1f, 1f)), 0f, 0, 2f);
		}

		private Point2 CellAt(Num.Vector2 mouse, Num.Vector2 origin)
		{
			var tw = _tileset.TileWidth + _tileset.Asset.Spacing;
			var th = _tileset.TileHeight + _tileset.Asset.Spacing;
			var margin = _tileset.Asset.Margin;

			var localX = (mouse.X - origin.X) / _zoom - margin;
			var localY = (mouse.Y - origin.Y) / _zoom - margin;

			return new Point2(
				(int)Math.Floor(localX / Math.Max(1, tw)),
				(int)Math.Floor(localY / Math.Max(1, th)));
		}

		private bool IsInsideAtlas(Point2 cell) =>
			cell.X >= 0 && cell.Y >= 0 && cell.X < _tileset.Columns && cell.Y < _tileset.Rows;

		private Point2 Clamp(Point2 cell) => new(
			Math.Clamp(cell.X, 0, Math.Max(0, _tileset.Columns - 1)),
			Math.Clamp(cell.Y, 0, Math.Max(0, _tileset.Rows - 1)));

		#region Tileset binding

		private void EnsureTilesetLoaded(TilePaintTool tool)
		{
			// With no layer targeted the palette holds a tileset staged for the next New Layer — leave it be.
			if (tool.Target == null)
				return;

			var reference = tool.Target.Tileset;

			if (!reference.IsValid)
			{
				if (_tileset != null)
					SyncTilesetFromTarget(tool);

				return;
			}

			var path = reference.ResolvePath();
			var pathChanged = path != null && !string.Equals(path, _tilesetPath, StringComparison.OrdinalIgnoreCase);

			if (pathChanged || _tilesetGeneration != TilesetRuntime.Generation)
				SyncTilesetFromTarget(tool);
		}

		private void AssignTileset(TilePaintTool tool, EngineAssetReference reference)
		{
			if (tool.Target != null)
			{
				tool.Target.Tileset = reference;
				tool.Target.ApplyDeferredMaterial();
				EditorChangeTracker.MarkChanged(tool.Target.Entity, "Assign tileset");
			}

			_hasSelection = false;
			tool.SetSelection(Array.Empty<int>(), 0, 0);
			LoadTileset(reference);
		}

		private void SyncTilesetFromTarget(TilePaintTool tool)
		{
			_hasSelection = false;
			tool.SetSelection(Array.Empty<int>(), 0, 0);
			LoadTileset(tool.Target?.Tileset ?? default);
		}

		private void LoadTileset(EngineAssetReference reference)
		{
			ReleaseAtlas();

			_tileset = null;
			_tilesetPath = null;
			_tilesetGeneration = TilesetRuntime.Generation;

			// Stage on the tool even before a layer exists: it is what the first stroke binds its new layer to.
			_tool.StagedTileset = reference;

			if (!reference.IsValid)
				return;

			_tilesetPath = reference.ResolvePath();
			_tileset = TilesetRuntime.Get(reference);

			if (_tileset?.Texture == null)
				return;

			_tool.FallbackTileWidth = _tileset.TileWidth;
			_tool.FallbackTileHeight = _tileset.TileHeight;

			var manager = Core.GetGlobalManager<ImGuiManager>();
			if (manager != null)
				_atlasTextureId = manager.BindTexture(_tileset.Texture);

			if (_tileset.TileCount > 0)
			{
				_selectionAnchor = new Point2(0, 0);
				_selectionEnd = new Point2(0, 0);
				_hasSelection = true;
				_tool.SetSingleSelection(0);
			}
		}

		private void ReleaseAtlas()
		{
			if (_atlasTextureId == IntPtr.Zero)
				return;

			Core.GetGlobalManager<ImGuiManager>()?.UnbindTexture(_atlasTextureId);
			_atlasTextureId = IntPtr.Zero;
		}

		#endregion
	}

	/// <summary>Shared draw-list helper: overlays the tile grid on an atlas image.</summary>
	public static class TilePaletteDrawing
	{
		public static void DrawSliceGrid(ImDrawListPtr drawList, Num.Vector2 origin, TilesetRuntime tileset, float zoom)
		{
			if (tileset?.Texture == null || tileset.Columns <= 0 || tileset.Rows <= 0)
				return;

			// A dense grid at low zoom is just noise — and thousands of draw-list lines.
			if (tileset.Columns > 256 || tileset.Rows > 256)
				return;

			var color = ImGui.GetColorU32(new Num.Vector4(1f, 1f, 1f, 0.25f));

			var tw = tileset.TileWidth;
			var th = tileset.TileHeight;
			var spacing = tileset.Asset.Spacing;
			var margin = tileset.Asset.Margin;

			for (var col = 0; col <= tileset.Columns; col++)
			{
				var x = origin.X + (margin + col * (tw + spacing)) * zoom;
				var yTop = origin.Y + margin * zoom;
				var yBottom = origin.Y + (margin + tileset.Rows * (th + spacing) - spacing) * zoom;

				drawList.AddLine(new Num.Vector2(x, yTop), new Num.Vector2(x, yBottom), color);
			}

			for (var row = 0; row <= tileset.Rows; row++)
			{
				var y = origin.Y + (margin + row * (th + spacing)) * zoom;
				var xLeft = origin.X + margin * zoom;
				var xRight = origin.X + (margin + tileset.Columns * (tw + spacing) - spacing) * zoom;

				drawList.AddLine(new Num.Vector2(xLeft, y), new Num.Vector2(xRight, y), color);
			}
		}
	}
}
