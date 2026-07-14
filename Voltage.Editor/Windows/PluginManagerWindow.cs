using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ImGuiNET;
using Voltage.Editor.FilePickers;
using Voltage.Editor.Plugins;
using Voltage.Editor.ProjectFile;
using Num = System.Numerics;

namespace Voltage.Editor.Windows
{
	/// <summary>
	/// Manages the current project's plugins (plugins.json): per-plugin state with errors, disable/
	/// enable, explicit update (re-pin), removal, and external-SDK install paths for NDA plugins.
	/// Assemblies cannot unload from a live process, so structural changes prompt a project reopen.
	/// </summary>
	public class PluginManagerWindow
	{
		public bool IsOpen;

		private static readonly Num.Vector4 ColorOk = new(0.2f, 1f, 0.3f, 1f);
		private static readonly Num.Vector4 ColorWarn = new(1f, 0.8f, 0.2f, 1f);
		private static readonly Num.Vector4 ColorError = new(1f, 0.2f, 0.2f, 1f);
		private static readonly Num.Vector4 ColorMuted = new(0.6f, 0.6f, 0.6f, 1f);

		private string _statusMessage;
		private bool _statusIsError;

		// Result of the last Add attempt, shown in red/green right inside the Add section.
		private string _addStatusMessage;
		private bool _addStatusIsError;

		/// <summary>Edit buffers for SDK path inputs, keyed by sdk id.</summary>
		private readonly Dictionary<string, string> _sdkPathBuffers = new();

		// "Add Plugin" form state.
		private static readonly string[] SourceTypes = { "Bundled", "Local folder", "Git URL", "Zip URL" };
		private int _addSourceType;
		private string _addPath = "";
		private bool _addDev;
		private string _addGitUrl = "";
		private string _addGitRef = "";
		private string _addZipUrl = "";
		private int _addBundledIndex;
		private string[] _bundledIds;

		// OS-native folder dialogs (with ImGui fallback) for the local-folder source and SDK paths.
		private readonly FolderBrowser _pluginFolderBrowser = new("plugin-folder-picker");
		private readonly FolderBrowser _sdkFolderBrowser = new("sdk-folder-picker");
		private readonly FolderBrowser _createLocationBrowser = new("create-location-picker");
		private string _sdkBrowseTargetId;

		// "Create New Plugin" popup state.
		private bool _showCreatePopup;
		private bool _newIdEdited;
		private string _newName = "My Plugin";
		private string _newId = "com.example.myplugin";
		private string _newDescription = "";
		private string _newAuthor = "";
		private string _newVersion = "1.0.0";
		private bool _newGameplay = true;
		private bool _newEditor;
		private string _newLocation = "";
		private bool _newAddToProject = true;
		private string _createStatusMessage;
		private bool _createStatusIsError;

