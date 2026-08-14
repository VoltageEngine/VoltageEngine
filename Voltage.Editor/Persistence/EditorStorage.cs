using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Voltage.Editor.Persistence
{
	/// <summary>
	/// Editor settings, layouts, logs and plugin cache, kept in OS user storage so deleting the
	/// engine checkout does not take them with it. VOLTAGE_EDITOR_DATA relocates both roots.
	/// </summary>
	public static class EditorStorage
	{
		public const string OverrideEnvVar = "VOLTAGE_EDITOR_DATA";

		private const string VendorFolderName = "VoltageEngine";
		private const string EditorFolderName = "Editor";

		private static readonly object _initLock = new();
		private static bool _initialized;

		private static readonly Lazy<string> _root = new(() => EnsureDirectory(ResolveRoot()));
		private static readonly Lazy<string> _cacheRoot = new(() => EnsureDirectory(ResolveCacheRoot()));
		private static readonly Lazy<string> _layouts = new(() => EnsureDirectory(Path.Combine(Root, "Layouts")));
		private static readonly Lazy<string> _logs = new(() => EnsureDirectory(Path.Combine(CacheRoot, "Logs")));
		private static readonly Lazy<string> _pluginCache = new(() => EnsureDirectory(Path.Combine(CacheRoot, "PluginCache")));

		/// <summary>Settings and layouts: worth keeping, so roaming storage.</summary>
		public static string Root => _root.Value;

		/// <summary>Plugin downloads and logs: regenerable, so local storage.</summary>
		public static string CacheRoot => _cacheRoot.Value;

		public static string SettingsFile => Path.Combine(Root, "Settings.json");

		public static string LayoutsDirectory => _layouts.Value;

		public static string LogsDirectory => _logs.Value;

		public static string PluginCacheDirectory => _pluginCache.Value;

		/// <summary>
		/// Idempotent, and safe from a static constructor: anything touching storage before Main
		/// still sees migrated data.
		/// </summary>
		public static void Initialize()
		{
			if (_initialized)
				return;

			lock (_initLock)
			{
				if (_initialized)
					return;

				_initialized = true;

				try
				{
					MigrateLegacyData();
				}
				catch (Exception ex)
				{
					// Starting from defaults beats not starting; the originals are untouched.
					Debug.Warn($"Could not migrate editor data to {Root}: {ex.Message}");
				}
			}
		}

		/// <summary>Walks up from the binary to the csproj; its own directory for a published editor.</summary>
		public static string FindEditorDirectory()
		{
			var di = new DirectoryInfo(AppContext.BaseDirectory);
			while (di != null)
			{
				if (File.Exists(Path.Combine(di.FullName, "Voltage.Editor.csproj")))
					return di.FullName;
				di = di.Parent;
			}

			return AppContext.BaseDirectory;
		}

		/// <summary>Fills gaps only: an existing file wins, and the original is never deleted.</summary>
		private static void MigrateLegacyData()
		{
			var editorDir = FindEditorDirectory();

			var legacySettings = Path.Combine(editorDir, "Content", "Voltage", "User", "Settings.json");
			CopyIfMissing(legacySettings, SettingsFile);

			var legacyLayouts = Path.Combine(editorDir, "Content", "Voltage", "Layouts");
			if (Directory.Exists(legacyLayouts))
			{
				foreach (var file in Directory.GetFiles(legacyLayouts, "*.ini"))
					CopyIfMissing(file, Path.Combine(LayoutsDirectory, Path.GetFileName(file)));
			}

			var legacyImGuiLayout = FindLegacyImGuiLayout();
			if (legacyImGuiLayout != null)
				CopyIfMissing(legacyImGuiLayout, Path.Combine(LayoutsDirectory, "imgui_layout.ini"));

			CopyIfMissing(
				Path.Combine(Voltage.Utils.Storage.GetStorageRoot(), "KeyValueData.bin"),
				Path.Combine(Root, "KeyValueData.bin"));
		}

		/// <summary>
		/// Earlier builds wrote this under the game save root, in a folder named after the entry
		/// assembly - a name that has since changed, so GetStorageRoot() no longer points at it.
		/// Every sibling is checked, newest wins.
		/// </summary>
		private static string FindLegacyImGuiLayout()
		{
			var saveRoot = Voltage.Utils.Storage.GetStorageRoot();
			var candidates = new System.Collections.Generic.List<string>
			{
				Path.Combine(saveRoot, "EditorLayouts", "imgui_layout.ini")
			};

			var siblingRoot = Path.GetDirectoryName(saveRoot);
			if (siblingRoot != null && Directory.Exists(siblingRoot))
			{
				foreach (var sibling in Directory.GetDirectories(siblingRoot))
					candidates.Add(Path.Combine(sibling, "EditorLayouts", "imgui_layout.ini"));
			}

			string newest = null;
			var newestTime = DateTime.MinValue;

			foreach (var candidate in candidates)
			{
				if (!File.Exists(candidate))
					continue;

				var written = File.GetLastWriteTimeUtc(candidate);
				if (written > newestTime)
				{
					newestTime = written;
					newest = candidate;
				}
			}

			return newest;
		}

		private static void CopyIfMissing(string source, string destination)
		{
			if (!File.Exists(source) || File.Exists(destination))
				return;

			EnsureDirectory(Path.GetDirectoryName(destination));
			File.Copy(source, destination);
			Debug.Log($"Moved editor data out of the engine folder: {Path.GetFileName(destination)} -> {destination}");
		}

		private static string ResolveRoot()
		{
			var overridden = OverrideRoot();
			if (overridden != null)
				return Path.Combine(overridden, "Data");

			if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
				return Path.Combine(HomeRelative("Library/Application Support"), VendorFolderName, EditorFolderName);

			if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
				return Path.Combine(XdgOrHome("XDG_CONFIG_HOME", ".config"), VendorFolderName, EditorFolderName);

			return Path.Combine(
				Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
				VendorFolderName, EditorFolderName);
		}

		private static string ResolveCacheRoot()
		{
			var overridden = OverrideRoot();
			if (overridden != null)
				return Path.Combine(overridden, "Cache");

			if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
				return Path.Combine(HomeRelative("Library/Caches"), VendorFolderName, EditorFolderName);

			if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
				return Path.Combine(XdgOrHome("XDG_CACHE_HOME", ".cache"), VendorFolderName, EditorFolderName);

			return Path.Combine(
				Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
				VendorFolderName, EditorFolderName);
		}

		private static string OverrideRoot()
		{
			var value = Environment.GetEnvironmentVariable(OverrideEnvVar);
			return string.IsNullOrWhiteSpace(value) ? null : value;
		}

		private static string HomeRelative(string relative)
		{
			var home = Environment.GetEnvironmentVariable("HOME");
			if (string.IsNullOrEmpty(home))
				home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

			return Path.Combine(home, relative);
		}

		private static string XdgOrHome(string xdgVariable, string fallbackRelative)
		{
			var xdg = Environment.GetEnvironmentVariable(xdgVariable);
			return !string.IsNullOrWhiteSpace(xdg) ? xdg : HomeRelative(fallbackRelative);
		}

		private static string EnsureDirectory(string path)
		{
			if (!string.IsNullOrEmpty(path))
				Directory.CreateDirectory(path);

			return path;
		}
	}
}
