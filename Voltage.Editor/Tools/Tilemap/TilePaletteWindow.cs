using System;
using System.Collections.Generic;
using System.IO;
using ImGuiNET;
using Voltage.Editor.Assets;
using Voltage.Editor.Gizmos;
using Voltage.Editor.ImGuiCore;
using Voltage.Editor.Persistence;
using Voltage.Editor.Undo.Core;
using Voltage.Editor.Utils;
using Voltage.Tilesets;
using EngineAssetReference = Voltage.Serialization.AssetReference;
using Num = System.Numerics;

namespace Voltage.Editor.Tools.Tilemap
{
	/// <summary>UI for tile painting: pick the layer, the tileset, the stamp and the active tool. Drives the shared <see cref="TilePaintTool"/>.</summary>
	public partial class TilePaletteWindow
	{
		public bool IsOpen;

		private static readonly string[] TilesetExtensions = { TilesetAssetIO.FileExtension };

		private TilePaintTool _tool;

		private TilesetRuntime _tileset;
		private string _tilesetPath;
		private IntPtr _atlasTextureId;

		// Cache generation the atlas was loaded at - a re-save under the same path moves it, which a path compare misses.
		private int _tilesetGeneration = -1;

		private float _zoom = 2f;

		// Backdrop behind the atlas and the brush swatch, packed 0xAARRGGBB.
		private const int DefaultAtlasBackground = unchecked((int)0xFF1E1E1E);
		private static readonly PersistentInt _atlasBackground =
			new("TilePalette_AtlasBackground", DefaultAtlasBackground);

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
			ImGui.Spacing();

			DrawIssues(tool);
			ImGui.Spacing();

			DrawTargetSelector(tool);
			ImGui.Spacing();
			ImGui.Separator();

			DrawTilesetSelector(tool, tilesetEditor);
			ImGui.Separator();

			DrawLayerOrder(tool);
			ImGui.Separator();

			DrawEditModeSwitch(tool);
			ImGui.Separator();

			DrawToolButtons(tool);
			DrawGridOptions(tool);
			ImGui.Separator();

			if (tool.EditMode == TileEditMode.Collision)
				DrawCollisionControls(tool);
			else
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
				: "No tileset selected - the layer will be created without one.");

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
				ImGui.TextColored(new Num.Vector4(0.4f, 0.9f, 0.5f, 1f), "Tile cursor active - paint in the game view.");
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

			// The "no tile selected" warning only makes sense while painting tiles; treat the collision brush
			// as always having a selection.
			var hasSelection = tool.EditMode == TileEditMode.Collision || tool.HasSelection;
			issues.AddRange(TileValidation.ValidatePainting(tool.Target, _tileset, hasSelection));

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
				ImGui.TextDisabled($"(render layer {map.RenderLayer})");

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

			// Layer depth is a fine tie-breaker used only when layers share a render layer; the arrows handle it
			// for you, so it lives behind an Advanced expander rather than in the common path.
			if (ImGui.CollapsingHeader("Advanced"))
			{
				var layerDepth = tool.Target.LayerDepth;
				ImGui.SetNextItemWidth(140f);
				if (ImGui.SliderFloat("Layer depth", ref layerDepth, 0f, 1f, "%.2f"))
				{
					tool.Target.LayerDepth = layerDepth;
					EditorChangeTracker.MarkChanged(tool.Target.Entity, "Change tilemap layer depth");
				}

				if (ImGui.IsItemHovered())
					ImGui.SetTooltip("Tie-breaker within one render layer. 0 = front, 1 = back. Usually left to the arrows.");
			}
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

		// The arrows reorder by RenderLayer — the same axis that positions a tilemap against sprites — by swapping
		// the two neighbours' values (never renumbering, so other layers are untouched). Only when the two share a
		// render layer does it fall back to the fine LayerDepth tie-breaker.
		private static void MoveLayer(List<TilemapRenderer> sorted, int index, int direction)
		{
			var other = index + direction;
			if (other < 0 || other >= sorted.Count)
				return;

			var a = sorted[index];
			var b = sorted[other];

			if (a.RenderLayer != b.RenderLayer)
			{
				(a.RenderLayer, b.RenderLayer) = (b.RenderLayer, a.RenderLayer);
			}
			else
			{
				// Same render layer: give this group distinct depths (preserving order), then swap the pair.
				SpreadDepthsWithinLayers(sorted);
				(a.LayerDepth, b.LayerDepth) = (b.LayerDepth, a.LayerDepth);
			}

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

			DrawOrientationControls(tool);
			DrawBrushPreview(tool);
			DrawTerrainSelector(tool);
		}