		public void Draw()
		{
			if (!IsOpen)
				return;

			ImGui.SetNextWindowSize(new Num.Vector2(720, 460), ImGuiCond.FirstUseEver);
			if (!ImGui.Begin("Plugin Manager ###PluginManagerWindow", ref IsOpen))
			{
				ImGui.End();
				return;
			}

			if (!ProjectManager.Instance.HasActiveProject)
			{
				ImGui.TextColored(ColorMuted, "Open a project to manage its plugins.");
				ImGui.End();
				return;
			}

			var plugins = PluginManager.Instance.Plugins;

			if (PluginManager.Instance.HasProblems)
			{
				var problemCount = plugins.Count(p => p.State is PluginState.Unavailable or PluginState.Failed);
				ImGui.TextColored(ColorWarn, $"{problemCount} plugin(s) have problems. Scenes using their components still load; unknown component data is preserved on save.");
				ImGui.Separator();
			}

			if (!string.IsNullOrEmpty(_statusMessage))
			{
				ImGui.TextColored(_statusIsError ? ColorError : ColorOk, _statusMessage);
				ImGui.Separator();
			}

			if (ImGui.Button("Create New Plugin..."))
			{
				ResetCreateForm();
				_showCreatePopup = true;
			}
			if (ImGui.IsItemHovered())
				ImGui.SetTooltip("Scaffold a new plugin folder (plugin.json + starter code) and optionally add it to this project.");

			DrawAddPluginSection();
			DrawCreatePluginPopup();

			// Drive the native/ImGui folder dialogs and apply their results.
			_pluginFolderBrowser.Draw("Select Plugin Folder");
			if (_pluginFolderBrowser.TryTakeResult(out var pluginFolder))
				_addPath = MakeProjectRelativeIfReasonable(pluginFolder);

			_sdkFolderBrowser.Draw("Select SDK Folder");
			if (_sdkFolderBrowser.TryTakeResult(out var sdkFolder) && _sdkBrowseTargetId != null)
			{
				_sdkPathBuffers[_sdkBrowseTargetId] = sdkFolder;
				_sdkBrowseTargetId = null;
			}

			_createLocationBrowser.Draw("Select Location Folder");
			if (_createLocationBrowser.TryTakeResult(out var createLocation))
				_newLocation = createLocation;

			if (plugins.Count == 0)
			{
				ImGui.TextColored(ColorMuted, "This project has no plugins yet. Add one above.");
				ImGui.End();
				return;
			}

			if (ImGui.BeginTable("PluginsTable", 6, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.SizingStretchProp))
			{
				ImGui.TableSetupColumn("Plugin", ImGuiTableColumnFlags.WidthStretch, 1.8f);
				ImGui.TableSetupColumn("Description", ImGuiTableColumnFlags.WidthStretch, 2.4f);
				ImGui.TableSetupColumn("Version", ImGuiTableColumnFlags.WidthStretch, 0.7f);
				ImGui.TableSetupColumn("Source", ImGuiTableColumnFlags.WidthStretch, 1.8f);
				ImGui.TableSetupColumn("Status", ImGuiTableColumnFlags.WidthStretch, 0.8f);
				ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthStretch, 1.6f);
				ImGui.TableHeadersRow();

				foreach (var plugin in plugins.ToList())
				{
					ImGui.PushID(plugin.Id ?? "?");
					ImGui.TableNextRow();

					ImGui.TableNextColumn();
					ImGui.TextUnformatted(plugin.DisplayName ?? "(unknown)");
					if (plugin.Manifest != null)
					{
						if (!string.IsNullOrWhiteSpace(plugin.Manifest.Author) && ImGui.IsItemHovered())
							ImGui.SetTooltip($"by {plugin.Manifest.Author}");
						ImGui.SameLine();
						ImGui.TextColored(ColorMuted, $"({plugin.Id})");
					}

					ImGui.TableNextColumn();
					var description = plugin.Manifest?.Description;
					if (string.IsNullOrWhiteSpace(description))
					{
						ImGui.TextColored(ColorMuted, "—");
					}
					else
					{
						ImGui.TextWrapped(description);
						if (ImGui.IsItemHovered())
							ImGui.SetTooltip(description);
					}

					ImGui.TableNextColumn();
					ImGui.TextUnformatted(plugin.Manifest?.Version ?? "—");

					ImGui.TableNextColumn();
					ImGui.TextUnformatted(plugin.Entry?.Source?.Describe() ?? "—");
					if (plugin.Entry is { Dev: true })
					{
						ImGui.SameLine();
						ImGui.TextColored(ColorWarn, "[live edit]");
						if (ImGui.IsItemHovered())
							ImGui.SetTooltip("You're editing this plugin: the editor uses its folder directly and picks up your changes automatically.");
					}

					ImGui.TableNextColumn();
					DrawStatus(plugin);

					ImGui.TableNextColumn();
					DrawActions(plugin);

					ImGui.PopID();
				}

				ImGui.EndTable();
			}

			// Full error text for any problematic plugin, below the table where it has room to wrap.
			foreach (var plugin in plugins.Where(p => p.Error != null))
			{
				ImGui.Spacing();
				ImGui.TextColored(ColorError, $"{plugin.Id}:");
				ImGui.SameLine();
				ImGui.TextWrapped(plugin.Error);
			}

			DrawExternalSdkSection(plugins);

			ImGui.End();
		}

