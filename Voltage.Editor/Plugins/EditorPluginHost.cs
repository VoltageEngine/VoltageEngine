using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using ImGuiNET;
using Voltage.Editor.DebugUtils;
using Voltage.Editor.ImGuiCore;
using Voltage.Editor.ProjectFile;

namespace Voltage.Editor.Plugins
{
	/// <summary>
	/// Loads and runs editor plugins: discovers <see cref="IEditorPlugin"/> implementations in the
	/// manifest-listed editor assemblies, gates them on <see cref="EditorPluginApi.Version"/>,
	/// initializes each in isolation (a throwing plugin is disabled and surfaced, never crashing the
	/// editor), and owns the window/menu registries the ImGui loop draws from.
	///
	/// Assemblies load into the default ALC and cannot unload - updating an editor plugin requires an
	/// editor restart (deliberate v1 trade-off, same as the script pipeline's accumulation).
	/// </summary>
	public static class EditorPluginHost
	{
		private class ActivePlugin
		{
			public IEditorPlugin Instance;
			public PluginInstance Owner;
		}

		private class PluginMenuItem
		{
			public string[] PathSegments;
			public Action OnClick;
			public string OwnerId;
			public EditorMenu Menu = EditorMenu.Plugins;
		}

		private static readonly List<ActivePlugin> _active = new();
		private static readonly List<EditorPluginWindow> _windows = new();
		private static readonly List<PluginMenuItem> _menuItems = new();
		private static readonly HashSet<string> _loadedAssemblyPaths = new(StringComparer.OrdinalIgnoreCase);
		private static PluginContext _context;

		public static bool HasMenuItems => _menuItems.Count > 0;

		/// <summary>
		/// Loads and initializes the editor side of every restored plugin with an "editor" kind.
		/// Called by <see cref="PluginManager"/> after gameplay assemblies are up.
		/// </summary>
		public static void InitializePlugins(IReadOnlyList<PluginInstance> plugins, IGameProject project)
		{
			_context ??= new PluginContext();
			_context.Project = project;

			foreach (var plugin in plugins)
			{
				if (plugin.Manifest is not { IsEditor: true })
					continue;
				if (plugin.State is not (PluginState.Restored or PluginState.Loaded))
					continue;

				// Hard ABI gate: an editor plugin built against a different API version must not load.
				if (plugin.Manifest.EditorPluginApiVersion != EditorPluginApi.Version)
				{
					plugin.State = PluginState.Failed;
					plugin.Error = $"Editor plugin was built for editor-plugin API v{plugin.Manifest.EditorPluginApiVersion}, " +
						$"but this editor provides v{EditorPluginApi.Version}. Update the plugin (or the editor).";
					PluginLog.Error($"Plugin '{plugin.Id}': {plugin.Error}");
					continue;
				}

				try
				{
					InitializePluginAssemblies(plugin);
					plugin.State = PluginState.Loaded;
				}
				catch (Exception ex)
				{
					plugin.State = PluginState.Failed;
					plugin.Error = $"Editor plugin failed to initialize: {ex.Message}";
					PluginLog.Error($"Plugin '{plugin.Id}': {plugin.Error}");
				}
			}
		}

		private static void InitializePluginAssemblies(PluginInstance plugin)
		{
			foreach (var rel in plugin.Manifest.Editor.Assemblies)
			{
				var dllPath = Path.Combine(plugin.PayloadPath, PluginManifest.NormalizeRelative(rel));
				if (!File.Exists(dllPath))
					throw new FileNotFoundException($"Editor assembly not found in payload: {rel}", dllPath);

				var assembly = Assembly.LoadFrom(dllPath);
				_loadedAssemblyPaths.Add(dllPath);

				// LoadFrom resolves by assembly IDENTITY, not by path. If this assembly is already loaded
				// - the same plugin installed earlier in this session, then removed and re-added from a
				// different source - the runtime hands back the old one and ignores dllPath entirely.
				// The type guard below then matches the old instance and skips Initialize, so the new
				// build contributes nothing and says nothing. Name it instead of letting it look like a
				// no-op, because "I re-added it and nothing happened" has no other explanation.
				if (!string.IsNullOrEmpty(assembly.Location) && !SamePath(assembly.Location, dllPath))
				{
					plugin.StaleAssemblyWarning =
						$"'{Path.GetFileName(dllPath)}' was already loaded in this session from " +
						$"{assembly.Location}, so the code running is still the previous build. .NET cannot " +
						"unload an assembly - restart the editor to pick up this one.";

					PluginLog.Warn($"Plugin '{plugin.Id}': {plugin.StaleAssemblyWarning}");
				}

				var pluginTypes = assembly.GetTypes()
					.Where(t => typeof(IEditorPlugin).IsAssignableFrom(t)
					            && t is { IsAbstract: false, IsInterface: false }
					            && t.GetConstructor(Type.EmptyTypes) != null)
					.ToList();

				if (pluginTypes.Count == 0)
				{
					PluginLog.Warn($"Plugin '{plugin.Id}': no IEditorPlugin implementation found in {Path.GetFileName(dllPath)}.");
					continue;
				}

				foreach (var type in pluginTypes)
				{
					// A previously-initialized instance survives project switches within a session
					// (assemblies never unload) - don't double-initialize the same plugin type.
					if (_active.Any(a => a.Instance.GetType() == type))
					{
						if (plugin.StaleAssemblyWarning == null)
						{
							plugin.StaleAssemblyWarning =
								$"'{type.Name}' is already running from an earlier load in this session, so it " +
								"was not initialized again and any new windows or menu items it registers are " +
								"absent. Restart the editor.";

							PluginLog.Warn($"Plugin '{plugin.Id}': {plugin.StaleAssemblyWarning}");
						}

						continue;
					}

					var instance = (IEditorPlugin)Activator.CreateInstance(type);
					_context.CurrentOwnerId = plugin.Id;
					instance.Initialize(_context);
					_context.CurrentOwnerId = null;

					_active.Add(new ActivePlugin { Instance = instance, Owner = plugin });
					PluginLog.Log($"Initialized editor plugin: {type.FullName} ({plugin.Id})");
				}
			}
		}

