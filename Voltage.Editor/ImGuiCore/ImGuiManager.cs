using ImGuiNET;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Voltage;
using Voltage.Data;
using Voltage.Editor.FilePickers;
using Voltage.Editor.Gizmos;
using Voltage.Editor.Inspectors;
using Voltage.Editor.Inspectors.CustomInspectors;
using Voltage.Editor.Interfaces;
using Voltage.Editor.Persistence;
using Voltage.Editor.Tools;
using Voltage.Editor.UndoActions;
using Voltage.Editor.Utils;
using Voltage.Sprites;
using Voltage.Utils;
using Num = System.Numerics;
using Voltage.Editor.Layouts;
using Voltage.Editor.ProjectManagement;
using Voltage.Editor.Scripting;


namespace Voltage.Editor.ImGuiCore;

public partial class ImGuiManager : GlobalManager, IFinalRenderDelegate, IDisposable
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

	private PersistentBool _showAnimationEventInspector = new("ImGui_ShowAnimationEventInspector", true);

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

	private PersistentString _lastSelectedLayout = new("ImGui_LastSelectedLayout", "Default");
	private PersistentString _lastSelectedTheme = new("ImGui_LastSelectedTheme", "DarkTheme1");

	#endregion

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
	public ImguiImageLoader ImageLoader => _imageLoader;
	public SceneGraphWindow SceneGraphWindow { get; private set; }
	public MainEntityInspector MainEntityInspector { get; private set; }
	public bool IsInspectorTabLocked = false;

	public AnimationEventInspector AnimationEventInspectorInstance
	{
		get => _animationEventInspector;
		private set => _animationEventInspector = value;
	}

	#endregion

	#region Private values

	private List<Type> _sceneSubclasses = new();
	private System.Reflection.MethodInfo[] _themes;
	private CoreWindow _coreWindow = new();
	private DebugWindow _debugWindow = new();
	private ProjectCreator _projectCreator = new();
	private ProjectManager _projectManager;

	private Num.Vector2 normalEntityInspectorStartPos;
	private int entitynspectorInitialSpawnOffset = 0;
	private static int entitynspectorSpawnOffsetIncremental = 20;

	private AnimationEventInspector _animationEventInspector;
	private SpriteAtlasEditorWindow _spriteAtlasEditorWindow;
	private List<EntityInspector> _entityInspectors = new();
	private List<Action> _drawCommands = new();
	private ImGuiRenderer _renderer;
	private ImGuiInput _input = new ImGuiInput();
	private GizmoSelectionManager _cursorSelectionManager;
	private ImguiImageLoader _imageLoader;
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
	private bool _pendingResetScene = false;
	private Type _requestedResetSceneType = null;
	private Task _pendingSaveTask = null;
	private ExitPromptType _pendingActionAfterSave;

	//Layout management
	private string _layoutFilePath;
	private LayoutManager _layoutManager;
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
	private EditorSettingsWindow _editorSettingsWindow;

	// Entity Selection
	private List<(Entity entity, Collider collider)> _highlightedEntities = new();
	private IReadOnlyList<Entity> _lastSelectedEntities = null;

	#endregion

	#region Event Handlers

	/// <summary>
	/// Can be used to wait for the scene changes to happen first.
	/// </summary>
	public event Func<Task> OnSaveSceneAsync;

	public event Func<Entity, bool, Task<bool>> OnPrefabCreated;
	public event Func<string, PrefabData> OnPrefabLoadRequested;
	public event Action<Entity, object> OnLoadEntityData; // Add this for loading entity data

	public void InvokeSaveSceneChanges()
	{
		OnSaveSceneAsync?.Invoke();
	}

	public async Task<bool> InvokePrefabCreated(Entity prefabEntity, bool overrideExistingPrefab)
	{
		if (OnPrefabCreated != null)
		{
			return await OnPrefabCreated.Invoke(prefabEntity, overrideExistingPrefab);
		}

		return false;
	}

	public PrefabData InvokePrefabLoadRequested(string prefabName)
	{
		if (OnPrefabLoadRequested != null)
		{
			return OnPrefabLoadRequested.Invoke(prefabName);
		}

		return new PrefabData();
	}

	public void InvokeLoadEntityData(Entity entity, object entityData)
	{
		OnLoadEntityData?.Invoke(entity, entityData);
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

		_renderer = new ImGuiRenderer(Core.Instance, _input);
		_renderer.RebuildFontAtlas(options);

		Core.Emitter.AddObserver(CoreEvents.SceneChanged, OnSceneChanged);
		VoltageEditorThemes.DarkTheme1();

		_sceneSubclasses = ReflectionUtils.GetAllSubclasses(typeof(Scene), true);

		ImGui.GetStyle().IndentSpacing = 12;
		ImGui.GetIO().ConfigWindowsMoveFromTitleBarOnly = true;


		var io = ImGui.GetIO();
		io.ConfigFlags |= ImGuiConfigFlags.DockingEnable;

		// Set the ini file path BEFORE loading
		unsafe
		{
			// This tells ImGui where to save/load settings
			var iniPath = System.Text.Encoding.UTF8.GetBytes(_layoutFilePath + "\0");
			fixed (byte* pathPtr = iniPath)
			{
				ImGuiNative.igGetIO()->IniFilename = (byte*)pathPtr;
			}
		}

		LoadLastSelectedLayout();

		// find all themes
		_themes = typeof(VoltageEditorThemes).GetMethods(System.Reflection.BindingFlags.Static |
		                                                 System.Reflection.BindingFlags.Public);

		ApplyThemeByName(_lastSelectedTheme.Value);

		SceneGraphWindow = new SceneGraphWindow();
		_cursorSelectionManager = new GizmoSelectionManager(this);

		_imageLoader = new ImguiImageLoader();
		_imageLoader.LoadImages(_renderer);

		_effectBuildProgressWindow = new EffectBuildProgressWindow();

		// Create default Main Entity Inspector window when current scene is finished loading the entities
		Scene.OnFinishedAddingEntitiesWithData += OpenMainEntityInspector;
		Core.EmitterWithPending.AddObserver(CoreEvents.Exiting, OnAppExitSaveChanges);

		Core.OnResetScene += RequestResetScene;
		Core.OnSwitchEditMode += OnEditModeSwitched;
		 
		MainEntityInspector = new MainEntityInspector(this, null);

		// Initialize project manager first
		_projectManager = ProjectManager.Instance;
		_projectManager.OnProjectLoaded += OnProjectLoaded;
		_projectManager.OnProjectUnloaded += OnProjectUnloaded;
		_projectManager.LoadLastProject();

		InitializeScriptManager();

		_editorSettingsWindow = new EditorSettingsWindow();
	}

	private void ApplyThemeByName(string themeName)
	{
		if (string.IsNullOrWhiteSpace(themeName))
		{
			VoltageEditorThemes.DarkTheme1(); // Default fallback
			return;
		}

		var themeMethod = typeof(VoltageEditorThemes).GetMethod(themeName,
			System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);

		if (themeMethod != null)
		{
			themeMethod.Invoke(null, null);
			Debug.Log($"Applied theme: {themeName}");
		}
		else
		{
			Debug.Warn($"Theme '{themeName}' not found, applying default");
			VoltageEditorThemes.DarkTheme1();
		}
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
				Debug.Log($"Loaded last used layout: {lastLayout}");
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
			Debug.Log("Loaded default layout");
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
		ShowSceneGraphWindow = SceneGraphWindow.Show(ShowSceneGraphWindow);
		DrawInspectorWindows();
		DrawEntityInspectors();

		_effectBuildProgressWindow.Draw();
		_projectCreator.Draw();

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

		_cursorSelectionManager.UpdateSelection();
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
	}

	/// <summary>
	/// Opens the file picker for selecting a project.json file
	/// </summary>
	private void OpenProjectFilePicker()
	{
		string startingPath = !string.IsNullOrWhiteSpace(_projectManager.LastProjectPath)
			? Path.GetDirectoryName(_projectManager.LastProjectPath)
			: Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

		_projectFilePicker = FilePicker.GetFilePicker(this, startingPath, ".json");
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
			ImGui.TextWrapped("Select a project.json file to load:");
			VoltageEditorUtils.SmallVerticalSpace();

			if (_projectFilePicker != null && _projectFilePicker.Draw())
			{
				var selectedFile = _projectFilePicker.SelectedFile;

				// Project file validation
				if (Path.GetFileName(selectedFile).Equals($"{_projectManager.CurrentProject.ProjectName}.voltage", StringComparison.OrdinalIgnoreCase))
				{
					bool success = _projectManager.LoadProject(selectedFile);

					if (success)
					{
						NotificationSystem.ShowTimedNotification(
							$"Project loaded: {_projectManager.CurrentProject.ProjectName}");
					}
					else
					{
						NotificationSystem.ShowTimedNotification(
							"Failed to load project. Check the console for details.");
					}
				}
				else
				{
					NotificationSystem.ShowTimedNotification("Please select a valid project.json file.");
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
			InvokeSaveSceneChanges();

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
			ImGui.ImageButton("Normal", _imageLoader.NormalCursorIconID, new Num.Vector2(iconSize, iconSize));
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
			ImGui.ImageButton("Resize", _imageLoader.ResizeCursorIconID, new Num.Vector2(iconSize, iconSize));
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
			ImGui.ImageButton("Rotate", _imageLoader.RotateCursorIconID, new Num.Vector2(iconSize, iconSize));
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

		bool colliderResizeHovered = ImGui.ImageButton("Collider Resize", _imageLoader.ColliderResizeCursorIconID,
			new Num.Vector2(iconSize, iconSize));
		if (colliderResizeHovered)
			_cursorSelectionManager.SelectionMode = CursorSelectionMode.ColliderResize;

		ImGui.PopStyleColor();

		if (ImGui.IsItemHovered())
		{
			ImGui.SetTooltip("Resize Colliders (Press T or 4)");
		}

		ImGui.End();
	}

	/// <summary>
	/// Inspector tabs for Entity Inspector, Core Window and Debug Window
	/// </summary>
	private void DrawInspectorWindows()
	{
		if (MainEntityInspector != null && MainEntityInspector.IsOpen)
		{
			MainEntityInspector.Draw();
		}

		if (ShowCoreWindow)
		{
			ShowCoreWindow = _coreWindow.Show(ShowCoreWindow);
		}

		_debugWindow.Draw();
	}

	/// <summary>
	/// draws all the EntityInspectors
	/// </summary>
	private void DrawEntityInspectors()
	{
		for (var i = _entityInspectors.Count - 1; i >= 0; i--)
			_entityInspectors[i].Draw();
	}

	#endregion

	private void UpdateCamera()
	{
		ManageCameraZoom();

		if (Core.IsEditMode)
		{
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
		if (Core.IsEditMode && Input.MouseWheelDelta != 0)
		{
			bool isShiftHeld = Input.IsKeyDown(Keys.LeftShift);

			if (isShiftHeld)
			{
				// Modify camera movement speed instead of zoom
				float speedDelta = Input.MouseWheelDelta * CameraSpeedAdjustmentStep * Time.DeltaTime;
				_dynamicCameraSpeed = MathHelper.Clamp(_dynamicCameraSpeed + speedDelta,
					EditModeCameraMinSpeed,
					EditModeCameraMaxSpeed);
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
	/// Creates a normal EntityInspector window
	/// </summary>
	/// <param name="entity"></param>
	public void OpenSeparateEntityInspector(Entity entity)
	{
		// Only add if not already present as a pop-out
		if (_entityInspectors.Any(i => i.Entity == entity))
			return;

		entitynspectorInitialSpawnOffset += entitynspectorSpawnOffsetIncremental;
		var inspector = new EntityInspector(entity, entitynspectorInitialSpawnOffset);
		_entityInspectors.Add(inspector);

		inspector.SetWindowFocus();
	}

	/// <summary>
	/// Creates (or replaces) a MainEntityInspector
	/// </summary>
	/// <param name="entity"></param>
	public void OpenMainEntityInspector(Entity entity = null)
	{
		if (IsInspectorTabLocked)
			return;

		if (MainEntityInspector != null)
		{
			if (MainEntityInspector.Entity == entity)
				return;

			MainEntityInspector.SetEntity(entity);
		}
		else
		{
			MainEntityInspector = new MainEntityInspector(this, entity);
		}
	}

	/// <summary>
	/// removes the EntityInspector for this Entity
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
	/// removes the EntityInspector
	/// </summary>
	/// <param name="entityInspector"></param>
	public void CloseEntityInspector(EntityInspector entityInspector)
	{
		_entityInspectors.RemoveAt(_entityInspectors.IndexOf(entityInspector));

		// Reset the previous spawn offset 
		if (entitynspectorInitialSpawnOffset - entitynspectorSpawnOffsetIncremental >= 0)
			entitynspectorInitialSpawnOffset -= entitynspectorSpawnOffsetIncremental;
	}

	public void CloseMainEntityInspector()
	{
		MainEntityInspector = null;
	}

	/// <summary>
	/// Refreshes the main entity inspector's component inspectors.
	/// Call this after making changes to entity components.
	/// </summary>
	public void RefreshMainEntityInspector()
	{
		MainEntityInspector?.RefreshComponentInspectors();
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

	private void RequestSceneChange(Type sceneType)
	{
		if (EditorChangeTracker.IsDirty)
		{
			TriggerSceneChangePrompt(sceneType);
		}
		else
		{
			ChangeScene(sceneType);
		}
	}

	private void TriggerSceneChangePrompt(Type sceneType)
	{
		_pendingSceneChange = true;
		_requestedSceneType = sceneType;
		_pendingExit = false;
	}

	private void ChangeScene(Type sceneType)
	{
		var scene = (Scene)Activator.CreateInstance(sceneType);
		Core.StartSceneTransition(new FadeTransition(() => scene));
	}

	private void OnEditModeSwitched(bool isEditMode)
	{
		// Only reset scene if switching to EditMode from PlayMode
		if (isEditMode && Core.ResetSceneAutomatically)
		{
			ResetScene();
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

	private void ResetScene()
	{
		var newScene = (Scene)Activator.CreateInstance(_requestedResetSceneType ?? Core.Scene.GetType());
		Core.Scene = newScene;
		EditorChangeTracker.Clear();
	}

	private async Task SaveSceneAsyncAndThenAct()
	{
		if (OnSaveSceneAsync != null)
			await OnSaveSceneAsync();
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
}