		/// <summary>
		/// The "Add Plugin" form: pick a source kind (bundled dropdown / local folder / git URL / zip
		/// URL), fill its fields, and add. The plugin's id is discovered from the resolved manifest.
		/// </summary>
		private void DrawAddPluginSection()
		{
			if (!ImGui.CollapsingHeader("Add Plugin"))
				return;

			ImGui.Indent();

			ImGui.SetNextItemWidth(220);
			ImGui.Combo("Source", ref _addSourceType, SourceTypes, SourceTypes.Length);

			ProjectPluginEntry entry = null;

			switch (_addSourceType)
			{
				case 0: // Bundled
					_bundledIds ??= PluginResolver.GetAvailableBundledPluginIds().ToArray();
					if (_bundledIds.Length == 0)
					{
						ImGui.TextColored(ColorMuted, "No bundled plugins ship with this editor build.");
					}
					else
					{
						ImGui.SetNextItemWidth(320);
						ImGui.Combo("Plugin##bundled", ref _addBundledIndex, _bundledIds, _bundledIds.Length);
						entry = new ProjectPluginEntry
						{
							Id = _bundledIds[System.Math.Clamp(_addBundledIndex, 0, _bundledIds.Length - 1)],
							Source = new PluginSourceSpec { Bundled = true },
						};
					}
					break;

				case 1: // Local folder
					ImGui.SetNextItemWidth(-100);
					ImGui.InputText("##addpath", ref _addPath, 1024);
					ImGui.SameLine();
					if (ImGui.Button("Browse...", new Num.Vector2(85, 0)))
						_pluginFolderBrowser.Open("Select plugin folder", _addPath, this);
					ImGui.Checkbox("I'm editing this plugin", ref _addDev);
					if (ImGui.IsItemHovered())
						ImGui.SetTooltip(
							"Turn this ON only if you are building or editing this plugin yourself.\n\n" +
							"ON  – the editor uses your folder directly and picks up your changes\n" +
							"        automatically (scripts reload live). Best while developing.\n\n" +
							"OFF – the editor takes a fixed snapshot of the folder now. Later edits\n" +
							"        won't apply until you press \"Update\". Best for a plugin you only\n" +
							"        want to use, not change.");
					if (!string.IsNullOrWhiteSpace(_addPath))
						entry = new ProjectPluginEntry { Source = new PluginSourceSpec { Path = _addPath.Trim() }, Dev = _addDev };
					break;

				case 2: // Git URL
					ImGui.SetNextItemWidth(-160);
					ImGui.InputText("Git URL", ref _addGitUrl, 1024);
					ImGui.SetNextItemWidth(220);
					ImGui.InputText("Ref (tag/branch/commit)", ref _addGitRef, 256);
					if (ImGui.IsItemHovered())
						ImGui.SetTooltip("Pinned to a commit SHA in plugins.lock.json. Private repos use your local git credentials.");
					if (!string.IsNullOrWhiteSpace(_addGitUrl) && !string.IsNullOrWhiteSpace(_addGitRef))
						entry = new ProjectPluginEntry { Source = new PluginSourceSpec { Git = _addGitUrl.Trim(), Ref = _addGitRef.Trim() } };
					break;

				case 3: // Zip URL
					ImGui.SetNextItemWidth(-160);
					ImGui.InputText("Zip URL", ref _addZipUrl, 1024);
					if (!string.IsNullOrWhiteSpace(_addZipUrl))
						entry = new ProjectPluginEntry { Source = new PluginSourceSpec { Zip = _addZipUrl.Trim() } };
					break;
			}

			ImGui.Spacing();

			var canAdd = entry != null;
			if (!canAdd)
				ImGui.BeginDisabled();

			if (ImGui.Button("Add Plugin", new Num.Vector2(140, 0)))
			{
				var result = PluginManager.Instance.AddPlugin(entry);
				// A successful add is the only message that starts with "Added"; everything else
				// (missing plugin.json, invalid source, duplicate, unavailable, ...) is an error.
				var success = result != null && result.StartsWith("Added");
				_addStatusMessage = result;
				_addStatusIsError = !success;

				// Also surface it in the always-visible top banner so it can't be missed even if this
				// section is collapsed/scrolled away.
				SetStatus(result);

				if (success)
				{
					_addPath = _addGitUrl = _addGitRef = _addZipUrl = "";
					_addDev = false;
				}
			}

			if (!canAdd)
				ImGui.EndDisabled();

			ImGui.SameLine();
			ImGui.TextColored(ColorMuted, "Fetches, verifies, and loads the plugin. Private git repos use your local credentials.");

			// Show the add result right here (red on failure) so it's next to the button, not buried
			// above the plugin list.
			if (!string.IsNullOrEmpty(_addStatusMessage))
			{
				ImGui.Spacing();
				ImGui.PushStyleColor(ImGuiCol.Text, _addStatusIsError ? ColorError : ColorOk);
				ImGui.TextWrapped(_addStatusMessage);
				ImGui.PopStyleColor();
			}

			ImGui.Unindent();
			ImGui.Separator();
		}

