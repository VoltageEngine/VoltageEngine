using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Voltage.Editor.DebugUtils;
using Voltage.Editor.ProjectFile;
using Voltage.Editor.Scripting;

namespace Voltage.Editor.Plugins
{
	/// <summary>Lifecycle state of one plugins.json entry within the current editor session.</summary>
	public enum PluginState
	{
		/// <summary>Listed in plugins.json with Disabled=true — not synced, not loaded.</summary>
		Disabled,

		/// <summary>Payload resolved, verified, and synced into PluginLibs.</summary>
		Restored,

		/// <summary>Restored and its assemblies are loaded into the editor.</summary>
		Loaded,

		/// <summary>Could not be acquired/verified (missing source, no repo access, SDK not configured…). The project still opens.</summary>
		Unavailable,

		/// <summary>Restored but loading/initializing its assemblies failed.</summary>
		Failed,
	}

	/// <summary>A plugins.json entry's live status: resolution result, payload location, and any error.</summary>
	public class PluginInstance
	{
		public ProjectPluginEntry Entry;

		/// <summary>Validated manifest. Null while <see cref="State"/> is Unavailable.</summary>
		public PluginManifest Manifest;

		public PluginState State;

		/// <summary>User-facing explanation when State is Unavailable or Failed.</summary>
		public string Error;

		/// <summary>The plugin's synced folder under the project's PluginLibs (null when not synced).</summary>
		public string PayloadPath;

		/// <summary>
		/// One public type name per managed assembly, auto-detected at load time when the manifest omits
		/// Gameplay.RootTypes. Used by the generated game bootstrap to root the assembly for AOT.
		/// </summary>
		public List<string> DetectedRootTypes = new();

		/// <summary>Resolution result kept for re-syncs within the session (dev plugins re-sync pre-build).</summary>
		internal ResolvedPlugin Resolved;

		public string Id => Entry?.Id ?? Manifest?.Id;
		public string DisplayName => Manifest?.Name ?? Id;

		/// <summary>Manifest root types when declared, otherwise the ones detected from the loaded assemblies.</summary>
		public IReadOnlyList<string> EffectiveRootTypes =>
			Manifest?.Gameplay?.RootTypes is { Count: > 0 } declared ? declared : DetectedRootTypes;
	}

	/// <summary>
	/// Orchestrates the project plugin system: on project open it restores every plugins.json entry
	/// (resolve → verify against plugins.lock.json → sync into PluginLibs), records per-plugin state for
	/// the Plugin Manager window, and exposes what the rest of the editor needs (payload paths, assembly
	/// lists). A failing plugin never blocks the project from opening — it is surfaced as Unavailable.
	/// </summary>
	public class PluginManager
	{
		private static PluginManager _instance;
		public static PluginManager Instance => _instance ??= new PluginManager();

		private readonly List<PluginInstance> _plugins = new();
		private string _projectPath;

		/// <summary>Full paths of plugin managed DLLs loaded this session (editor flavor when present).</summary>
		private readonly HashSet<string> _loadedAssemblyPaths = new(StringComparer.OrdinalIgnoreCase);

		/// <summary>Simple assembly names contributed by plugins, for collision checks and sync exclusion.</summary>
		private readonly HashSet<string> _pluginAssemblyNames = new(StringComparer.OrdinalIgnoreCase);

		/// <summary>All plugins.json entries with their live state, in config order.</summary>
		public IReadOnlyList<PluginInstance> Plugins => _plugins;

		/// <summary>True when any plugin is Unavailable or Failed (drives warning banners).</summary>
		public bool HasProblems => _plugins.Any(p => p.State == PluginState.Unavailable || p.State == PluginState.Failed);

		/// <summary>Fired after a restore pass completes (project open or manual refresh).</summary>
		public event Action OnPluginsRestored;

