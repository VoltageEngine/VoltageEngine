using ImGuiNET;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Voltage.Editor.DebugUtils;
using Voltage.Editor.Effects;
using Voltage.Editor.Persistence;
using Voltage.Editor.SceneFile;
using Voltage.Editor.Tools;
using Voltage.Editor.Utils;
using Voltage.Utils;

namespace Voltage.Editor.ImGuiCore;

public partial class ImGuiManager
{
	#region Persistent Settings

	private PersistentBool _showStyleEditor = new("ImGui_ShowStyleEditor", false);

	public bool ShowStyleEditor
	{
		get => _showStyleEditor.Value;
		set => _showStyleEditor.Value = value;
	}

	private PersistentBool _showSceneGraphWindow = new("ImGui_ShowSceneGraphWindow", true);

	public bool ShowSceneGraphWindow
	{
		get => _showSceneGraphWindow.Value;
		set => _showSceneGraphWindow.Value = value;
	}

	private PersistentBool _showMainInspectorWindow = new("ImGui_ShowMainInspectorWindow", true);

	public bool ShowMainInspectorWindow
	{
		get => _showMainInspectorWindow.Value;
		set => _showMainInspectorWindow.Value = value;
	}


	private PersistentBool _showCoreWindow = new("ImGui_ShowCoreWindow", true);

	public bool ShowCoreWindow
	{
		get => _showCoreWindow.Value;
		set => _showCoreWindow.Value = value;
	}

	private PersistentBool _showSeparateGameWindow = new("ImGui_ShowSeparateGameWindow", true);

	public bool ShowSeparateGameWindow
	{
		get => _showSeparateGameWindow.Value;
		set => _showSeparateGameWindow.Value = value;
	}

	private PersistentBool _showAnimationEventInspector = new("ImGui_ShowAnimationEventInspector", false);

	public bool ShowAnimationEventInspector
	{
		get => _showAnimationEventInspector.Value;
		set => _showAnimationEventInspector.Value = value;
	}

	private PersistentBool _showMenuBar = new("ImGui_ShowMenuBar", true);

	public bool ShowMenuBar
	{
		get => _showMenuBar.Value;
		set => _showMenuBar.Value = value;
	}

	private PersistentBool _preserveGameWindowAspectRatio = new("ImGui_PreserveGameWindowAspectRatio", true);

	public bool PreserveGameWindowAspectRatio
	{
		get => _preserveGameWindowAspectRatio.Value;
		set => _preserveGameWindowAspectRatio.Value = value;
	}

	private PersistentBool _showAssetBrowser = new("ImGui_ShowAssetBrowser", false);

	public bool ShowAssetBrowser
	{
		get => _showAssetBrowser.Value;
		set => _showAssetBrowser.Value = value;
	}

	private PersistentString _lastSelectedLayout = new("ImGui_LastSelectedLayout", "Default");
	private PersistentString _lastSelectedTheme = new("ImGui_LastSelectedTheme", "DarkTheme1");
	#endregion

	private void DrawFileMenu()
	{
		if (ImGui.BeginMenu("File"))
		{
			if (ImGui.MenuItem("New Project"))
			{
				_projectCreatorWindow.OpenCreateProjectPopup();
			}

			if (ImGui.MenuItem("Load Project..."))
			{
				OpenProjectFilePicker();
			}

			if (_projectManager.GetRecentProjects().Count > 0)
			{
				if (ImGui.BeginMenu("Recent Projects"))
				{
					foreach (var projectPath in _projectManager.GetRecentProjects())
					{
						var projectDir = Path.GetDirectoryName(projectPath);
						var projectName = Path.GetFileName(projectDir);

						if (ImGui.MenuItem(projectName))
						{
							_projectManager.LoadProject(projectPath);
						}

						if (ImGui.IsItemHovered())
						{
							ImGui.SetTooltip(projectPath);
						}
					}

					ImGui.Separator();

					if (ImGui.MenuItem("Clear Recent Projects"))
					{
						_projectManager.ClearRecentProjects();
					}

					ImGui.EndMenu();
				}
			}

			ImGui.Separator();

			if (ImGui.MenuItem("Settings"))
			{
				_editorSettingsWindow.IsOpen = true;
			}

			ImGui.Separator();

			if (ImGui.MenuItem("Load Tiled Map"))
			{
				SceneGraphWindow.TmxFilePicker.Open();
			}

			if (ImGui.MenuItem("Load Aseprite Images"))
			{
				SceneGraphWindow.AsepriteFilePicker.Open();
			}

			ImGui.Separator();

			if (ImGui.MenuItem("Open Sprite Atlas Editor"))
				_spriteAtlasEditorWindow = _spriteAtlasEditorWindow ?? new SpriteAtlasEditorWindow();

			if (ImGui.MenuItem("Close ImGui Editor"))
				SetEnabled(false);

			if (ImGui.MenuItem("Exit"))
			{
				Core.ConfirmAndExit();
			}

			ImGui.EndMenu();
		}
	}