		private void ResetCreateForm()
		{
			_newName = "My Plugin";
			_newId = PluginScaffolder.SuggestId(_newName);
			_newIdEdited = false;
			_newDescription = "";
			_newAuthor = "";
			_newVersion = "1.0.0";
			_newGameplay = true;
			_newEditor = false;
			_newAddToProject = true;
			_createStatusMessage = null;
			_createStatusIsError = false;

			// Default the location to the folder that holds the current project, so new plugins land next to it.
			_newLocation = ProjectManager.Instance.HasActiveProject
				? Path.GetDirectoryName(ProjectManager.Instance.CurrentProject.ProjectPath) ?? ""
				: "";
		}

		/// <summary>
		/// The "Create New Plugin" modal: collects name/id/description/kind/location, scaffolds the package
		/// (plugin.json + starter code), and optionally adds it to the project as a live-edit plugin.
		/// </summary>
		private void DrawCreatePluginPopup()
		{
			if (_showCreatePopup)
			{
				ImGui.OpenPopup("create-plugin");
				_showCreatePopup = false;
			}

			var center = ImGui.GetMainViewport().GetCenter();
			ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new Num.Vector2(0.5f, 0.5f));
			ImGui.SetNextWindowSize(new Num.Vector2(680, 0), ImGuiCond.Appearing);

			var open = true;
			if (!ImGui.BeginPopupModal("create-plugin", ref open, ImGuiWindowFlags.None))
				return;

			ImGui.TextColored(new Num.Vector4(0.2f, 0.8f, 1f, 1f), "Create New Plugin");
			ImGui.Separator();

			ImGui.TextUnformatted("Name");
			ImGui.SetNextItemWidth(-1);
			if (ImGui.InputText("##name", ref _newName, 128) && !_newIdEdited)
				_newId = PluginScaffolder.SuggestId(_newName);

			ImGui.TextUnformatted("Id");
			ImGui.SetNextItemWidth(-1);
			if (ImGui.InputText("##id", ref _newId, 128))
				_newIdEdited = true;
			if (ImGui.IsItemHovered())
				ImGui.SetTooltip("A unique, permanent id in reverse-domain style (e.g. com.you.myplugin). Don't change it later — saved scenes rely on it.");

			ImGui.TextUnformatted("Description");
			ImGui.SetNextItemWidth(-1);
			ImGui.InputTextMultiline("##description", ref _newDescription, 512, new Num.Vector2(-1, 54));

			ImGui.TextUnformatted("Author");
			ImGui.SetNextItemWidth(-1);
			ImGui.InputText("##author", ref _newAuthor, 128);

			ImGui.TextUnformatted("Version");
			ImGui.SetNextItemWidth(120);
			ImGui.InputText("##version", ref _newVersion, 32);

			ImGui.Spacing();
			ImGui.TextUnformatted("What does this plugin add?");
			ImGui.Checkbox("Gameplay (components used in the game)", ref _newGameplay);
			ImGui.Checkbox("Editor tools (windows/menus in this editor)", ref _newEditor);

			ImGui.Spacing();
			ImGui.TextUnformatted("Location");
			ImGui.SetNextItemWidth(-90);
			ImGui.InputText("##location", ref _newLocation, 1024);
			ImGui.SameLine();
			if (ImGui.Button("Browse...", new Num.Vector2(80, 0)))
				_createLocationBrowser.Open("Select where to create the plugin", _newLocation, this);
			if (ImGui.IsItemHovered())
				ImGui.SetTooltip("The new plugin folder is created inside this location.");

