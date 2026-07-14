using System;
using System.Collections.Generic;
using System.IO;
using ImGuiNET;
using Voltage.Editor.Assets;
using Voltage.Editor.DebugUtils;
using Voltage.Editor.FilePickers;
using Voltage.Editor.ImGuiCore;
using Voltage.Editor.Persistence;
using Voltage.Editor.ProjectFile;
using Voltage.Tilesets;
using Num = System.Numerics;

namespace Voltage.Editor.Tools.Tilemap
{
	/// <summary>Authors a <c>.vtileset</c>: source image, grid, optional normal map. Previews the slicing live.</summary>
	public class TilesetEditorWindow
	{
		public bool IsOpen;

		private static readonly string[] ImageExtensions = { ".png", ".aseprite", ".ase" };

		private readonly FolderBrowser _folderBrowser = new("tileset-save-folder");
		private readonly AssetFileBrowser _imageBrowser =
			new("tileset-image-browser", ImageExtensions, "Images (png, aseprite)");
		private readonly AsepriteLayerPopup _asepritePopup = new("tileset-aseprite-layers");

		/// <summary>Which slot the in-flight Browse / layer popup is filling.</summary>
		private bool _browsingNormalMap;

		private static readonly PersistentString _lastSaveFolder = new("Tileset_LastSaveFolder", string.Empty);

		private static string SaveFolder
		{
			get
			{
				var stored = _lastSaveFolder.Value;
				if (!string.IsNullOrEmpty(stored) && Directory.Exists(stored))
					return stored;

				return TilesetPaths.DefaultTilesetFolder() ?? string.Empty;
			}
			set => _lastSaveFolder.Value = value;
		}

		private TilesetAsset _asset;
		private string _path;
		private bool _dirty;

		private TilesetRuntime _preview;
		private IntPtr _previewTextureId;
		private IntPtr _previewNormalId;
		private bool _previewStale = true;

		private float _zoom = 2f;
		private bool _showNormalMap;

		/// <summary>Opens an existing tileset for editing.</summary>
		public void Open(string absolutePath)
		{
			var asset = TilesetAssetIO.Load(absolutePath);
			if (asset == null)
			{
				EditorDebug.Log($"TilesetEditor: could not load '{absolutePath}'.", "Tileset");
				return;
			}

			_asset = asset;
			_path = absolutePath;
			_dirty = false;
			_previewStale = true;
			IsOpen = true;
		}

		public void NewTileset()
		{
			_asset = TilesetAssetIO.CreateDefault("New Tileset");
			_path = null;
			_dirty = true;
			_previewStale = true;
			IsOpen = true;
		}

		public void Draw()
		{
			if (!IsOpen)
				return;

			ImGui.SetNextWindowSize(new Num.Vector2(720, 620), ImGuiCond.FirstUseEver);

			var open = IsOpen;
			if (!ImGui.Begin("Tileset Editor ###TilesetEditorWindow", ref open))
			{
				IsOpen = open;
				ImGui.End();
				return;
			}

			IsOpen = open;

			DrawToolbar();
			ImGui.Separator();

			if (_asset == null)
			{
				ImGui.TextDisabled("No tileset open. Use \"New\", or open a .vtileset from the Asset Browser.");
				ImGui.End();
				PumpBrowsers();
				return;
			}

			DrawSettings();
			ImGui.Separator();
			DrawPreview();

			ImGui.End();

			// Modals must be driven outside Begin/End.
			PumpBrowsers();
		}

		private void DrawToolbar()
		{
			if (ImGui.Button("New"))
				NewTileset();

			ImGui.SameLine();

			if (ImGui.Button("Save") && _asset != null)
				Save();

			ImGui.SameLine();
			ImGui.TextDisabled(_path == null
				? "(unsaved)"
				: $"{Path.GetFileName(_path)}{(_dirty ? " *" : "")}");
		}

