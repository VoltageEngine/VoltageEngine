using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ImGuiNET;
using Voltage.Editor.DebugUtils;
using Voltage.Editor.Persistence;
using Voltage.Editor.ProjectFile;
using Voltage.Editor.SceneFile;
using Voltage.Editor.Utils;
using Voltage.Utils;
using Num = System.Numerics;

namespace Voltage.Editor.Builders;

/// <summary>
/// ImGui window that provides build options and displays build progress for game export.
/// </summary>
public class GameBuildWindow
{
	private bool _showBuildPopup = false;
	private bool _isBuilding = false;

	// Persistent user preferences
	private readonly PersistentBool _compileAssets = new("GameBuild.CompileAssets", false);
	private readonly PersistentBool _debugBuild = new("GameBuild.DebugBuild", false);
	private readonly PersistentInt _selectedPlatformIndex = new("GameBuild.SelectedPlatformIndex", 0);

	private CancellationTokenSource _buildCancellationToken;

	// Initial scene selection
	private List<string> _availableScenes = new();
	private int _selectedSceneIndex = 0;

	// Progress tracking
	private CompilationProgressWindow _progressWindow;
	private CompilationProgress _currentProgress;
	private int _completedSteps;

	/// <summary>True while a build is in progress.</summary>
	public bool IsBuilding => _isBuilding;

	public GameBuildWindow()
	{
		_progressWindow = new CompilationProgressWindow(isScriptProgress: false, onCancel: OnCancelRequested);

		GameBuilder.OnBuildStarted += OnBuildStarted;
		GameBuilder.OnBuildStepStarted += OnBuildStepStarted;
		GameBuilder.OnBuildStepCompleted += OnBuildStepCompleted;
		GameBuilder.OnBuildFinished += OnBuildFinished;
	}

	/// <summary>
	/// Opens the build options popup.
	/// </summary>
	public void OpenBuildPopup()
	{
		_showBuildPopup = true;
		RefreshAvailableScenes();
	}

	/// <summary>
	/// Builds the game using the last selected options and then launches the executable.
	/// Called from the menu bar "Build and Run" shortcut.
	/// </summary>
	public void BuildAndRun()
	{
		var projectManager = ProjectManager.Instance;
		if (!projectManager.HasActiveProject)
		{
			EditorDebug.Error("No active project to build.", "GameBuildWindow");
			return;
		}

		if (_isBuilding)
		{
			EditorDebug.Warn("A build is already in progress.", "GameBuildWindow");
			return;
		}

		RefreshAvailableScenes();
		if (_availableScenes.Count == 0)
		{
			EditorDebug.Error("No scenes found. Create at least one scene before building.", "GameBuildWindow");
			return;
		}

		var project = projectManager.CurrentProject;
		var platform = BuildPlatform.All[_selectedPlatformIndex.Value];

		if (!platform.IsAvailable)
		{
			EditorDebug.Error($"Selected platform '{platform.DisplayName}' is not available.", "GameBuildWindow");
			return;
		}

		SaveInitialSceneSetting(project);
		StartBuild(project, platform, runAfterBuild: true);
	}

	/// <summary>
	/// Draws the build options popup and progress window.
	/// </summary>
	public void Draw()
	{
		DrawBuildOptionsPopup();
		_progressWindow.Draw();
	}

	/// <summary>
	/// Refreshes the list of .vscene files from the current project and
	/// pre-selects the scene that matches ProjectSettings.InitialScene.
	/// </summary>
	private void RefreshAvailableScenes()
	{
		_availableScenes.Clear();
		_selectedSceneIndex = 0;

		var projectManager = ProjectManager.Instance;
		if (!projectManager.HasActiveProject)
			return;

		var sceneNames = SceneManager.Instance.GetAllSceneNames();
		_availableScenes = sceneNames;

		// Pre-select the scene stored in ProjectSettings
		var settings = projectManager.CurrentProject.Settings;
		if (settings != null && !string.IsNullOrEmpty(settings.InitialScene))
		{
			var idx = _availableScenes.IndexOf(settings.InitialScene);
			if (idx >= 0)
				_selectedSceneIndex = idx;
		}
	}