			// Auto-add only works for a gameplay-only plugin: an editor plugin's DLL doesn't exist until
			// you build it, so its manifest can't be validated/added yet.
			var canAutoAdd = _newGameplay && !_newEditor && ProjectManager.Instance.HasActiveProject;
			if (!canAutoAdd)
				ImGui.BeginDisabled();
			ImGui.Checkbox("Add to this project now (as a live-edit plugin)", ref _newAddToProject);
			if (!canAutoAdd)
				ImGui.EndDisabled();
			if (_newEditor && ImGui.IsItemHovered())
				ImGui.SetTooltip("Editor plugins must be built first, then added manually — see the generated README.");

			if (!string.IsNullOrEmpty(_createStatusMessage))
			{
				ImGui.Spacing();
				ImGui.PushStyleColor(ImGuiCol.Text, _createStatusIsError ? ColorError : ColorOk);
				ImGui.TextWrapped(_createStatusMessage);
				ImGui.PopStyleColor();
			}

			ImGui.Separator();
			if (ImGui.Button("Create", new Num.Vector2(120, 0)))
				DoCreatePlugin(canAutoAdd);

			ImGui.SameLine();
			if (ImGui.Button("Cancel", new Num.Vector2(120, 0)))
				ImGui.CloseCurrentPopup();