		/// <summary>
		/// Shows the WHOLE selected stamp with the brush's current rotation/flip applied - each tile is oriented
		/// about its own centre in the same grid arrangement a stroke actually paints (the block itself is not
		/// rotated, matching StampAt), so the preview never lies about the result.
		/// </summary>
		private void DrawBrushPreview(TilePaintTool tool)
		{
			if (_tileset?.Texture == null || _atlasTextureId == IntPtr.Zero || !tool.HasSelection)
				return;

			var cols = tool.SelectionWidth;
			var rows = tool.SelectionHeight;
			var texW = (float)_tileset.Texture.Width;
			var texH = (float)_tileset.Texture.Height;

			var flipX = tool.OrientationFlipX ? -1f : 1f;
			var flipY = tool.OrientationFlipY ? -1f : 1f;
			var angle = tool.OrientationRotation * (float)(Math.PI * 0.5);
			var cos = (float)Math.Cos(angle);
			var sin = (float)Math.Sin(angle);

			// Fit the whole colsxrows block into a fixed box, preserving its shape.
			const float box = 96f;
			var cellPx = box / Math.Max(cols, rows);
			var blockW = cols * cellPx;
			var blockH = rows * cellPx;

			var origin = ImGui.GetCursorScreenPos();
			var blockX = origin.X + (box - blockW) * 0.5f;
			var blockY = origin.Y + (box - blockH) * 0.5f;

			var drawList = ImGui.GetWindowDrawList();
			var boxMax = new Num.Vector2(origin.X + box, origin.Y + box);
			drawList.AddRectFilled(origin, boxMax, ImGui.GetColorU32(AtlasBackground));

			var half = cellPx * 0.5f;
			for (var sy = 0; sy < rows; sy++)
			{
				for (var sx = 0; sx < cols; sx++)
				{
					var tile = tool.Selection[sy * cols + sx];
					if (!_tileset.IsValidIndex(tile))
						continue;

					// Animated tiles play in the preview too, so a placed animation reads at a glance.
					var displayTile = _tileset.ResolveFrame(tile, (float)ImGui.GetTime());
					if (!_tileset.IsValidIndex(displayTile))
						displayTile = tile;

					var rect = _tileset.SourceRects[displayTile];
					var uvTL = new Num.Vector2(rect.X / texW, rect.Y / texH);
					var uvTR = new Num.Vector2((rect.X + rect.Width) / texW, rect.Y / texH);
					var uvBR = new Num.Vector2((rect.X + rect.Width) / texW, (rect.Y + rect.Height) / texH);
					var uvBL = new Num.Vector2(rect.X / texW, (rect.Y + rect.Height) / texH);

					var cx = blockX + sx * cellPx + half;
					var cy = blockY + sy * cellPx + half;

					// Flip in local space, then rotate about the tile's centre - the renderer's exact order.
					Num.Vector2 Corner(float lx, float ly)
					{
						lx *= half * flipX;
						ly *= half * flipY;
						return new Num.Vector2(cx + (lx * cos - ly * sin), cy + (lx * sin + ly * cos));
					}

					drawList.AddImageQuad(_atlasTextureId,
						Corner(-1, -1), Corner(1, -1), Corner(1, 1), Corner(-1, 1),
						uvTL, uvTR, uvBR, uvBL, 0xFFFFFFFF);
				}
			}

			drawList.AddRect(origin, boxMax, ImGui.GetColorU32(new Num.Vector4(0.5f, 0.5f, 0.5f, 1f)));

			ImGui.Dummy(new Num.Vector2(box, box));
			ImGui.SameLine();
			ImGui.TextDisabled($"brush {cols}x{rows}{(tool.CurrentOrientation == 0 ? "" : " (oriented)")}");
		}

		/// <summary>Autotile terrain picker. When set, the brush paints the terrain and auto-selects tiles by neighbour.</summary>
		private void DrawTerrainSelector(TilePaintTool tool)
		{
			var terrains = _tileset?.Asset?.Terrains;
			if (terrains == null || terrains.Count == 0)
			{
				tool.ActiveTerrain = -1;
				return;
			}

			var current = 0;
			var labels = new List<string> { "Off (paint tiles)" };
			for (var i = 0; i < terrains.Count; i++)
			{
				labels.Add(string.IsNullOrEmpty(terrains[i].Name) ? $"Terrain {terrains[i].Id}" : terrains[i].Name);
				if (terrains[i].Id == tool.ActiveTerrain)
					current = i + 1;
			}

			ImGui.SetNextItemWidth(200f);
			if (ImGui.Combo("Autotile", ref current, labels.ToArray(), labels.Count))
				tool.ActiveTerrain = current == 0 ? -1 : terrains[current - 1].Id;

			if (ImGui.IsItemHovered())
				ImGui.SetTooltip("Paint a terrain; the right edge/corner tile is chosen automatically from neighbours.\nDefine terrains and per-tile masks in the Tileset Editor.");
		}