	private void DrawProjectMenu()
	{
		if (ImGui.BeginMenu("Project"))
		{
			if (ImGui.BeginMenu("New Scene"))
			{
				if (!_projectManager.HasActiveProject)
				{
					ImGui.TextDisabled("No active project");
				}
				else if (ImGui.MenuItem("Create New Scene..."))
				{
					_sceneCreator.OpenCreateScenePopup();
				}
				
				ImGui.EndMenu();
			}

			if (ImGui.BeginMenu("Load Scene..."))
			{
				if (_projectManager.HasActiveProject)
				{
					var sceneManager = SceneManager.Instance;
					var sceneNames = sceneManager.GetAllSceneNames();
					
					if (sceneNames.Count > 0)
					{
						ImGui.TextColored(new Vector4(0.7f, 1.0f, 0.7f, 1.0f), "Project Scenes:");
						ImGui.Separator();
						
						foreach (var sceneName in sceneNames)
						{
							if (ImGui.MenuItem(sceneName))
							{
								RequestSceneChange(sceneName);
							}
						}

					}
					else
					{
						ImGui.TextDisabled("No scenes found");
					}

				}
				else
				{
					ImGui.TextDisabled("No active project");
				}

				ImGui.EndMenu();
			}

			ImGui.Separator();

			if (ImGui.MenuItem("Save Scene", "Ctrl+S"))
			{
				if (Core.Scene == null)
				{
					Debug.Error("No active scene to save!");
					return;
				}

				if (!SceneManager.Instance.HasLoadedScene)
				{
					_newSceneNameForSave = "NewScene";
					_showCreateSceneForSavePrompt = true;
					Debug.Error("No Scene has been loaded yet!");
					return;
				}
				InvokeSaveSceneChanges();
			}

			ImGui.Separator();

			if (!_projectManager.HasActiveProject)
			{
				ImGui.BeginDisabled();
			}

			if (ImGui.MenuItem("Settings"))
			{
				_projectSettingsWindow.IsOpen = true;
			}

			ImGui.Separator();

			if (_projectManager.HasActiveProject)
			{
				if (ImGui.MenuItem("Close Project"))
				{
					RequestProjectClose();
				}
			}

			if (!_projectManager.HasActiveProject)
			{
				ImGui.EndDisabled();
			}

			ImGui.EndMenu();
		}
	}

