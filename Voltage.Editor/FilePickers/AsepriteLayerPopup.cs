using System;
using System.Collections.Generic;
using System.IO;
using ImGuiNET;
using Voltage.Editor.DebugUtils;
using Num = System.Numerics;

namespace Voltage.Editor.FilePickers
{
	/// <summary>Picks which layers and which frame of an Aseprite file get flattened into a single image.</summary>
	public sealed class AsepriteLayerPopup
	{
		private readonly string _popupId;

		private string _filePath;
		private readonly List<string> _layers = new();
		private readonly HashSet<string> _selected = new(StringComparer.Ordinal);
		private int _frame;
		private int _frameCount = 1;

		private bool _openNextFrame;
		private bool _confirmed;

		public string FilePath => _filePath;

		/// <summary>Layers to composite. Empty means "all visible layers".</summary>
		public List<string> SelectedLayers { get; } = new();

		/// <summary>Zero-based frame index.</summary>
		public int Frame { get; private set; }

		public AsepriteLayerPopup(string popupId) => _popupId = popupId;

		/// <summary>Reads the file's layers and frame count and arms the popup. False when the file cannot be read.</summary>
		public bool Open(string absolutePath, IEnumerable<string> preselectedLayers = null, int frame = 0)
		{
			_layers.Clear();
			_selected.Clear();

			try
			{
				var file = Core.Content.LoadAsepriteFile(absolutePath);
				if (file == null)
					return false;

				foreach (var layer in file.Layers)
				{
					if (!string.IsNullOrEmpty(layer.Name))
						_layers.Add(layer.Name);
				}

				_frameCount = Math.Max(1, file.Frames.Count);
			}
			catch (Exception e)
			{
				EditorDebug.Log($"Aseprite: could not read '{absolutePath}': {e.Message}", "Tileset");
				return false;
			}

			if (preselectedLayers != null)
			{
				foreach (var layer in preselectedLayers)
				{
					if (_layers.Contains(layer))
						_selected.Add(layer);
				}
			}

			_filePath = absolutePath;
			_frame = Math.Clamp(frame, 0, _frameCount - 1);
			_confirmed = false;
			_openNextFrame = true;
			return true;
		}

		/// <summary>Draws the popup. Returns true on the single frame the user confirms.</summary>
		public bool Draw()
		{
			if (_openNextFrame)
			{
				ImGui.OpenPopup(_popupId);
				_openNextFrame = false;
			}

			var center = ImGui.GetMainViewport().GetCenter();
			ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new Num.Vector2(0.5f, 0.5f));
			ImGui.SetNextWindowSize(new Num.Vector2(420, 460), ImGuiCond.Appearing);

			var open = true;
			if (!ImGui.BeginPopupModal(_popupId, ref open, ImGuiWindowFlags.NoResize))
				return false;

			ImGui.TextUnformatted(Path.GetFileName(_filePath ?? string.Empty));
			ImGui.Separator();

			ImGui.TextUnformatted("Frame");
			ImGui.SetNextItemWidth(-1);

			var frame = _frame;
			if (ImGui.SliderInt("##frame", ref frame, 0, _frameCount - 1, $"%d / {_frameCount - 1}"))
				_frame = Math.Clamp(frame, 0, _frameCount - 1);

			ImGui.Spacing();
			ImGui.TextUnformatted("Layers");
			ImGui.TextDisabled(_selected.Count == 0
				? "None ticked — all visible layers will be flattened."
				: $"{_selected.Count} layer(s) will be composited together.");

			ImGui.BeginChild("layers", new Num.Vector2(0, 260), true);

			foreach (var layer in _layers)
			{
				var ticked = _selected.Contains(layer);
				if (ImGui.Checkbox(layer, ref ticked))
				{
					if (ticked)
						_selected.Add(layer);
					else
						_selected.Remove(layer);
				}
			}

			if (_layers.Count == 0)
				ImGui.TextDisabled("This file reports no named layers.");

			ImGui.EndChild();

			if (ImGui.Button("Select all"))
			{
				foreach (var layer in _layers)
					_selected.Add(layer);
			}

			ImGui.SameLine();

			if (ImGui.Button("Clear"))
				_selected.Clear();

			ImGui.Separator();

			var confirmed = false;
			if (ImGui.Button("Use this", new Num.Vector2(120, 0)))
			{
				SelectedLayers.Clear();

				// File order, not tick order: that is how Aseprite composites, so the result matches the artist's view.
				foreach (var layer in _layers)
				{
					if (_selected.Contains(layer))
						SelectedLayers.Add(layer);
				}

				Frame = _frame;
				_confirmed = true;
				confirmed = true;
				ImGui.CloseCurrentPopup();
			}

			ImGui.SameLine();

			if (ImGui.Button("Cancel", new Num.Vector2(120, 0)))
				ImGui.CloseCurrentPopup();

			ImGui.EndPopup();
			return confirmed;
		}
	}
}