		/// <summary>Save location for a new tileset. Defaults to the last folder used, persisted across sessions.</summary>
		private void DrawSaveFolder()
		{
			if (_path != null)
				return;

			ImGui.TextUnformatted("Save to");
			ImGui.SameLine(LabelWidth);

			var folder = SaveFolder;
			ImGui.SetNextItemWidth(FieldWidth(1));
			ImGui.InputText("##savefolder", ref folder, 512, ImGuiInputTextFlags.ReadOnly);

			if (ImGui.IsItemHovered() && !string.IsNullOrEmpty(folder))
				ImGui.SetTooltip(folder);

			ImGui.SameLine();
			if (ImGui.Button("Browse...##savefolder", new Num.Vector2(ButtonWidth, 0)))
				_folderBrowser.Open("Select the folder to save the tileset in", SaveFolder, this);

			if (ImGui.IsItemHovered())
				ImGui.SetTooltip("Remembered for the next tileset you create.");
		}

		private void DrawSettings()
		{
			var changed = false;

			var name = _asset.Name ?? string.Empty;
			if (ImGui.InputText("Name", ref name, 128))
			{
				_asset.Name = name;
				changed = true;
			}

			DrawSaveFolder();

			ImGui.Spacing();
			ImGui.TextUnformatted("Source image");

			changed |= DrawImageSlot("Image", isNormalMap: false);

			ImGui.Spacing();
			ImGui.TextUnformatted("Normal map (optional — enables deferred lighting for these tiles)");

			changed |= DrawImageSlot("Normal map", isNormalMap: true);

			ImGui.TextDisabled("The normal map must be the same size as the source image — it is sampled with the same UVs.");

			ImGui.Spacing();
			ImGui.TextUnformatted("Grid");

			var tileWidth = _asset.TileWidth;
			if (ImGui.InputInt("Tile width", ref tileWidth))
			{
				_asset.TileWidth = Math.Max(1, tileWidth);
				changed = true;
			}

			var tileHeight = _asset.TileHeight;
			if (ImGui.InputInt("Tile height", ref tileHeight))
			{
				_asset.TileHeight = Math.Max(1, tileHeight);
				changed = true;
			}

			var spacing = _asset.Spacing;
			if (ImGui.InputInt("Spacing", ref spacing))
			{
				_asset.Spacing = Math.Max(0, spacing);
				changed = true;
			}

			var margin = _asset.Margin;
			if (ImGui.InputInt("Margin", ref margin))
			{
				_asset.Margin = Math.Max(0, margin);
				changed = true;
			}

			if (changed)
			{
				_dirty = true;
				_previewStale = true;
			}
		}

		/// <summary>One image slot. A .png is taken as-is; an .aseprite opens the layer/frame popup.</summary>
		private bool DrawImageSlot(string label, bool isNormalMap)
		{
			var reference = isNormalMap ? _asset.NormalMap : _asset.Texture;
			var source = isNormalMap ? _asset.NormalMapSource : _asset.TextureSource;
			var layers = isNormalMap ? _asset.NormalMapLayers : _asset.TextureLayers;
			var frame = isNormalMap ? _asset.NormalMapFrame : _asset.TextureFrame;

			ImGui.TextUnformatted(label);
			ImGui.SameLine(LabelWidth);

			var display = reference.IsValid
				? reference.AssetName ?? Path.GetFileName(reference.AssetPath)
				: "(None)";

			var buttons = reference.IsValid ? 2 : 1;
			ImGui.SetNextItemWidth(FieldWidth(buttons));
			ImGui.InputText($"##slot{label}", ref display, 256, ImGuiInputTextFlags.ReadOnly);

			if (ImGui.IsItemHovered() && reference.IsValid)
				ImGui.SetTooltip(reference.AssetPath ?? display);

			ImGui.SameLine();
			if (ImGui.Button($"Browse...##{label}", new Num.Vector2(ButtonWidth, 0)))
			{
				_browsingNormalMap = isNormalMap;
				_imageBrowser.Open($"Select the {label.ToLowerInvariant()}", ImageBrowseStart(reference), this);
			}

			var changed = false;

			if (reference.IsValid)
			{
				ImGui.SameLine();
				if (ImGui.Button($"Clear##{label}", new Num.Vector2(ButtonWidth, 0)))
				{
					SetImage(isNormalMap, default, TilesetImageSource.Png, null, 0);
					changed = true;
				}
			}

			// Own row: SameLine here would draw this over the top of the text field above.
			if (reference.IsValid && source == TilesetImageSource.Aseprite)
			{
				var summary = layers == null || layers.Count == 0
					? $"all visible layers, frame {frame}"
					: $"{string.Join(", ", layers)} — frame {frame}";

				ImGui.SetCursorPosX(ImGui.GetCursorPosX() + LabelWidth);

				if (ImGui.SmallButton($"Layers...##{label}"))
				{
					var path = reference.ResolvePath();
					if (path != null && _asepritePopup.Open(path, layers, frame))
						_browsingNormalMap = isNormalMap;
				}

				ImGui.SameLine();
				ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + FieldWidth(1));
				ImGui.TextDisabled(summary);
				ImGui.PopTextWrapPos();
			}

