using System;
using Voltage;
using Voltage.Editor.Utils;

namespace Voltage.Editor.Persistence
{
        /// <summary>
        /// Tracks and persists the last opened scene path across sessions using EditorSettingsLoader.
        /// Paths are stored with forward slashes for cross-platform portability.
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
                /// Saves the current scene path, normalizing separators for cross-platform portability.
                /// </summary>
                public static void SetLastScenePath(string scenePath)
                {
                        if (string.IsNullOrWhiteSpace(scenePath))
                        {
                                Clear();
                                return;
                        }

                        // Normalize to native OS separators before persisting
                        EditorSettingsLoader.SaveSetting(SettingsKey, CrossPlatformPath.Normalize(scenePath));
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