		/// <summary>
		/// Restores all plugins for a freshly opened project. Never throws: each plugin fails
		/// independently into an Unavailable/Failed state that the Plugin Manager window surfaces.
		/// </summary>
		public void RestoreForProject(IGameProject project)
		{
			_plugins.Clear();
			_projectPath = project.ProjectPath;

			ProjectPluginsConfig config;
			try
			{
				config = ProjectPluginsConfig.LoadFrom(project.ProjectPath);
			}
			catch (Exception ex)
			{
				EditorDebug.Error($"Failed to read {ProjectPluginsConfig.FileName}: {ex.Message}", "Plugins");
				OnPluginsRestored?.Invoke();
				return;
			}

			if (config == null || config.Plugins.Count == 0)
			{
				OnPluginsRestored?.Invoke();
				return;
			}

			var lockFile = PluginLockFile.LoadFrom(project.ProjectPath);
			var lockChanged = false;

			ValidateNoDuplicateIds(config);

			foreach (var entry in config.Plugins)
			{
				var instance = new PluginInstance { Entry = entry };
				_plugins.Add(instance);

				if (entry.Disabled)
				{
					instance.State = PluginState.Disabled;
					continue;
				}

				try
				{
					var lockEntry = lockFile.FindById(entry.Id);
					var resolved = PluginResolver.Resolve(entry, lockEntry, project.ProjectPath);

					instance.Resolved = resolved;
					instance.Manifest = resolved.Manifest;

					CheckEngineVersion(resolved.Manifest);

					instance.PayloadPath = PluginSync.SyncPlugin(project.ProjectPath, resolved);
					instance.State = PluginState.Restored;

					lockChanged |= UpdateLockEntry(lockFile, entry, resolved);
				}
				catch (Exception ex) when (ex is PluginResolveException or PluginManifestException)
				{
					instance.State = PluginState.Unavailable;
					instance.Error = ex.Message;
					EditorDebug.Warn($"Plugin '{entry.Id}' unavailable: {ex.Message}", "Plugins");
				}
				catch (Exception ex)
				{
					instance.State = PluginState.Unavailable;
					instance.Error = $"Unexpected error: {ex.Message}";
					EditorDebug.Error($"Plugin '{entry.Id}' failed to restore: {ex}", "Plugins");
				}
			}

			CheckDependencies();

			// Reflect removals/renames on disk, then persist any new pins.
			PluginSync.RemoveStalePayloads(project.ProjectPath, config.Plugins.Select(p => p.Id));

			if (lockChanged)
			{
				try
				{
					lockFile.SaveTo(project.ProjectPath);
				}
				catch (Exception ex)
				{
					EditorDebug.Error($"Failed to write {PluginLockFile.FileName}: {ex.Message}", "Plugins");
				}
			}

			var restoredCount = _plugins.Count(p => p.State == PluginState.Restored);
			if (restoredCount > 0 || HasProblems)
				EditorDebug.Log($"Plugins restored: {restoredCount} ok, {_plugins.Count(p => p.State == PluginState.Unavailable)} unavailable.", "Plugins");

			LoadGameplayAssemblies();

			// Editor-kind plugins: discover and initialize their IEditorPlugin implementations
			// (windows/menu items). Each initializes in isolation — a throwing plugin is disabled.
			EditorPluginHost.InitializePlugins(_plugins, project);

			// Keep the game build glue (Plugins.g.props / bootstrap / trimmer roots) in step with the
			// restored set so IDE builds of the game project work between editor sessions too.
			try
			{
				PluginSync.GenerateBuildFiles(project.ProjectPath, _plugins);

				// Older projects predate the plugin system — give their csproj the (Exists-conditioned)
				// Plugins.g.props import so IDE and publish builds pick the plugins up.
				var csprojPath = Path.Combine(project.ProjectPath, $"{project.ProjectName}.csproj");
				if (File.Exists(csprojPath))
					Builders.GameBuilder.EnsurePluginsImportInCsproj(csprojPath);
			}
			catch (Exception ex)
			{
				EditorDebug.Error($"Failed to generate plugin build files: {ex.Message}", "Plugins");
			}

			OnPluginsRestored?.Invoke();
		}

