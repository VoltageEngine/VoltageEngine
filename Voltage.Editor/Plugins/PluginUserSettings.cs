using System;
using System.IO;
using Voltage.Editor.Persistence;

namespace Voltage.Editor.Plugins
{
	/// <summary>
	/// Per-user, per-machine plugin settings — primarily the local install paths of external SDKs
	/// (FMOD, console SDKs) whose files cannot be redistributed. Stored via EditorSettingsLoader in
	/// the editor's user storage: never inside the project, never in source control.
	/// </summary>
	public static class PluginUserSettings
	{
		private const string SdkPathKeyPrefix = "PluginSdk_";

		/// <summary>
		/// Resolves the local root of an external SDK: the user-configured path first, the manifest's
		/// environment variable as fallback. Returns null when neither yields an existing directory.
		/// </summary>
		public static string ResolveSdkRoot(PluginExternalSdk sdk)
		{
			if (sdk == null || string.IsNullOrWhiteSpace(sdk.Id))
				return null;

			var configured = EditorSettingsLoader.LoadSetting(SdkPathKeyPrefix + sdk.Id, "");
			if (!string.IsNullOrWhiteSpace(configured) && Directory.Exists(configured))
				return configured;

			if (!string.IsNullOrWhiteSpace(sdk.EnvVar))
			{
				var fromEnv = Environment.GetEnvironmentVariable(sdk.EnvVar);
				if (!string.IsNullOrWhiteSpace(fromEnv) && Directory.Exists(fromEnv))
					return fromEnv;
			}

			return null;
		}

		/// <summary>The raw user-configured path (may be empty/nonexistent) for settings UI display.</summary>
		public static string GetConfiguredSdkPath(string sdkId)
		{
			return EditorSettingsLoader.LoadSetting(SdkPathKeyPrefix + sdkId, "");
		}

		public static void SetSdkPath(string sdkId, string path)
		{
			EditorSettingsLoader.SaveSetting(SdkPathKeyPrefix + sdkId, path ?? "");
		}
	}
}