		/// <summary>Orientation applied to newly painted tiles. Hotkeys Z/X rotate, V/C flip.</summary>
		private static void DrawOrientationControls(TilePaintTool tool)
		{
			ImGui.TextUnformatted("Orient");
			ImGui.SameLine();

			if (ImGui.SmallButton("Rot -##orient")) tool.RotateBrush(-1);
			if (ImGui.IsItemHovered()) ImGui.SetTooltip("Rotate 90° left (Z)");
			ImGui.SameLine();

			if (ImGui.SmallButton("Rot +##orient")) tool.RotateBrush(1);
			if (ImGui.IsItemHovered()) ImGui.SetTooltip("Rotate 90° right (X)");
			ImGui.SameLine();

			var flipX = tool.OrientationFlipX;
			if (ToggleSmall("Flip X##orient", flipX)) tool.ToggleFlipX();
			if (ImGui.IsItemHovered()) ImGui.SetTooltip("Flip horizontally (V)");
			ImGui.SameLine();

			var flipY = tool.OrientationFlipY;
			if (ToggleSmall("Flip Y##orient", flipY)) tool.ToggleFlipY();
			if (ImGui.IsItemHovered()) ImGui.SetTooltip("Flip vertically (C)");
			ImGui.SameLine();

			ImGui.BeginDisabled(tool.CurrentOrientation == 0);
			if (ImGui.SmallButton("Reset##orient")) tool.ResetOrientation();
			ImGui.EndDisabled();

			if (tool.CurrentOrientation != 0)
			{
				ImGui.SameLine();
				ImGui.TextDisabled($"({tool.OrientationRotation * 90}°{(flipX ? " X" : "")}{(flipY ? " Y" : "")})");
			}
		}

		private static bool ToggleSmall(string label, bool active)
		{
			if (active)
				ImGui.PushStyleColor(ImGuiCol.Button, new Num.Vector4(0.2f, 0.5f, 1f, 1f));

			var clicked = ImGui.SmallButton(label);

			if (active)
				ImGui.PopStyleColor();

			return clicked;
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
				ImGui.SetTooltip("Line width in screen pixels - stays constant as you zoom.");

			ImGui.SetNextItemWidth(140f);
			ImGui.SliderFloat("Cursor thickness", ref tool.HighlightThickness, 0.5f, 6f, "%.2f px");
		}

		// The tile-grid overlay is skipped past this many tiles per axis (see TilePaletteDrawing.DrawSliceGrid);
		// beyond it the atlas still shows but without visible tile boundaries.
		private const int GridDisplayLimit = 256;

		/// <summary>Yellow warning (with the shared warning icon) when the tileset is too large to display its grid.</summary>
		private void DrawAtlasWarnings()
		{
			if (_tileset.Columns <= GridDisplayLimit && _tileset.Rows <= GridDisplayLimit)
				return;

			var warning = new Num.Vector4(1f, 0.85f, 0.25f, 1f);

			if (ImguiImageLoader.WarningIconId != IntPtr.Zero)
			{
				var iconSize = ImGui.GetFontSize() + 2f;
				ImGui.Image(ImguiImageLoader.WarningIconId, new Num.Vector2(iconSize, iconSize),
					Num.Vector2.Zero, Num.Vector2.One, warning);
				ImGui.SameLine();
			}

			ImGui.PushStyleColor(ImGuiCol.Text, warning);
			ImGui.TextWrapped(
				$"Large tileset ({_tileset.Columns}x{_tileset.Rows} tiles): the tile grid is hidden beyond " +
				$"{GridDisplayLimit} per axis. Zoom in to line up tile edges, or split the tileset.");
			ImGui.PopStyleColor();
		}

