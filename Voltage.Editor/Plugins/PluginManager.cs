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
		/// <summary>Listed in plugins.json with Disabled=true - not synced, not loaded.</summary>
		Disabled,

		/// <summary>Payload resolved, verified, and synced into PluginLibs.</summary>
		Restored,

		/// <summary>Restored and its assemblies are loaded into the editor.</summary>
		Loaded,

		/// <summary>Could not be acquired/verified (missing source, no repo access, SDK not configured...). The project still opens.</summary>
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

		/// <summary>
		/// Set when the plugin declares an <c>EngineVersion</c> range this engine does not satisfy.
		/// </summary>
		public string CompatibilityWarning;

		/// <summary>
		/// Set when this plugin's editor assembly was already loaded in this session, so the code running
		/// is not the code that was just installed. Only restarting the editor clears it - .NET cannot
		/// unload an assembly, which is the whole reason this state exists.
		/// </summary>
		public string StaleAssemblyWarning;

		/// <summary>This machine resolves the plugin from a folder of its own, whatever the project declares.</summary>
		public bool IsLocalOverride;

		/// <summary>
		/// The plugin exists only as a local override - nothing in the committed config names it, so no
		/// teammate has any way to get it until it is published or vendored.
		/// </summary>
		public bool IsLocalOnly;

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
	/// (resolve -> verify against plugins.lock.json -> sync into PluginLibs), records per-plugin state for
	/// the Plugin Manager window, and exposes what the rest of the editor needs (payload paths, assembly
	/// lists). A failing plugin never blocks the project from opening - it is surfaced as Unavailable.
	/// </summary>
	public class PluginManager
	{
		private static PluginManager _instance;
		public static PluginManager Instance => _instance ??= new PluginManager();

		private readonly List<PluginInstance> _plugins = new();

		/// <summary>Plugins present only as a local override, so no teammate can restore them.</summary>
		private HashSet<string> _localOnlyIds = new(StringComparer.OrdinalIgnoreCase);
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
				PluginLog.Error($"Failed to read {ProjectPluginsConfig.FileName}: {ex.Message}");
				OnPluginsRestored?.Invoke();
				return;
			}

			config ??= new ProjectPluginsConfig();

			// What the project declares, and what this machine substitutes for it. Loaded before the
			// nothing-to-do check below: a project whose only plugins are local to this machine has an empty
			// plugins.json and is not an empty project.
			var overrides = PluginLocalOverrides.LoadFrom(project.ProjectPath);

			var configChanged = PluginLocalOverrides.Migrate(config, overrides, project.ProjectPath);

			// A plugin nobody can fetch is still a plugin this project uses. Recording the id - and only the id,
			// never the path - is what lets a fresh clone say "this project needs DialogueMaker" instead of
			// opening with no plugins and no explanation.
			configChanged |= PluginLocalOverrides.DeclareLocalOnly(config, overrides);

			if (configChanged)
			{
				try
				{
					config.SaveTo(project.ProjectPath);
					overrides.SaveIfMeaningful(project.ProjectPath);
				}
				catch (Exception ex)
				{
					PluginLog.Error($"Could not split local plugin paths out of the project config: {ex.Message}");
				}
			}

			var entries = PluginLocalOverrides.Apply(config, overrides, project.ProjectPath);
			_localOnlyIds = PluginLocalOverrides.LocalOnlyIds(config, overrides);

			if (entries.Count == 0)
			{
				OnPluginsRestored?.Invoke();
				return;
			}

			var lockFile = PluginLockFile.LoadFrom(project.ProjectPath);
			var lockChanged = false;

			ValidateNoDuplicateIds(config);

			// Before anything is resolved, synced or loaded: assemblies load with LoadFrom, which is not
			// collectible, so a plugin rebuilt after this point could not be swapped in without restarting the
			// editor. Only checkouts that have actually fallen behind are built, so the usual cost here is a
			// directory walk - and when the editor's own build already rebuilt them, none at all.
			PluginDevRebuild.RebuildStale(entries, project.ProjectPath);

			foreach (var entry in entries)
			{
				var instance = new PluginInstance
				{
					Entry = entry,
					IsLocalOverride = overrides.FindById(entry.Id) != null,
					IsLocalOnly = _localOnlyIds.Contains(entry.Id ?? string.Empty),
				};

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

					instance.CompatibilityWarning = CheckEngineVersion(resolved.Manifest);

					instance.PayloadPath = PluginSync.SyncPlugin(project.ProjectPath, resolved);
					instance.State = PluginState.Restored;

					lockChanged |= UpdateLockEntry(lockFile, entry, resolved);
				}
				catch (Exception ex) when (ex is PluginResolveException or PluginManifestException)
				{
					instance.State = PluginState.Unavailable;
					instance.Error = ex.Message;
					PluginLog.Warn($"Plugin '{entry.Id}' unavailable: {ex.Message}");
				}
				catch (Exception ex)
				{
					instance.State = PluginState.Unavailable;
					instance.Error = $"Unexpected error: {ex.Message}";
					PluginLog.Error($"Plugin '{entry.Id}' failed to restore: {ex}");
				}
			}

			CheckDependencies();

			// Reflect removals/renames on disk, then persist any new pins.
			PluginSync.RemoveStalePayloads(project.ProjectPath, entries.Select(p => p.Id));

			if (lockChanged)
			{
				try
				{
					lockFile.SaveTo(project.ProjectPath);
				}
				catch (Exception ex)
				{
					PluginLog.Error($"Failed to write {PluginLockFile.FileName}: {ex.Message}");
				}
			}

			var restoredCount = _plugins.Count(p => p.State == PluginState.Restored);
			if (restoredCount > 0 || HasProblems)
				PluginLog.Log($"Plugins restored: {restoredCount} ok, {_plugins.Count(p => p.State == PluginState.Unavailable)} unavailable.");

			LoadGameplayAssemblies();

			// Editor-kind plugins: discover and initialize their IEditorPlugin implementations
			// (windows/menu items). Each initializes in isolation - a throwing plugin is disabled.
			EditorPluginHost.InitializePlugins(_plugins, project);

			// Keep the game build glue (Plugins.g.props / bootstrap / trimmer roots) in step with the
			// restored set so IDE builds of the game project work between editor sessions too.
			try
			{
				PluginSync.GenerateBuildFiles(project.ProjectPath, _plugins);

				// Older projects predate the plugin system - give their csproj the (Exists-conditioned)
				// Plugins.g.props import so IDE and publish builds pick the plugins up.
				var csprojPath = Path.Combine(project.ProjectPath, $"{project.ProjectName}.csproj");
				if (File.Exists(csprojPath))
					Builders.GameBuilder.EnsurePluginsImportInCsproj(csprojPath);
			}
			catch (Exception ex)
			{
				PluginLog.Error($"Failed to generate plugin build files: {ex.Message}");
			}

			OnPluginsRestored?.Invoke();
		}

		#region Gameplay assembly loading (editor process)

		/// <summary>
		/// Loads every restored gameplay plugin's managed assemblies into the editor so their Components
		/// exist for the Add Component menu, inspectors, and scene deserialization. Prefers the plugin's
		/// EDITOR-flavored twins (editor-lib) when declared. Mirrors the script pipeline: LoadFrom ->
		/// RunModuleConstructor (fires the source-generated [ModuleInitializer] registrations) ->
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

						// Natives must be resolvable before module initializers run - an initializer
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
								PluginLog.Warn($"Plugin '{plugin.Id}': no public type found in {Path.GetFileName(dllPath)} to use as an AOT root - declare Gameplay.RootTypes in plugin.json.");
						}

						PluginLog.Log($"Loaded plugin assembly: {Path.GetFileName(dllPath)} ({plugin.Id})");
					}

					plugin.State = PluginState.Loaded;
					loadedAny = true;
				}
				catch (Exception ex)
				{
					plugin.State = PluginState.Failed;
					plugin.Error = $"Failed to load assemblies: {ex.Message}";
					PluginLog.Error($"Plugin '{plugin.Id}' assembly load failed: {ex.Message}");
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
		/// silently shadowed by the already-loaded engine copy - surface it as a hard failure instead.
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
		/// can use plugin types. Explicit manifest-listed DLLs only - callers must never glob PluginLibs.
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
		/// never be mutated on disk (e.g. by the ComponentIdStamper) - the package is an immutable,
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
		/// <summary>
		/// The slow half of an add: fetch and unpack into the cache. Touches no manager state and loads no
		/// assemblies, so it is safe to run off the UI thread - which is what
		/// <see cref="PluginInstaller"/> does so a download can show progress and be cancelled.
		/// Throws on failure.
		/// </summary>
		public ResolvedPlugin ResolveForAdd(ProjectPluginEntry entry)
		{
			if (ProjectManager.Instance.CurrentProject == null || _projectPath == null)
				throw new PluginResolveException("No project open.");
			if (entry?.Source == null || !entry.Source.IsValid())
				throw new PluginResolveException("Choose exactly one source (local folder, git URL, or zip URL).");

			// Dev mode only makes sense for a local folder source.
			entry.Dev = entry.Dev && !string.IsNullOrWhiteSpace(entry.Source.Path);

			// The only caller that may build an unbuilt source checkout: this runs on the install worker,
			// which reports progress and is not the thread drawing the editor.
			return PluginResolver.Resolve(entry, null, _projectPath, allowRepin: true, allowSourceBuild: true);
		}

		/// <summary>
		/// The second half: record the plugin and load it. Must run on the UI thread - the restore it
		/// triggers loads editor-plugin assemblies and calls their Initialize, which registers windows and
		/// menu items the UI is meanwhile enumerating.
		/// </summary>
		public string CompleteAdd(ProjectPluginEntry entry, ResolvedPlugin resolved)
		{
			var project = ProjectManager.Instance.CurrentProject;
			if (project == null || _projectPath == null)
				return "No project open.";
			if (resolved?.Manifest == null)
				return "Could not add plugin: nothing was resolved.";

			try
			{
				entry.Id = resolved.Manifest.Id;

				var config = ProjectPluginsConfig.LoadFrom(_projectPath) ?? new ProjectPluginsConfig();
				var overrides = PluginLocalOverrides.LoadFrom(_projectPath);

				if (config.Plugins.Any(p => string.Equals(p.Id, entry.Id, StringComparison.OrdinalIgnoreCase))
				    || overrides.FindById(entry.Id) != null)
				{
					return $"Could not add plugin: '{entry.Id}' is already in this project.";
				}

				// A folder on this machine goes in the gitignored file, never the committed one: it would
				// resolve to nothing on every other machine and break their restore on a path only you have.
				// The id is still declared, with no source - so a teammate is told the project uses this
				// plugin, rather than opening it to an empty list and a scene full of missing components.
				if (PluginLocalOverrides.IsMachineLocal(entry.Source, _projectPath))
				{
					overrides.Upsert(entry.Id, entry.Source.Path);
					overrides.SaveTo(_projectPath);

					config.Plugins.Add(new ProjectPluginEntry { Id = entry.Id, Source = new PluginSourceSpec() });
					config.SaveTo(_projectPath);
				}
				else
				{
					config.Plugins.Add(entry);
					config.SaveTo(_projectPath);

					// Pre-write the lock pin so the restore below is a cache hit rather than a second fetch.
					var lockFile = PluginLockFile.LoadFrom(_projectPath);
					UpdateLockEntry(lockFile, entry, resolved);
					lockFile.SaveTo(_projectPath);
				}

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
				PluginLog.Warn($"Add plugin failed: {ex.Message}");
				return $"Could not add plugin: {ex.Message}";
			}
		}

		/// <summary>
		/// Resolve and add in one call, on the calling thread. Kept for callers that are already on the UI
		/// thread and have nothing to show progress with.
		/// </summary>
		public string AddPlugin(ProjectPluginEntry entry)
		{
			// Never let an exception escape into the ImGui frame (it would be swallowed and the user would
			// see nothing); every failure comes back as a message the window renders in red.
			try
			{
				return CompleteAdd(entry, ResolveForAdd(entry));
			}
			catch (Exception ex)
			{
				PluginLog.Warn($"Add plugin failed: {ex.Message}");
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

		/// <summary>
		/// Turns a plugin this machine resolves from a folder into one the project declares from git, at the
		/// tag that was just published. This is the step that makes a plugin you wrote available to the rest of
		/// the team: until it exists somewhere they can fetch from, no amount of project config can help them.
		/// </summary>
		public string ShareLocalPluginAsGit(string id, string gitUrl, string tag)
		{
			if (_projectPath == null)
				return "No project open.";

			if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(gitUrl) || string.IsNullOrWhiteSpace(tag))
				return "Need a plugin id, a git URL and a tag to share a plugin.";

			try
			{
				var config = ProjectPluginsConfig.LoadFrom(_projectPath) ?? new ProjectPluginsConfig();
				var overrides = PluginLocalOverrides.LoadFrom(_projectPath);

				var existing = config.Plugins.FirstOrDefault(p =>
					string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));

				if (existing == null)
				{
					config.Plugins.Add(new ProjectPluginEntry
					{
						Id = id,
						Source = new PluginSourceSpec { Git = gitUrl, Ref = tag },
					});
				}
				else
				{
					existing.Source = new PluginSourceSpec { Git = gitUrl, Ref = tag };
					existing.Dev = false;
				}

				config.SaveTo(_projectPath);

				// The override stays: you keep working on your copy, and the project now names a source your
				// teammates can restore from. Removing it would swap you onto the published build mid-session.
				return overrides.FindById(id) != null
					? $"'{id}' is now declared from {gitUrl} @ {tag}. You keep using your local folder; teammates get the published tag."
					: $"'{id}' is now declared from {gitUrl} @ {tag}.";
			}
			catch (Exception ex)
			{
				return $"Could not share '{id}': {ex.Message}";
			}
		}

		/// <summary>
		/// Copies a plugin's payload into the repository at <c>Plugins/&lt;id&gt;/</c> and declares it from
		/// there with a relative path.
		///
		/// <para>The answer for a plugin that will never be published - an internal tool, or something under an
		/// NDA. A path inside the repository travels with the checkout, so it is shareable in a way an absolute
		/// folder never is. This is what Unreal and Godot do with every plugin by default.</para>
		/// </summary>
		public string VendorPluginIntoProject(string id)
		{
			if (_projectPath == null)
				return "No project open.";

			var instance = FindById(id);
			var payload = instance?.Resolved?.PayloadDir ?? instance?.PayloadPath;

			if (string.IsNullOrWhiteSpace(payload) || !Directory.Exists(payload))
				return $"'{id}' has no resolved payload to copy - restore it first.";

			try
			{
				var relative = Path.Combine("Plugins", id);
				var destination = Path.Combine(_projectPath, relative);

				if (Directory.Exists(destination))
					Directory.Delete(destination, recursive: true);

				PluginCache.CopyDirectory(payload, destination);

				var config = ProjectPluginsConfig.LoadFrom(_projectPath) ?? new ProjectPluginsConfig();
				var overrides = PluginLocalOverrides.LoadFrom(_projectPath);

				var existing = config.Plugins.FirstOrDefault(p =>
					string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));

				// Forward slashes: this path is committed, and a backslash means nothing on a teammate's Mac.
				var source = new PluginSourceSpec { Path = relative.Replace('\\', '/') };

				if (existing == null)
					config.Plugins.Add(new ProjectPluginEntry { Id = id, Source = source });
				else
					existing.Source = source;

				config.SaveTo(_projectPath);

				if (overrides.RemoveById(id))
					overrides.SaveTo(_projectPath);

				return $"'{id}' is now vendored at {relative}. Commit that folder and the whole team has it.";
			}
			catch (Exception ex)
			{
				return $"Could not vendor '{id}': {ex.Message}";
			}
		}

		/// <summary>
		/// Points this machine's override for a plugin at a different folder - what you need after moving or
		/// renaming a plugin checkout, which otherwise leaves the project pointing at somewhere that is gone.
		/// </summary>
		public string RepointLocalOverride(string id, string folder)
		{
			if (_projectPath == null)
				return "No project open.";

			if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
				return $"'{folder}' is not a folder.";

			// Checked here, where the folder was just chosen. Pointing a plugin at the wrong folder otherwise
			// fails much later and much less clearly, as a manifest id that does not match the entry.
			var manifestPath = Path.Combine(folder, PluginManifest.FileName);

			if (!File.Exists(manifestPath))
				return $"No {PluginManifest.FileName} there - '{folder}' is not a plugin folder.";

			try
			{
				var manifest = Voltage.Persistence.Json.FromJson<PluginManifest>(File.ReadAllText(manifestPath));

				if (manifest != null && !string.IsNullOrWhiteSpace(manifest.Id)
				    && !string.Equals(manifest.Id, id, StringComparison.OrdinalIgnoreCase))
				{
					return $"That folder holds '{manifest.Id}', not '{id}'.";
				}
			}
			catch
			{
				// Unreadable manifest: let the restore below report it properly rather than guessing here.
			}

			try
			{
				var overrides = PluginLocalOverrides.LoadFrom(_projectPath);
				overrides.Upsert(id, folder);
				overrides.SaveTo(_projectPath);

				RestoreCurrentProject();
				return $"'{id}' now resolves from {folder}.";
			}
			catch (Exception ex)
			{
				return $"Could not re-point '{id}': {ex.Message}";
			}
		}

		/// <summary>
		/// Drops this machine's override for a plugin, falling back to whatever the project declares - the way
		/// out when the folder is gone for good and the project's own source is the one you want.
		/// </summary>
		public string ForgetLocalOverride(string id)
		{
			if (_projectPath == null)
				return "No project open.";

			try
			{
				var overrides = PluginLocalOverrides.LoadFrom(_projectPath);

				if (!overrides.RemoveById(id))
					return $"'{id}' has no local folder set on this machine.";

				overrides.SaveIfMeaningful(_projectPath);

				RestoreCurrentProject();
				return $"'{id}' no longer resolves from a local folder here.";
			}
			catch (Exception ex)
			{
				return $"Could not forget the local folder for '{id}': {ex.Message}";
			}
		}

		/// <summary>
		/// Re-runs a restore for the open project. Safe for a plugin that never loaded; a plugin whose
		/// assemblies are already in the process still needs a restart, which the restore itself reports.
		/// </summary>
		private void RestoreCurrentProject()
		{
			var project = ProjectManager.Instance?.CurrentProject;
			if (project != null)
				RestoreForProject(project);
		}

		/// <summary>Explicit user-driven update: re-resolves accepting new content and re-pins the lock.</summary>
		public string UpdatePlugin(string id)
		{
			var entry = PrepareUpdate(id, out var message);
			if (entry == null)
				return message;

			try
			{
				var lockEntry = PluginLockFile.LoadFrom(_projectPath).FindById(id);
				return CompleteUpdate(entry, PluginResolver.Resolve(entry, lockEntry, _projectPath, allowRepin: true));
			}
			catch (Exception ex) when (ex is PluginResolveException or PluginManifestException)
			{
				return $"Update failed: {ex.Message}";
			}
		}

		/// <summary>
		/// First half of an update: validates the plugin and decides what to fetch.
		///
		/// <para>An entry installed from the catalogue pins the URL of the version it was installed from,
		/// so re-resolving it can only ever return the same bytes - which is why Update used to look like
		/// it did nothing. When the registry advertises something newer, the returned entry carries the
		/// catalogue's current source instead.</para>
		///
		/// <para>Returns a detached copy: plugins.json is not touched until the fetch succeeds, so a failed
		/// update cannot leave the project pointing at a source it never managed to download.</para>
		/// </summary>
		public ProjectPluginEntry PrepareUpdate(string id, out string message)
		{
			message = null;

			if (_projectPath == null)
			{
				message = "No project open.";
				return null;
			}

			var config = ProjectPluginsConfig.LoadFrom(_projectPath);
			var entry = config?.Plugins.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
			if (entry == null)
			{
				message = $"Plugin '{id}' not found in plugins.json.";
				return null;
			}

			var candidate = new ProjectPluginEntry
			{
				Id = entry.Id,
				Source = entry.Source,
				Dev = entry.Dev,
				Disabled = entry.Disabled,
			};

			TryAdoptRegistrySource(candidate, FindById(id)?.Manifest?.Version);
			return candidate;
		}

		/// <summary>
		/// Points a registry-sourced entry at the catalogue's current release, when the catalogue has
		/// something newer than what is installed. A local folder is the author's own working copy and a
		/// bundled entry ships with the editor, so neither is the registry's to redirect.
		/// </summary>
		private static void TryAdoptRegistrySource(ProjectPluginEntry entry, string installedVersion)
		{
			if (entry.Dev || entry.Source == null || entry.Source.Bundled
			    || !string.IsNullOrWhiteSpace(entry.Source.Path))
				return;

			var listing = PluginRegistryIndex.FindUpdateFor(entry.Id, installedVersion);
			if (listing == null)
				return;

			var adopted = listing.ToSourceSpec();
			if (adopted.IsValid() && !adopted.Matches(entry.Source))
				entry.Source = adopted;
		}

		/// <summary>
		/// Second half of an update: records the (possibly repointed) source, re-pins the lock, and syncs
		/// the new payload. Must run on the UI thread for the same reason as <see cref="CompleteAdd"/>.
		///
		/// <para>The loaded assembly is not replaced - it cannot be, since assemblies never unload - so the
		/// message says what still has to happen for the new code to run.</para>
		/// </summary>
		public string CompleteUpdate(ProjectPluginEntry entry, ResolvedPlugin resolved)
		{
			if (_projectPath == null)
				return "Update failed: no project open.";
			if (resolved?.Manifest == null)
				return "Update failed: nothing was resolved.";

			try
			{
				var id = entry.Id ?? resolved.Manifest.Id;
				var instance = FindById(id);
				var previousVersion = instance?.Manifest?.Version;

				// Persist the repointed source only now that we know it actually resolves.
				var config = ProjectPluginsConfig.LoadFrom(_projectPath);
				var stored = config?.Plugins.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
				if (stored != null && !stored.Source.Matches(entry.Source))
				{
					stored.Source = entry.Source;
					config.SaveTo(_projectPath);
				}

				PluginSync.SyncPlugin(_projectPath, resolved);

				var lockFile = PluginLockFile.LoadFrom(_projectPath);
				if (UpdateLockEntry(lockFile, entry, resolved))
					lockFile.SaveTo(_projectPath);

				if (instance != null)
				{
					instance.Entry = stored ?? instance.Entry;
					instance.Resolved = resolved;
					instance.Manifest = resolved.Manifest;
					instance.PayloadPath = PluginSync.GetPluginPayloadPath(_projectPath, id);
				}

				var newVersion = resolved.Manifest.Version;
				if (previousVersion != null && string.Equals(previousVersion, newVersion, StringComparison.Ordinal))
					return $"'{id}' is already at {newVersion} - its source has nothing newer.";

				var reload = resolved.Manifest.IsEditor
					? "Restart the editor to load it (editor assemblies cannot be swapped in place)."
					: "Reopen the project to load it.";

				return previousVersion == null
					? $"Updated '{id}' to {newVersion}. {reload}"
					: $"Updated '{id}' {previousVersion} -> {newVersion}. {reload}";
			}
			catch (Exception ex)
			{
				PluginLog.Warn($"Update of '{entry.Id}' failed while applying: {ex.Message}");
				return $"Update failed: {ex.Message}";
			}
		}

		/// <summary>Removes the plugin from plugins.json + lock and deletes its PluginLibs payload.</summary>
		public string RemovePlugin(string id)
		{
			if (_projectPath == null)
				return "No project open.";

			var config = ProjectPluginsConfig.LoadFrom(_projectPath) ?? new ProjectPluginsConfig();
			var overrides = PluginLocalOverrides.LoadFrom(_projectPath);

			// A plugin can be declared by the project, overridden here, or only here - removing it has to clear
			// whichever of the two files actually names it.
			var removedCount = config.Plugins.RemoveAll(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
			var removedLocal = overrides.RemoveById(id);

			if (removedCount == 0 && !removedLocal)
				return $"Plugin '{id}' is not in this project.";

			if (removedCount > 0)
				config.SaveTo(_projectPath);

			if (removedLocal)
				overrides.SaveTo(_projectPath);

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
				PluginLog.Warn($"Could not regenerate plugin build files after removal: {ex.Message}");
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
				PluginLog.Error($"plugins.json lists plugin '{dup}' more than once - only the first entry is honored.");
		}

		/// <summary>
		/// Engine version range mismatch is a warning, not a block: the plugin still loads.
		/// </summary>
		private static string CheckEngineVersion(PluginManifest manifest)
		{
			if (SemVerRange.Satisfies(VoltageVersion.Engine, manifest.EngineVersion))
				return null;

			var message =
				$"Plugin '{manifest.Id}' declares EngineVersion '{manifest.EngineVersion}' but this engine is " +
				$"{VoltageVersion.Engine}. It may not work correctly.";

			PluginLog.Warn(message);
			return message;
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

			// Bundled payloads are built locally, so their hash differs per machine - recording it would
			// churn the lockfile on every clone. They pin on editor-provided Version instead.
			var contentHash = resolved.IsPinnable ? resolved.ContentHash : null;

			if (existing != null
			    && existing.ContentHash == contentHash
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
				ContentHash = contentHash,
			});
			return true;
		}

		#endregion
	}
}
