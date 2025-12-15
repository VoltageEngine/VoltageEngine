using ImGuiNET;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Voltage.Editor.Tools;
using Voltage.Editor.Utils;
using Voltage.Utils;

namespace Voltage.Editor.ImGuiCore;

public partial class ImGuiManager
{
	private void DrawFileMenu()
	{
		if (ImGui.BeginMenu("File"))
		{
			if (ImGui.MenuItem("New Project"))
			{
				_projectCreator.OpenCreateProjectPopup();
			}

			if (ImGui.MenuItem("Load Project..."))
			{
				OpenProjectFilePicker();
			}

			// Show recent projects submenu
			var recentProjects = _projectManager.GetRecentProjects();
			if (recentProjects.Count > 0)
			{
				if (ImGui.BeginMenu("Recent Projects"))
				{
					foreach (var projectPath in recentProjects)
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

			// Show close project option if a project is loaded
			if (_projectManager.HasActiveProject)
			{
				ImGui.Separator();

				if (ImGui.MenuItem("Close Project"))
				{
					_projectManager.UnloadCurrentProject();
					NotificationSystem.ShowTimedNotification(
						$"Project closed: {_projectManager.CurrentProject?.ProjectName}");
				}
			}

			ImGui.Separator();

			if (ImGui.MenuItem("Settings"))
			{
				_editorSettingsWindow.IsOpen = true;
			}

			ImGui.Separator();

			if (ImGui.MenuItem("Save Scene", "Ctrl+S"))
			{
				InvokeSaveSceneChanges();
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
				Voltage.Core.ConfirmAndExit();
			}

			ImGui.EndMenu();
		}
	}

	private void DrawProjectMenu()
	{
		//TODO: Add ProjectSettings menu item
		if (ImGui.BeginMenu("Project"))
		{
			if (ImGui.MenuItem("New Project"))
			{
				_projectCreator.OpenCreateProjectPopup();
			}

			if (_sceneSubclasses.Count > 0 && ImGui.BeginMenu("New Scene"))

			{
				
				ImGui.EndMenu();
			}

			if (_sceneSubclasses.Count > 0 && ImGui.BeginMenu("Change To Scene..."))

			{
				foreach (var sceneType in _sceneSubclasses)
					if (ImGui.MenuItem(sceneType.Name))
					{
						RequestSceneChange(sceneType);
					}

				ImGui.EndMenu();
			}

			ImGui.EndMenu();
		}
	}

	private void DrawViewMenu()
	{
		if (ImGui.BeginMenu("View"))
		{
			if (_themes.Length > 0 && ImGui.BeginMenu("Themes"))
			{
				foreach (var theme in _themes)
				{
					bool isCurrentTheme =
						theme.Name.Equals(_lastSelectedTheme.Value, StringComparison.OrdinalIgnoreCase);

					if (ImGui.MenuItem(theme.Name, "", isCurrentTheme))
					{
						theme.Invoke(null, new object[] { });
						_lastSelectedTheme.Value = theme.Name;
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
						// Highlight the currently active layout
						bool isCurrentLayout = layoutName.Equals(_layoutManager.CurrentLayoutName,
							StringComparison.OrdinalIgnoreCase);

						if (ImGui.MenuItem(layoutName, "", isCurrentLayout))
						{
							_layoutManager.LoadLayout(layoutName);
							_lastSelectedLayout.Value = layoutName;
							Debug.Log($"Loaded layout: {layoutName}");

							if (!layoutName.Equals("Default", StringComparison.OrdinalIgnoreCase))
							{
								NotificationSystem.ShowTimedNotification(
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
				var showCoreWindow = ShowCoreWindow;
				ImGui.MenuItem("Core Window", null, ref showCoreWindow);
				ShowCoreWindow = showCoreWindow;

				if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
				{
					ImGui.OpenPopup("CoreWindowContextMenu");
				}

				// Scene Graph Window 
				var showSceneGraphWindow = ShowSceneGraphWindow;
				ImGui.MenuItem("Scene Graph Window", null, ref showSceneGraphWindow);
				ShowSceneGraphWindow = showSceneGraphWindow;

				if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
				{
					ImGui.OpenPopup("SceneGraphWindowContextMenu");
				}

				// Separate Game Window
				var showSeparateGameWindow = ShowSeparateGameWindow;
				ImGui.MenuItem("Separate Game Window", null, ref showSeparateGameWindow);
				ShowSeparateGameWindow = showSeparateGameWindow;

				if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
				{
					ImGui.OpenPopup("SeparateGameWindowContextMenu");
				}

				// Animation Event Inspector 
				var showAnimationEventInspector = ShowAnimationEventInspector;
				ImGui.MenuItem("Animation Event Inspector", null, ref showAnimationEventInspector);
				ShowAnimationEventInspector = showAnimationEventInspector;

				if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
				{
					ImGui.OpenPopup("AnimationEventInspectorContextMenu");
				}

				ImGui.Separator();

				// Game Window Aspect Ratio toggle
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

					Debug.Log($"Layout saved: {layoutName}");

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
				"Warning: No compiled default engine effects were found in your 'Content/Effects' directory.");
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
				EffectBuilder.BuildEditorEngineEffects(_effectBuildProgressWindow, _effectBuildCancelToken);
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

		// If popup was closed via X button
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
					NotificationSystem.ShowTimedNotification("Script manager not initialized. Open Scripting window to initialize.");
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
					NotificationSystem.ShowTimedNotification("Script manager not initialized.");
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
					NotificationSystem.ShowTimedNotification("Script manager not initialized.");
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
					NotificationSystem.ShowTimedNotification($"Scripts folder not found: {scriptsPath}");
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

	/// <summary>
	/// Draws a visual indicator showing the currently loaded project in the menu bar
	/// </summary>
	private void DrawCurrentProjectIndicator()
	{
		string displayText;
		Vector4 iconColor;

		if (_projectManager.HasActiveProject)
		{
			var project = _projectManager.CurrentProject;
			displayText = $"[*] {project.ProjectName} v{project.Version}";
			iconColor = new Vector4(0.4f, 0.8f, 0.4f, 1.0f);
		}
		else
		{
			displayText = "[ ] No Project";
			iconColor = new Vector4(0.6f, 0.6f, 0.6f, 1.0f);
		}

		var textSize = ImGui.CalcTextSize(displayText);
		var windowWidth = ImGui.GetWindowWidth();
		var centerX = (windowWidth - textSize.X) * 0.5f;

		ImGui.SetCursorPosX(centerX);

		if (_projectManager.HasActiveProject)
		{
			var project = _projectManager.CurrentProject;

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
				ImGui.EndTooltip();
			}

			ImGui.SameLine();
			ImGui.TextColored(new Vector4(0.9f, 0.9f, 1.0f, 1.0f), project.ProjectName);

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

	private void DrawBuildMenu()
	{
		if (ImGui.BeginMenu("Build"))
		{
			bool hasProject = _projectManager.HasActiveProject;

			if (ImGui.BeginMenu("Build Effects"))
			{
				if (!hasProject)
				{
					ImGui.BeginDisabled();
				}

				if (hasProject)
				{
					var projectName = _projectManager.CurrentProject.ProjectName;

					if (ImGui.MenuItem($"Build \"{projectName}\" Effects"))
					{
						EffectBuilder.BuildEditorProjectEffects(_projectManager, _effectBuildProgressWindow, _effectBuildCancelToken);
					}
				}
				else
				{
					if (ImGui.MenuItem("Build Project Effects"))
					{
						NotificationSystem.ShowTimedNotification("No active project loaded!");
					}
				}

				if (!hasProject)
				{
					ImGui.EndDisabled();
				}

				if (ImGui.MenuItem("Build Engine Effects"))
				{
					EffectBuilder.BuildEditorEngineEffects(_effectBuildProgressWindow, _effectBuildCancelToken);
				}

				ImGui.Separator();

				if (!hasProject)
				{
					ImGui.BeginDisabled();
				}

				if (ImGui.MenuItem("Build ALL Effects"))
				{
					EffectBuilder.BuildEditorAllEffects(_projectManager, _effectBuildProgressWindow, _effectBuildCancelToken);
				}

				if (!hasProject)
				{
					ImGui.EndDisabled();
				}

				ImGui.EndMenu();
			}

			if (!hasProject)
			{
				ImGui.BeginDisabled();
			}

			if (ImGui.MenuItem("Build Game"))
			{
				NotificationSystem.ShowTimedNotification("Build-Game = Not Implemented Yet!");
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
}