		#region Gameplay assembly loading (editor process)

		/// <summary>
		/// Loads every restored gameplay plugin's managed assemblies into the editor so their Components
		/// exist for the Add Component menu, inspectors, and scene deserialization. Prefers the plugin's
		/// EDITOR-flavored twins (editor-lib) when declared. Mirrors the script pipeline: LoadFrom →
		/// RunModuleConstructor (fires the source-generated [ModuleInitializer] registrations) →
		/// invalidate the component type cache once at the end.
		/// </summary>
		private void LoadGameplayAssemblies()
		{
			var loadedAny = false;

			foreach (var plugin in _plugins)
			{
				if (plugin.State != PluginState.Restored || plugin.Manifest is not { IsGameplay: true })
					continue;

				var gameplay = plugin.Manifest.Gameplay;
				var assemblyList = gameplay.EditorManagedAssemblies is { Count: > 0 }
					? gameplay.EditorManagedAssemblies
					: gameplay.ManagedAssemblies;

				if (assemblyList == null || assemblyList.Count == 0)
				{
					plugin.State = PluginState.Loaded; // Source-only or content-only plugin: nothing to load here.
					continue;
				}

				try
				{
					foreach (var relPath in assemblyList)
					{
						var dllPath = Path.Combine(plugin.PayloadPath, PluginManifest.NormalizeRelative(relPath));
						if (!File.Exists(dllPath))
							throw new FileNotFoundException($"Managed assembly not found in payload: {relPath}", dllPath);

						ValidateNoAssemblyNameCollision(plugin, dllPath);

						var assembly = Assembly.LoadFrom(dllPath);

						// Natives must be resolvable before module initializers run — an initializer
						// could touch P/Invoke (e.g. an SDK version query).
						PluginNativeResolver.Register(assembly, plugin.PayloadPath, plugin.Manifest);

						// Force [ModuleInitializer]s now so ComponentIdRegistry / ComponentAotFactory /
						// ComponentDataAotDeserializer registration happens before any scene loads.
						foreach (var module in assembly.GetModules())
							System.Runtime.CompilerServices.RuntimeHelpers.RunModuleConstructor(module.ModuleHandle);

						_loadedAssemblyPaths.Add(dllPath);
						_pluginAssemblyNames.Add(assembly.GetName().Name ?? Path.GetFileNameWithoutExtension(dllPath));

						// When the manifest omits RootTypes, record one public type per assembly for the
						// generated game bootstrap (AOT rooting + module-initializer forcing).
						if (plugin.Manifest.Gameplay.RootTypes is not { Count: > 0 })
						{
							var rootType = DetectRootType(assembly);
							if (rootType != null)
								plugin.DetectedRootTypes.Add(rootType);
							else
								EditorDebug.Warn($"Plugin '{plugin.Id}': no public type found in {Path.GetFileName(dllPath)} to use as an AOT root — declare Gameplay.RootTypes in plugin.json.", "Plugins");
						}

						EditorDebug.Log($"Loaded plugin assembly: {Path.GetFileName(dllPath)} ({plugin.Id})", "Plugins");
					}

					plugin.State = PluginState.Loaded;
					loadedAny = true;
				}
				catch (Exception ex)
				{
					plugin.State = PluginState.Failed;
					plugin.Error = $"Failed to load assemblies: {ex.Message}";
					EditorDebug.Error($"Plugin '{plugin.Id}' assembly load failed: {ex.Message}", "Plugins");
				}
			}

			if (loadedAny)
				Windows.EntityInspectorWindow.InvalidateComponentTypeCache();
		}

