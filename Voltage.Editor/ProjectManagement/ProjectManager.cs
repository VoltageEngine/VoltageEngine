using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Voltage.Editor.EditorDebug;
using Voltage.Editor.Persistence;
using Voltage.Utils;

namespace Voltage.Editor.ProjectManagement
{
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
		
		/// <summary>
		/// The currently active game project.
		/// </summary>
		public IGameProject CurrentProject { get; private set; }
		
		/// <summary>
		/// The path to the last opened project.
		/// </summary>
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
		
		/// <summary>
		/// Invoked when a project is loaded.
		/// </summary>
		public event Action<IGameProject> OnProjectLoaded;
		/// <summary>
		/// Invoked when a project is changed (old project unloaded, new one loaded).
		/// </summary>
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
				if (CurrentProject != null)
				{
					UnloadCurrentProject();
				}
				
				// Set as current project
				var oldProject = CurrentProject;
				CurrentProject = project;
				LastProjectPath = voltageFilePath;
				
				AddToRecentProjects(voltageFilePath);
				
				project.Initialize();
				
				ProjectSettings.Instance = project.Settings;
				
				Debug.Log($"Successfully loaded project: {project.ProjectName} from {voltageFilePath}");
				
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
		/// Loads a project from a directory by searching for a .voltage file.
		/// If multiple .voltage files exist, loads the first one found.
		/// </summary>
		/// <param name="projectDirectory">Directory containing the .voltage file</param>
		/// <returns>True if the project was loaded successfully</returns>
		public bool LoadProjectFromDirectory(string projectDirectory)
		{
			if (string.IsNullOrWhiteSpace(projectDirectory))
			{
				Debug.Error("Project directory cannot be null or empty.");
				return false;
			}
			
			if (!Directory.Exists(projectDirectory))
			{
				Debug.Error($"Project directory not found: {projectDirectory}");
				return false;
			}
			
			// Look for .voltage files in the directory
			var voltageFiles = Directory.GetFiles(projectDirectory, "*.voltage");
			
			if (voltageFiles.Length == 0)
			{
				Debug.Error($"No .voltage file found in directory: {projectDirectory}");
				return false;
			}
			
			if (voltageFiles.Length > 1)
			{
				Debug.Warn($"Multiple .voltage files found in directory: {projectDirectory}. Loading the first one.");
			}
			
			return LoadProject(voltageFiles[0]);
		}
		
		/// <summary>
		/// Unloads the current project.
		/// </summary>
		public void UnloadCurrentProject()
		{
			if (CurrentProject == null)
			{
				Debug.Warn("No project to unload.");
				return;
			}
			
			var project = CurrentProject;
			
			project.UnloadContent();
			Debug.Log($"Unloaded project: {project.ProjectName}");
			
			// Clear current project
			CurrentProject = null;
			
			OnProjectUnloaded?.Invoke();
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
			
			// Remove if already exists
			recentProjects.Remove(projectPath);
			
			// Add to front
			recentProjects.Insert(0, projectPath);
			
			// Limit to max recent projects
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
			EditorProcessDebugger.Log("Cleared recent projects list.");
		}
		
		#endregion
	}
}