	private void DrawViewMenu()
	{
		if (ImGui.BeginMenu("View"))
		{
			if (ImGui.BeginMenu("Themes"))
			{
				ImGui.TextColored(new Vector4(0.5f, 0.8f, 1.0f, 1.0f),
					$"Current: {_themeManager.CurrentThemeName}");
				ImGui.Separator();

				foreach (var themeName in _themeManager.GetAvailableThemes())
				{
					bool isCurrentTheme = themeName.Equals(_themeManager.CurrentThemeName, 
						StringComparison.OrdinalIgnoreCase);

					if (ImGui.MenuItem(themeName, "", isCurrentTheme))
					{
						if (_themeManager.ApplyTheme(themeName))
						{
							EditorDebug.Log($"Theme changed to: {themeName}");
						}
					}
				}

				ImGui.EndMenu();
			}

			if (ImGui.BeginMenu("Layout"))
			{
				ImGui.TextColored(new Vector4(0.5f, 0.8f, 1.0f, 1.0f),
					$"Current: {_layoutManager.CurrentLayoutName}");
				ImGui.Separator();

				if (ImGui.MenuItem("Save Layout As..."))
				{
					_newLayoutName = "";
					_showSaveLayoutPopup = true;
				}

				ImGui.Separator();

				if (ImGui.BeginMenu("Load Layout"))
				{
					foreach (var layoutName in _layoutManager.GetLayoutNames())
					{
						bool isCurrentLayout = layoutName.Equals(_layoutManager.CurrentLayoutName,
							StringComparison.OrdinalIgnoreCase);

						if (ImGui.MenuItem(layoutName, "", isCurrentLayout))
						{
							_layoutManager.LoadLayout(layoutName);
							_lastSelectedLayout.Value = layoutName;
							Debug.Log($"Loaded layout: {layoutName}");

							if (!layoutName.Equals("Default", StringComparison.OrdinalIgnoreCase))
							{
								EditorDebug.Log(
									$"Layout '{layoutName}' loaded. Some windows may require restart for full effect.");
							}
						}
					}

					ImGui.EndMenu();
				}

				if (ImGui.BeginMenu("Delete Layout"))
				{
					var layoutNames = new List<string>(_layoutManager.GetLayoutNames());
					string layoutToDelete = null;

					foreach (var layoutName in layoutNames)
					{
						if (layoutName != "Default" && ImGui.MenuItem(layoutName))
						{
							layoutToDelete = layoutName;
							break;
						}
					}

					// Delete outside of the enumeration
					if (layoutToDelete != null)
					{
						// If deleting the currently active layout, switch to Default
						if (layoutToDelete.Equals(_layoutManager.CurrentLayoutName, StringComparison.OrdinalIgnoreCase))
						{
							_lastSelectedLayout.Value = "Default";
							_layoutManager.LoadLayout("Default");
						}

						_layoutManager.DeleteLayout(layoutToDelete);
						_layoutManager.RefreshLayoutList();
					}

					ImGui.EndMenu();
				}

				ImGui.Separator();

				// Show different text depending on current layout
				string resetText =
					_layoutManager.CurrentLayoutName.Equals("Default", StringComparison.OrdinalIgnoreCase)
						? "Reset to Default"
						: "Switch to Default";

				if (ImGui.MenuItem(resetText))
				{
					_layoutManager.LoadLayout("Default");
					_lastSelectedLayout.Value = "Default";
				}

				ImGui.EndMenu();
			}

			if (ImGui.BeginMenu("Window"))
			{
				var showMainInspector = ShowMainInspectorWindow;
				ImGui.MenuItem("Inspector Window", null, ref showMainInspector);
				ShowMainInspectorWindow = showMainInspector;

				if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
				{
					ImGui.OpenPopup("MainInspectorContextMenu");
				}

				var showCoreWindow = ShowCoreWindow;
				ImGui.MenuItem("Core Window", null, ref showCoreWindow);
				ShowCoreWindow = showCoreWindow;

				if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
				{
					ImGui.OpenPopup("CoreWindowContextMenu");
				}

				var showSceneGraphWindow = ShowSceneGraphWindow;
				ImGui.MenuItem("Scene Graph Window", null, ref showSceneGraphWindow);
				ShowSceneGraphWindow = showSceneGraphWindow;

				if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
				{
					ImGui.OpenPopup("SceneGraphWindowContextMenu");
				}

				var showSeparateGameWindow = ShowSeparateGameWindow;
				ImGui.MenuItem("Separate Game Window", null, ref showSeparateGameWindow);
				ShowSeparateGameWindow = showSeparateGameWindow;

				if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
				{
					ImGui.OpenPopup("SeparateGameWindowContextMenu");
				}

				var showAnimationEventInspector = ShowAnimationEventInspector;
				ImGui.MenuItem("Animation Event Inspector", null, ref showAnimationEventInspector);
				ShowAnimationEventInspector = showAnimationEventInspector;

				var showAssetBrowser = ShowAssetBrowser;
				ImGui.MenuItem("Asset Browser", null, ref showAssetBrowser);
				ShowAssetBrowser = showAssetBrowser;

				if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
				{
					ImGui.OpenPopup("AnimationEventInspectorContextMenu");
				}

				ImGui.Separator();

				var preserveAspectRatio = PreserveGameWindowAspectRatio;
				ImGui.MenuItem("Preserve Game Window Aspect Ratio", null, ref preserveAspectRatio);
				PreserveGameWindowAspectRatio = preserveAspectRatio;

				ImGui.EndMenu();
			}

			ImGui.EndMenu();
		}

		DrawSaveLayoutPopup();
	}