		/// <summary>
		/// Picks a stable AOT-root type from a plugin assembly: prefer a public Component subclass
		/// (guaranteed to exist for component plugins), otherwise the first public non-nested type.
		/// </summary>
		private static string DetectRootType(Assembly assembly)
		{
			try
			{
				var publicTypes = assembly.GetExportedTypes()
					.Where(t => !t.IsNested && !t.IsGenericTypeDefinition)
					.OrderBy(t => t.FullName, StringComparer.Ordinal)
					.ToList();

				var component = publicTypes.FirstOrDefault(t => typeof(Component).IsAssignableFrom(t));
				return (component ?? publicTypes.FirstOrDefault())?.FullName;
			}
			catch (ReflectionTypeLoadException ex)
			{
				return ex.Types.FirstOrDefault(t => t is { IsPublic: true, IsNested: false })?.FullName;
			}
		}

		/// <summary>
		/// A plugin assembly whose simple name matches an engine DLL (or another plugin's) would be
		/// silently shadowed by the already-loaded engine copy — surface it as a hard failure instead.
		/// Exception: an EDITOR-flavored twin of the plugin's own runtime DLL shares its name by design.
		/// </summary>
		private void ValidateNoAssemblyNameCollision(PluginInstance plugin, string dllPath)
		{
			var simpleName = Path.GetFileNameWithoutExtension(dllPath);

			var collidesWithEngine = EngineLibsSync.ManagedReferenceDlls
				.Select(Path.GetFileNameWithoutExtension)
				.Any(engineName => string.Equals(engineName, simpleName, StringComparison.OrdinalIgnoreCase));

			if (collidesWithEngine)
				throw new PluginResolveException(
					$"Plugin '{plugin.Id}' ships assembly '{simpleName}.dll', which collides with an engine assembly of the same name.");

			if (_pluginAssemblyNames.Contains(simpleName) && !_loadedAssemblyPaths.Contains(dllPath))
				throw new PluginResolveException(
					$"Plugin '{plugin.Id}' ships assembly '{simpleName}.dll', which collides with an assembly from another plugin.");
		}

		/// <summary>
		/// Full paths of plugin managed DLLs the Roslyn script compiler should reference so game scripts
		/// can use plugin types. Explicit manifest-listed DLLs only — callers must never glob PluginLibs.
		/// </summary>
		public IReadOnlyCollection<string> GetEditorReferenceAssemblyPaths()
		{
			return _loadedAssemblyPaths;
		}

		/// <summary>
		/// True when the given assembly file location belongs to a loaded plugin. EngineLibsSync uses this
		/// to keep editor-flavored plugin DLLs from leaking into the game project's EngineLibs folder.
		/// </summary>
		public bool IsPluginAssembly(string assemblyLocation)
		{
			if (string.IsNullOrEmpty(assemblyLocation))
				return false;

			if (_loadedAssemblyPaths.Contains(assemblyLocation))
				return true;

			// Also match by simple name: Assembly.Location can normalize casing/paths differently.
			var simpleName = Path.GetFileNameWithoutExtension(assemblyLocation);
			return _pluginAssemblyNames.Contains(simpleName);
		}

		/// <summary>Absolute source-root folders of restored source-form plugins (compiled with game scripts).</summary>
		public List<string> GetSourceRoots()
		{
			return CollectSourceRoots(devOnly: false);
		}

		/// <summary>
		/// Source roots of dev-mode plugins only. These are the user's own working folders, so the script
		/// watcher hot-reloads on changes there; cache-installed payloads are immutable and never watched.
		/// </summary>
		public List<string> GetDevSourceRoots()
		{
			return CollectSourceRoots(devOnly: true);
		}

		/// <summary>
		/// True when the file lives inside a read-only (non-dev) plugin's source root. Such files must
		/// never be mutated on disk (e.g. by the ComponentIdStamper) — the package is an immutable,
		/// hash-pinned install.
		/// </summary>
		public bool IsReadOnlyPluginSource(string filePath)
		{
			if (string.IsNullOrEmpty(filePath))
				return false;

			foreach (var plugin in _plugins)
			{
				if (plugin.Entry is { Dev: true })
					continue;
				if (plugin.State is not (PluginState.Restored or PluginState.Loaded) || plugin.PayloadPath == null)
					continue;

				var srcRoots = plugin.Manifest?.Gameplay?.SourceRoots;
				if (srcRoots == null)
					continue;

				foreach (var rel in srcRoots)
				{
					var dir = Path.Combine(plugin.PayloadPath, PluginManifest.NormalizeRelative(rel));
					if (Utils.CrossPlatformPath.IsPathUnder(dir, filePath))
						return true;
				}
			}

			return false;
		}

