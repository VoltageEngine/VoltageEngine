using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ImGuiNET;
using Voltage.Editor.Utils;
using Voltage.Utils;

namespace Voltage.Editor.Layouts
{
    public class LayoutManager
    {
        private readonly string _layoutsDirectory;
        private readonly string _defaultLayoutPath;
        private readonly string _defaultContentLayoutPath;
        private List<string> _availableLayouts = new();
        private string _currentLayoutName = "Default";

        public LayoutManager(string defaultLayoutPath)
        {
            _defaultLayoutPath = defaultLayoutPath;
            
            // Use Content/Layouts directory in the Voltage.Editor project
            var projectDir = FindProjectDir();
            _layoutsDirectory = Path.Combine(projectDir, "Content", "Layouts");
            _defaultContentLayoutPath = Path.Combine(projectDir, "DefaultContent", "Layouts", "DefaultLayout.ini");
            
            Directory.CreateDirectory(_layoutsDirectory);
            
            // Initialize default layout if needed
            InitializeDefaultLayout();
            
            RefreshLayoutList();
        }

        /// <summary>
        /// Gets the name of the currently active layout
        /// </summary>
        public string CurrentLayoutName => _currentLayoutName;

        /// <summary>
        /// Initializes the default layout from DefaultContent if Content/Layouts is empty
        /// </summary>
        private void InitializeDefaultLayout()
        {
            try
            {
                // Check if Content/Layouts has any .ini files
                var existingLayouts = Directory.Exists(_layoutsDirectory) 
                    ? Directory.GetFiles(_layoutsDirectory, "*.ini") 
                    : Array.Empty<string>();

                // If no layouts exist and DefaultContent layout exists, copy it as the active layout
                if (existingLayouts.Length == 0 && File.Exists(_defaultContentLayoutPath))
                {
                    if (!File.Exists(_defaultLayoutPath))
                    {
                        File.Copy(_defaultContentLayoutPath, _defaultLayoutPath, overwrite: false);
                        Debug.Log($"Initialized default layout from: {_defaultContentLayoutPath}");
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

            // Fallback to base directory
            return AppContext.BaseDirectory;
        }

        /// <summary>
        /// Refreshes the list of available layouts from disk
        /// </summary>
        public void RefreshLayoutList()
        {
            _availableLayouts.Clear();
            
            // Always include "Default" as the first option
            _availableLayouts.Add("Default");
            
            // Add all .ini files from the Layouts directory
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
                Debug.Warn("Cannot save layout with empty name");
                return;
            }

            // Prevent saving to "Default" layout
            if (layoutName.Equals("Default", StringComparison.OrdinalIgnoreCase))
            {
                Debug.Warn("Cannot save changes to the Default layout. Create a new layout instead.");
                NotificationSystem.ShowTimedNotification("Cannot save to Default layout. Create a new layout instead.");
                return;
            }

            try
            {
                string targetPath = Path.Combine(_layoutsDirectory, $"{layoutName}.ini");

                // Force ImGui to save current state to its configured ini file
                ImGui.SaveIniSettingsToDisk(_defaultLayoutPath);
                
                // Copy the default layout to the target location
                File.Copy(_defaultLayoutPath, targetPath, overwrite: true);

                _currentLayoutName = layoutName;

                Debug.Log($"Layout '{layoutName}' saved successfully to: {targetPath}");
                RefreshLayoutList();
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
            if (string.IsNullOrWhiteSpace(layoutName))
            {
                Debug.Warn("Cannot load layout with empty name");
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

                // Read the layout content into memory first
                string layoutContent = File.ReadAllText(sourcePath);

                // Tell ImGui to clear its current settings and load from memory
                // This releases any file handles ImGui might have
                ImGui.LoadIniSettingsFromMemory(layoutContent);

                // Now save it to the default location for persistence
                // Use a small delay to ensure ImGui has released any handles
                System.Threading.Thread.Sleep(50);
                
                // Write directly instead of File.Copy to avoid handle conflicts
                File.WriteAllText(_defaultLayoutPath, layoutContent);

                _currentLayoutName = layoutName;

                Debug.Log($"Layout '{layoutName}' loaded successfully from: {sourcePath}");
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
                    Debug.Log($"Layout '{layoutName}' deleted successfully");
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
        /// Resets to the default layout from DefaultContent
        /// </summary>
        public void ResetToDefault()
        {
            try
            {
                if (File.Exists(_defaultContentLayoutPath))
                {
                    // Read content first
                    string defaultContent = File.ReadAllText(_defaultContentLayoutPath);
                    
                    // Load into ImGui memory
                    ImGui.LoadIniSettingsFromMemory(defaultContent);
                    
                    // Wait for ImGui to release handles
                    System.Threading.Thread.Sleep(50);
                    
                    // Write to active layout file
                    File.WriteAllText(_defaultLayoutPath, defaultContent);
                    
                    _currentLayoutName = "Default";
                    Debug.Log("Layout reset to DefaultContent default");
                }
                else
                {
                    // If DefaultContent layout doesn't exist, clear the ini file
                    if (File.Exists(_defaultLayoutPath))
                    {
                        File.Delete(_defaultLayoutPath);
                    }
                    
                    _currentLayoutName = "Default";
                    Debug.Log("Layout reset to ImGui defaults");
                }
            }
            catch (Exception ex)
            {
                Debug.Error($"Failed to reset layout: {ex.Message}");
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