		private static bool SamePath(string a, string b)
		{
			try
			{
				return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
			}
			catch
			{
				return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
			}
		}

		/// <summary>Shuts every active editor plugin down (project close / editor exit).</summary>
		public static void ShutdownPlugins()
		{
			_context?.FireProjectClosing();

			foreach (var active in _active)
			{
				try
				{
					active.Instance.Shutdown();
				}
				catch (Exception ex)
				{
					PluginLog.Warn($"Editor plugin {active.Instance.GetType().Name} threw during Shutdown: {ex.Message}");
				}
			}

			_active.Clear();
			_windows.Clear();
			_menuItems.Clear();
			if (_context != null)
				_context.Project = null;
		}

		/// <summary>Draws all open plugin windows. Called once per frame from ImGuiManager.LayoutGui.</summary>
		public static void DrawWindows()
		{
			foreach (var window in _windows)
			{
				if (!window.IsOpen)
					continue;

				try
				{
					window.Draw();
				}
				catch (Exception ex)
				{
					// One broken window must not take the editor's UI loop down; close it and surface.
					window.IsOpen = false;
					PluginLog.Error($"Plugin window '{window.Title}' threw during Draw and was closed: {ex.Message}");
				}
			}
		}

		/// <summary>Draws the plugin-registered entries of the Plugins menu (supports "A/B/C" nesting).</summary>
		public static void DrawMenuItems() => DrawMenuItems(EditorMenu.Plugins);

		/// <summary>
		/// Draws the plugin contributions for one menu. Called at the end of each host menu, so plugin
		/// entries always appear below the editor's own commands rather than interleaved with them.
		/// </summary>
		public static void DrawMenuItems(EditorMenu menu)
		{
			var items = _menuItems.Where(i => i.Menu == menu).ToList();
			if (items.Count == 0)
				return;

			ImGui.Separator();
			DrawMenuLevel(items, 0);
		}

		private static void DrawMenuLevel(List<PluginMenuItem> items, int depth)
		{
			// Leaves at this depth first, then grouped submenus.
			foreach (var item in items.Where(i => i.PathSegments.Length == depth + 1))
			{
				if (ImGui.MenuItem(item.PathSegments[depth]))
				{
					try
					{
						item.OnClick?.Invoke();
					}
					catch (Exception ex)
					{
						PluginLog.Error($"Plugin menu item '{string.Join("/", item.PathSegments)}' threw: {ex.Message}");
					}
				}
			}

			foreach (var group in items.Where(i => i.PathSegments.Length > depth + 1)
				         .GroupBy(i => i.PathSegments[depth], StringComparer.Ordinal))
			{
				if (ImGui.BeginMenu(group.Key))
				{
					DrawMenuLevel(group.ToList(), depth + 1);
					ImGui.EndMenu();
				}
			}
		}

		#region Context implementation

		private class PluginContext : IEditorPluginContext
		{
			public IGameProject Project;

			/// <summary>Plugin id being initialized right now - stamps ownership onto registrations.</summary>
			public string CurrentOwnerId;

			public ImGuiManager ImGuiManager => Core.GetGlobalManager<ImGuiManager>();
			public IGameProject CurrentProject => Project;

			public event Action ProjectClosing;

			public void RegisterWindow(EditorPluginWindow window)
			{
				if (window == null)
					throw new ArgumentNullException(nameof(window));
				if (!_windows.Contains(window))
					_windows.Add(window);
			}

			public void AddMenuItem(string path, Action onClick) =>
				AddMenuItem(EditorMenu.Plugins, path, onClick);

			public void AddMenuItem(EditorMenu menu, string path, Action onClick)
			{
				if (string.IsNullOrWhiteSpace(path))
					throw new ArgumentException("Menu path cannot be empty.", nameof(path));

				_menuItems.Add(new PluginMenuItem
				{
					PathSegments = path.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
					OnClick = onClick,
					OwnerId = CurrentOwnerId,
					Menu = menu,
				});
			}

			public void FireProjectClosing()
			{
				try
				{
					ProjectClosing?.Invoke();
				}
				catch (Exception ex)
				{
					PluginLog.Warn($"A plugin ProjectClosing handler threw: {ex.Message}");
				}
			}
		}

		#endregion
	}
}
