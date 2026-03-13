using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Voltage.Editor.DebugUtils;
using Voltage.Editor.Persistence;
using Voltage.Editor.SceneFile;
using Voltage.Editor.Scripting;
using Voltage.Project;
using Voltage.Utils;

namespace Voltage.Editor.ProjectFile;

/// <summary>
/// Global manager that tracks the current and last opened IGameProject.
/// Used by the Editor to manage project state across sessions.
/// </summary>
public class ProjectManager : GlobalManager
{
	private PersistentString _recentProjects = new("ProjectManager_RecentProjects", "");
	private const int MaxRecentProjects = 10;

	#region Singleton/Instance Access

	private static ProjectManager _instance;

	/// <summary>
	/// Gets the singleton instance of the ProjectManager.
	/// </summary>
	public static ProjectManager Instance
	{
		get
		{
			if (_instance == null)
			{
				_instance = Core.GetGlobalManager<ProjectManager>();
				if (_instance == null)
				{
					_instance = new ProjectManager();
					Core.RegisterGlobalManager(_instance);
				}
			}
			return _instance;
		}
	}

	#endregion

	#region Persistent Settings

	private PersistentString _lastProjectPath = new("ProjectManager_LastProjectPath", "");

	#endregion

	#region Properties

	public IGameProject CurrentProject { get; private set; }

	public string LastProjectPath
	{
		get => _lastProjectPath.Value;
		private set => _lastProjectPath.Value = value;
	}

	/// <summary>
	/// Indicates whether a project is currently loaded.
	/// </summary>
	public bool HasActiveProject => CurrentProject != null;

	#endregion

	#region Events

	public event Action<IGameProject> OnProjectLoaded;
	public event Action<IGameProject, IGameProject> OnProjectChanged;
	public event Action OnProjectUnloaded;

	#endregion

	#region Initialization

	public ProjectManager()
	{
		_instance = this;
	}

	/// <summary>
	/// Attempts to load the last opened project if available.
	/// Should be called during editor startup.
	/// </summary>
	public void LoadLastProject()
	{
		if (string.IsNullOrWhiteSpace(LastProjectPath))
		{
			Debug.Log("No last project path found.");
			return;
		}

		if (!File.Exists(LastProjectPath))
		{
			Debug.Warn($"Last project file not found: {LastProjectPath}");
			LastProjectPath = ""; // Clear invalid path
			return;
		}

		try
		{
			LoadProject(LastProjectPath);
		}
		catch (Exception ex)
		{
			Debug.Error($"Failed to load last project from '{LastProjectPath}': {ex.Message}");
			LastProjectPath = ""; // Clear invalid path
		}
	}

	#endregion

	#region Project Management

	/// <summary>
	/// Loads a project from the specified .voltage file path.
	/// </summary>
	/// <param name="voltageFilePath">Path to the .voltage file</param>
	/// <returns>True if the project was loaded successfully</returns>
	public bool LoadProject(string voltageFilePath)
	{
		if (string.IsNullOrWhiteSpace(voltageFilePath))
		{
			Debug.Error("Project path cannot be null or empty.");
			return false;
		}

		if (!File.Exists(voltageFilePath))
		{
			Debug.Error($"Project file not found: {voltageFilePath}");
			return false;
		}

		try
		{
			var jsonContent = File.ReadAllText(voltageFilePath);
			var metadata = Voltage.Persistence.Json.FromJson<ProjectCreator.ProjectMetadata>(jsonContent);

			if (metadata == null)
			{
				Debug.Error($"Failed to deserialize project metadata from: {voltageFilePath}");
				return false;
			}

			// Create a RuntimeGameProject from the metadata
			var project = new RuntimeGameProject(metadata);

			// Unload current project if exists
			var oldProject = CurrentProject;
			if (CurrentProject != null)
			{
				UnloadCurrentProject();
			}

			// Set as current project
			CurrentProject = project;
			LastProjectPath = voltageFilePath;

			AddToRecentProjects(voltageFilePath);

			project.Initialize();

			// Sync engine DLLs into the project's EngineLibs folder so the Roslyn script
			// compiler and the game project's IDE always reference up-to-date assemblies.
			EngineLibsSync.SyncToProject(project.ProjectPath);
			ProjectStructureGenerator.EnsureDefaultFontExists(project.ProjectPath);
			ProjectSettings.Instance = project.Settings;

			EditorDebug.Log($"Successfully loaded project: {project.ProjectName} from {voltageFilePath}");

			// Invoke events
			OnProjectLoaded?.Invoke(project);
			if (oldProject != null)
			{
				OnProjectChanged?.Invoke(oldProject, project);
			}

			return true;
		}
		catch (Exception ex)
		{
			Debug.Error($"Failed to load project from '{voltageFilePath}': {ex.Message}");
			Debug.Error($"Stack trace: {ex.StackTrace}");
			return false;
		}
	}