	private void DrawBuildOptionsPopup()
	{
		if (_showBuildPopup)
		{
			ImGui.OpenPopup("Build Game##Options");
			_showBuildPopup = false;
		}

		var center = new Num.Vector2(Screen.Width * 0.5f, Screen.Height * 0.4f);
		ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new Num.Vector2(0.5f, 0.5f));
		ImGui.SetNextWindowSize(new Num.Vector2(550, 0), ImGuiCond.Appearing);

		bool open = true;
		if (ImGui.BeginPopupModal("Build Game##Options", ref open, ImGuiWindowFlags.AlwaysAutoResize))
		{
			var projectManager = ProjectManager.Instance;
			if (!projectManager.HasActiveProject)
			{
				ImGui.TextColored(new Num.Vector4(1.0f, 0.4f, 0.4f, 1.0f), "No active project.");
				if (ImGui.Button("Close", new Num.Vector2(100, 0)))
					ImGui.CloseCurrentPopup();
				ImGui.EndPopup();
				return;
			}

			var project = projectManager.CurrentProject;
			var selectedPlatform = BuildPlatform.All[_selectedPlatformIndex.Value];

			ImGui.TextColored(new Num.Vector4(0.2f, 0.8f, 1.0f, 1.0f), "Build Game");
			ImGui.Separator();

			VoltageEditorUtils.SmallVerticalSpace();

			// Project info
			ImGui.Text("Project:");
			ImGui.SameLine();
			ImGui.TextColored(new Num.Vector4(0.7f, 1.0f, 0.7f, 1.0f), project.ProjectName);

			ImGui.Text("Version:");
			ImGui.SameLine();
			ImGui.TextDisabled(project.Version.ToString());

			var buildDir = Path.Combine(project.ProjectPath, "Build", selectedPlatform.FolderSuffix);
			ImGui.Text("Output:");
			ImGui.SameLine();
			ImGui.TextDisabled(buildDir);

			VoltageEditorUtils.MediumVerticalSpace();

			// Initial Scene
			ImGui.TextColored(new Num.Vector4(0.8f, 0.9f, 1.0f, 1.0f), "Initial Scene");
			ImGui.Separator();

			VoltageEditorUtils.SmallVerticalSpace();

			DrawInitialSceneSelector();

			VoltageEditorUtils.MediumVerticalSpace();

			// --- Target Platform ---
			ImGui.TextColored(new Num.Vector4(0.8f, 0.9f, 1.0f, 1.0f), "Target Platform");
			ImGui.Separator();

			VoltageEditorUtils.SmallVerticalSpace();

			DrawPlatformSelector();

			VoltageEditorUtils.MediumVerticalSpace();

			// --- Build Options ---
			ImGui.TextColored(new Num.Vector4(0.8f, 0.9f, 1.0f, 1.0f), "Build Options");
			ImGui.Separator();

			VoltageEditorUtils.SmallVerticalSpace();

			// Debug / Release toggle — use a local for ImGui ref, write back if changed
			var debugBuildValue = _debugBuild.Value;
			if (ImGui.Checkbox("Debug Build", ref debugBuildValue))
				_debugBuild.Value = debugBuildValue;

			if (ImGui.IsItemHovered())
			{
				ImGui.SetTooltip("When enabled, the game is published in Debug configuration.\n" +
				                 "Debug builds include full symbols and skip optimizations,\n" +
				                 "making it easier to diagnose runtime issues.\n\n" +
				                 "When disabled (default), the game is published in Release\n" +
				                 "configuration with full optimizations.");
			}

			ImGui.SameLine();
			ImGui.TextColored(
				_debugBuild.Value
					? new Num.Vector4(1.0f, 0.8f, 0.2f, 1.0f)
					: new Num.Vector4(0.5f, 1.0f, 0.5f, 1.0f),
				_debugBuild.Value ? "(Debug)" : "(Release)");

			VoltageEditorUtils.SmallVerticalSpace();

			ImGui.TextWrapped("The game will be published as a self-contained, trimmed executable for standalone deployment.");

			VoltageEditorUtils.SmallVerticalSpace();

			// Compile Assets option
			var compileAssetsValue = _compileAssets.Value;
			if (compileAssetsValue)
				ImGui.BeginDisabled();

			if (ImGui.Checkbox("Compile Assets with MGCB", ref compileAssetsValue))
			{
				// Force back to false since it's not implemented
				_compileAssets.Value = false;
			}

			if (compileAssetsValue)
				ImGui.EndDisabled();

			if (ImGui.IsItemHovered())
			{
				ImGui.SetTooltip("Not yet implemented. Assets will be copied as-is to the build output.");
			}

			ImGui.SameLine();
			ImGui.TextColored(new Num.Vector4(1.0f, 0.6f, 0.2f, 1.0f), "(Not Available)");

			VoltageEditorUtils.SmallVerticalSpace();

			ImGui.PushStyleColor(ImGuiCol.Text, new Num.Vector4(0.6f, 0.6f, 0.6f, 1.0f));
			ImGui.TextWrapped("Assets will be copied directly to the output Content folder without compilation. " +
			                  "MGCB asset compilation will be available in a future update.");
			ImGui.PopStyleColor();

			VoltageEditorUtils.MediumVerticalSpace();

			// Build contents summary
			if (ImGui.CollapsingHeader("Build Contents", ImGuiTreeNodeFlags.DefaultOpen))
			{
				ImGui.Indent();
				ImGui.BulletText("Game executable (AOT + Trimmed)");
				ImGui.BulletText("Voltage engine content (compiled effects, fonts)");
				ImGui.BulletText("Project assets (Content folder, copied as-is)");
				ImGui.BulletText("Project scripts (compiled into executable)");
				ImGui.BulletText("Project data (scenes, prefabs, component data)");
				ImGui.BulletText("Project settings (ProjectSettings.json)");
				ImGui.Unindent();
			}

			VoltageEditorUtils.MediumVerticalSpace();

			// Action buttons
			ImGui.Separator();

			var buttonWidth = 120f;
			var spacing = 10f;
			var totalButtonWidth = buttonWidth * 3 + spacing * 2;
			var windowWidth = ImGui.GetWindowSize().X;
			var centerStart = (windowWidth - totalButtonWidth) * 0.5f;

			ImGui.SetCursorPosX(centerStart);

			bool hasScene = _availableScenes.Count > 0;
			bool canBuild = selectedPlatform.IsAvailable && !_isBuilding && hasScene;
			if (!canBuild)
				ImGui.BeginDisabled();

			if (ImGui.Button("Build", new Num.Vector2(buttonWidth, 30)))
			{
				SaveInitialSceneSetting(project);
				ImGui.CloseCurrentPopup();
				StartBuild(project, selectedPlatform, runAfterBuild: false);
			}

			ImGui.SameLine();

			if (ImGui.Button("Build and Run", new Num.Vector2(buttonWidth, 30)))
			{
				SaveInitialSceneSetting(project);
				ImGui.CloseCurrentPopup();
				StartBuild(project, selectedPlatform, runAfterBuild: true);
			}

			if (!canBuild)
				ImGui.EndDisabled();

			ImGui.SameLine();

			if (ImGui.Button("Cancel", new Num.Vector2(buttonWidth, 30)))
			{
				ImGui.CloseCurrentPopup();
			}

			ImGui.EndPopup();
		}
	}

	/// <summary>
	/// Draws the initial scene combo selector.
	/// </summary>
	private void DrawInitialSceneSelector()
	{
		if (_availableScenes.Count == 0)
		{
			ImGui.TextColored(new Num.Vector4(1.0f, 0.4f, 0.4f, 1.0f),
				"No scenes found. Create at least one scene before building.");
			return;
		}

		ImGui.Text("Scene to load on startup:");

		ImGui.SetNextItemWidth(-1);
		if (ImGui.BeginCombo("##InitialScene", _availableScenes[_selectedSceneIndex]))
		{
			for (int i = 0; i < _availableScenes.Count; i++)
			{
				bool isSelected = _selectedSceneIndex == i;
				if (ImGui.Selectable(_availableScenes[i], isSelected))
				{
					_selectedSceneIndex = i;
				}

				if (isSelected)
					ImGui.SetItemDefaultFocus();
			}

			ImGui.EndCombo();
		}

		if (ImGui.IsItemHovered())
		{
			ImGui.SetTooltip("The .vscene file that will be loaded when the game starts.\n" +
			                 "This is saved into ProjectSettings.json so the game executable\n" +
			                 "knows which scene to load at runtime.");
		}
	}

	/// <summary>
	/// Writes the selected initial scene name into ProjectSettings.json
	/// so the built game executable reads it at startup.
	/// </summary>
	private void SaveInitialSceneSetting(IGameProject project)
	{
		if (_availableScenes.Count == 0 || _selectedSceneIndex < 0 || _selectedSceneIndex >= _availableScenes.Count)
			return;

		var selectedScene = _availableScenes[_selectedSceneIndex];

		var settings = project.Settings;
		if (settings == null)
			return;

		settings.InitialScene = selectedScene;

		try
		{
			var settingsPath = Path.Combine(project.ProjectPath, "ProjectSettings.json");
			var json = settings.SaveToJson();
			File.WriteAllText(settingsPath, json, new System.Text.UTF8Encoding(false));
			EditorDebug.Log($"Saved InitialScene '{selectedScene}' to ProjectSettings.json", "GameBuildWindow");
		}
		catch (Exception ex)
		{
			EditorDebug.Error($"Failed to save InitialScene setting: {ex.Message}", "GameBuildWindow");
		}
	}

	private void DrawPlatformSelector()
	{
		var platforms = BuildPlatform.All;

		for (int i = 0; i < platforms.Count; i++)
		{
			var platform = platforms[i];

			if (!platform.IsAvailable)
				ImGui.BeginDisabled();

			bool isSelected = _selectedPlatformIndex.Value == i;

			if (ImGui.RadioButton(platform.DisplayName, isSelected))
				_selectedPlatformIndex.Value = i;

			if (!platform.IsAvailable)
			{
				ImGui.EndDisabled();

				ImGui.SameLine();
				ImGui.TextColored(new Num.Vector4(1.0f, 0.6f, 0.2f, 1.0f), "(Not Available)");

				if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
				{
					ImGui.SetTooltip(platform.UnavailableReason ?? "This platform is not yet supported.");
				}
			}
		}
	}

	private void StartBuild(IGameProject project, BuildPlatform platform, bool runAfterBuild)
	{
		_isBuilding = true;

		_buildCancellationToken?.Cancel();
		_buildCancellationToken?.Dispose();
		_buildCancellationToken = new CancellationTokenSource();

		var token = _buildCancellationToken.Token;
		var debugBuild = _debugBuild.Value;

		Task.Run(async () =>
		{
			try
			{
				bool success = await GameBuilder.BuildGameAsync(project, platform, _compileAssets.Value, debugBuild, token);

				if (success && runAfterBuild)
				{
					LaunchGameExecutable(project, platform);
				}
			}
			catch (OperationCanceledException)
			{
				EditorDebug.Log("Game build was cancelled.", "GameBuildWindow");
			}
			catch (Exception ex)
			{
				EditorDebug.Error($"Unexpected build error: {ex.Message}", "GameBuildWindow");
			}
			finally
			{
				_isBuilding = false;
			}
		}, token);
	}

	/// <summary>
	/// Launches the built game executable and captures its console output.
	/// In debug builds, a visible console window is created alongside the game for diagnostics.
	/// In release builds, output is silently captured and forwarded to the editor log.
	/// </summary>
	private void LaunchGameExecutable(IGameProject project, BuildPlatform platform)
	{
		try
		{
			var exePath = GameBuilder.FindGameExecutable(project, platform);
			if (exePath == null)
			{
				Debug.Error("Could not find the game executable in the build output.");
				return;
			}

			EditorDebug.Log($"Launching game: {exePath}", "GameBuildWindow");

			ProcessStartInfo startInfo;

			if (_debugBuild.Value)
			{
				// Debug: let the OS open the exe normally so a console window appears
				// alongside the game. Stream redirection is not possible in this mode.
				startInfo = new ProcessStartInfo
				{
					FileName = exePath,
					WorkingDirectory = Path.GetDirectoryName(exePath)!,
					UseShellExecute = true
				};
			}
			else
			{
				// Release: redirect stdout/stderr into the editor log so errors
				// are visible without a separate console window.
				startInfo = new ProcessStartInfo
				{
					FileName = exePath,
					WorkingDirectory = Path.GetDirectoryName(exePath)!,
					UseShellExecute = false,
					RedirectStandardOutput = true,
					RedirectStandardError = true,
					CreateNoWindow = false
				};
			}

			var process = Process.Start(startInfo);
			if (process == null)
			{
				Debug.Error("Failed to start game process.");
				return;
			}

			if (!_debugBuild.Value)
			{
				// Read stdout/stderr on background threads so the editor doesn't block
				process.OutputDataReceived += (sender, e) =>
				{
					if (!string.IsNullOrEmpty(e.Data))
						EditorDebug.Log($"[Game] {e.Data}", "GameOutput");
				};

				process.ErrorDataReceived += (sender, e) =>
				{
					if (!string.IsNullOrEmpty(e.Data))
						Debug.Error($"[Game] {e.Data}");
				};

				process.BeginOutputReadLine();
				process.BeginErrorReadLine();
			}
		}
		catch (Exception ex)
		{
			Debug.Error($"Failed to launch game executable: {ex.Message}");
		}
	}

	private void OnBuildStarted(int totalSteps)
	{
		_completedSteps = 0;

		_currentProgress = new CompilationProgress
		{
			Title = "Building Game",
			TotalItems = totalSteps,
			CompletedItems = 0,
			SuccessCount = 0,
			FailureCount = 0,
			CurrentItem = "Initializing...",
			IsComplete = false
		};

		_progressWindow.Show(_currentProgress);
	}

	private void OnBuildStepStarted(string stepName)
	{
		if (_currentProgress != null && !_currentProgress.IsComplete)
		{
			_currentProgress.CurrentItem = stepName;
		}
	}

	private void OnBuildStepCompleted(string stepName, bool success)
	{
		if (_currentProgress != null && !_currentProgress.IsComplete)
		{
			_completedSteps++;
			_currentProgress.CompletedItems = _completedSteps;

			if (success)
				_currentProgress.SuccessCount++;
			else
				_currentProgress.FailureCount++;
		}
	}

	private void OnBuildFinished(bool success, string message)
	{
		if (_currentProgress != null)
		{
			_currentProgress.IsComplete = true;
			_currentProgress.CurrentItem = "";
			_currentProgress.CompletionMessage = message;
		}
	}

	private void OnCancelRequested()
	{
		if (_currentProgress != null)
		{
			_currentProgress.IsComplete = true;
		}

		if (_buildCancellationToken != null && !_buildCancellationToken.IsCancellationRequested)
		{
			_buildCancellationToken.Cancel();
		}

		_currentProgress = null;
		_progressWindow.Hide();
		_isBuilding = false;
	}
}
