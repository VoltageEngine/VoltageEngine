using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ImGuiNET;
using Voltage.Editor.Utils;

namespace Voltage.Editor.Styling
{
    public class LayoutManager
    {
	    /// <summary>
	    /// Filename pointer for the layout output path
	    /// </summary>
	    public byte[] LayoutInitPathUtf8;

		public bool HasPendingReload => _pendingLayoutReload;

	    private bool _pendingLayoutReload = false;
	    private string _pendingLayoutContent = null;
		private readonly string _layoutsDirectory;
        private readonly string _defaultLayoutPath;
        private readonly string _defaultContentLayoutPath;
        private List<string> _availableLayouts = new();
        private string _currentLayoutName = "Default";


        public LayoutManager(string defaultLayoutPath)
        {
            _defaultLayoutPath = defaultLayoutPath;
            
            var projectDir = FindProjectDir();
            _layoutsDirectory = Path.Combine(projectDir, "Content", "Voltage", "Layouts");
            _defaultContentLayoutPath = Path.Combine(projectDir, "DefaultContent", "Layouts", "DefaultLayout.ini");
            
            Directory.CreateDirectory(_layoutsDirectory);
            
            InitializeDefaultLayout();
            CreateCustomLayoutIfNeeded();
            SetDefaultContentLayoutReadOnly();
            RefreshLayoutList();
        }

        /// <summary>
        /// Gets the name of the currently active layout
        /// </summary>
        public string CurrentLayoutName => _currentLayoutName;

