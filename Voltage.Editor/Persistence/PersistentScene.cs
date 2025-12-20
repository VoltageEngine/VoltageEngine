using System;
using Voltage;

namespace Voltage.Editor.Persistence
{
	/// <summary>
	/// Tracks and persists the last opened scene path across sessions using EditorSettingsLoader.
	/// </summary>
	public static class PersistentScene
	{
		private const string SettingsKey = "PersistentScene";
		private const string DefaultScenePath = "";

		/// <summary>
		/// Gets the last opened scene path (can be absolute or relative to the Scenes folder).
		/// </summary>
		public static string GetLastScenePath()
		{
			return EditorSettingsLoader.LoadSetting(SettingsKey, DefaultScenePath);
		}

		/// <summary>
		/// Saves the current scene path.
		/// </summary>
		public static void SetLastScenePath(string scenePath)
		{
			if (string.IsNullOrWhiteSpace(scenePath))
			{
				Clear();
				return;
			}

			EditorSettingsLoader.SaveSetting(SettingsKey, scenePath);
		}

		/// <summary>
		/// Clears the stored scene reference.
		/// </summary>
		public static void Clear()
		{
			EditorSettingsLoader.SaveSetting(SettingsKey, DefaultScenePath);
		}
	}
}