	private void DrawSaveLayoutPopup()
	{
		if (_showSaveLayoutPopup)
		{
			ImGui.OpenPopup("SaveLayoutPopup");
			_showSaveLayoutPopup = false;
		}

		var center = new Vector2(Screen.Width * 0.5f, Screen.Height * 0.4f);
		ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
		ImGui.SetNextWindowSize(new Vector2(400, 0), ImGuiCond.Appearing);

		bool open = true;
		if (ImGui.BeginPopupModal("SaveLayoutPopup", ref open, ImGuiWindowFlags.AlwaysAutoResize))
		{
			ImGui.Text("Save Layout");
			ImGui.Separator();

			ImGui.Text("Enter layout name:");
			ImGui.SetNextItemWidth(350);
			ImGui.InputText("##layoutname", ref _newLayoutName, 50);

			bool layoutExists = _layoutManager.GetLayoutNames().Contains(_newLayoutName.Trim());
			if (!string.IsNullOrWhiteSpace(_newLayoutName) && layoutExists)
			{
				ImGui.TextColored(new Vector4(1.0f, 0.6f, 0.2f, 1.0f),
					$"Warning: Layout '{_newLayoutName.Trim()}' already exists and will be overwritten!");
			}

			VoltageEditorUtils.MediumVerticalSpace();

			var buttonWidth = 100f;
			var spacing = 10f;
			var totalButtonWidth = (buttonWidth * 2) + spacing;
			var windowWidth = ImGui.GetWindowSize().X;
			var centerStart = (windowWidth - totalButtonWidth) * 0.5f;

			ImGui.SetCursorPosX(centerStart);

			bool canSave = !string.IsNullOrWhiteSpace(_newLayoutName);
			if (!canSave)
				ImGui.BeginDisabled();

			if (ImGui.Button("Save", new Vector2(buttonWidth, 0)))
			{
				if (canSave)
				{
					string layoutName = _newLayoutName.Trim();
					_layoutManager.SaveLayout(layoutName);
					_layoutManager.RefreshLayoutList();

					// Update the last selected layout to the newly saved one
					_lastSelectedLayout.Value = layoutName;

					Debug.Success($"Layout saved: {layoutName}");

					_newLayoutName = "";
					ImGui.CloseCurrentPopup();
				}
			}

			if (!canSave)
				ImGui.EndDisabled();

			ImGui.SameLine();

			if (ImGui.Button("Cancel", new Vector2(buttonWidth, 0)))
			{
				_newLayoutName = "";
				ImGui.CloseCurrentPopup();
			}

			ImGui.EndPopup();
		}
	}

