using ImGuiNET;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Voltage.Editor.DebugUtils;
using Voltage.Editor.Styling;
using Voltage.Editor.FilePickers;
using Voltage.Editor.Gizmos;
using Voltage.Editor.Inspectors.CustomInspectors;
using Voltage.Editor.ProjectFile;
using Voltage.Editor.SceneFile;
using Voltage.Editor.Scripting;
using Voltage.Editor.SerializedData;
using Voltage.Editor.Tools;
using Voltage.Editor.Undo.Core;
using Voltage.Editor.Utils;
using Voltage.Sprites;
using Voltage.Utils;
using Num = System.Numerics;
using Voltage.Editor.Windows;


namespace Voltage.Editor.ImGuiCore;

public partial class ImGuiManager : GlobalManager, IFinalRenderDelegate, IDisposable
{
	#region Public values

	public bool ShowDemoWindow = false;
	public bool IsGameWindowFocused = false;
	public Num.Vector2 GameWindowPosition;
	public Num.Vector2 GameWindowSize;
	public bool FocusGameWindowOnMiddleClick = false;
	public bool FocusGameWindowOnRightClick = false;
	public bool DisableKeyboardInputWhenGameWindowUnfocused = true;
	public bool DisableMouseWheelWhenGameWindowUnfocused = true;
	public float GameWindowWidth { get; private set; }
	public float GameWindowHeight { get; private set; }

	public float FontSizeMultiplier;

	// Public instances
	public GizmoSelectionManager CursorSelectionManager => _cursorSelectionManager;
	public SceneGraphWindow SceneGraphWindow { get; private set; }

	private List<EntityInspectorWindow> _entityInspectors = new();
	public EntityInspectorWindow MainEntityInspectorWindow { get; private set; }
	public bool IsInspectorTabLocked = false;

	public AnimationEventInspector AnimationEventInspectorInstance
	{
		get => _animationEventInspector;
		private set => _animationEventInspector = value;
	}

	#endregion

	#region Private values

	private System.Reflection.MethodInfo[] _themes;
	private CoreWindow _coreWindow = new();
	private DebugWindow _debugWindow = new();
	private ProjectCreator _projectCreator = new();
	private SceneCreator _sceneCreator = new();
	private ProjectManager _projectManager;
	private DataManager _dataManager;
	
	private Num.Vector2 normalEntityInspectorStartPos;
	private int entitynspectorInitialSpawnOffset = 0;
	private static int entitynspectorSpawnOffsetIncremental = 20;

	private AnimationEventInspector _animationEventInspector;
	private SpriteAtlasEditorWindow _spriteAtlasEditorWindow;
	private List<Action> _drawCommands = new();
	private ImGuiRenderer _renderer;
	//private ImGuiInput _input = new ImGuiInput();
	private GizmoSelectionManager _cursorSelectionManager;
	private ImGuiWindowFlags _gameWindowFlags = 0;

	private RenderTarget2D _lastRenderTarget;
	private IntPtr _renderTargetId = IntPtr.Zero;
	private Num.Vector2? _gameViewForcedSize;
	private WindowPosition? _gameViewForcedPos;
	private readonly float _editorToolsBarHeight = 30f;

	// Camera Params
	public static float EditModeCameraSpeed = 250f;
	public static float EditModeCameraFastSpeed = 500f;
	private const float EditorCameraZoomSpeed = 1f;
	public static float EditModeCameraMinSpeed = 50f;
	public static float EditModeCameraMaxSpeed = 3000f;
	private static float _dynamicCameraSpeed = EditModeCameraSpeed; // Current dynamic speed
	private const float CameraSpeedAdjustmentStep = 20f; // How much to change per scroll
	public static float CurrentCameraSpeed { get; private set; }

	public Vector2 CameraTargetPosition
	{
		get => _cameraTargetPosition;
		set => _cameraTargetPosition = value;
	}

	private Vector2 _cameraTargetPosition;
	private float _cameraLerp = 0.4f;

	// Camera dragging with middle mouse button
	private bool _isCameraDragging = false;
	private Vector2 _cameraDragStartMouse;
	private Vector2 _cameraDragStartPosition;

	// Before game exits, we may need to prompt to save changes (aka, pending actions)
	private bool _pendingExit = false;
	private bool _pendingSceneChange = false;
	private Type _requestedSceneType = null;
	private string _requestedSceneName = null;
	private bool _pendingResetScene = false;
	private Type _requestedResetSceneType = null;
	private Task _pendingSaveTask = null;
	private bool _pendingProjectClose = false;
	private ExitPromptType _pendingActionAfterSave;

	//EditorStyling management
	private string _layoutFilePath;
	private LayoutManager _layoutManager;
	private ThemeManager _themeManager;
	private string _newLayoutName = "";
	private bool _showSaveLayoutPopup = false;
	private bool _isFirstFrame = true;

	// Build effects progress window
	private EffectBuildProgressWindow _effectBuildProgressWindow;
	private System.Threading.CancellationTokenSource _effectBuildCancelToken;

	// Engine effects check
	private bool _hasCheckedEngineEffects = false;
	private bool _showEngineEffectsPrompt = false;
	private bool _engineEffectsCheckComplete = false;

	// File picker for project loading
	private FilePicker _projectFilePicker;
	private bool _showProjectFilePicker = false;
	private bool _reopenMenuAfterProjectPicker = false;

	// Script management
	private ScriptManager _scriptManager;

	// Editor settings window
	private EditorSettingsWindow _editorSettingsWindow = new();
	private ProjectSettingsWindow _projectSettingsWindow = new();

	// Entity Selection
	private List<(Entity entity, Collider collider)> _highlightedEntities = new();
	private IReadOnlyList<Entity> _lastSelectedEntities = null;

	private bool _showCreateSceneForSavePrompt = false;
	private string _newSceneNameForSave = "";

	#endregion

	#region Scene Saving UI

