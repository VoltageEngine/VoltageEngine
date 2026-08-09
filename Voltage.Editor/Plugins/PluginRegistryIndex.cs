using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Voltage.Editor.DebugUtils;
using Voltage.Persistence;

namespace Voltage.Editor.Plugins
{
	/// <summary>One plugin advertised by a registry index.</summary>
	public class PluginRegistryEntry
	{
		public string Id;
		public string Name;
		public string Description;

		/// <summary>Git clone URL.</summary>
		public string Git;

		/// <summary>Tag or branch to install by default. Resolved to a commit SHA in plugins.lock.json.</summary>
		public string Ref;

		public string Author;

		/// <summary>Free-form tags for filtering ("physics", "dialogue", "tools").</summary>
		public List<string> Tags = new();

		/// <summary>Engine range the listing claims support for; informational until the plugin is resolved.</summary>
		public string EngineVersion = "*";

		public string Homepage;

		/// <summary>
		/// Zip URL to install from, preferred over <see cref="Git"/> when set.
		/// </summary>
		public string Zip;

		/// <summary>Which registry this listing came from. Set by the fetcher, not by the document.</summary>
		public string RegistryName;

		/// <summary>True when this listing can actually be installed.</summary>
		public bool IsInstallable => !string.IsNullOrWhiteSpace(Zip) || !string.IsNullOrWhiteSpace(Git);

		/// <summary>
		/// What pressing Install would actually fetch, as a short label. A registry listing carries no
		/// version field of its own - the version is whatever the source points at - so this reports the
		/// source rather than guessing: the ref if the listing pins one, otherwise the tag embedded in a
		/// release zip URL, otherwise an honest "default branch".
		/// </summary>
		public string VersionLabel
		{
			get
			{
				if (!string.IsNullOrWhiteSpace(Ref))
					return Ref.Trim();

				var fromZip = VersionFromZipUrl(Zip);
				if (fromZip != null)
					return fromZip;

				if (!string.IsNullOrWhiteSpace(Zip))
					return "unversioned zip";

				return !string.IsNullOrWhiteSpace(Git) ? "default branch" : "no source";
			}
		}

		/// <summary>
		/// Pulls the tag out of a GitHub release asset URL (".../releases/download/&lt;tag&gt;/&lt;file&gt;"),
		/// falling back to a version-looking suffix on the file name ("voltage.farseer-1.2.0.zip").
		/// Returns null when the URL says nothing about a version.
		/// </summary>
		private static string VersionFromZipUrl(string zip)
		{
			if (string.IsNullOrWhiteSpace(zip))
				return null;

			var url = zip.Trim();

			const string marker = "/releases/download/";
			var at = url.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
			if (at >= 0)
			{
				var rest = url.Substring(at + marker.Length);
				var slash = rest.IndexOf('/');
				var tag = slash >= 0 ? rest.Substring(0, slash) : rest;
				if (tag.Length > 0)
					return tag;
			}

			var name = url.Substring(url.LastIndexOf('/') + 1);
			if (!name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
				return null;

			name = name.Substring(0, name.Length - 4);
			var dash = name.LastIndexOf('-');
			if (dash < 0 || dash + 1 >= name.Length)
				return null;

			var candidate = name.Substring(dash + 1);
			return char.IsDigit(candidate[0]) ? candidate : null;
		}

		public bool Matches(string search)
		{
			if (string.IsNullOrWhiteSpace(search))
				return true;

			search = search.Trim();
			return Contains(Id, search) || Contains(Name, search) || Contains(Description, search)
				|| Contains(Author, search) || (Tags?.Any(t => Contains(t, search)) ?? false);
		}

		private static bool Contains(string haystack, string needle) =>
			haystack != null && haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
	}

	/// <summary>The document a registry URL serves.</summary>
	public class PluginRegistryDocument
	{
		public int SchemaVersion = 1;
		public string Name;
		public List<PluginRegistryEntry> Plugins = new();
	}

	/// <summary>
	/// Browsable catalogue of installable plugins.
	/// </summary>
	public static class PluginRegistryIndex
	{
		/// <summary>The official catalogue, always first so its listings win an id collision.</summary>
		public const string DefaultRegistryUrl =
			"https://raw.githubusercontent.com/VoltageEngine/plugin-registry/main/registry.json";

		private static readonly object _lock = new();
		private static List<PluginRegistryEntry> _entries = new();
		private static Task _inFlight;

		/// <summary>
		/// Registries to aggregate, in priority order.
		/// </summary>
		public static List<string> RegistryUrls { get; } = new() { DefaultRegistryUrl };