	private void DrawEngineEffectsPrompt()
	{
		ImGui.OpenPopup("Missing Engine Effects");

		var center = new Vector2(Screen.Width * 0.5f, Screen.Height * 0.4f);
		ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
		ImGui.SetNextWindowSize(new Vector2(500, 0), ImGuiCond.Appearing);

		bool open = true;
		if (ImGui.BeginPopupModal("Missing Engine Effects", ref open, ImGuiWindowFlags.AlwaysAutoResize))
		{
			ImGui.PushTextWrapPos(480);
			ImGui.TextWrapped(
				"Warning: No compiled default engine effects were found in the Editor's 'Content/Voltage/Effects' directory.");
			ImGui.PopTextWrapPos();

			ImGui.Spacing();
			ImGui.TextWrapped("Would you like to compile them now?");

			VoltageEditorUtils.MediumVerticalSpace();

			var buttonWidth = 120f;
			var spacing = 10f;
			var totalButtonWidth = (buttonWidth * 2) + spacing;
			var windowWidth = ImGui.GetWindowSize().X;
			var centerStart = (windowWidth - totalButtonWidth) * 0.5f;

		ImGui.SetCursorPosX(centerStart);

		if (ImGui.Button("Yes", new Vector2(buttonWidth, 0)))
		{
			EffectsCompiler.BuildEditorEngineEffects(_effectsCompileProgressWindow, ref _effectBuildCancelToken);
			_showEngineEffectsPrompt = false;
			_engineEffectsCheckComplete = true;
			ImGui.CloseCurrentPopup();
		}

			ImGui.SameLine();

			if (ImGui.Button("No", new Vector2(buttonWidth, 0)))
			{
				_showEngineEffectsPrompt = false;
				_engineEffectsCheckComplete = true;
				ImGui.CloseCurrentPopup();
			}

			ImGui.EndPopup();
		}

		if (!open)
		{
			_showEngineEffectsPrompt = false;
			_engineEffectsCheckComplete = true;
		}
	}