			return changed;
		}

		// Field width is what's left after the label and buttons, so a long path shrinks the field
		// instead of pushing the buttons out of the window.
		private const float LabelWidth = 160f;
		private const float ButtonWidth = 90f;

		private static float FieldWidth(int buttonCount)
		{
			var spacing = ImGui.GetStyle().ItemSpacing.X;
			var reserved = buttonCount * (ButtonWidth + spacing);
			return Math.Max(80f, ImGui.GetContentRegionAvail().X - reserved);
		}

		private static string ImageBrowseStart(Voltage.Serialization.AssetReference reference)
		{
			var current = reference.ResolvePath();
			if (!string.IsNullOrEmpty(current))
				return Path.GetDirectoryName(current);

			return ProjectManager.Instance?.CurrentProject?.ContentsFolder;
		}

		private void SetImage(bool isNormalMap, Voltage.Serialization.AssetReference reference,
			TilesetImageSource source, List<string> layers, int frame)
		{
			if (isNormalMap)
			{
				_asset.NormalMap = reference;
				_asset.NormalMapSource = source;
				_asset.NormalMapLayers = layers ?? new List<string>();
				_asset.NormalMapFrame = frame;
			}
			else
			{
				_asset.Texture = reference;
				_asset.TextureSource = source;
				_asset.TextureLayers = layers ?? new List<string>();
				_asset.TextureFrame = frame;
			}

			_dirty = true;
			_previewStale = true;
		}

		/// <summary>Feeds browse/popup results back into the asset. Called every frame from Draw.</summary>
		private void PumpBrowsers()
		{
			_folderBrowser.Draw("Select the folder to save the tileset in");
			_imageBrowser.Draw("Select an image");

			if (_folderBrowser.TryTakeResult(out var folder) && !string.IsNullOrEmpty(folder))
				SaveFolder = folder;

			if (_imageBrowser.TryTakeResult(out var file) && !string.IsNullOrEmpty(file))
			{
				var ext = Path.GetExtension(file).ToLowerInvariant();

				if (ext is ".aseprite" or ".ase")
				{
					// Defer: the asset is only set once the layer/frame choice is confirmed.
					if (!_asepritePopup.Open(file))
						EditorDebug.Log($"Tileset: could not read the Aseprite file '{file}'.", "Tileset");
				}
				else
				{
					SetImage(_browsingNormalMap, ReferenceFor(file), TilesetImageSource.Png, null, 0);
				}
			}

			if (_asepritePopup.Draw())
			{
				SetImage(_browsingNormalMap, ReferenceFor(_asepritePopup.FilePath),
					TilesetImageSource.Aseprite,
					new List<string>(_asepritePopup.SelectedLayers),
					_asepritePopup.Frame);
			}
		}

		/// <summary>Registers the file with the AssetDatabase (minting its .meta GUID if new) and references it.</summary>
		private static Voltage.Serialization.AssetReference ReferenceFor(string absolutePath)
		{
			var db = AssetDatabase.Instance;
			if (db == null || string.IsNullOrEmpty(absolutePath))
				return default;

			return TileAssetUtils.ToEngineReference(db.GetReference(absolutePath));
		}

