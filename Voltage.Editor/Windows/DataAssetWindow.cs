using System;
using System.Collections.Generic;
using System.IO;
using ImGuiNET;
using Voltage.Data;
using Voltage.Editor.DebugUtils;
using Voltage.Editor.Inspectors;
using Voltage.Editor.Inspectors.TypeInspectors;
using Voltage.Editor.Utils;
using Num = System.Numerics;

namespace Voltage.Editor.Windows
{
	/// <summary>
	/// The editor for <b>every</b> <c>.vasset</c> data container — one window, all user-declared types.
	/// </summary>
	public class DataAssetWindow
	{
		public bool IsOpen;

		private DataAsset _asset;
		private string _path;
		private string _typeId;
		private List<AbstractTypeInspector> _inspectors;

		private bool _wasAnyItemActive;
		private bool _dirty;
		private int _seenReloadVersion;
		private string _status;
		private double _statusClearAt;

		/// <summary>Replaces whatever was open.</summary>
		public void Open(string absolutePath)
		{
			if (string.IsNullOrEmpty(absolutePath))
				return;

			var asset = DataAssetCache.GetByPath(absolutePath);
			if (asset == null)
			{
				_status = $"Could not open '{Path.GetFileName(absolutePath)}' — see the console.";
				_statusClearAt = ImGui.GetTime() + 6.0;
				IsOpen = true;
				return;
			}

			_asset = asset;
			_path = absolutePath;
			_typeId = DataAssetRegistry.TryGetId(asset.GetType()) ?? asset.GetType().Name;
			_inspectors = TypeInspectorUtils.GetInspectableProperties(asset);
			_dirty = false;
			_seenReloadVersion = DataAssetCache.ReloadVersion;
			_status = null;
			IsOpen = true;
		}

		/// <summary>Drops the open asset, e.g. on project close.</summary>
		public void Close()
		{
			_asset = null;
			_path = null;
			_inspectors = null;
			IsOpen = false;
		}

		/// <summary>True when that path is the asset currently being edited.</summary>
		public bool IsEditing(string absolutePath) =>
			_path != null && string.Equals(_path, absolutePath, StringComparison.OrdinalIgnoreCase);

		/// <summary>
		/// Rebuilds the inspector list.
		/// </summary>
		public void RefreshFromDisk()
		{
			if (_asset == null)
				return;

			_inspectors = TypeInspectorUtils.GetInspectableProperties(_asset);
			_seenReloadVersion = DataAssetCache.ReloadVersion;
			_dirty = false;
		}

		public void Draw()
		{
			if (!IsOpen)
				return;

			ImGui.SetNextWindowSize(new Num.Vector2(460, 520), ImGuiCond.FirstUseEver);

			var open = IsOpen;
			if (!ImGui.Begin("Data Asset", ref open, ImGuiWindowFlags.MenuBar))
			{
				ImGui.End();
				IsOpen = open;
				return;
			}
			IsOpen = open;

			DrawMenuBar();

			if (_asset == null)
			{
				ImGuiSafe.TextColoredSafe(new Num.Vector4(0.6f, 0.6f, 0.6f, 1f),
					"No data asset open.\n\nDouble-click a .vasset in the Asset Browser, or create one with\n" +
					"right-click ▸ Create ▸ Data Asset.");
				DrawStatus();
				ImGui.End();
				return;
			}

			if (_seenReloadVersion != DataAssetCache.ReloadVersion)
			{
				RefreshFromDisk();
				SetStatus("Reloaded — the file changed on disk.");
			}

			DrawHeader();
			ImGui.Separator();

			if (_inspectors.Count == 0)
			{
				ImGuiSafe.TextColoredSafe(new Num.Vector4(1f, 0.7f, 0.3f, 1f),
					"This type has no public fields, so there is nothing to edit.");
			}
			else
			{
				foreach (var inspector in _inspectors)
					inspector.Draw();
			}

			var anyActive = ImGui.IsAnyItemActive();
			if (_wasAnyItemActive && !anyActive)
				_dirty = true;
			_wasAnyItemActive = anyActive;

			if (_dirty && !anyActive)
				Save();

			if (ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows) &&
				(ImGui.GetIO().KeySuper || ImGui.GetIO().KeyCtrl) && ImGui.IsKeyPressed(ImGuiKey.S))
			{
				Save();
			}

			DrawStatus();
			ImGui.End();
		}

		private void DrawMenuBar()
		{
			if (!ImGui.BeginMenuBar())
				return;

			if (ImGui.MenuItem("Save", "Cmd/Ctrl+S", false, _asset != null))
				Save();

			if (ImGui.MenuItem("Reveal", null, false, _path != null))
				AssetBrowserWindow.PingAsset(_path);

			if (ImGui.MenuItem("Reload", null, false, _path != null))
			{
				DataAssetCache.ReloadPath(_path);
				RefreshFromDisk();
				SetStatus("Reloaded from disk.");
			}

			ImGui.EndMenuBar();
		}

		private void DrawHeader()
		{
			var grey = new Num.Vector4(0.6f, 0.6f, 0.6f, 1f);

			ImGuiSafe.TextColoredSafe(new Num.Vector4(0.3f, 0.8f, 1f, 1f), Path.GetFileNameWithoutExtension(_path));

			ImGui.SameLine();
			ImGuiSafe.TextColoredSafe(grey, $"({_asset.GetType().Name})");

			if (ImGui.IsItemHovered())
			{
				ImGui.BeginTooltip();
				ImGuiSafe.TextSafe($"Asset type id: {_typeId}");
				ImGuiSafe.TextSafe($"CLR type:      {_asset.GetType().FullName}");
				ImGuiSafe.TextSafe($"GUID:          {_asset.SourceGuid}");
				ImGuiSafe.TextSafe($"Path:          {_path}");
				ImGuiSafe.TextSafe(string.Empty);
				ImGuiSafe.TextSafe("The id — not the class name — is what .vasset files store,");
				ImGuiSafe.TextSafe("so renaming the class keeps every reference working.");
				ImGui.EndTooltip();
			}

			ImGuiSafe.TextColoredSafe(grey, "Shared instance — edits apply everywhere this asset is used.");
		}

		private void DrawStatus()
		{
			if (_status == null)
				return;

			if (ImGui.GetTime() > _statusClearAt)
			{
				_status = null;
				return;
			}

			ImGui.Separator();
			ImGuiSafe.TextColoredSafe(new Num.Vector4(0.5f, 0.9f, 0.5f, 1f), _status);
		}

		private void SetStatus(string message)
		{
			_status = message;
			_statusClearAt = ImGui.GetTime() + 3.0;
		}

		private void Save()
		{
			_dirty = false;

			if (_asset == null || string.IsNullOrEmpty(_path))
				return;

			try
			{
				DataAssetIO.Save(_asset, _path);
				SetStatus($"Saved {Path.GetFileName(_path)}.");
			}
			catch (Exception ex)
			{
				EditorDebug.Log($"DataAssetWindow: failed to save '{_path}': {ex.Message}", "DataAsset");
				_status = $"Save failed: {ex.Message}";
				_statusClearAt = ImGui.GetTime() + 8.0;
			}
		}
	}
}
