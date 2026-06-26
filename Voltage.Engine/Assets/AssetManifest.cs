using System;
using System.Collections.Generic;
using System.IO;
using Voltage.Persistence;

namespace Voltage.Assets
{
	/// <summary>
	/// Runtime resolver for <c>asset GUID → file path</c> — the published-build counterpart to the
	/// editor's <c>AssetDatabase</c>.
	///
	/// <para>The editor maintains stable GUIDs for assets in <c>.meta</c> sidecars, but those are an
	/// editor-only system that does not ship. This class closes that gap: at build time the editor
	/// bakes the GUID→path map into <c>Data/assets.manifest</c> (which ships with the game), and at
	/// runtime this loads it so references stored by GUID (e.g. <c>Entity.OriginalPrefabGuid</c>)
	/// resolve to the current file — even after the asset was renamed or moved. The GUID never
	/// changes, so renaming an asset is transparent to everything that references it.</para>
	///
	/// <para>NativeAOT-safe: the manifest is plain data parsed with <see cref="JsonTokenReader"/> —
	/// no reflection. Loading is lazy and best-effort; if the manifest is absent (e.g. running from
	/// the editor, which resolves via the live AssetDatabase instead) lookups simply return false and
	/// callers fall back to name/path resolution.</para>
	/// </summary>
	public static class AssetManifest
	{
		// guid string ("D" form) → project-relative path (forward slashes).
		private static Dictionary<string, string> _map;
		private static string _baseDir;
		private static bool _loadAttempted;

		/// <summary>Path of the manifest relative to the game's base directory.</summary>
		public const string DefaultRelativePath = "Data/assets.manifest";

		/// <summary>True once a non-empty manifest has been loaded.</summary>
		public static bool IsLoaded => _map != null && _map.Count > 0;

		/// <summary>
		/// Loads the manifest from <c>&lt;BaseDirectory&gt;/Data/assets.manifest</c> on first use.
		/// Idempotent; safe to call from any resolution path.
		/// </summary>
		public static void EnsureLoaded()
		{
			if (_loadAttempted)
				return;
			var baseDir = AppContext.BaseDirectory;
			LoadFrom(Path.Combine(baseDir, "Data", "assets.manifest"), baseDir);
		}

		/// <summary>
		/// Explicitly loads the manifest from <paramref name="manifestPath"/>, resolving relative
		/// asset paths against <paramref name="baseDir"/> (defaults to the manifest's own directory's
		/// parent — i.e. the game base directory). Used at startup or by tooling/tests.
		/// </summary>
		public static void LoadFrom(string manifestPath, string baseDir = null)
		{
			_loadAttempted = true;
			_baseDir = baseDir ?? AppContext.BaseDirectory;

			if (string.IsNullOrEmpty(manifestPath) || !File.Exists(manifestPath))
			{
				_map = null;
				return;
			}

			try
			{
				var json = File.ReadAllText(manifestPath);
				var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
				using var r = new JsonTokenReader(json);
				if (r.BeginObject())
				{
					while (r.ReadNextKey(out var key))
						map[key] = r.ReadString();
				}
				_map = map;
			}
			catch (Exception ex)
			{
				Debug.Warn($"[AssetManifest] Failed to load '{manifestPath}': {ex.Message}");
				_map = null;
			}
		}

		/// <summary>
		/// Resolves an asset GUID to its absolute file path via the manifest, or returns <c>false</c>
		/// when the manifest is absent, the GUID is unknown, or the file no longer exists.
		/// </summary>
		public static bool TryGetAbsolutePath(Guid guid, out string absolutePath)
		{
			absolutePath = null;
			if (guid == Guid.Empty)
				return false;

			EnsureLoaded();
			if (_map == null || !_map.TryGetValue(guid.ToString(), out var rel) || string.IsNullOrEmpty(rel))
				return false;

			var baseDir = _baseDir ?? AppContext.BaseDirectory;
			absolutePath = Path.Combine(baseDir, rel.Replace('/', Path.DirectorySeparatorChar));
			return File.Exists(absolutePath);
		}
	}
}