			ImGui.EndPopup();
		}

		private void DoCreatePlugin(bool canAutoAdd)
		{
			var result = PluginScaffolder.Create(new PluginScaffolder.Options
			{
				Location = _newLocation,
				Name = _newName,
				Id = _newId,
				Version = _newVersion,
				Description = _newDescription,
				Author = _newAuthor,
				Gameplay = _newGameplay,
				Editor = _newEditor,
			});

			if (!result.Success)
			{
				// Keep the popup open so the user can fix the input; show the reason in red.
				_createStatusMessage = result.Message;
				_createStatusIsError = true;
				return;
			}

			var summary = result.Message;

			if (_newAddToProject && canAutoAdd)
			{
				var entry = new ProjectPluginEntry
				{
					Source = new PluginSourceSpec { Path = MakeProjectRelativeIfReasonable(result.PluginRoot) },
					Dev = true, // live-edit: pick up the author's changes as they work
				};
				summary += " " + PluginManager.Instance.AddPlugin(entry);
			}

			SetStatus(summary);
			ImGui.CloseCurrentPopup();
		}

		/// <summary>
		/// Converts a browsed absolute folder to a project-relative path when that's sensible (same volume,
		/// keeps plugins.json portable across teammates who share the repo layout); otherwise keeps it
		/// absolute. Mirrors how plugins.json already records local sources like "../MyPlugin".
		/// </summary>
		private static string MakeProjectRelativeIfReasonable(string absolutePath)
		{
			if (string.IsNullOrWhiteSpace(absolutePath) || !ProjectManager.Instance.HasActiveProject)
				return absolutePath;

			var projectPath = ProjectManager.Instance.CurrentProject.ProjectPath;
			try
			{
				var rel = Path.GetRelativePath(projectPath, absolutePath);
				return Path.IsPathRooted(rel) ? absolutePath : rel;
			}
			catch
			{
				return absolutePath;
			}
		}

		private void DrawActions(PluginInstance plugin)
		{
			var disabled = plugin.State == PluginState.Disabled;

			if (ImGui.SmallButton(disabled ? "Enable" : "Disable"))
				SetStatus(PluginManager.Instance.SetPluginDisabled(plugin.Id, !disabled));

			// Bundled plugins version with the editor; dev plugins re-sync automatically — neither updates.
			var canUpdate = plugin.Entry is { Dev: false, Source.Bundled: false };
			if (canUpdate)
			{
				ImGui.SameLine();
				if (ImGui.SmallButton("Update"))
					SetStatus(PluginManager.Instance.UpdatePlugin(plugin.Id));
				if (ImGui.IsItemHovered())
					ImGui.SetTooltip("Re-resolves the source (latest ref/zip/folder content) and re-pins plugins.lock.json.");
			}

			ImGui.SameLine();
			if (ImGui.SmallButton("Remove"))
				ImGui.OpenPopup("ConfirmRemove");

			if (ImGui.BeginPopup("ConfirmRemove"))
			{
				ImGui.TextWrapped($"Remove plugin '{plugin.DisplayName}' from this project? Scenes using its components will show missing-component entries (data is preserved).");
				if (ImGui.Button("Remove"))
				{
					SetStatus(PluginManager.Instance.RemovePlugin(plugin.Id));
					ImGui.CloseCurrentPopup();
				}
				ImGui.SameLine();
				if (ImGui.Button("Cancel"))
					ImGui.CloseCurrentPopup();
				ImGui.EndPopup();
			}
		}

		// SDK path configuration for plugins that declare external (non-redistributable) SDKs.
		private void DrawExternalSdkSection(IReadOnlyList<PluginInstance> plugins)
		{
			var sdks = plugins
				.Where(p => p.Manifest?.ExternalSdks is { Count: > 0 })
				.SelectMany(p => p.Manifest.ExternalSdks.Select(sdk => (Plugin: p, Sdk: sdk)))
				.ToList();

			if (sdks.Count == 0)
				return;

			ImGui.Spacing();
			ImGui.SeparatorText("External SDKs");
			ImGui.TextColored(ColorMuted, "These SDKs cannot be redistributed with plugins — point the editor at your local installs. Paths are per-user (never committed).");
			ImGui.Spacing();

			foreach (var (plugin, sdk) in sdks)
			{
				ImGui.PushID(sdk.Id);

				var resolvedRoot = PluginUserSettings.ResolveSdkRoot(sdk);
				var label = sdk.DisplayName ?? sdk.Id;

				ImGui.TextUnformatted(label);
				ImGui.SameLine();
				if (resolvedRoot != null)
					ImGui.TextColored(ColorOk, "(found)");
				else
					ImGui.TextColored(ColorError, sdk.Required ? "(missing — plugin unavailable)" : "(missing — optional)");

				if (!_sdkPathBuffers.TryGetValue(sdk.Id, out var buffer))
					buffer = PluginUserSettings.GetConfiguredSdkPath(sdk.Id);

				ImGui.SetNextItemWidth(-210);
				if (ImGui.InputText($"##sdkpath_{sdk.Id}", ref buffer, 512))
					_sdkPathBuffers[sdk.Id] = buffer;

				ImGui.SameLine();
				if (ImGui.Button("Browse..."))
				{
					_sdkBrowseTargetId = sdk.Id;
					_sdkFolderBrowser.Open($"Select {sdk.DisplayName ?? sdk.Id} folder", buffer, this);
				}

				ImGui.SameLine();
				if (ImGui.Button("Save Path"))
				{
					PluginUserSettings.SetSdkPath(sdk.Id, buffer);
					_sdkPathBuffers.Remove(sdk.Id);
					SetStatus($"SDK path saved. Reopen the project so '{plugin.Id}' can pull its SDK files.");
				}

				if (!string.IsNullOrWhiteSpace(sdk.EnvVar))
					ImGui.TextColored(ColorMuted, $"Fallback environment variable: {sdk.EnvVar}");

				ImGui.Spacing();
				ImGui.PopID();
			}
		}

		// Records a table-action status message and classifies whether it reads as an error (red).
		private void SetStatus(string message)
		{
			_statusMessage = message;
			_statusIsError = IsErrorStatus(message);
		}

		/// <summary>
		/// Heuristic error classification for the plugin action messages returned by PluginManager
		/// (add/update/remove/disable) so failures render in red rather than the success color.
		/// </summary>
		private static bool IsErrorStatus(string message)
		{
			if (string.IsNullOrEmpty(message))
				return false;

			string[] errorMarkers =
			{
				"Could not", "failed", "not found", "No project", "no plugins.json",
				"Cannot", "invalid", "already in this project", "Choose", "unavailable",
			};

			return errorMarkers.Any(m => message.Contains(m, StringComparison.OrdinalIgnoreCase));
		}

		private static void DrawStatus(PluginInstance plugin)
		{
			switch (plugin.State)
			{
				case PluginState.Loaded:
					ImGui.TextColored(ColorOk, "Loaded");
					break;
				case PluginState.Restored:
					ImGui.TextColored(ColorOk, "Restored");
					break;
				case PluginState.Disabled:
					ImGui.TextColored(ColorMuted, "Disabled");
					break;
				case PluginState.Unavailable:
					ImGui.TextColored(ColorError, "Unavailable");
					break;
				case PluginState.Failed:
					ImGui.TextColored(ColorError, "Failed");
					break;
			}

			if (plugin.Error != null && ImGui.IsItemHovered())
				ImGui.SetTooltip(plugin.Error);
		}
	}
}