	private void DrawScriptingMenu()
	{
		if (ImGui.BeginMenu("Scripting"))
		{
			if (!_projectManager.HasActiveProject)
			{
				ImGui.TextDisabled("No active project");
				ImGui.Separator();
			}
			else
			{
				ImGui.TextColored(new Vector4(0.7f, 1.0f, 0.7f, 1.0f),
					$"Project: {_projectManager.CurrentProject.ProjectName}");
				ImGui.Separator();
			}

			if (ImGui.MenuItem("Scripting Window", "", _showScriptingWindow))
			{
				_showScriptingWindow = !_showScriptingWindow;
			}

			ImGui.Separator();

			bool hasProject = _projectManager.HasActiveProject;
			bool hasScriptManager = _scriptManager != null;

			if (!hasProject)
			{
				ImGui.BeginDisabled();
			}

			if (ImGui.MenuItem("Compile Scripts"))
			{
				if (hasScriptManager)
				{
					_scriptManager.CompileScripts(EditorSettingsWindow.AutoReloadSceneAfterScriptCompile);
				}
				else
				{
					EditorDebug.Log("Script manager not initialized. Open Scripting window to initialize.");
				}
			}

			if (ImGui.MenuItem("Compile & Reload Scene"))
			{
				if (hasScriptManager)
				{
					_scriptManager.CompileScripts(reloadSceneOnSuccess: true);
				}
				else
				{
					EditorDebug.Log("Script manager not initialized.");
				}
			}

			if (ImGui.MenuItem("Reload Scene", "F6"))
			{
				if (hasScriptManager)
				{
					_scriptManager.ReloadScene();
				}
				else
				{
					EditorDebug.Log("Script manager not initialized.");
				}
			}

			ImGui.Separator();

			if (ImGui.MenuItem("Open Scripts Folder"))
			{
				var scriptsPath = _projectManager.CurrentProject.ScriptsFolder;
				if (Directory.Exists(scriptsPath))
				{
					System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
					{
						FileName = scriptsPath,
						UseShellExecute = true
					});
				}
				else
				{
					EditorDebug.Log($"Scripts folder not found: {scriptsPath}");
				}
			}

			if (hasScriptManager)
			{
				bool enableHotReload = _scriptManager.EnableHotReload;
				if (ImGui.MenuItem("Enable Hot Reload", "", enableHotReload))
				{
					_scriptManager.EnableHotReload = !enableHotReload;
				}
			}
			else
			{
				ImGui.MenuItem("Enable Hot Reload", "", false);
			}

			if (!hasProject)
			{
				ImGui.EndDisabled();
			}

			ImGui.EndMenu();
		}
	}

	private void DrawCurrentProjectIndicator()
	{
		string displayText;
		Vector4 iconColor;

		if (_projectManager.HasActiveProject)
		{
			var project = _projectManager.CurrentProject;
			var sceneManager = SceneManager.Instance;

			// Get current scene name
			string sceneName = sceneManager.HasLoadedScene
				? sceneManager.CurrentSceneName
				: "NONE";

			displayText = $"[*] {project.ProjectName} | Scene: {sceneName} | v{project.Version}";
			iconColor = new Vector4(0.4f, 0.8f, 0.4f, 1.0f);
		}
		else
		{
			displayText = "[ ] No Project";
			iconColor = new Vector4(0.6f, 0.6f, 0.6f, 1.0f);
		}

		// Center the indicator within the menu bar.
		// The play/stop/pause/reset cluster has moved to the Editor Tools bar so
		// there is no longer any collision risk here.
		var textSize = ImGui.CalcTextSize(displayText);
		var windowWidth = ImGui.GetWindowWidth();
		var centeredX = (windowWidth - textSize.X) * 0.5f;

		// Clamp so we never overdraw content already placed to the left.
		var safeX = Math.Max(ImGui.GetCursorPosX(), centeredX);

		ImGui.SetCursorPosX(safeX);

		if (_projectManager.HasActiveProject)
		{
			var project = _projectManager.CurrentProject;
			var sceneManager = SceneManager.Instance;

			ImGui.PushStyleColor(ImGuiCol.Text, iconColor);
			ImGui.Text("[*]");
			ImGui.PopStyleColor();

			if (ImGui.IsItemHovered())
			{
				ImGui.BeginTooltip();
				ImGui.Text("Active Project");
				ImGui.Separator();
				ImGui.TextColored(new Vector4(0.7f, 0.9f, 1.0f, 1.0f), project.ProjectName);
				ImGui.Text($"Version: {project.Version}");
				ImGui.Text($"Path: {project.ProjectPath}");
				
				if (sceneManager.HasLoadedScene)
				{
					ImGui.Separator();
					ImGui.Text($"Scene: {sceneManager.CurrentSceneName}");
					ImGui.Text($"Scene Path: {sceneManager.CurrentScenePath}");
				}
				
				ImGui.EndTooltip();
			}

			ImGui.SameLine();
			ImGui.TextColored(new Vector4(0.9f, 0.9f, 1.0f, 1.0f), project.ProjectName);

			ImGui.SameLine();
			ImGui.TextDisabled("|");
			
			ImGui.SameLine();
			if (sceneManager.HasLoadedScene)
			{
				ImGui.TextColored(new Vector4(0.7f, 0.9f, 0.7f, 1.0f), $"Scene: {sceneManager.CurrentSceneName}");
			}
			else
			{
				ImGui.TextDisabled("Scene: NONE");
			}

			ImGui.SameLine();
			ImGui.TextDisabled("|");
			
			ImGui.SameLine();
			ImGui.TextDisabled($"v{project.Version}");
		}
		else
		{
			ImGui.PushStyleColor(ImGuiCol.Text, iconColor);
			ImGui.Text("[ ]");
			ImGui.PopStyleColor();

			if (ImGui.IsItemHovered())
			{
				ImGui.SetTooltip("No project loaded");
			}

			ImGui.SameLine();
			ImGui.TextDisabled("No Project");
		}
	}

	private void DrawEffectsMenu()
	{
		if (ImGui.BeginMenu("Effects"))
		{
			bool hasProject = _projectManager.HasActiveProject;

			if (!hasProject)
			{
				ImGui.BeginDisabled();
			}

			if (hasProject)
			{
				var projectName = _projectManager.CurrentProject.ProjectName;

				if (ImGui.MenuItem($"Compile \"{projectName}\" Effects"))
				{
					EffectsCompiler.BuildEditorProjectEffects(_projectManager, _effectsCompileProgressWindow, ref _effectBuildCancelToken);
				}
			}
			else
			{
				if (ImGui.MenuItem("Compile Project Effects"))
				{
					EditorDebug.Log("No active project loaded!");
				}
			}

			if (!hasProject)
			{
				ImGui.EndDisabled();
			}

			if (ImGui.MenuItem("Compile Engine Effects"))
			{
				EffectsCompiler.BuildEditorEngineEffects(_effectsCompileProgressWindow, ref _effectBuildCancelToken);
			}

			ImGui.Separator();

			if (!hasProject)
			{
				ImGui.BeginDisabled();
			}

			if (ImGui.MenuItem("Compile ALL Effects"))
			{
				EffectsCompiler.BuildEditorAllEffects(_projectManager, _effectsCompileProgressWindow, ref _effectBuildCancelToken);
			}

			if (!hasProject)
			{
				ImGui.EndDisabled();
			}

			ImGui.EndMenu();
		}
	}

	private void DrawBuildMenu()
	{
		if (ImGui.BeginMenu("Build"))
		{
			bool hasProject = _projectManager.HasActiveProject;

			if (!hasProject)
			{
				ImGui.BeginDisabled();
			}

			if (ImGui.MenuItem("Build Game..."))
			{
				_gameBuildWindow.OpenBuildPopup();
			}

			bool isBuildInProgress = _gameBuildWindow.IsBuilding;
			if (isBuildInProgress)
			{
				ImGui.BeginDisabled();
			}

			if (ImGui.MenuItem("Build and Run", "Ctrl+F5"))
			{
				_gameBuildWindow.BuildAndRun();
			}

			if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
			{
				if (isBuildInProgress)
					ImGui.SetTooltip("A build is already in progress.");
				else if (!hasProject)
					ImGui.SetTooltip("No active project.");
				else
					ImGui.SetTooltip("Builds with the last selected options and launches the game executable.");
			}

			if (isBuildInProgress)
			{
				ImGui.EndDisabled();
			}

			if (!hasProject)
			{
				ImGui.EndDisabled();
			}

			ImGui.EndMenu();
		}
	}

	private void DrawHelpMenu()
	{
		if (ImGui.BeginMenu("Help"))
		{
			ImGui.MenuItem("ImGui Demo Window", null, ref ShowDemoWindow);

			var showStyleEditor = ShowStyleEditor;
			ImGui.MenuItem("Style Editor", null, ref showStyleEditor);
			ShowStyleEditor = showStyleEditor;

			if (ImGui.MenuItem("Open imgui_demo.cpp on GitHub"))
			{
				var url = "https://github.com/ocornut/imgui/blob/master/imgui_demo.cpp";
				var startInfo = new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true };
				System.Diagnostics.Process.Start(startInfo);
			}

			ImGui.EndMenu();
		}
	}

	/// <summary>
	/// Audio mute toggle — right-aligned inside the Editor Tools bar.
	/// Shows volume-up icon when <see cref="Core.IsAudioOn"/> is true, volume-mute when false.
	/// Clicking toggles <see cref="Core.IsAudioOn"/>.
	/// </summary>
	private void DrawAudioToggleRightAligned()
	{
		float iconSize    = 24f * FontSizeMultiplier;
		float framePad    = ImGui.GetStyle().FramePadding.X;
		float buttonWidth = iconSize + framePad * 2f;
		const float rightPadding = 8f;

		// Position the cursor so the button sits flush against the right edge.
		float posX = ImGui.GetWindowWidth() - buttonWidth - rightPadding;
		if (posX > ImGui.GetCursorPosX())
			ImGui.SetCursorPosX(posX);

		var iconSizeVec = new Vector2(iconSize, iconSize);
		bool audioOn = Core.IsAudioOn;

		IntPtr icon = audioOn
			? ImguiImageLoader.AudioOn
			: ImguiImageLoader.AudioMute;

		// Tint the button red-ish when muted so the state is immediately obvious.
		if (!audioOn)
		{
			ImGui.PushStyleColor(ImGuiCol.Button,        new Vector4(0.6f, 0.1f, 0.1f, 1.0f));
			ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.8f, 0.2f, 0.2f, 1.0f));
			ImGui.PushStyleColor(ImGuiCol.ButtonActive,  new Vector4(0.9f, 0.3f, 0.3f, 1.0f));
		}

		if (ImGui.ImageButton("AudioToggle", icon, iconSizeVec))
			Core.IsAudioOn = !Core.IsAudioOn;

		if (!audioOn)
			ImGui.PopStyleColor(3);

		if (ImGui.IsItemHovered())
			ImGui.SetTooltip(Core.IsAudioOn ? "Audio: ON (click to mute)" : "Audio: MUTED (click to unmute)");
	}

	/// <summary>
	/// Play / Stop / Pause / Reset icon-button cluster — drawn inside the Editor Tools bar.
	///
	/// Centering math:
	///   Preferred center: game-window center X = _gameImageScreenPos.X + _gameImageSize.X * 0.5f
	///   Fall-back center: tools-bar center = ImGui.GetWindowWidth() * 0.5f
	///   cursor X = center - (totalClusterWidth * 0.5f)
	///
	/// Because the audio toggle is right-aligned AFTER this call, the cluster's
	/// SetCursorPosX is applied before the audio button takes the right side.
	/// </summary>
	private void DrawEditorModeControls()
	{
		float iconSize    = 24f * FontSizeMultiplier;
		float buttonSpace = iconSize + ImGui.GetStyle().FramePadding.X * 2f;
		float spacing     = 4f * FontSizeMultiplier;

		float totalWidth  = buttonSpace * 3f + spacing * 2f;

		// Center the cluster within the Editor Tools bar.
		float centerX = ImGui.GetWindowWidth() * 0.5f;
		float cursorX = centerX - totalWidth * 0.5f;

		// Clamp so the cluster never goes off-screen on very small windows.
		cursorX = Math.Max(cursorX, ImGui.GetCursorPosX());

		ImGui.SetCursorPosX(cursorX);

		var iconSizeVec = new Vector2(iconSize, iconSize);

		if (Core.IsEditMode)
		{
			if (ImGui.ImageButton("EditorPlay", ImguiImageLoader.EditorModePlay, iconSizeVec))
				Core.InvokeSwitchEditMode(false);

			if (ImGui.IsItemHovered())
				ImGui.SetTooltip("Play (F1)");
		}
		else
		{
			ImGui.PushStyleColor(ImGuiCol.Button,        new Vector4(0.6f, 0.1f, 0.1f, 0.9f));
			ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.8f, 0.2f, 0.2f, 1.0f));
			ImGui.PushStyleColor(ImGuiCol.ButtonActive,  new Vector4(0.9f, 0.3f, 0.3f, 1.0f));

			if (ImGui.ImageButton("EditorPlay", ImguiImageLoader.EditorModeStop, iconSizeVec))
			{
				Core.IsPauseMode = false;
				Core.InvokeSwitchEditMode(true);
			}

			ImGui.PopStyleColor(3);

			if (ImGui.IsItemHovered())
				ImGui.SetTooltip("Stop (F1)");
		}

		ImGui.SameLine(0, spacing);

		if (Core.IsEditMode)
			ImGui.BeginDisabled();

		bool isPaused = Core.IsPauseMode;
		if (isPaused)
		{
			ImGui.PushStyleColor(ImGuiCol.Button,        new Vector4(0.2f, 0.5f, 1.0f, 0.9f));
			ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.3f, 0.6f, 1.0f, 1.0f));
			ImGui.PushStyleColor(ImGuiCol.ButtonActive,  new Vector4(0.4f, 0.7f, 1.0f, 1.0f));
		}

		if (ImGui.ImageButton("EditorPause", ImguiImageLoader.EditorModePause, iconSizeVec))
			Core.InvokeSwitchPauseMode(!Core.IsPauseMode);

		if (isPaused)
			ImGui.PopStyleColor(3);

		if (Core.IsEditMode)
			ImGui.EndDisabled();

		if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
			ImGui.SetTooltip(Core.IsEditMode ? "Pause (unavailable in Edit Mode)" : isPaused ? "Unpause (F2)" : "Pause (F2)");

		ImGui.SameLine(0, spacing);

		if (ImGui.ImageButton("EditorReset", ImguiImageLoader.EditorModeReset, iconSizeVec))
			Core.InvokeResetScene();

		if (ImGui.IsItemHovered())
			ImGui.SetTooltip("Reset Scene (F5)");

		// Keep cursor on the same line so DrawAudioToggleRightAligned can
		// use SetCursorPosX to jump to the right edge within the same row.
		ImGui.SameLine();
	}
}

