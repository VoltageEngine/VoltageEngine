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

		/// <summary>
		/// Results of plugin actions, oldest first. A list rather than one slot because these arrive from
		/// several places - installs finishing on a worker, table actions, SDK edits - and a single slot
		/// meant the last one silently erased whichever you had not read yet.
		/// </summary>
		private sealed class StatusMessage
		{
			public int Id;
			public string Text;
			public bool IsError;
		}

		private readonly List<StatusMessage> _messages = new();
		private int _nextMessageId;

		/// <summary>Plugin problems the user has dismissed, keyed by id and text.</summary>
		private readonly HashSet<string> _dismissedProblems = new(StringComparer.Ordinal);

		/// <summary>Enough to see a burst of results; beyond that the oldest are dropped.</summary>
		private const int MaxMessages = 20;

		/// <summary>Edit buffers for SDK path inputs, keyed by sdk id.</summary>
		private readonly Dictionary<string, string> _sdkPathBuffers = new();

		// "Add Plugin" form state. No "Bundled" source: nothing ships inside the editor any more, so the
		// option could only ever list a leftover from an older build. PluginResolver still resolves
		// Bundled entries, so a plugins.json written before the change keeps loading.
		private static readonly string[] SourceTypes = { "Local folder", "Git URL", "Zip URL" };
		private int _addSourceType;
		private string _addPath = "";
		private bool _addDev;
		private string _addGitUrl = "";
		private string _addGitRef = "";
		private string _addZipUrl = "";

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

			// Finish any completed download on this thread before reading the list, so the new plugin is
			// visible in the same frame it lands.
			PluginInstaller.Pump();

			// Snapshot once: an install runs on a background thread and can add to this list mid-frame,
			// which would otherwise throw part-way through drawing.
			var plugins = PluginManager.Instance.Plugins.ToList();

			DrawMessages(plugins);
			DrawActiveInstalls(); // Live progress stays outside the dropdown

			if (ImGui.Button("Create New Plugin..."))
			{
				ResetCreateForm();
				_showCreatePopup = true;
			}
			if (ImGui.IsItemHovered())
				ImGui.SetTooltip("Scaffold a new plugin folder (plugin.json + starter code) and optionally add it to this project.");

			DrawBrowsePluginsSection();
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
				ImGui.TableSetupColumn("Version", ImGuiTableColumnFlags.WidthStretch, 1.1f);
				ImGui.TableSetupColumn("Source", ImGuiTableColumnFlags.WidthStretch, 1.8f);
				ImGui.TableSetupColumn("Status", ImGuiTableColumnFlags.WidthStretch, 0.8f);
				ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthStretch, 1.6f);
				ImGui.TableHeadersRow();

				foreach (var plugin in plugins)
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
						ImGui.TextColored(ColorMuted, "-");
					}
					else
					{
						ImGui.TextWrapped(description);
						if (ImGui.IsItemHovered())
							ImGui.SetTooltip(description);
					}

					ImGui.TableNextColumn();
					ImGui.TextUnformatted(plugin.Manifest?.Version ?? "-");

					// The one place you would look to find out you are behind.
					var newer = PluginRegistryIndex.FindUpdateFor(plugin.Id, plugin.Manifest?.Version);
					if (newer != null)
					{
						ImGui.SameLine();
						ImGui.TextColored(ColorWarn, "-> " + newer.VersionLabel);
						if (ImGui.IsItemHovered())
							ImGui.SetTooltip($"{newer.RegistryName ?? "The registry"} lists {newer.VersionLabel}. Press Update to fetch it.");
					}

					ImGui.TableNextColumn();
					ImGui.TextUnformatted(plugin.Entry?.Source?.Describe() ?? "-");
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

			DrawPublishReadinessSection(plugins);
			DrawExternalSdkSection(plugins);

			ImGui.End();
		}

		/// <summary>
		/// The "Add Plugin" form: pick a source kind (bundled dropdown / local folder / git URL / zip
		/// URL), fill its fields, and add. The plugin's id is discovered from the resolved manifest.
		/// </summary>
		private string _browseSearch = string.Empty;
		private bool _browseOpenedOnce;

		/// <summary>
		/// The catalogue: search a registry and install with one click.
		/// </summary>
		private void DrawBrowsePluginsSection()
		{
			if (!ImGui.CollapsingHeader("Browse Plugins"))
				return;

			ImGui.Indent();

			if (!_browseOpenedOnce)
			{
				_browseOpenedOnce = true;
				PluginRegistryIndex.LoadCache();
				PluginRegistryIndex.RefreshAsync();
			}

			ImGui.SetNextItemWidth(260);
			ImGui.InputTextWithHint("##pluginsearch", "Search plugins...", ref _browseSearch, 128);
			ImGui.SameLine();
			if (ImGui.Button("Refresh"))
				PluginRegistryIndex.RefreshAsync();

			if (PluginRegistryIndex.IsFetching)
			{
				ImGui.SameLine();
				ImGui.TextColored(ColorWarn, "fetching...");
			}

			if (PluginRegistryIndex.LastError != null && !PluginRegistryIndex.HasEntries)
			{
				ImGui.TextWrapped(PluginRegistryIndex.LastError);
				ImGui.Unindent();
				return;
			}

			if (PluginRegistryIndex.LastError != null)
				ImGui.TextColored(ColorWarn, "Showing a cached list - the registry could not be reached.");

			// Built with the indexer rather than ToDictionary: a malformed plugins.json can list an id
			// twice, and a duplicate key would throw here instead of anywhere useful.
			var installedVersions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			foreach (var installed in PluginManager.Instance.Plugins)
			{
				if (!string.IsNullOrEmpty(installed.Id))
					installedVersions[installed.Id] = installed.Manifest?.Version;
			}

			var results = PluginRegistryIndex.Search(_browseSearch, installedVersions);

			if (results.Count == 0)
			{
				ImGui.TextColored(ColorMuted, PluginRegistryIndex.HasEntries
					? "No matches (plugins already in this project are hidden unless the registry has a newer version)."
					: "The registry list is empty.");
				ImGui.Unindent();
				return;
			}

			foreach (var listing in results)
			{
				ImGui.PushID(listing.Id);

				var expanded = ImGui.TreeNodeEx(listing.Name ?? listing.Id, ImGuiTreeNodeFlags.FramePadding);

				// On the collapsed row, not just inside: what Install downloads has to be readable before
				// anyone reaches for the button.
				ImGui.SameLine();
				ImGui.TextColored(ColorMuted, listing.VersionLabel);
				if (ImGui.IsItemHovered())
					ImGui.SetTooltip(VersionTooltip(listing));

				ImGui.Indent();

				var job = FindJob(listing.Id);
				if (job is { IsFinished: false })
				{
					DrawInstallProgress(job, compact: false);
				}
				else if (listing.IsInstallable)
				{
					// Present only when this listing is an upgrade: Search keeps an installed plugin
					// visible for exactly that case, and adding it again would be rejected as a duplicate.
					var isUpgrade = installedVersions.TryGetValue(listing.Id, out var installedVersion);

					if (PluginInstaller.IsBusy)
						ImGui.BeginDisabled();

					var verb = isUpgrade ? "Update to" : "Install";
					if (ImGui.Button($"{verb} {listing.VersionLabel}##install-{listing.Id}", new Num.Vector2(0, 0)))
					{
						if (isUpgrade)
							StartUpdate(listing.Id, listing.Name ?? listing.Id);
						else
							InstallFromRegistry(listing);
					}

					if (ImGui.IsItemHovered())
						ImGui.SetTooltip(VersionTooltip(listing));

					if (PluginInstaller.IsBusy)
						ImGui.EndDisabled();

					if (isUpgrade)
					{
						ImGui.SameLine();
						ImGui.TextColored(ColorWarn, $"installed: {installedVersion ?? "unknown"}");
					}

					if (!string.IsNullOrEmpty(listing.Author))
					{
						ImGui.SameLine();
						ImGui.TextColored(ColorMuted, $"by {listing.Author}");
					}
				}
				else
				{
					ImGui.TextColored(ColorWarn, "[no source] - this listing has no Zip or Git source.");
				}

				ImGui.Unindent();

				if (expanded)
				{
					if (!string.IsNullOrEmpty(listing.Description))
						ImGui.TextWrapped(listing.Description);

					if (listing.Tags is { Count: > 0 })
						ImGui.TextColored(ColorMuted, string.Join("  ", listing.Tags.Select(t => "#" + t)));

					if (!string.IsNullOrEmpty(listing.RegistryName))
						ImGui.TextColored(ColorMuted, $"from {listing.RegistryName}");

					ImGui.TreePop();
				}

				ImGui.Separator();
				ImGui.PopID();
			}

			ImGui.Unindent();
		}

		private void InstallFromRegistry(PluginRegistryEntry listing)
		{
			var started = PluginInstaller.Start(
				new ProjectPluginEntry { Id = listing.Id, Source = listing.ToSourceSpec() },
				listing.Name ?? listing.Id);

			if (started == null)
				SetStatus("Another install is already running.");
		}

		/// <summary>
		/// Runs an update through the same worker as an install. The fetch is identical in size, and doing
		/// it inline would freeze the editor for its duration.
		/// </summary>
		private void StartUpdate(string pluginId, string displayName)
		{
			var entry = PluginManager.Instance.PrepareUpdate(pluginId, out var message);
			if (entry == null)
			{
				SetStatus(message, isError: true);
				return;
			}

			if (PluginInstaller.Start(entry, displayName, isUpdate: true) == null)
				SetStatus("Another install is already running.");
		}

		/// <summary>
		/// Spells out what the version label means for this listing, since "v1.2.0" and "default branch"
		/// promise very different things, and names the URL it came from.
		/// </summary>
		private static string VersionTooltip(PluginRegistryEntry listing)
		{
			var source = !string.IsNullOrWhiteSpace(listing.Zip) ? listing.Zip : listing.Git;

			var what = !string.IsNullOrWhiteSpace(listing.Ref)
				? $"Installs {listing.Ref.Trim()}, the ref this listing pins."
				: !string.IsNullOrWhiteSpace(listing.Zip)
					? $"Installs {listing.VersionLabel}, read from the release URL."
					: "This listing pins no ref, so you get whatever the default branch holds right now.";

			var engine = string.IsNullOrWhiteSpace(listing.EngineVersion) || listing.EngineVersion == "*"
				? "Claims no engine requirement."
				: $"Claims engine {listing.EngineVersion}.";

			return string.IsNullOrWhiteSpace(source)
				? what + "\n" + engine
				: what + "\n" + engine + "\n\n" + source;
		}

		private static PluginInstallJob FindJob(string pluginId) =>
			PluginInstaller.Jobs.FirstOrDefault(j => string.Equals(j.PluginId, pluginId, StringComparison.Ordinal));

		private static string Bytes(long value) => value switch
		{
			>= 1024 * 1024 => $"{value / (1024f * 1024f):0.0} MB",
			>= 1024 => $"{value / 1024f:0} KB",
			_ => $"{value} B",
		};

		/// <summary>Progress for a running install, with a way out when it stops responding.</summary>
		private void DrawInstallProgress(PluginInstallJob job, bool compact)
		{
			if (job.State is PluginInstallState.Working or PluginInstallState.ReadyToApply)
			{
				ImGui.TextColored(ColorWarn, job.State == PluginInstallState.Working ? "unpacking..." : "installing...");
				return;
			}

			var progress = job.Progress;
			var width = compact ? 90f : 220f;

			if (progress >= 0f)
			{
				ImGui.PushItemWidth(width);
				ImGui.ProgressBar(progress, new Num.Vector2(width, 0), $"{progress * 100f:0}%");
				ImGui.PopItemWidth();
			}
			else
			{
				// No Content-Length, so a bar would be a lie; show what has actually arrived.
				ImGui.TextColored(ColorMuted, Bytes(job.BytesRead));
			}

			if (!compact && job.TotalBytes > 0)
			{
				ImGui.SameLine();
				ImGui.TextColored(ColorMuted, $"{Bytes(job.BytesRead)} / {Bytes(job.TotalBytes)}");
			}

			if (job.Stalled)
			{
				ImGui.SameLine();
				ImGui.TextColored(ColorWarn, compact ? "stalled" : $"stalled for {job.SecondsSinceProgress}s");
			}

			ImGui.SameLine();
			if (ImGui.SmallButton($"Cancel##{job.PluginId}"))
				job.Cancel();
		}

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
				case 0: // Local folder
					ImGui.SetNextItemWidth(-100);
					ImGui.InputText("##addpath", ref _addPath, 1024);
					ImGui.SameLine();
					if (ImGui.Button("Browse...", new Num.Vector2(85, 0)))
						_pluginFolderBrowser.Open("Select plugin folder", _addPath, this);
					ImGui.Checkbox("I'm editing this plugin", ref _addDev);
					if (ImGui.IsItemHovered())
						ImGui.SetTooltip(
							"Turn this ON only if you are building or editing this plugin yourself.\n\n" +
							"ON  - the editor uses your folder directly and picks up your changes\n" +
							"        automatically (scripts reload live). Best while developing.\n\n" +
							"OFF - the editor takes a fixed snapshot of the folder now. Later edits\n" +
							"        won't apply until you press \"Update\". Best for a plugin you only\n" +
							"        want to use, not change.");
					if (!string.IsNullOrWhiteSpace(_addPath))
						entry = new ProjectPluginEntry { Source = new PluginSourceSpec { Path = _addPath.Trim() }, Dev = _addDev };
					break;

				case 1: // Git URL
					ImGui.SetNextItemWidth(-160);
					ImGui.InputText("Git URL", ref _addGitUrl, 1024);
					ImGui.SetNextItemWidth(220);
					ImGui.InputText("Ref (tag/branch/commit)", ref _addGitRef, 256);
					if (ImGui.IsItemHovered())
						ImGui.SetTooltip("Pinned to a commit SHA in plugins.lock.json. Private repos use your local git credentials.");
					if (!string.IsNullOrWhiteSpace(_addGitUrl) && !string.IsNullOrWhiteSpace(_addGitRef))
						entry = new ProjectPluginEntry { Source = new PluginSourceSpec { Git = _addGitUrl.Trim(), Ref = _addGitRef.Trim() } };
					break;

				case 2: // Zip URL
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
				// Same path as Browse: a zip or git URL here would otherwise freeze the editor for the
				// length of the fetch. The result lands in the install-jobs list above.
				var started = PluginInstaller.Start(entry, entry.Source?.Describe() ?? "plugin");
				if (started == null)
				{
					SetStatus("Another install is already running.", isError: true);
				}
				else
				{
					_addPath = _addGitUrl = _addGitRef = _addZipUrl = "";
					_addDev = false;
				}
			}

			if (!canAdd)
				ImGui.EndDisabled();

			ImGui.SameLine();
			ImGui.TextColored(ColorMuted, "Fetches, verifies, and loads the plugin. Private git repos use your local credentials.");

			// The result goes to the message list at the top rather than here: the install runs on a
			// worker now, so it does not arrive while this section is still on screen.

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
				ImGui.SetTooltip("A unique, permanent id in reverse-domain style (e.g. com.you.myplugin). Don't change it later - saved scenes rely on it.");

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
				ImGui.SetTooltip("Editor plugins must be built first, then added manually - see the generated README.");

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

			// Bundled plugins version with the editor; dev plugins re-sync automatically - neither updates.
			var canUpdate = plugin.Entry is { Dev: false, Source.Bundled: false };
			if (canUpdate)
			{
				var newer = PluginRegistryIndex.FindUpdateFor(plugin.Id, plugin.Manifest?.Version);

				ImGui.SameLine();
				if (PluginInstaller.IsBusy)
					ImGui.BeginDisabled();

				if (newer != null)
					ImGui.PushStyleColor(ImGuiCol.Text, ColorWarn);

				if (ImGui.SmallButton(newer != null ? $"Update to {newer.VersionLabel}" : "Update"))
					StartUpdate(plugin.Id, plugin.DisplayName);

				if (newer != null)
					ImGui.PopStyleColor();

				if (PluginInstaller.IsBusy)
					ImGui.EndDisabled();

				if (ImGui.IsItemHovered())
				{
					ImGui.SetTooltip(newer != null
						? $"Re-points this plugin at {newer.VersionLabel} in {newer.RegistryName ?? "the registry"}, fetches it, and re-pins plugins.lock.json."
						: "Re-resolves the source (latest ref/zip/folder content) and re-pins plugins.lock.json.\n\n" +
						  "A source pinned to a fixed tag or a versioned zip has nothing newer to give, so this\n" +
						  "only changes anything once the registry lists a newer release.");
				}
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

		/// <summary>
		/// For a plugin you author: which step of tag -> release -> registry is missing, and the command
		/// that fixes it. Every one of those failures otherwise looks identical - the plugin just never
		/// shows up in Browse Plugins.
		/// </summary>
		private void DrawPublishReadinessSection(IReadOnlyList<PluginInstance> plugins)
		{
			var authorable = plugins.Where(PluginPublishReadiness.IsAuthorable).ToList();
			if (authorable.Count == 0)
				return;

			ImGui.Spacing();
			if (!ImGui.CollapsingHeader("Publish Readiness"))
				return;

			ImGui.Indent();
			ImGui.TextColored(ColorMuted,
				"Checks a plugin you are authoring. Read-only: it runs your git and one anonymous GitHub request, never writes.");
			ImGui.Spacing();

			foreach (var plugin in authorable)
			{
				ImGui.PushID("publish-" + plugin.Id);

				var report = PluginPublishReadiness.Get(plugin.Id);
				var expanded = ImGui.TreeNodeEx(plugin.DisplayName ?? plugin.Id, ImGuiTreeNodeFlags.DefaultOpen);

				// Own row, same reason as Install: a button sharing a tree node's row loses the click.
				ImGui.Indent();
				if (report is { Running: true })
					ImGui.TextColored(ColorWarn, "checking...");
				else if (ImGui.Button($"Check##check-{plugin.Id}", new Num.Vector2(110, 0)))
					PluginPublishReadiness.RefreshAsync(plugin);
				ImGui.Unindent();

				if (expanded)
				{
					if (report == null)
						ImGui.TextColored(ColorMuted, "Not checked yet.");
					else if (report.FatalError != null)
						ImGui.TextColored(ColorError, report.FatalError);
					else
						DrawPublishChecks(report);

					ImGui.TreePop();
				}

				ImGui.PopID();
				ImGui.Spacing();
			}

			ImGui.Unindent();
		}

		private void DrawPublishChecks(PublishReport report)
		{
			if (report.Checks.Count == 0 && report.Running)
			{
				ImGui.TextColored(ColorMuted, "Running...");
				return;
			}

			foreach (var check in report.Checks)
			{
				var (color, marker) = check.State switch
				{
					PublishCheckState.Ok => (ColorOk, "[ok]"),
					PublishCheckState.Warning => (ColorWarn, "[!]"),
					PublishCheckState.Blocked => (ColorError, "[x]"),
					_ => (ColorMuted, "[?]"),
				};

				ImGui.TextColored(color, marker);
				ImGui.SameLine();
				ImGui.TextUnformatted(check.Label);
				ImGui.SameLine();
				ImGui.TextColored(ColorMuted, check.Detail ?? "");

				if (string.IsNullOrEmpty(check.Fix))
					continue;

				ImGui.Indent();
				ImGui.TextColored(ColorMuted, check.Fix);
				ImGui.SameLine();
				if (ImGui.SmallButton($"Copy##{check.Label}"))
					ImGui.SetClipboardText(check.Fix);
				ImGui.Unindent();
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
			ImGui.TextColored(ColorMuted, "These SDKs cannot be redistributed with plugins - point the editor at your local installs. Paths are per-user (never committed).");
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
					ImGui.TextColored(ColorError, sdk.Required ? "(missing - plugin unavailable)" : "(missing - optional)");

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

		// Records a plugin action result and classifies whether it reads as an error (red).
		private void SetStatus(string message) => SetStatus(message, IsErrorStatus(message));

		private void SetStatus(string message, bool isError)
		{
			if (string.IsNullOrWhiteSpace(message))
				return;

			_messages.Add(new StatusMessage { Id = _nextMessageId++, Text = message, IsError = isError });

			if (_messages.Count > MaxMessages)
				_messages.RemoveRange(0, _messages.Count - MaxMessages);
		}

		/// <summary>
		/// Every result in one place: action outcomes, finished installs, and problems reported by the
		/// plugins themselves. They used to be spread across a banner, an inline block and two loops under
		/// the table, so a result could appear anywhere depending on what produced it.
		///
		/// <para>A plugin problem is not an event, so dismissing one records the text rather than deleting
		/// it. If the plugin later reports something different, it returns.</para>
		/// </summary>
		private void DrawMessages(IReadOnlyList<PluginInstance> plugins)
		{
			var finishedJobs = PluginInstaller.Jobs.Where(j => j.IsFinished).ToList();

			var problems = plugins
				.SelectMany(p => new[]
				{
					(Plugin: p, Text: p.Error, IsError: true),
					(Plugin: p, Text: p.CompatibilityWarning, IsError: false),
				})
				.Where(x => !string.IsNullOrWhiteSpace(x.Text))
				.Where(x => !_dismissedProblems.Contains(ProblemKey(x.Plugin.Id, x.Text)))
				.ToList();

			var total = _messages.Count + finishedJobs.Count + problems.Count;
			if (total == 0)
				return;

			var anyError = _messages.Any(m => m.IsError)
			               || finishedJobs.Any(j => j.State == PluginInstallState.Failed)
			               || problems.Any(p => p.IsError);

			ImGui.PushStyleColor(ImGuiCol.Text, anyError ? ColorError : ColorOk);
			var open = ImGui.CollapsingHeader($"Messages ({total})###plugin-messages");
			ImGui.PopStyleColor();

			if (!open)
			{
				ImGui.Separator();
				return;
			}

			ImGui.Indent();

			// Always offered, even for a single message, so there is one predictable way to clear.
			if (ImGui.SmallButton("Clear All"))
			{
				_messages.Clear();
				foreach (var job in finishedJobs)
					PluginInstaller.Dismiss(job);
				foreach (var problem in problems)
					_dismissedProblems.Add(ProblemKey(problem.Plugin.Id, problem.Text));
			}

			ImGui.Separator();

			// Newest first: the thing you just did is the thing you want to read.
			for (var i = _messages.Count - 1; i >= 0; i--)
			{
				var message = _messages[i];
				ImGui.PushID(message.Id);
				if (DrawDismissableMessage(message.Text, message.IsError))
					_messages.RemoveAt(i);
				ImGui.PopID();
			}

			foreach (var job in finishedJobs)
			{
				ImGui.PushID("job-" + job.PluginId);

				var text = job.State switch
				{
					// An update's message already names the plugin and both versions.
					PluginInstallState.Succeeded when job.IsUpdate => job.Message,
					PluginInstallState.Succeeded => $"Installed {job.DisplayName}. {job.Message}",
					PluginInstallState.Failed => $"{job.DisplayName} failed: {job.Message ?? "unknown error"}",
					_ => $"{job.DisplayName}: {job.Message}",
				};

				if (DrawDismissableMessage(text, job.State == PluginInstallState.Failed))
					PluginInstaller.Dismiss(job);

				ImGui.PopID();
			}

			foreach (var problem in problems)
			{
				ImGui.PushID("problem-" + problem.Plugin.Id + problem.IsError);

				var label = problem.IsError
					? $"{problem.Plugin.Id}: {problem.Text}"
					: $"{problem.Plugin.Id} (version mismatch): {problem.Text}";

				if (DrawDismissableMessage(label, problem.IsError))
					_dismissedProblems.Add(ProblemKey(problem.Plugin.Id, problem.Text));

				ImGui.PopID();
			}

			ImGui.Unindent();
			ImGui.Separator();
		}

		/// <summary>Returns true when the x was pressed this frame.</summary>
		private bool DrawDismissableMessage(string text, bool isError)
		{
			var dismissed = ImGui.SmallButton("x");

			ImGui.SameLine();
			ImGui.PushStyleColor(ImGuiCol.Text, isError ? ColorError : ColorOk);
			ImGui.TextWrapped(text);
			ImGui.PopStyleColor();

			return dismissed;
		}

		private static string ProblemKey(string pluginId, string text) => pluginId + "\u0000" + text;

		/// <summary>Installs still running, with progress and a way out.</summary>
		private void DrawActiveInstalls()
		{
			var running = PluginInstaller.Jobs.Where(j => !j.IsFinished).ToList();
			if (running.Count == 0)
				return;

			foreach (var job in running)
			{
				ImGui.PushID("active-" + job.PluginId);
				ImGui.TextUnformatted($"{(job.IsUpdate ? "Updating" : "Installing")} {job.DisplayName}");
				ImGui.SameLine();
				DrawInstallProgress(job, compact: false);
				ImGui.PopID();
			}

			ImGui.Separator();
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