	private void DrawCreateSceneForSavePrompt()
	{
		if (_showCreateSceneForSavePrompt)
		{
			ImGui.OpenPopup("create-scene-for-save");
			_showCreateSceneForSavePrompt = false;
		}

		var center = new Num.Vector2(Screen.Width * 0.5f, Screen.Height * 0.4f);
		ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new Num.Vector2(0.5f, 0.5f));
		ImGui.SetNextWindowSize(new Num.Vector2(500, 0), ImGuiCond.Appearing);

		bool open = true;
		if (ImGui.BeginPopupModal("create-scene-for-save", ref open, ImGuiWindowFlags.AlwaysAutoResize))
		{
			ImGui.Text("Save Current Scene");
			ImGui.Separator();

			ImGui.TextWrapped("You have unsaved changes but no scene file is associated with this scene.");
			ImGui.TextWrapped("Would you like to create a new scene file to save your changes?");

			VoltageEditorUtils.MediumVerticalSpace();

			ImGui.Text("Scene Name:");
			ImGui.SetNextItemWidth(450);
			ImGui.InputText("##SceneName", ref _newSceneNameForSave, 50);

			// Validate scene name
			bool isValidName = !string.IsNullOrWhiteSpace(_newSceneNameForSave);
			bool sceneExists = false;

			if (isValidName && _projectManager.HasActiveProject)
			{
				var scenePath = Path.Combine(
					_projectManager.CurrentProject.ScenesFolder,
					$"{_newSceneNameForSave}.vscene"
				);
				sceneExists = File.Exists(scenePath);
			}

			if (sceneExists)
			{
				ImGui.TextColored(new Num.Vector4(1.0f, 0.6f, 0.2f, 1.0f),
					$"Warning: A scene with name '{_newSceneNameForSave}' already exists!");
			}

			VoltageEditorUtils.MediumVerticalSpace();

			var buttonWidth = 100f;
			var spacing = 10f;
			var totalButtonWidth = (buttonWidth * 3) + (spacing * 2);
			var windowWidth = ImGui.GetWindowSize().X;
			var centerStart = (windowWidth - totalButtonWidth) * 0.5f;

			ImGui.SetCursorPosX(centerStart);

			// Disable save button if invalid
			if (!isValidName || sceneExists)
				ImGui.BeginDisabled();

			if (ImGui.Button("Save", new Num.Vector2(buttonWidth, 0)))
			{
				CreateAndSaveScene(_newSceneNameForSave);
				ImGui.CloseCurrentPopup();
			}

			if (!isValidName || sceneExists)
				ImGui.EndDisabled();

			ImGui.SameLine();

			if (ImGui.Button("Don't Save", new Num.Vector2(buttonWidth, 0)))
			{
				ImGui.CloseCurrentPopup();
			}

			ImGui.SameLine();

			if (ImGui.Button("Cancel", new Num.Vector2(buttonWidth, 0)))
			{
				ImGui.CloseCurrentPopup();
			}

			ImGui.EndPopup();
		}
	}

	private void CreateAndSaveScene(string sceneName)
	{
		if (!_projectManager.HasActiveProject)
		{
			Debug.Error("No active project!");
			return;
		}

		try
		{
			var sceneManager = SceneManager.Instance;

			// Create and save the new scene file
			if (sceneManager.CreateSceneFile(sceneName))
			{
				Debug.Info($"Scene created and saved: {sceneName}");
			}
			else
			{
				Debug.Error($"Failed to create scene: {sceneName}");
			}
		}
		catch (Exception ex)
		{
			Debug.Error($"Failed to create scene: {ex.Message}");
		}
	}

	#endregion

	public ImGuiManager(ImGuiOptions options = null)
	{
		if (options == null)
			options = new ImGuiOptions();

		FontSizeMultiplier = options.FontSizeMultiplier;
		_gameWindowFlags = options._gameWindowFlags;
		_gameViewForcedPos = WindowPosition.Top;

		string layoutDirectory = Path.Combine(Storage.GetStorageRoot(), "EditorLayouts");
		Directory.CreateDirectory(layoutDirectory);
		_layoutFilePath = Path.Combine(layoutDirectory, "imgui_layout.ini");
		_layoutManager = new LayoutManager(_layoutFilePath);

		LoadSettings();

		_renderer = new ImGuiRenderer(Core.Instance);
		_renderer.RebuildFontAtlas(options);

		Core.Emitter.AddObserver(CoreEvents.SceneChanged, OnSceneChanged);

		ImGui.GetStyle().IndentSpacing = 12;
		ImGui.GetIO().ConfigWindowsMoveFromTitleBarOnly = true;

		var io = ImGui.GetIO();
		io.ConfigFlags |= ImGuiConfigFlags.DockingEnable;

		// Set the ini file path BEFORE loading
		unsafe
		{
			//Make GC does not collect LayoutInitPathUtf8 
			_layoutManager.LayoutInitPathUtf8 = System.Text.Encoding.UTF8.GetBytes(_layoutFilePath + "\0");
			fixed (byte* ptr = _layoutManager.LayoutInitPathUtf8)
			{
				ImGuiNative.igGetIO()->IniFilename = ptr;
			}
		}

		LoadLastSelectedLayout();

		// Initialize theme manager (will apply last selected theme automatically)
		_themeManager = new ThemeManager();

		SceneGraphWindow = new SceneGraphWindow();
		_cursorSelectionManager = new GizmoSelectionManager(this);

		ImguiImageLoader.LoadImages(_renderer);

		_effectBuildProgressWindow = new EffectBuildProgressWindow();

		_dataManager = DataManager.Instance;

		// Create default Main Entity Inspector window when current scene is finished loading the entities
		Scene.OnFinishedAddingEntitiesWithData += OpenMainEntityInspector;
		Core.EmitterWithPending.AddObserver(CoreEvents.Exiting, OnAppExitSaveChanges);

		Core.OnResetScene += RequestResetScene;
		Core.OnSwitchEditMode += OnEditModeSwitched;

		SceneManager.Instance.OnSceneLoaded += OnSceneLoadedHandler;
		SceneManager.Instance.OnSceneSaved += OnSceneSavedHandler;

		OpenMainEntityInspector();
		_entityInspectors.Add(MainEntityInspectorWindow);

		// Initialize project manager
		_projectManager = ProjectManager.Instance;
		_projectManager.OnProjectLoaded += OnProjectLoaded;
		_projectManager.OnProjectUnloaded += OnProjectUnloaded;
		_projectManager.LoadLastProject();

		InitializeScriptManager();
	}

	private void OnSceneLoadedHandler(string scenePath)
	{
	}

	private void OnSceneSavedHandler(string scenePath)
	{
	}

	/// <summary>
	/// Loads the last selected layout from persistent settings
	/// </summary>
	private void LoadLastSelectedLayout()
	{
		string lastLayout = _lastSelectedLayout.Value;

		if (!string.IsNullOrWhiteSpace(lastLayout) && !lastLayout.Equals("Default", StringComparison.OrdinalIgnoreCase))
		{
			string layoutPath = _layoutManager.GetLayoutPath(lastLayout);

			if (File.Exists(layoutPath))
			{
				_layoutManager.LoadLayout(lastLayout);
			}
			else
			{
				Debug.Warn($"Last used layout '{lastLayout}' not found, loading default");
				LoadDefaultLayout();
			}
		}
		else
		{
			LoadDefaultLayout();
		}
	}

	/// <summary>
	/// Loads the default layout
	/// </summary>
	private void LoadDefaultLayout()
	{
		if (File.Exists(_layoutFilePath))
		{
			ImGui.LoadIniSettingsFromDisk(_layoutFilePath);
		}
	}

	/// <summary>
	/// This is where we issue any and all ImGui commands to be drawn
	/// </summary>
	private void LayoutGui()
	{
		if (_layoutManager.HasPendingReload)
		{
			_layoutManager.ApplyPendingReload();
		}

		if (_isFirstFrame)
		{
			_isFirstFrame = false;
			return;
		}

		if (ShowMenuBar)
			DrawMainMenuBar();

		CreateDockspace();
		DrawEditorToolsBar();
		DrawProjectFilePicker();
		DrawSaveChangesPrompt();
		DrawCreateSceneForSavePrompt();
		ShowSceneGraphWindow = SceneGraphWindow.Show(ShowSceneGraphWindow);
		ShowCoreWindow = _coreWindow.Show(ShowCoreWindow);
		_debugWindow.Draw();
		DrawEntityInspectors();
		_effectBuildProgressWindow.Draw();
		_projectCreator.Draw();
		_sceneCreator.Draw();
		_projectSettingsWindow.Draw();

		for (var i = _drawCommands.Count - 1; i >= 0; i--)
			_drawCommands[i]();

		if (_spriteAtlasEditorWindow != null)
			if (!_spriteAtlasEditorWindow.Show())
				_spriteAtlasEditorWindow = null;

		if (ShowDemoWindow)
			ImGui.ShowDemoWindow(ref ShowDemoWindow);

		if (ShowStyleEditor)
		{
			var showStyleEditor = ShowStyleEditor;
			ImGui.Begin("Style Editor", ref showStyleEditor);
			ShowStyleEditor = showStyleEditor;

			ImGui.ShowStyleEditor();
			ImGui.End();
		}

		if (!_hasCheckedEngineEffects && !_engineEffectsCheckComplete)
		{
			CheckEngineEffectsExist();
			_hasCheckedEngineEffects = true;
		}

		if (_showEngineEffectsPrompt)
		{
			DrawEngineEffectsPrompt();
		}

		UpdateCamera();
		NotificationSystem.Draw();
		GlobalKeyCommands();

		DrawSelectedEntityOutlines();

		if (ShowAnimationEventInspector)
		{
			if (_animationEventInspector == null)
			{
				_animationEventInspector = new AnimationEventInspector(null);
				RegisterDrawCommand(_animationEventInspector.Draw);
			}
		}
		else
		{
			if (_animationEventInspector != null)
			{
				UnregisterDrawCommand(_animationEventInspector.Draw);
				_animationEventInspector = null;
			}
		}

		DrawScriptingWindow();
		_editorSettingsWindow.Draw();
		_cursorSelectionManager.UpdateSelection();
	}

	private void OnEditModeSwitched(bool isEditMode)
	{
		// Only reset scene if switching to EditMode from PlayMode
		if (isEditMode && Core.ResetSceneAutomatically)
		{
			var sceneManager = SceneManager.Instance;
			if (sceneManager.HasLoadedScene)
			{
				sceneManager.ReloadCurrentScene();
			}
			else
			{
				// Fallback for old system
				Core.InvokeResetScene();
			}
		}
	}

	/// <summary>
	/// Opens the file picker for selecting a .voltage file
	/// </summary>
	private void OpenProjectFilePicker()
	{
		string startingPath = !string.IsNullOrWhiteSpace(_projectManager.LastProjectPath)
			? Path.GetDirectoryName(_projectManager.LastProjectPath)
			: Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

		_projectFilePicker = FilePicker.GetFilePicker(this, startingPath, ".voltage");
		_projectFilePicker.DontAllowTraverselBeyondRootFolder = false;
		_showProjectFilePicker = true;
	}

	/// <summary>
	/// Draws the project file picker popup
	/// </summary>
	private void DrawProjectFilePicker()
	{
		if (_showProjectFilePicker && _projectFilePicker != null)
		{
			ImGui.OpenPopup("load-project-popup");
			_showProjectFilePicker = false;
		}

		var center = new Num.Vector2(Screen.Width * 0.5f, Screen.Height * 0.5f);
		ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new Num.Vector2(0.5f, 0.5f));
		ImGui.SetNextWindowSize(new Num.Vector2(600, 500), ImGuiCond.Appearing);

		bool open = true;
		if (ImGui.BeginPopupModal("load-project-popup", ref open, ImGuiWindowFlags.NoResize))
		{
			ImGui.TextColored(new Num.Vector4(0.2f, 0.8f, 1.0f, 1.0f), "Load Project");
			ImGui.Separator();
			ImGui.TextWrapped("Select a .voltage file to load:");
			VoltageEditorUtils.SmallVerticalSpace();

			if (_projectFilePicker != null && _projectFilePicker.Draw())
			{
				var selectedFile = _projectFilePicker.SelectedFile;

				// Validate .voltage file
				if (Path.GetExtension(selectedFile).Equals(".voltage", StringComparison.OrdinalIgnoreCase))
				{
					bool success = _projectManager.LoadProject(selectedFile);

					if (success)
					{
						EditorDebug.Log(
							$"Project loaded: {_projectManager.CurrentProject.ProjectName}");
					}
					else
					{
						EditorDebug.Error(
							"Failed to load project.");
					}
				}
				else
				{
					EditorDebug.Warn("Please select a valid .voltage file.");
				}

				FilePicker.RemoveFilePicker(_projectFilePicker);
				_projectFilePicker = null;
				ImGui.CloseCurrentPopup();
			}

			ImGui.EndPopup();
		}

		// Handle cancel/close via X button
		if (!open && _projectFilePicker != null)
		{
			FilePicker.RemoveFilePicker(_projectFilePicker);
			_projectFilePicker = null;
		}
	}

	/// <summary>
	/// Checks if engine effects exist in Content/Voltage/Effects
	/// </summary>
	private void CheckEngineEffectsExist()
	{
		try
		{
			var projectDir = FindProjectDir();
			var effectsDir = Path.Combine(projectDir, "Content", "Voltage", "Effects");

			bool needsBuild = false;

			if (!Directory.Exists(effectsDir))
			{
				Debug.Warn("Content/Voltage/Effects directory not found");
				needsBuild = true;
			}
			else
			{
				var effectFiles = Directory.GetFiles(effectsDir, "*.mgfxo", SearchOption.AllDirectories);
				if (effectFiles.Length == 0)
				{
					Debug.Warn("Content/Voltage/Effects directory is empty");
					needsBuild = true;
				}
			}

			if (needsBuild)
			{
				_showEngineEffectsPrompt = true;
			}
			else
			{
				_engineEffectsCheckComplete = true;
			}
		}
		catch (Exception ex)
		{
			Debug.Error($"Error checking engine effects: {ex.Message}");
			_engineEffectsCheckComplete = true;
		}
	}

	/// <summary>
	/// Finds the Voltage.Editor project directory
	/// </summary>
	private static string FindProjectDir()
	{
		var dir = AppContext.BaseDirectory;
		var di = new DirectoryInfo(dir);
		while (di != null)
		{
			if (File.Exists(Path.Combine(di.FullName, "Voltage.Editor.csproj")))
				return di.FullName;
			di = di.Parent;
		}

		return AppContext.BaseDirectory;
	}

	private void CreateDockspace()
	{
		var viewport = ImGui.GetMainViewport();
		var dockspaceSize = viewport.WorkSize;

		ImGui.SetNextWindowPos(viewport.WorkPos);
		ImGui.SetNextWindowSize(dockspaceSize);
		ImGui.SetNextWindowViewport(viewport.ID);

		ImGuiWindowFlags windowFlags = ImGuiWindowFlags.NoDocking;
		windowFlags |= ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoCollapse;
		windowFlags |= ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove;
		windowFlags |= ImGuiWindowFlags.NoBringToFrontOnFocus | ImGuiWindowFlags.NoNavFocus;
		windowFlags |= ImGuiWindowFlags.NoBackground;

		ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0.0f);
		ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0.0f);
		ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Num.Vector2(0.0f, 0.0f));
		ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Num.Vector2(0.0f, 0.0f));

		ImGui.Begin("DockSpaceWindow", windowFlags);
		ImGui.PopStyleVar(4);

		var dockspaceId = ImGui.GetID("MainDockSpace");
		ImGui.DockSpace(dockspaceId, new Num.Vector2(0.0f, 0.0f),
			ImGuiDockNodeFlags.PassthruCentralNode | ImGuiDockNodeFlags.NoDockingInCentralNode);

		ImGui.End();
	}

	public void GlobalKeyCommands()
	{
		if (ImGui.IsKeyPressed(ImGuiKey.F5, false))
			Core.InvokeResetScene();

		if (ImGui.IsKeyPressed(ImGuiKey.F1, false) || ImGui.IsKeyPressed(ImGuiKey.F2, false))
			Core.InvokeSwitchEditMode(!Core.IsEditMode);

		if (ImGui.GetIO().KeyCtrl && ImGui.IsKeyPressed(ImGuiKey.S, false))
		{
			InvokeSaveSceneChanges();
		}

		if (ImGui.IsKeyPressed(ImGuiKey.F6, false))
			_scriptManager?.ReloadScene();

		// This triggers the same exit/save prompt as the window close event
#if OS_WINDOWS || LINUX
		if (ImGui.GetIO().KeyAlt && ImGui.IsKeyPressed(ImGuiKey.F4, false) && !_pendingExit)
			OnAppExitSaveChanges(true);
#elif OS_MAC
		if (ImGui.GetIO().KeySuper && ImGui.IsKeyPressed(ImGuiKey.Q, false) && !_pendingExit)
			OnAppExitSaveChanges(true);
#endif
	}

	/// <summary>
	/// Invokes scene save through DataManager with proper UI validation.
	/// </summary>
	private void InvokeSaveSceneChanges()
	{
		if (Core.Scene == null)
		{
			Debug.Error("No active scene to save!");
			EditorDebug.Log("No active scene to save!");
			return;
		}

		if (!SceneManager.Instance.HasLoadedScene)
		{
			_newSceneNameForSave = "NewScene";
			_showCreateSceneForSavePrompt = true;
			return;
		}

		// Schedule the async save operation
		Core.Schedule(0f, false, this, async _ =>
		{
			try
			{
				await _dataManager.SaveSceneChangesAsync();
			}
			catch (Exception ex)
			{
				Debug.Error($"Failed to save scene: {ex.Message}");
				EditorDebug.Log($"Save failed: {ex.Message}");
			}
		});
	}

	private void ManageUndoAndRedo()
	{
		if (ImGui.GetIO().KeyCtrl && ImGui.IsKeyPressed(ImGuiKey.Z, false))
		{
			EditorChangeTracker.Undo();
		}

		if (ImGui.GetIO().KeyCtrl && ImGui.IsKeyPressed(ImGuiKey.Y, false))
		{
			EditorChangeTracker.Redo();
		}
	}

	#region Drawing Methods

	/// <summary>
	/// draws the main menu bar
	/// </summary>
	private void DrawMainMenuBar()
	{
		if (ImGui.BeginMainMenuBar())
		{
			DrawFileMenu();
			DrawProjectMenu();
			DrawViewMenu();
			DrawScriptingMenu();
			DrawBuildMenu();
			DrawHelpMenu();

			// Must be the last one, so that it's centered properly
			DrawCurrentProjectIndicator();

			ImGui.EndMainMenuBar();
		}
	}

	private void DrawEditorToolsBar()
	{
		ImGui.Begin("Editor Tools", ImGuiWindowFlags.NoScrollbar);

		float spacing = 12f * FontSizeMultiplier;
		float iconSize = 24f * FontSizeMultiplier;

		// Normal Button
		System.Numerics.Vector4 normalButtonColor;
		if (_cursorSelectionManager.SelectionMode == CursorSelectionMode.Normal)
			normalButtonColor = new System.Numerics.Vector4(0.2f, 0.5f, 1f, 1f);
		else
			normalButtonColor = ImGui.GetStyle().Colors[(int)ImGuiCol.Button];

		ImGui.PushStyleColor(ImGuiCol.Button, normalButtonColor);
		bool normalHovered =
			ImGui.ImageButton("Normal", ImguiImageLoader.NormalCursorIconID, new Num.Vector2(iconSize, iconSize));
		if (normalHovered)
			_cursorSelectionManager.SelectionMode = CursorSelectionMode.Normal;
		ImGui.PopStyleColor();

		if (ImGui.IsItemHovered())
		{
			ImGui.SetTooltip("Default Cursor (Press Q or 1)");
		}

		ImGui.SameLine(0, spacing);

		// Resize Button 
		System.Numerics.Vector4 resizeButtonColor;
		if (_cursorSelectionManager.SelectionMode == CursorSelectionMode.Resize)
			resizeButtonColor = new System.Numerics.Vector4(0.2f, 0.5f, 1f, 1f);
		else
			resizeButtonColor = ImGui.GetStyle().Colors[(int)ImGuiCol.Button];

		ImGui.PushStyleColor(ImGuiCol.Button, resizeButtonColor);
		bool resizeHovered =
			ImGui.ImageButton("Resize", ImguiImageLoader.ResizeCursorIconID, new Num.Vector2(iconSize, iconSize));
		if (resizeHovered)
			_cursorSelectionManager.SelectionMode = CursorSelectionMode.Resize;
		ImGui.PopStyleColor();

		if (ImGui.IsItemHovered())
		{
			ImGui.SetTooltip("Resize Entities (Press E or 2)");
		}

		ImGui.SameLine(0, spacing);

		// Rotate Button
		System.Numerics.Vector4 rotateButtonColor;
		if (_cursorSelectionManager.SelectionMode == CursorSelectionMode.Rotate)
			rotateButtonColor = new System.Numerics.Vector4(0.2f, 0.5f, 1f, 1f);
		else
			rotateButtonColor = ImGui.GetStyle().Colors[(int)ImGuiCol.Button];

		ImGui.PushStyleColor(ImGuiCol.Button, rotateButtonColor);
		bool rotateHovered =
			ImGui.ImageButton("Rotate", ImguiImageLoader.RotateCursorIconID, new Num.Vector2(iconSize, iconSize));
		if (rotateHovered)
			_cursorSelectionManager.SelectionMode = CursorSelectionMode.Rotate;
		ImGui.PopStyleColor();

		if (ImGui.IsItemHovered())
		{
			ImGui.SetTooltip("Rotate Entities (Press R or 3)");
		}

		ImGui.SameLine(0, spacing);

		// Collider Resize Button
		System.Numerics.Vector4 colliderResizeButtonColor;
		if (_cursorSelectionManager.SelectionMode == CursorSelectionMode.ColliderResize)
			colliderResizeButtonColor = new System.Numerics.Vector4(0.2f, 0.5f, 1f, 1f);
		else
			colliderResizeButtonColor = ImGui.GetStyle().Colors[(int)ImGuiCol.Button];

		ImGui.PushStyleColor(ImGuiCol.Button, colliderResizeButtonColor);

		bool colliderResizeHovered = ImGui.ImageButton("Collider Resize", ImguiImageLoader.ColliderResizeCursorIconID,
			new Num.Vector2(iconSize, iconSize));
		if (colliderResizeHovered)
			_cursorSelectionManager.SelectionMode = CursorSelectionMode.ColliderResize;

		ImGui.PopStyleColor();

		if (ImGui.IsItemHovered())
		{
			ImGui.SetTooltip("Resize Colliders (Press T or 4)");
		}

		// Zoom indicator
		ImGui.SameLine(0, spacing * 2f);
		float currentZoom = Core.Scene.Camera.Zoom;
		int zoomPercentage = (int)(currentZoom * 100f);
		string zoomText = $"Zoom: {zoomPercentage}%";

		ImGui.TextColored(new System.Numerics.Vector4(0.7f, 0.7f, 0.7f, 1.0f), zoomText);

		if (ImGui.IsItemHovered())
		{
			ImGui.SetTooltip("Camera zoom level (Mouse Wheel to adjust)");
		}

		ImGui.End();
	}

	/// <summary>
	/// draws all the separate EntityInspectors (not Main Entity Inspector)
	/// </summary>
	private void DrawEntityInspectors()
	{
		MainEntityInspectorWindow.IsOpen = ShowMainInspectorWindow;
		MainEntityInspectorWindow.Draw();

		// Only draw and remove non-main inspectors (make sure i starts from 1!)
		for (int i = 1; i < _entityInspectors.Count; i++)
		{
			if (_entityInspectors[i] == null)
				continue;

			if (!_entityInspectors[i].IsOpen)
			{
				_entityInspectors.RemoveAt(i);
				continue;
			}

			_entityInspectors[i].Draw();
		}

	}

	#endregion

	private void UpdateCamera()
	{
		if (Core.IsEditMode)
		{
			ManageCameraZoom();

			// Camera Dragging with Middle Mouse
			var mousePos = Input.ScaledMousePosition;
			if (Input.MiddleMouseButtonPressed)
			{
				_isCameraDragging = true;
				_cameraDragStartMouse = mousePos;
				_cameraDragStartPosition = _cameraTargetPosition;
			}
			else if (_isCameraDragging && Input.MiddleMouseButtonDown)
			{
				var delta = mousePos - _cameraDragStartMouse;
				_cameraTargetPosition = _cameraDragStartPosition - delta;
			}
			else if (_isCameraDragging && !Input.MiddleMouseButtonDown)
			{
				_isCameraDragging = false;
			}

			if (_cameraTargetPosition == default)
				_cameraTargetPosition = Core.Scene.Camera.Position;

			bool isMovingCamera = Input.IsKeyDown(Keys.W) || Input.IsKeyDown(Keys.A) ||
			                      Input.IsKeyDown(Keys.S) || Input.IsKeyDown(Keys.D);

			if (Input.IsKeyDown(Keys.LeftShift))
			{
				if (isMovingCamera)
				{
					CurrentCameraSpeed = _dynamicCameraSpeed;
				}
				else
				{
					CurrentCameraSpeed = EditModeCameraFastSpeed;
				}
			}
			else
			{
				CurrentCameraSpeed = EditModeCameraSpeed;
			}

			if (!Input.IsKeyDown(Keys.LeftControl) && !Input.IsKeyDown(Keys.RightControl))
			{
				if (Input.IsKeyDown(Keys.D))
					_cameraTargetPosition += new Vector2(CurrentCameraSpeed, 0) * Time.DeltaTime;
				if (Input.IsKeyDown(Keys.A))
					_cameraTargetPosition -= new Vector2(CurrentCameraSpeed, 0) * Time.DeltaTime;
				if (Input.IsKeyDown(Keys.W))
					_cameraTargetPosition -= new Vector2(0, CurrentCameraSpeed) * Time.DeltaTime;
				if (Input.IsKeyDown(Keys.S))
					_cameraTargetPosition += new Vector2(0, CurrentCameraSpeed) * Time.DeltaTime;
			}

			Core.Scene.Camera.Position =
				Vector2.Lerp(Core.Scene.Camera.Position, _cameraTargetPosition, _cameraLerp);
		}
	}

	private void ManageCameraZoom()
	{
		if (Input.MouseWheelDelta != 0)
		{
			bool isShiftHeld = Input.IsKeyDown(Keys.LeftShift);

			if (isShiftHeld)
			{
				// Modify camera movement speed instead of zoom
				float speedDelta = Input.MouseWheelDelta * CameraSpeedAdjustmentStep * Time.DeltaTime;
				SetDynamicCameraSpeed(_dynamicCameraSpeed + speedDelta);
			}
			else
			{
				// Normal zoom behavior
				if (Input.MouseWheelDelta > 0)
				{
					Core.Scene.Camera.Zoom += EditorCameraZoomSpeed * Time.DeltaTime;
				}
				else if (Input.MouseWheelDelta < 0)
				{
					if (Core.Scene.Camera.Zoom - EditorCameraZoomSpeed * Time.DeltaTime > -0.9)
						Core.Scene.Camera.Zoom -= EditorCameraZoomSpeed * Time.DeltaTime;
				}
			}
		}
		else if (!Core.IsEditMode)
		{
			Core.Scene.Camera.Zoom = Camera.DefaultZoom;
		}
	}

	/// <summary>
	/// Reset dynamic camera speed to default
	/// </summary>
	public static void ResetDynamicCameraSpeed()
	{
		_dynamicCameraSpeed = EditModeCameraSpeed;
	}

	/// <summary>
	/// Set the dynamic camera speed directly
	/// </summary>
	public static void SetDynamicCameraSpeed(float speed)
	{
		_dynamicCameraSpeed = MathHelper.Clamp(speed, EditModeCameraMinSpeed, EditModeCameraMaxSpeed);
	}

	/// <summary>
	/// Get the current dynamic camera speed
	/// </summary>
	public static float GetDynamicCameraSpeed()
	{
		return _dynamicCameraSpeed;
	}

	#region Public API

	/// <summary>
	/// registers an Action that will be called and any ImGui drawing can be done in it
	/// </summary>
	/// <param name="drawCommand"></param>
	public void RegisterDrawCommand(Action drawCommand)
	{
		_drawCommands.Add(drawCommand);
	}

	/// <summary>
	/// removes the Action from the draw commands
	/// </summary>
	/// <param name="drawCommand"></param>
	public void UnregisterDrawCommand(Action drawCommand)
	{
		_drawCommands.Remove(drawCommand);
		Scene.OnFinishedAddingEntitiesWithData -= OpenMainEntityInspector;
	}

	/// <summary>
	/// Creates a pointer to a texture, which can be passed through ImGui calls such as <see cref="ImGui.Image" />.
	/// That pointer is then used by ImGui to let us know what texture to draw
	/// </summary>
	/// <param name="textureId"></param>
	public void UnbindTexture(IntPtr textureId)
	{
		_renderer.UnbindTexture(textureId);
	}

	/// <summary>
	/// Removes a previously created texture pointer, releasing its reference and allowing it to be deallocated
	/// </summary>
	/// <param name="texture"></param>
	/// <returns></returns>
	public IntPtr BindTexture(Texture2D texture)
	{
		return _renderer.BindTexture(texture);
	}

	/// <summary>
	/// Creates a normal EntityInspectorWindow window
	/// </summary>
	/// <param name="entity"></param>
	public void OpenSeparateEntityInspector(Entity entity)
	{
		// Only add if not already present as a pop-out
		if (_entityInspectors.Any(i => i.Entity == entity))
			return;

		entitynspectorInitialSpawnOffset += entitynspectorSpawnOffsetIncremental;

		var inspector = new EntityInspectorWindow(this, entity, isMain: false, entitynspectorInitialSpawnOffset);
		_entityInspectors.Add(inspector);

		inspector.SetWindowFocus();
	}

	/// <summary>
	/// Creates (or replaces) a EntityInspectorWindow
	/// </summary>
	/// <param name="entity"></param>
	public void OpenMainEntityInspector(Entity entity = null)
	{
		if (IsInspectorTabLocked)
			return;

		if (MainEntityInspectorWindow != null)
		{
			if (MainEntityInspectorWindow.Entity == entity)
				return;

			MainEntityInspectorWindow.SetEntity(entity);
		}
		else
		{
			MainEntityInspectorWindow = new EntityInspectorWindow(this, null, isMain: true);
		}
	}

	/// <summary>
	/// removes the EntityInspectorWindow for this Entity
	/// </summary>
	/// <param name="entity"></param>
	public void CloseEntityInspector(Entity entity)
	{
		for (var i = 0; i < _entityInspectors.Count; i++)
		{
			var inspector = _entityInspectors[i];
			if (inspector.Entity == entity)
			{
				_entityInspectors.RemoveAt(i);

				if (entitynspectorInitialSpawnOffset - entitynspectorSpawnOffsetIncremental >=
				    0) // Reset the previous spawn offset 
					entitynspectorInitialSpawnOffset -= entitynspectorSpawnOffsetIncremental;

				return;
			}
		}
	}

	/// <summary>
	/// removes the EntityInspectorWindow
	/// </summary>
	/// <param name="entityInspectorWindow"></param>
	public void CloseEntityInspector(EntityInspectorWindow entityInspectorWindow)
	{
		_entityInspectors.RemoveAt(_entityInspectors.IndexOf(entityInspectorWindow));

		// Reset the previous spawn offset 
		if (entitynspectorInitialSpawnOffset - entitynspectorSpawnOffsetIncremental >= 0)
			entitynspectorInitialSpawnOffset -= entitynspectorSpawnOffsetIncremental;
	}

	public void CloseMainEntityInspector()
	{
		MainEntityInspectorWindow = null;
	}

	/// <summary>
	/// Refreshes the main entity inspector's component inspectors.
	/// Call this after making changes to entity components.
	/// </summary>
	public void RefreshMainEntityInspector()
	{
		MainEntityInspectorWindow?.RefreshComponentInspectors();
	}

	#endregion

	#region Save Changes for AppExit/ SceneChange / SceneReset

	private void OnAppExitSaveChanges(bool pending)
	{
		if (pending)
		{
			// Only show the prompt if there are unsaved changes
			if (EditorChangeTracker.IsDirty)
				_pendingExit = true;
			else
				Core.ConfirmAndExit();
		}
	}

	private void DrawSaveChangesPrompt()
	{
		// Handle exit prompt
		if (_pendingExit)
		{
			ImGui.OpenPopup("Save Changes?##Exit");
			_pendingExit = false;
		}

		// Handle scene change prompt
		if (_pendingSceneChange)
		{
			ImGui.OpenPopup("Save Changes?##SceneChange");
			_pendingSceneChange = false;
		}

		// Handle reset scene prompt
		if (_pendingResetScene)
		{
			ImGui.OpenPopup("Save Changes?##ResetScene");
			_pendingResetScene = false;
		}

		// Handle project close prompt
		if (_pendingProjectClose)
		{
			ImGui.OpenPopup("Save Changes?##ProjectClose");
			_pendingProjectClose = false;
		}

		DrawSavePromptModal("Save Changes?##Exit", () =>
		{
			Core.ConfirmAndExit();
		});

		DrawSavePromptModal("Save Changes?##SceneChange", () =>
		{
			if (!string.IsNullOrEmpty(_requestedSceneName))
			{
				LoadSceneByName(_requestedSceneName);
			}
			else if (_requestedSceneType != null)
			{
				// Fallback for old system
				var scene = (Scene)Activator.CreateInstance(_requestedSceneType);
				Core.StartSceneTransition(new FadeTransition(() => scene));
			}
			_requestedSceneName = null;
			_requestedSceneType = null;
		});

		DrawSavePromptModal("Save Changes?##ResetScene", () =>
		{
			ResetScene();
		});

		DrawSavePromptModal("Save Changes?##ProjectClose", () =>
		{
			CloseCurrentProject();
		});
	}

	private void DrawSavePromptModal(string popupId, Action onDiscardChanges)
	{
		var center = new System.Numerics.Vector2(Screen.Width * 0.5f, Screen.Height * 0.4f);
		ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new System.Numerics.Vector2(0.5f, 0.5f));
		ImGui.SetNextWindowSize(new System.Numerics.Vector2(450, 0), ImGuiCond.Appearing);

		bool open = true;
		if (ImGui.BeginPopupModal(popupId, ref open, ImGuiWindowFlags.AlwaysAutoResize))
		{
			// Determine the action context from popup ID
			string actionContext = "continuing";
			if (popupId.Contains("Exit"))
				actionContext = "exiting";
			else if (popupId.Contains("SceneChange"))
				actionContext = "changing scenes";
			else if (popupId.Contains("ResetScene"))
				actionContext = "resetting the scene";
			else if (popupId.Contains("ProjectClose"))
				actionContext = "closing the project";

			ImGui.Text("You have unsaved changes!");
			ImGui.Spacing();

			// Display pending changes
			var pendingChanges = EditorChangeTracker.ChangedObjects;
			if (pendingChanges.Count > 0)
			{
				ImGui.TextWrapped($"The following changes will be lost if you don't save before {actionContext}:");
				ImGui.Spacing();

				float maxHeight = Math.Min(300f, pendingChanges.Count * 25f);

				ImGui.BeginChild("##changes_list", new System.Numerics.Vector2(420, maxHeight), true);

				foreach (var (obj, description) in pendingChanges)
				{
					ImGui.BulletText(description);

					if (ImGui.IsItemHovered() && obj != null)
					{
						ImGui.SetTooltip($"Type: {obj.GetType().Name}");
					}
				}

				ImGui.EndChild();
				ImGui.Spacing();
			}

			ImGui.TextWrapped($"Do you want to save your changes before {actionContext}?");

			VoltageEditorUtils.MediumVerticalSpace();

			var buttonWidth = 120f;
			var spacing = 10f;
			var totalButtonWidth = (buttonWidth * 3) + (spacing * 2);
			var windowWidth = ImGui.GetWindowSize().X;
			var centerStart = (windowWidth - totalButtonWidth) * 0.5f;

			ImGui.SetCursorPosX(centerStart);

			if (ImGui.Button("Save", new System.Numerics.Vector2(buttonWidth, 0)))
			{
				Core.Schedule(0.1f, false, this, async _ =>
				{
					await SaveSceneAsyncAndThenAct();
					onDiscardChanges();
				});
				ImGui.CloseCurrentPopup();
			}

			ImGui.SameLine();

			if (ImGui.Button("Don't Save", new System.Numerics.Vector2(buttonWidth, 0)))
			{
				EditorChangeTracker.Clear();
				onDiscardChanges();
				ImGui.CloseCurrentPopup();
			}

			ImGui.SameLine();

			if (ImGui.Button("Cancel", new System.Numerics.Vector2(buttonWidth, 0)))
			{
				_requestedSceneName = null;
				_requestedSceneType = null;
				_pendingProjectClose = false;
				ImGui.CloseCurrentPopup();
			}

			ImGui.EndPopup();
		}
	}

	public void RequestResetScene()
	{
		if (EditorChangeTracker.IsDirty)
		{
			_pendingResetScene = true;
			_requestedResetSceneType = Core.Scene.GetType();
		}
		else
		{
			ResetScene();
		}
	}

	private void RequestProjectClose()
	{
		if (EditorChangeTracker.IsDirty)
		{
			_pendingProjectClose = true;
		}
		else
		{
			CloseCurrentProject();
		}
	}

	private void CloseCurrentProject()
	{
		var projectName = _projectManager.CurrentProject?.ProjectName;
		var sceneManager = SceneManager.Instance;
		sceneManager.ClearCurrentScene();

		_projectManager.UnloadCurrentProject();
		EditorDebug.Log($"Project closed: {projectName}");

		EditorChangeTracker.Clear();
	}

	private void ResetScene()
	{
		var sceneManager = SceneManager.Instance;
		if (sceneManager.HasLoadedScene)
		{
			// Reload from file
			sceneManager.ReloadCurrentScene();
		}
		else
		{
			// Fallback to old system for backwards compatibility
			var newScene = (Scene)Activator.CreateInstance(_requestedResetSceneType ?? Core.Scene.GetType());
			Core.Scene = newScene;
		}
		EditorChangeTracker.Clear();
	}

	private async Task SaveSceneAsyncAndThenAct()
	{
		await _dataManager.SaveSceneChangesAsync();
	}

	#endregion

	public void OpenAnimationEventInspector(SpriteAnimator animator)
	{
		if (_animationEventInspector == null)
		{
			_animationEventInspector = new AnimationEventInspector(animator);
			AnimationEventInspectorInstance = _animationEventInspector;
			RegisterDrawCommand(_animationEventInspector.Draw);
		}
		else
		{
			_animationEventInspector.SetAnimator(animator);
		}

		ShowAnimationEventInspector = true;
	}

	public void DrawSelectedEntityOutlines()
	{
		// Only update cache if selection changed
		if (_lastSelectedEntities == null ||
		    !SceneGraphWindow.EntityPane.SelectedEntities.SequenceEqual(_lastSelectedEntities))
		{
			_highlightedEntities.Clear();
			foreach (var entity in SceneGraphWindow.EntityPane.SelectedEntities)
			{
				var collider = entity.GetComponent<Collider>();
				_highlightedEntities.Add((entity, collider));
			}

			_lastSelectedEntities = SceneGraphWindow.EntityPane.SelectedEntities.ToList();
		}

		// Draw highlights using cached info
		foreach (var (entity, collider) in _highlightedEntities)
		{
			RectangleF bounds;
			if (collider != null)
			{
				bounds = collider.Bounds;
			}
			else
			{
				var pos = entity.Transform.Position;
				bounds = new RectangleF(pos.X - 8, pos.Y - 8, 16, 16);
			}

			Debug.DrawHollowRect(bounds, Color.Yellow);
		}
	}

	public void ClearHighlightCache()
	{
		_highlightedEntities.Clear();
		_lastSelectedEntities = null;
	}

	private void RequestSceneChange(string sceneName)
	{
		if (EditorChangeTracker.IsDirty)
		{
			TriggerSceneChangePrompt(sceneName);
		}
		else
		{
			LoadSceneByName(sceneName);
		}
	}

	private void TriggerSceneChangePrompt(string sceneName)
	{
		_pendingSceneChange = true;
		_requestedSceneName = sceneName;
		_requestedSceneType = null;
		_pendingExit = false;
	}

	private void LoadSceneByName(string sceneName)
	{
		SceneManager.Instance.LoadSceneByName(sceneName);
	}
}