		private void DrawPreview()
		{
			if (_previewStale)
				RebuildPreview();

			TileValidation.Draw(TileValidation.ValidateTileset(_asset, _preview));

			if (_preview?.Texture == null)
			{
				ImGui.TextDisabled("Assign a source image to see the sliced preview.");
				return;
			}

			ImGui.TextUnformatted($"{_preview.Columns} x {_preview.Rows} = {_preview.TileCount} tiles " +
			                      $"({_preview.Texture.Width}x{_preview.Texture.Height} px)");

			ImGui.SliderFloat("Zoom", ref _zoom, 1f, 8f, "%.1fx");

			if (_preview.HasNormalMap)
				ImGui.Checkbox("Show normal map", ref _showNormalMap);
			else
				_showNormalMap = false;

			var textureId = _showNormalMap ? _previewNormalId : _previewTextureId;
			if (textureId == IntPtr.Zero)
				return;

			ImGui.BeginChild("tileset_preview", new Num.Vector2(0, 0), true,
				ImGuiWindowFlags.HorizontalScrollbar);

			var origin = ImGui.GetCursorScreenPos();
			var size = new Num.Vector2(_preview.Texture.Width * _zoom, _preview.Texture.Height * _zoom);
			ImGui.Image(textureId, size);

			TilePaletteDrawing.DrawSliceGrid(ImGui.GetWindowDrawList(), origin, _preview, _zoom);

			ImGui.EndChild();
		}

		private void RebuildPreview()
		{
			_previewStale = false;

			ReleasePreviewTextures();
			_preview = null;

			if (_asset == null || !_asset.Texture.IsValid)
				return;

			_preview = TilesetRuntime.Build(_asset);
			if (_preview?.Texture == null)
				return;

			var manager = Core.GetGlobalManager<ImGuiManager>();
			if (manager == null)
				return;

			_previewTextureId = manager.BindTexture(_preview.Texture);

			if (_preview.NormalMap != null)
				_previewNormalId = manager.BindTexture(_preview.NormalMap);
		}

		private void ReleasePreviewTextures()
		{
			var manager = Core.GetGlobalManager<ImGuiManager>();
			if (manager == null)
			{
				_previewTextureId = IntPtr.Zero;
				_previewNormalId = IntPtr.Zero;
				return;
			}

			if (_previewTextureId != IntPtr.Zero)
			{
				manager.UnbindTexture(_previewTextureId);
				_previewTextureId = IntPtr.Zero;
			}

			if (_previewNormalId != IntPtr.Zero)
			{
				manager.UnbindTexture(_previewNormalId);
				_previewNormalId = IntPtr.Zero;
			}
		}

		private static TilesetImageSource SourceFor(string path)
		{
			var ext = Path.GetExtension(path)?.ToLowerInvariant();
			return ext is ".aseprite" or ".ase" ? TilesetImageSource.Aseprite : TilesetImageSource.Png;
		}

		private void Save()
		{
			if (_path == null)
			{
				var folder = SaveFolder;
				if (string.IsNullOrEmpty(folder))
				{
					EditorDebug.Log("TilesetEditor: no project open — cannot save.", "Tileset");
					return;
				}

				Directory.CreateDirectory(folder);

				var baseName = string.IsNullOrWhiteSpace(_asset.Name) ? "Tileset" : _asset.Name;
				_path = TilesetPaths.UniquePath(folder, baseName, TilesetAssetIO.FileExtension);
				SaveFolder = folder;
			}

			try
			{
				TilesetAssetIO.Save(_asset, _path);
				_dirty = false;

				// Drop the cached runtime and re-resolve every live map so the change shows up immediately.
				TilesetRuntime.Invalidate(_path);
				TilemapSceneUtils.ReloadTilesetsInScene();

				AssetDatabase.Instance?.Refresh();
				EditorDebug.Log($"Saved tileset '{Path.GetFileName(_path)}'.", "Tileset");
			}
			catch (Exception ex)
			{
				EditorDebug.Log($"TilesetEditor: failed to save '{_path}': {ex.Message}", "Tileset");
			}
		}
	}
}