	/// <summary>
	/// Unloads the current project and resets all dependent subsystems.
	/// </summary>
	public void UnloadCurrentProject()
	{
		if (CurrentProject == null)
		{
			Debug.Warn("No project to unload.");
			return;
		}

		var project = CurrentProject;

		// Clear scene state BEFORE nulling the project so subsystems can still
		// read CurrentProject if needed during cleanup.
		SceneManager.Instance.ClearCurrentScene();
		PersistentScene.Clear();

		project.UnloadContent();
		EditorDebug.Log($"Unloaded project: {project.ProjectName}");

		CurrentProject = null;
		OnProjectUnloaded?.Invoke();
	}

	#endregion

	#region Project Context Validation

	/// <summary>
	/// Validates that a given file path belongs to the currently loaded project.
	/// Use this before any save/load operation to prevent cross-project contamination.
	/// </summary>
	/// <param name="filePath">Absolute file path to validate</param>
	/// <returns>True if the path is inside the current project's directory tree</returns>
	public bool IsPathInCurrentProject(string filePath)
	{
		if (!HasActiveProject || string.IsNullOrWhiteSpace(filePath))
			return false;

		try
		{
			var normalizedFilePath = Path.GetFullPath(filePath);
			var normalizedProjectPath = Path.GetFullPath(CurrentProject.ProjectPath);

			return normalizedFilePath.StartsWith(normalizedProjectPath, StringComparison.OrdinalIgnoreCase);
		}
		catch
		{
			return false;
		}
	}

	/// <summary>
	/// Returns the scenes folder of the current project, or null if no project is loaded.
	/// All scene operations should use this to resolve paths.
	/// </summary>
	public string GetScenesFolder()
	{
		return CurrentProject?.ScenesFolder;
	}

	/// <summary>
	/// Returns the scripts folder of the current project, or null if no project is loaded.
	/// All script operations should use this to resolve paths.
	/// </summary>
	public string GetScriptsFolder()
	{
		return CurrentProject?.ScriptsFolder;
	}

	/// <summary>
	/// Returns the prefabs folder of the current project, or null if no project is loaded.
	/// All prefab operations should use this to resolve paths.
	/// </summary>
	public string GetPrefabsFolder()
	{
		return CurrentProject?.PrefabsFolder;
	}

	#endregion

	#region Recent Projects

	/// <summary>
	/// Gets the list of recent project paths.
	/// </summary>
	public List<string> GetRecentProjects()
	{
		if (string.IsNullOrWhiteSpace(_recentProjects.Value))
		{
			return new List<string>();
		}

		return _recentProjects.Value
			.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries)
			.Where(path => File.Exists(path))
			.ToList();
	}

	/// <summary>
	/// Adds a project to the recent projects list.
	/// </summary>
	private void AddToRecentProjects(string projectPath)
	{
		var recentProjects = GetRecentProjects();

		// Remove and add to front if already exists
		recentProjects.Remove(projectPath);
		recentProjects.Insert(0, projectPath);

		if (recentProjects.Count > MaxRecentProjects)
		{
			recentProjects = recentProjects.Take(MaxRecentProjects).ToList();
		}

		// Save
		_recentProjects.Value = string.Join("|", recentProjects);
	}

	/// <summary>
	/// Clears the recent projects list.
	/// </summary>
	public void ClearRecentProjects()
	{
		_recentProjects.Value = "";
	}

	#endregion
}