		private List<string> CollectSourceRoots(bool devOnly)
		{
			var roots = new List<string>();

			foreach (var plugin in _plugins)
			{
				if (plugin.State is not (PluginState.Restored or PluginState.Loaded))
					continue;
				if (devOnly && plugin.Entry is not { Dev: true })
					continue;

				var srcRoots = plugin.Manifest?.Gameplay?.SourceRoots;
				if (srcRoots == null)
					continue;

				// Dev plugins compile straight from their working folder (not the PluginLibs copy) so
				// edits take effect immediately without a re-sync.
				var baseDir = plugin.Entry is { Dev: true }
					? ResolveDevSourceDir(plugin)
					: plugin.PayloadPath;

				if (baseDir == null)
					continue;

				foreach (var rel in srcRoots)
				{
					var dir = Path.Combine(baseDir, PluginManifest.NormalizeRelative(rel));
					if (Directory.Exists(dir))
						roots.Add(dir);
				}
			}

			return roots;
		}

		private string ResolveDevSourceDir(PluginInstance plugin)
		{
			var sourcePath = plugin.Entry?.Source?.Path;
			if (string.IsNullOrWhiteSpace(sourcePath) || _projectPath == null)
				return plugin.PayloadPath;

			return Path.IsPathRooted(sourcePath) ? sourcePath : Path.GetFullPath(Path.Combine(_projectPath, sourcePath));
		}

		#endregion

		/// <summary>Clears all plugin state when the project closes.</summary>
		public void OnProjectUnloaded()
		{
			EditorPluginHost.ShutdownPlugins();
			_plugins.Clear();
			_projectPath = null;
		}

		public PluginInstance FindById(string id)
		{
			return _plugins.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
		}

		#region Config-editing actions (Plugin Manager window)

		/// <summary>
		/// Adds a plugin to the project from a source (bundled / local folder / git URL / zip URL),
		/// then restores so it resolves, syncs, and loads live. The plugin's id is discovered from the
		/// resolved manifest (except bundled, where the caller supplies the id from the dropdown), so
		/// the caller does not need to know it in advance. Returns a user-facing status message.
		/// </summary>
		public string AddPlugin(ProjectPluginEntry entry)
		{
			var project = ProjectManager.Instance.CurrentProject;
			if (project == null || _projectPath == null)
				return "No project open.";
			if (entry?.Source == null || !entry.Source.IsValid())
				return "Choose exactly one source (bundled, local folder, git URL, or zip URL).";

			// Dev mode only makes sense for a local folder source.
			entry.Dev = entry.Dev && !string.IsNullOrWhiteSpace(entry.Source.Path);

			// Never let an exception escape into the ImGui frame (it would be swallowed and the user
			// would see nothing). Any failure — expected validation error or unexpected IO/parse error —
			// comes back as a "Could not add plugin: …" message the window renders in red.
			try
			{
				var resolved = PluginResolver.Resolve(entry, null, _projectPath, allowRepin: true);

				entry.Id = resolved.Manifest.Id;

				var config = ProjectPluginsConfig.LoadFrom(_projectPath) ?? new ProjectPluginsConfig();
				if (config.Plugins.Any(p => string.Equals(p.Id, entry.Id, StringComparison.OrdinalIgnoreCase)))
					return $"Could not add plugin: '{entry.Id}' is already in this project.";

				config.Plugins.Add(entry);
				config.SaveTo(_projectPath);

				// Pre-write the lock pin so the restore below is a cache hit rather than a second git/zip fetch.
				var lockFile = PluginLockFile.LoadFrom(_projectPath);
				UpdateLockEntry(lockFile, entry, resolved);
				lockFile.SaveTo(_projectPath);

				// Full restore: re-resolves everything (cache hits), syncs, regenerates build files, and
				// loads the new plugin live. Already-loaded plugins re-load idempotently.
				RestoreForProject(project);

				var added = FindById(entry.Id);
				return added?.State switch
				{
					PluginState.Loaded => $"Added and loaded plugin '{entry.Id}' ({resolved.Manifest.Version}). Reload the scene to fill in any missing-component entries.",
					PluginState.Restored => $"Added plugin '{entry.Id}' ({resolved.Manifest.Version}).",
					PluginState.Unavailable => $"Added '{entry.Id}', but it is unavailable: {added.Error}",
					PluginState.Failed => $"Added '{entry.Id}', but it failed to load: {added.Error}",
					_ => $"Added plugin '{entry.Id}'.",
				};
			}
			catch (Exception ex)
			{
				EditorDebug.Warn($"Add plugin failed: {ex.Message}", "Plugins");
				return $"Could not add plugin: {ex.Message}";
			}
		}