        /// <summary>
        /// Creates a "Custom" layout if no user-defined layouts exist in Content/Voltage/Layouts
        /// </summary>
        private void CreateCustomLayoutIfNeeded()
        {
            try
            {
                // Check if any user-defined layouts exist
                if (Directory.Exists(_layoutsDirectory))
                {
                    var existingLayouts = Directory.GetFiles(_layoutsDirectory, "*.ini");
                    
                    // If no layouts exist, create a "Custom" layout from the default
                    if (existingLayouts.Length == 0 && File.Exists(_defaultContentLayoutPath))
                    {
                        string customLayoutPath = Path.Combine(_layoutsDirectory, "Custom.ini");
                        File.Copy(_defaultContentLayoutPath, customLayoutPath, overwrite: false);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.Error($"Failed to create Custom layout: {ex.Message}");
            }
        }

        /// <summary>
        /// Sets the DefaultContent layout file to read-only to prevent accidental modification
        /// </summary>
        private void SetDefaultContentLayoutReadOnly()
        {
            try
            {
                if (File.Exists(_defaultContentLayoutPath))
                {
                    var fileInfo = new FileInfo(_defaultContentLayoutPath);
                    if (!fileInfo.IsReadOnly)
                    {
                        fileInfo.IsReadOnly = true;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.Warn($"Could not set DefaultLayout.ini to read-only: {ex.Message}");
            }
        }

        /// <summary>
        /// Initializes the default layout from DefaultContent if needed
        /// </summary>
        private void InitializeDefaultLayout()
        {
            try
            {
                if (File.Exists(_defaultContentLayoutPath))
                {
                    if (!File.Exists(_defaultLayoutPath))
                    {
                        File.Copy(_defaultContentLayoutPath, _defaultLayoutPath, overwrite: false);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.Error($"Failed to initialize default layout: {ex.Message}");
            }
        }

        /// <summary>
        /// Finds the Voltage.Editor project directory
        /// </summary>
        private static string FindProjectDir()
        {
            var dir = AppContext.BaseDirectory;
            var di = new DirectoryInfo(dir);
            while (di != null)
            {
                if (File.Exists(Path.Combine(di.FullName, "Voltage.Editor.csproj")))
                    return di.FullName;
                di = di.Parent;
            }

            return AppContext.BaseDirectory;
        }

        /// <summary>
        /// Refreshes the list of available layouts from disk
        /// </summary>
        public void RefreshLayoutList()
        {
            _availableLayouts.Clear();	
            _availableLayouts.Add("Default");
            
            if (Directory.Exists(_layoutsDirectory))
            {
                var layoutFiles = Directory.GetFiles(_layoutsDirectory, "*.ini")
                    .Select(Path.GetFileNameWithoutExtension)
                    .OrderBy(name => name);
                
                _availableLayouts.AddRange(layoutFiles);
            }
        }

        /// <summary>
        /// Gets the list of available layout names
        /// </summary>
        public IReadOnlyList<string> GetLayoutNames() => _availableLayouts;

        /// <summary>
        /// Saves the current ImGui layout with a given name
        /// </summary>
        public void SaveLayout(string layoutName)
        {
            if (string.IsNullOrWhiteSpace(layoutName))
            {
	            NotificationSystem.ShowTimedNotification("Cannot save layout with empty name");
                return;
            }

            // Prevent saving to "Default" layout
            if (layoutName.Equals("Default", StringComparison.OrdinalIgnoreCase))
            {
                NotificationSystem.ShowTimedNotification("Cannot save changes to the Default layout. Create a new layout instead.");
                return;
            }

            try
            {
                string targetPath = Path.Combine(_layoutsDirectory, $"{layoutName}.ini");

                ImGui.SaveIniSettingsToDisk(_defaultLayoutPath);
                
                // Read the content and write to target (not File.Copy to avoid handle issues)
                string layoutContent = File.ReadAllText(_defaultLayoutPath);
                File.WriteAllText(targetPath, layoutContent);

                _currentLayoutName = layoutName;

                RefreshLayoutList();
            }
            catch (Exception ex)
            {
                Debug.Error($"Failed to save layout '{layoutName}': {ex.Message}");
            }
        }

        /// <summary>
        /// Applies the pending layout reload (call this early in frame, before any windows are created)
        /// </summary>
        public void ApplyPendingReload()
        {
            if (_pendingLayoutReload && _pendingLayoutContent != null)
            {
                try
                {
                    // Apply the layout NOW, before any windows are created this frame
                    ImGui.LoadIniSettingsFromMemory(_pendingLayoutContent);
                    
                    _pendingLayoutReload = false;
                    _pendingLayoutContent = null;
                }
                catch (Exception ex)
                {
                    Debug.Error($"Failed to apply pending layout reload: {ex.Message}");
                    _pendingLayoutReload = false;
                    _pendingLayoutContent = null;
                }
            }
        }

        /// <summary>
        /// Loads a saved layout by name
        /// </summary>
        public void LoadLayout(string layoutName)
        {
            if (string.IsNullOrWhiteSpace(layoutName))
            {
	            NotificationSystem.ShowTimedNotification("Cannot load layout with empty name");
                return;
            }

            try
            {
                string sourcePath;
                
                if (layoutName.Equals("Default", StringComparison.OrdinalIgnoreCase))
                {
                    if (File.Exists(_defaultContentLayoutPath))
                    {
                        sourcePath = _defaultContentLayoutPath;
                    }
                    else
                    {
                        Debug.Warn("Default layout file not found");
                        return;
                    }
                }
                else
                {
                    sourcePath = Path.Combine(_layoutsDirectory, $"{layoutName}.ini");
                }

                if (!File.Exists(sourcePath))
                {
                    Debug.Warn($"Layout file not found: {sourcePath}");
                    return;
                }

                string layoutContent = File.ReadAllText(sourcePath);

                // Schedule layout reload for NEXT FRAME (before windows are created)
                _pendingLayoutContent = layoutContent;
                _pendingLayoutReload = true;

                // Also write to disk for persistence
                File.WriteAllText(_defaultLayoutPath, layoutContent);

                _currentLayoutName = layoutName;
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
            if (string.IsNullOrWhiteSpace(layoutName))
            {
                Debug.Warn("Cannot delete layout with empty name");
                return;
            }

            if (layoutName.Equals("Default", StringComparison.OrdinalIgnoreCase))
            {
                Debug.Warn("Cannot delete the Default layout");
                return;
            }

            try
            {
                var layoutPath = Path.Combine(_layoutsDirectory, $"{layoutName}.ini");
                
                if (File.Exists(layoutPath))
                {
                    File.Delete(layoutPath);
                    Debug.Success($"Layout '{layoutName}' deleted successfully");
                    RefreshLayoutList();
                }
                else
                {
                    Debug.Warn($"Layout '{layoutName}' not found");
                }
            }
            catch (Exception ex)
            {
                Debug.Error($"Failed to delete layout '{layoutName}': {ex.Message}");
            }
        }

        /// <summary>
        /// Gets the full path to a layout file
        /// </summary>
        public string GetLayoutPath(string layoutName)
        {
            if (layoutName.Equals("Default", StringComparison.OrdinalIgnoreCase))
            {
                return _defaultLayoutPath;
            }
            
            return Path.Combine(_layoutsDirectory, $"{layoutName}.ini");
        }

        /// <summary>
        /// Saves the current layout only if it's not the Default layout
        /// </summary>
        public void AutoSaveCurrentLayout()
        {
            if (!_currentLayoutName.Equals("Default", StringComparison.OrdinalIgnoreCase))
            {
                SaveLayout(_currentLayoutName);
            }
        }
    }
}