		private void DrawAtlas(TilePaintTool tool)
		{
			if (_tileset?.Texture == null || _atlasTextureId == IntPtr.Zero)
				return;

			DrawAtlasWarnings();

			if (_tileset.Asset?.TextureIsAsepriteAnimation == true)
			{
				ImGui.TextDisabled("Animated tileset: the strip below is the animation frames. Paint tile 0 (top-left) — it plays automatically.");
			}

			// Logarithmic + a sub-1x floor so an oversized atlas can be zoomed OUT to fit, not only scrolled.
			ImGui.SetNextItemWidth(160f);
			ImGui.SliderFloat("Zoom", ref _zoom, 0.1f, 8f, "%.2fx", ImGuiSliderFlags.Logarithmic);

			ImGui.SameLine();

			// "Fit" scales the whole tileset into the space the atlas child is about to occupy - the answer for a
			// tileset too big to browse at 1x. GetContentRegionAvail here is the remaining window, ~= the child.
			if (ImGui.Button("Fit"))
			{
				var avail = ImGui.GetContentRegionAvail();
				var fitX = avail.X / _tileset.Texture.Width;
				var fitY = Math.Max(80f, avail.Y - 4f) / _tileset.Texture.Height;
				_zoom = Math.Clamp(Math.Min(fitX, fitY), 0.02f, 8f);
			}

			if (ImGui.IsItemHovered())
				ImGui.SetTooltip("Zoom the whole tileset to fit the panel.");

			ImGui.SameLine();
			if (ImGui.Button("1:1"))
				_zoom = 1f;

			ImGui.SameLine();
			DrawBackgroundPicker();

			if (_hasSelection)
			{
				var w = Math.Abs(_selectionEnd.X - _selectionAnchor.X) + 1;
				var h = Math.Abs(_selectionEnd.Y - _selectionAnchor.Y) + 1;
				ImGui.TextDisabled($"Selection: {w} x {h} tile{(w * h == 1 ? "" : "s")}");
			}
			else
			{
				ImGui.TextDisabled("Click a tile - or drag across several - to choose the stamp.");
			}

			// Both scrollbars, so a large atlas at any zoom pans in every direction.
			ImGui.PushStyleColor(ImGuiCol.ChildBg, AtlasBackground);
			ImGui.BeginChild("tile_atlas", new Num.Vector2(0, 0), true,
				ImGuiWindowFlags.HorizontalScrollbar | ImGuiWindowFlags.AlwaysVerticalScrollbar);

			var origin = ImGui.GetCursorScreenPos();
			var size = new Num.Vector2(_tileset.Texture.Width * _zoom, _tileset.Texture.Height * _zoom);

			ImGui.Image(_atlasTextureId, size);

			var drawList = ImGui.GetWindowDrawList();
			TilePaletteDrawing.DrawSliceGrid(drawList, origin, _tileset, _zoom);

			HandleAtlasSelection(tool, origin);
			DrawSelectionOverlay(drawList, origin);

			ImGui.EndChild();
			ImGui.PopStyleColor();
		}

		/// <summary>Backdrop for the atlas and the brush swatch. A viewing aid - never painted.</summary>
		private static Num.Vector4 AtlasBackground => UnpackColor(_atlasBackground.Value);

		private static void DrawBackgroundPicker()
		{
			var color = AtlasBackground;

			if (ImGui.ColorEdit4("##atlasbg", ref color,
				    ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.NoLabel |
				    ImGuiColorEditFlags.AlphaBar | ImGuiColorEditFlags.AlphaPreview))
			{
				_atlasBackground.Value = PackColor(color);
			}

			if (ImGui.IsItemHovered())
				ImGui.SetTooltip("Background behind the tiles.\n" +
				                 "A viewing aid only - tiles are still placed with their real transparency.");

			ImGui.SameLine();
			if (ImGui.SmallButton("Reset bg"))
				_atlasBackground.Value = DefaultAtlasBackground;
		}

		private static Num.Vector4 UnpackColor(int packed) => new(
			((packed >> 16) & 0xFF) / 255f,
			((packed >> 8) & 0xFF) / 255f,
			(packed & 0xFF) / 255f,
			((packed >> 24) & 0xFF) / 255f);

		private static int PackColor(Num.Vector4 color)
		{
			static int Channel(float v) => (int)(Math.Clamp(v, 0f, 1f) * 255f + 0.5f);

			return (Channel(color.W) << 24) | (Channel(color.X) << 16) | (Channel(color.Y) << 8) | Channel(color.Z);
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
			// With no layer targeted the palette holds a tileset staged for the next New Layer - leave it be.
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

			// A dense grid at low zoom is just noise - and thousands of draw-list lines.
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