		/// <summary>
		/// Loaded assemblies cannot unload, so config changes to already-loaded plugins only fully
		/// apply after the project is reopened. Actions below return a user-facing status message.
		/// </summary>
		public string SetPluginDisabled(string id, bool disabled)
		{
			if (_projectPath == null)
				return "No project open.";

			var config = ProjectPluginsConfig.LoadFrom(_projectPath);
			var entry = config?.Plugins.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
			if (entry == null)
				return $"Plugin '{id}' not found in plugins.json.";

			if (entry.Disabled == disabled)
				return null;

			entry.Disabled = disabled;
			config.SaveTo(_projectPath);

			var instance = FindById(id);
			if (instance != null && disabled && instance.State is PluginState.Unavailable or PluginState.Failed)
			{
				// Disabling a broken plugin takes effect immediately for build gating.
				instance.State = PluginState.Disabled;
				instance.Error = null;
			}

			return disabled
				? $"Plugin '{id}' disabled. Reopen the project to unload it fully."
				: $"Plugin '{id}' enabled. Reopen the project to load it.";
		}

		/// <summary>Explicit user-driven update: re-resolves accepting new content and re-pins the lock.</summary>
		public string UpdatePlugin(string id)
		{
			if (_projectPath == null)
				return "No project open.";

			var config = ProjectPluginsConfig.LoadFrom(_projectPath);
			var entry = config?.Plugins.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
			if (entry == null)
				return $"Plugin '{id}' not found in plugins.json.";

			try
			{
				var lockFile = PluginLockFile.LoadFrom(_projectPath);
				var resolved = PluginResolver.Resolve(entry, lockFile.FindById(id), _projectPath, allowRepin: true);
				PluginSync.SyncPlugin(_projectPath, resolved);

				if (UpdateLockEntry(lockFile, entry, resolved))
					lockFile.SaveTo(_projectPath);

				var instance = FindById(id);
				if (instance != null)
				{
					instance.Resolved = resolved;
					instance.Manifest = resolved.Manifest;
					instance.PayloadPath = PluginSync.GetPluginPayloadPath(_projectPath, id);
				}

				return $"Plugin '{id}' updated to {resolved.Manifest.Version}. Reopen the project to load the new version.";
			}
			catch (Exception ex) when (ex is PluginResolveException or PluginManifestException)
			{
				return $"Update failed: {ex.Message}";
			}
		}

