using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Voltage.Editor.Persistence;
using Voltage.Editor.Utils;
using Voltage.Utils;

namespace Voltage.Editor.EditorStyling
{
	/// <summary>
	/// Manages ImGui theme selection and persistence.
	/// </summary>
	public class ThemeManager
	{
		private readonly PersistentString _lastSelectedTheme;
		private readonly Dictionary<string, MethodInfo> _availableThemes;
		private string _currentThemeName;

		public ThemeManager()
		{
			_lastSelectedTheme = new PersistentString("ImGui_LastSelectedTheme", "");
			_availableThemes = new Dictionary<string, MethodInfo>(StringComparer.OrdinalIgnoreCase);
			
			LoadAvailableThemes();
			
			// Apply last selected theme or default
			string lastTheme = _lastSelectedTheme.Value;
			if (string.IsNullOrWhiteSpace(lastTheme))
			{
				// First time - use default
				ApplyTheme("DarkTheme1");
			}
			else
			{
				ApplyTheme(lastTheme);
			}
		}

		/// <summary>
		/// Gets the name of the currently active theme.
		/// </summary>
		public string CurrentThemeName => _currentThemeName;

		/// <summary>
		/// Gets all available theme names.
		/// </summary>
		public IReadOnlyList<string> GetAvailableThemes() => _availableThemes.Keys.ToList();

		/// <summary>
		/// Loads all available theme methods from VoltageEditorThemes class.
		/// </summary>
		private void LoadAvailableThemes()
		{
			var themeMethods = typeof(VoltageEditorThemes).GetMethods(
				BindingFlags.Static | BindingFlags.Public);

			foreach (var method in themeMethods)
			{
				// Only include methods with no parameters (theme methods)
				if (method.GetParameters().Length == 0)
				{
					_availableThemes[method.Name] = method;
				}
			}

			Debug.Log($"Loaded {_availableThemes.Count} available themes");
		}

		/// <summary>
		/// Applies a theme by name.
		/// </summary>
		/// <param name="themeName">Name of the theme to apply</param>
		/// <returns>True if the theme was applied successfully</returns>
		public bool ApplyTheme(string themeName)
		{
			if (string.IsNullOrWhiteSpace(themeName))
			{
				Debug.Warn("Cannot apply theme with empty name");
				return false;
			}

			if (_availableThemes.TryGetValue(themeName, out var themeMethod))
			{
				try
				{
					themeMethod.Invoke(null, null);
					_currentThemeName = themeName;
					_lastSelectedTheme.Value = themeName;
					Debug.Log($"Applied theme: {themeName}");
					return true;
				}
				catch (Exception ex)
				{
					Debug.Error($"Failed to apply theme '{themeName}': {ex.Message}");
					return false;
				}
			}
			else
			{
				Debug.Warn($"Theme '{themeName}' not found");
				return false;
			}
		}

		/// <summary>
		/// Checks if a theme exists.
		/// </summary>
		public bool ThemeExists(string themeName)
		{
			return _availableThemes.ContainsKey(themeName);
		}

		/// <summary>
		/// Resets to the default theme.
		/// </summary>
		public void ResetToDefault()
		{
			ApplyTheme("DarkTheme1");
		}
	}
}