		/// <summary>Null while never fetched or fetching; a user-facing message when the last fetch failed.</summary>
		public static string LastError { get; private set; }

		public static bool IsFetching
		{
			get { lock (_lock) return _inFlight is { IsCompleted: false }; }
		}

		/// <summary>True once entries are available, whether from the network or the on-disk cache.</summary>
		public static bool HasEntries
		{
			get { lock (_lock) return _entries.Count > 0; }
		}

		public static IReadOnlyList<PluginRegistryEntry> Entries
		{
			get { lock (_lock) return _entries.ToList(); }
		}

		/// <summary>Entries matching <paramref name="search"/>, minus anything already in the project.</summary>
		public static IReadOnlyList<PluginRegistryEntry> Search(string search, IEnumerable<string> installedIds)
		{
			var installed = new HashSet<string>(installedIds ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);

			lock (_lock)
			{
				return _entries
					.Where(e => !string.IsNullOrEmpty(e.Id) && !installed.Contains(e.Id) && e.Matches(search))
					.OrderBy(e => e.Name ?? e.Id, StringComparer.OrdinalIgnoreCase)
					.ToList();
			}
		}

		/// <summary>
		/// Starts a fetch if one is not already running.
		/// </summary>
		public static void RefreshAsync()
		{
			lock (_lock)
			{
				if (_inFlight is { IsCompleted: false })
					return;

				_inFlight = Task.Run(FetchAsync);
			}
		}

		/// <summary>Loads the on-disk cache, so the catalogue is browsable offline and on first paint.</summary>
		public static void LoadCache()
		{
			try
			{
				var path = CachePath();
				if (!File.Exists(path))
					return;

				Apply(Json.FromJson<PluginRegistryDocument>(File.ReadAllText(path)), fromCache: true);
			}
			catch (Exception ex)
			{
				EditorDebug.Log($"PluginRegistry: could not read the cached index: {ex.Message}", "Plugins");
			}
		}

		private static async Task FetchAsync()
		{
			var merged = new List<PluginRegistryEntry>();
			var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			var failures = new List<string>();

			using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

			foreach (var url in RegistryUrls.ToList())
			{
				if (string.IsNullOrWhiteSpace(url))
					continue;

				try
				{
					using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(12));
					var json = await http.GetStringAsync(url, cts.Token).ConfigureAwait(false);
					var document = Json.FromJson<PluginRegistryDocument>(json);

					if (document?.Plugins == null)
						throw new InvalidOperationException("no plugin list in the document.");

					WarnOnNewerSchema(document);

					foreach (var plugin in document.Plugins)
					{
						if (plugin == null || string.IsNullOrEmpty(plugin.Id) || !seen.Add(plugin.Id))
							continue;

						plugin.RegistryName = document.Name ?? url;
						merged.Add(plugin);
					}

					if (url == DefaultRegistryUrl)
						TryWriteCache(json);
				}
				catch (Exception ex)
				{
					failures.Add($"{url} ({ex.Message})");
				}
			}

			if (merged.Count > 0)
			{
				lock (_lock)
					_entries = merged;

				EditorDebug.Log($"PluginRegistry: {merged.Count} plugin(s) available.", "Plugins");
			}

			LastError = failures.Count == 0
				? null
				: $"Could not reach {failures.Count} registry/registries: {string.Join("; ", failures)}.";

			if (LastError != null)
				EditorDebug.Log($"PluginRegistry: {LastError}", "Plugins");
		}

		private static void WarnOnNewerSchema(PluginRegistryDocument document)
		{
			if (document.SchemaVersion > 1)
			{
				EditorDebug.Log(
					$"PluginRegistry: '{document.Name}' declares schema {document.SchemaVersion}, newer than " +
					"this editor understands. Unknown fields are ignored.", "Plugins");
			}
		}

		private static void Apply(PluginRegistryDocument document, bool fromCache)
		{
			if (document?.Plugins == null)
				return;

			WarnOnNewerSchema(document);

			lock (_lock)
			{
				_entries = document.Plugins
					.Where(p => p != null && !string.IsNullOrEmpty(p.Id))
					.Select(p => { p.RegistryName ??= document.Name; return p; })
					.ToList();
			}
		}

		private static void TryWriteCache(string json)
		{
			try
			{
				var path = CachePath();
				Directory.CreateDirectory(Path.GetDirectoryName(path)!);
				File.WriteAllText(path, json);
			}
			catch (Exception ex)
			{
				EditorDebug.Log($"PluginRegistry: could not cache the index: {ex.Message}", "Plugins");
			}
		}

		private static string CachePath() => Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
			"Voltage", "plugin-registry.json");
	}
}