		/// <summary>Removes the plugin from plugins.json + lock and deletes its PluginLibs payload.</summary>
		public string RemovePlugin(string id)
		{
			if (_projectPath == null)
				return "No project open.";

			var config = ProjectPluginsConfig.LoadFrom(_projectPath);
			if (config == null)
				return "This project has no plugins.json.";

			var removedCount = config.Plugins.RemoveAll(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
			if (removedCount == 0)
				return $"Plugin '{id}' not found in plugins.json.";

			config.SaveTo(_projectPath);

			var lockFile = PluginLockFile.LoadFrom(_projectPath);
			lockFile.RemoveById(id);
			lockFile.SaveTo(_projectPath);

			PluginSync.RemoveStalePayloads(_projectPath, config.Plugins.Select(p => p.Id));

			try
			{
				PluginSync.GenerateBuildFiles(_projectPath, _plugins.Where(p => p.Id != id).ToList());
			}
			catch (Exception ex)
			{
				EditorDebug.Warn($"Could not regenerate plugin build files after removal: {ex.Message}", "Plugins");
			}

			var instance = FindById(id);
			if (instance != null)
				_plugins.Remove(instance);

			return $"Plugin '{id}' removed. Already-loaded assemblies unload on the next editor restart.";
		}

		#endregion

		#region Validation

		private static void ValidateNoDuplicateIds(ProjectPluginsConfig config)
		{
			var duplicates = config.Plugins
				.GroupBy(p => p.Id, StringComparer.OrdinalIgnoreCase)
				.Where(g => g.Count() > 1)
				.Select(g => g.Key)
				.ToList();

			foreach (var dup in duplicates)
				EditorDebug.Error($"plugins.json lists plugin '{dup}' more than once — only the first entry is honored.", "Plugins");
		}

		/// <summary>Engine version range mismatch is a warning in the editor (plugin still loads).</summary>
		private static void CheckEngineVersion(PluginManifest manifest)
		{
			if (!SemVerRange.Satisfies(VoltageVersion.Engine, manifest.EngineVersion))
			{
				EditorDebug.Warn(
					$"Plugin '{manifest.Id}' declares EngineVersion '{manifest.EngineVersion}' but this engine is {VoltageVersion.Engine}. It may not work correctly.",
					"Plugins");
			}
		}

		/// <summary>Flags restored plugins whose declared dependencies are missing, disabled, or too old.</summary>
		private void CheckDependencies()
		{
			foreach (var plugin in _plugins)
			{
				if (plugin.State != PluginState.Restored || plugin.Manifest?.Dependencies == null)
					continue;

				foreach (var dep in plugin.Manifest.Dependencies)
				{
					var found = FindById(dep.Id);
					if (found == null || found.State is PluginState.Disabled or PluginState.Unavailable)
					{
						plugin.State = PluginState.Unavailable;
						plugin.Error = $"Missing dependency: plugin '{dep.Id}' ({dep.Version}) is not installed or unavailable.";
						break;
					}

					if (found.Manifest != null && !SemVerRange.Satisfies(found.Manifest.Version, dep.Version))
					{
						plugin.State = PluginState.Unavailable;
						plugin.Error = $"Dependency version mismatch: needs '{dep.Id}' {dep.Version}, found {found.Manifest.Version}.";
						break;
					}
				}
			}
		}

		private static bool UpdateLockEntry(PluginLockFile lockFile, ProjectPluginEntry entry, ResolvedPlugin resolved)
		{
			var existing = lockFile.FindById(entry.Id);

			// Dev plugins are intentionally unpinned; drop any stale pin left from a non-dev past.
			if (resolved.IsDev)
			{
				if (existing == null)
					return false;
				lockFile.RemoveById(entry.Id);
				return true;
			}

			if (existing != null
			    && existing.ContentHash == resolved.ContentHash
			    && existing.Version == resolved.Manifest.Version
			    && existing.Commit == resolved.Commit
			    && existing.Source.Matches(entry.Source))
				return false;

			lockFile.Upsert(new PluginLockEntry
			{
				Id = entry.Id,
				Version = resolved.Manifest.Version,
				Source = entry.Source,
				Commit = resolved.Commit,
				ContentHash = resolved.ContentHash,
			});
			return true;
		}

		#endregion
	}
}
