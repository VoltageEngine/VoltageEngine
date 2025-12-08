using System;
using System.Collections.Generic;
using System.IO;
using ImGuiNET;
using Voltage.Utils;

namespace Voltage.Editor.Layouts
{
	public class LayoutManager
	{
		private readonly string _layoutsDirectory;
		private readonly string _currentLayoutPath;
		private readonly Dictionary<string, string> _savedLayouts = new();
		private bool _isFirstLoad = true;

		public LayoutManager(string defaultLayoutPath)
		{
			_layoutsDirectory = Path.GetDirectoryName(defaultLayoutPath);
			_currentLayoutPath = defaultLayoutPath;
			
			Directory.CreateDirectory(_layoutsDirectory);
			
			RefreshLayoutList();
		}

		/// <summary>
		/// Refreshes the list of available layouts from disk
		/// </summary>
		public void RefreshLayoutList()
		{
			_savedLayouts.Clear();
			
			var layoutFiles = Directory.GetFiles(_layoutsDirectory, "*.ini");
			foreach (var file in layoutFiles)
			{
				var layoutName = Path.GetFileNameWithoutExtension(file);
				_savedLayouts[layoutName] = file;
			}
		}

		/// <summary>
		/// Gets all available layout names
		/// </summary>
		public IEnumerable<string> GetLayoutNames() => _savedLayouts.Keys;

		/// <summary>
		/// Saves the current layout with a given name
		/// </summary>
		public void SaveLayout(string layoutName)
		{
			try
			{
				// Force ImGui to save current settings to the current ini file
				ImGui.SaveIniSettingsToDisk(_currentLayoutPath);
				
				var newLayoutPath = Path.Combine(_layoutsDirectory, $"{layoutName}.ini");
				
				// Copy current ini file to new layout file
				if (File.Exists(_currentLayoutPath))
				{
					File.Copy(_currentLayoutPath, newLayoutPath, overwrite: true);
					_savedLayouts[layoutName] = newLayoutPath;
					
					Debug.Log($"Layout '{layoutName}' saved successfully");
				}
				else
				{
					Debug.Error($"Current ini file not found: {_currentLayoutPath}");
				}
			}
			catch (Exception ex)
			{
				Debug.Error($"Failed to save layout '{layoutName}': {ex.Message}");
			}
		}

		/// <summary>
		/// Loads a saved layout by name
		/// </summary>
		public void LoadLayout(string layoutName)
		{
			try
			{
				if (!_savedLayouts.TryGetValue(layoutName, out var layoutPath))
				{
					Debug.Warn($"Layout '{layoutName}' not found");
					return;
				}

				if (!File.Exists(layoutPath))
				{
					Debug.Error($"Layout file not found: {layoutPath}");
					return;
				}

				// Copy the saved layout to the current ini file
				File.Copy(layoutPath, _currentLayoutPath, overwrite: true);
				
				// Tell ImGui to reload settings from disk
				ImGui.LoadIniSettingsFromDisk(_currentLayoutPath);
				
				Debug.Log($"Layout '{layoutName}' loaded successfully");
			}
			catch (Exception ex)
			{
				Debug.Error($"Failed to load layout '{layoutName}': {ex.Message}");
			}
		}

		/// <summary>
		/// Deletes a saved layout
		/// </summary>
		public void DeleteLayout(string layoutName)
		{
			try
			{
				if (layoutName == "Default")
				{
					Debug.Warn("Cannot delete the Default layout");
					return;
				}

				if (!_savedLayouts.TryGetValue(layoutName, out var layoutPath))
				{
					Debug.Warn($"Layout '{layoutName}' not found");
					return;
				}

				if (File.Exists(layoutPath))
				{
					File.Delete(layoutPath);
					_savedLayouts.Remove(layoutName);
					
					Debug.Log($"Layout '{layoutName}' deleted successfully");
				}
			}
			catch (Exception ex)
			{
				Debug.Error($"Failed to delete layout '{layoutName}': {ex.Message}");
			}
		}

		/// <summary>
		/// Resets to the default layout
		/// </summary>
		public void ResetToDefault()
		{
			var defaultLayoutPath = Path.Combine(_layoutsDirectory, "Default.ini");
			
			if (File.Exists(defaultLayoutPath))
			{
				LoadLayout("Default");
			}
			else
			{
				// If no default exists, create a fresh one by deleting current
				if (File.Exists(_currentLayoutPath))
				{
					File.Delete(_currentLayoutPath);
				}
				
				Debug.Log("Reset to factory default layout");
			}
		}
	}
}