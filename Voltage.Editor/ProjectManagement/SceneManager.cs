using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Voltage.Data;
using Voltage.Editor.EditorDebug;
using Voltage.Editor.Scenes;
using Voltage.Editor.Tools;
using Voltage.Editor.Utils;
using Voltage.Utils;

namespace Voltage.Editor.ProjectManagement
{
	/// <summary>
	/// Manages scene creation, loading, and saving for the current project.
	/// </summary>
	public class SceneManager : GlobalManager
	{
		private static SceneManager _instance;
		
		/// <summary>
		/// Gets the singleton instance of the SceneManager.
		/// </summary>
		public static SceneManager Instance
		{
			get
			{
				if (_instance == null)
				{
					_instance = Core.GetGlobalManager<SceneManager>();
					if (_instance == null)
					{
						_instance = new SceneManager();
						Core.RegisterGlobalManager(_instance);
					}
				}
				return _instance;
			}
		}
		
		#region Properties
		
		/// <summary>
		/// The name of the currently loaded scene file (without path or extension).
		/// </summary>
		public string CurrentSceneName { get; private set; }
		
		/// <summary>
		/// The full path to the currently loaded scene file.
		/// </summary>
		public string CurrentScenePath { get; private set; }
		
		/// <summary>
		/// Indicates whether a scene is currently loaded from a file.
		/// </summary>
		public bool HasLoadedScene => !string.IsNullOrEmpty(CurrentScenePath);
		
		#endregion
		
		#region Events
		
		/// <summary>
		/// Invoked when a scene is successfully loaded from a file.
		/// </summary>
		public event Action<string> OnSceneLoaded;
		
		/// <summary>
		/// Invoked when a scene is successfully saved to a file.
		/// </summary>
		public event Action<string> OnSceneSaved;
		
		/// <summary>
		/// Invoked when a new scene is created.
		/// </summary>
		public event Action<string> OnSceneCreated;
		public void InvokeSceneCreated(string sceneName)
		{
			OnSceneCreated?.Invoke(sceneName);
		}
		#endregion

		#region Initialization

		public SceneManager()
		{
			_instance = this;
		}
		
		#endregion
		
		#region Scene File Management
		
		/// <summary>
		/// Gets all scene file paths in the current project's Scenes folder.
		/// </summary>
		/// <returns>List of scene file paths, or empty list if no project is loaded</returns>
		public List<string> GetAllSceneFiles()
		{
			var projectManager = ProjectManager.Instance;
			if (!projectManager.HasActiveProject)
			{
				Debug.Warn("No active project. Cannot get scene files.");
				return new List<string>();
			}
			
			var scenesFolder = projectManager.CurrentProject.ScenesFolder;
			if (!Directory.Exists(scenesFolder))
			{
				Debug.Warn($"Scenes folder does not exist: {scenesFolder}");
				return new List<string>();
			}
			
			return Directory.GetFiles(scenesFolder, "*.json", SearchOption.TopDirectoryOnly).ToList();
		}
		
		/// <summary>
		/// Gets all scene names (without path or extension) in the current project.
		/// </summary>
		public List<string> GetAllSceneNames()
		{
			return GetAllSceneFiles()
				.Select(path => Path.GetFileNameWithoutExtension(path))
				.ToList();
		}
		
		/// <summary>
		/// Checks if a scene file exists with the given name.
		/// </summary>
		public bool SceneExists(string sceneName)
		{
			var projectManager = ProjectManager.Instance;
			if (!projectManager.HasActiveProject)
				return false;
			
			var scenePath = Path.Combine(projectManager.CurrentProject.ScenesFolder, $"{sceneName}.json");
			return File.Exists(scenePath);
		}
		
		#endregion
		
		#region Scene Loading
		
		/// <summary>
		/// Loads a scene from the specified file path.
		/// </summary>
		/// <param name="sceneFilePath">Full path to the scene JSON file</param>
		/// <returns>True if the scene was loaded successfully</returns>
		public bool LoadScene(string sceneFilePath)
		{
			EditorProcessDebugger.LogInfo($"=== Loading Scene ===", "SceneManagement");
			EditorProcessDebugger.LogInfo($"Scene file: {sceneFilePath}", "SceneManagement");
			
			if (string.IsNullOrWhiteSpace(sceneFilePath))
			{
				EditorProcessDebugger.LogError("Scene file path cannot be null or empty.", "SceneManagement");
				return false;
			}
			
			if (!File.Exists(sceneFilePath))
			{
				Debug.Error($"Scene file not found: {sceneFilePath}");
				return false;
			}
			
			try
			{
				// Read and deserialize the scene data
				var jsonContent = File.ReadAllText(sceneFilePath);
				var sceneData = Voltage.Persistence.Json.FromJson<SceneData>(jsonContent);
				
				if (sceneData == null)
				{
					Debug.Error($"Failed to deserialize scene data from: {sceneFilePath}");
					return false;
				}
				
				// Store the current scene info
				CurrentScenePath = sceneFilePath;
				CurrentSceneName = Path.GetFileNameWithoutExtension(sceneFilePath);
				
				// Apply the scene data to the active scene
				if (Core.Scene != null)
				{
					ApplySceneData(Core.Scene, sceneData);
				}
				
				Debug.Log($"Successfully loaded scene: {CurrentSceneName} from {sceneFilePath}");
				OnSceneLoaded?.Invoke(sceneFilePath);
				NotificationSystem.ShowTimedNotification($"Scene loaded: {CurrentSceneName}");
				
				return true;
			}
			catch (Exception ex)
			{
				Debug.Error($"Failed to load scene from '{sceneFilePath}': {ex.Message}");
				Debug.Error($"Stack trace: {ex.StackTrace}");
				return false;
			}
		}
		
		/// <summary>
		/// Loads a scene by name from the current project's Scenes folder.
		/// </summary>
		public bool LoadSceneByName(string sceneName)
		{
			var projectManager = ProjectManager.Instance;
			if (!projectManager.HasActiveProject)
			{
				Debug.Error("No active project. Cannot load scene.");
				return false;
			}
			
			var sceneFilePath = Path.Combine(projectManager.CurrentProject.ScenesFolder, $"{sceneName}.json");
			return LoadScene(sceneFilePath);
		}
		
		/// <summary>
		/// Applies loaded scene data to the current scene.
		/// </summary>
		private void ApplySceneData(Scene scene, SceneData sceneData)
		{
			// Ensure we're working with a GameScene
			if (scene is not GameScene gameScene)
			{
				Debug.Error("Current scene is not a GameScene. Cannot apply scene data.");
				return;
			}
			
			// Clear existing entities (except camera and other essential entities)
			var entitiesToRemove = new List<Entity>();
			for (int i = 0; i < gameScene.Entities.Count; i++)
			{
				var entity = gameScene.Entities[i];
				// Keep hardcoded entities like the camera
				if (entity.Type != Entity.InstanceType.HardCoded || entity.Name != "camera")
				{
					entitiesToRemove.Add(entity);
				}
			}
			
			foreach (var entity in entitiesToRemove)
			{
				entity.Destroy();
			}
			
			// Assign the new scene data
			gameScene.SceneData = sceneData;
			
			// Trigger the scene to load entities from the data
			Scene.InvokeFinishedAddingEntities();
		}
		
		#endregion
		
		#region Scene Saving
		
		/// <summary>
		/// Saves the current scene to the specified file path.
		/// </summary>
		public bool SaveScene(string sceneFilePath)
		{
			EditorProcessDebugger.LogInfo($"=== Saving Scene ===", "SceneManagement");
			EditorProcessDebugger.LogInfo($"Scene file: {sceneFilePath}", "SceneManagement");
			
			if (string.IsNullOrWhiteSpace(sceneFilePath))
			{
				Debug.Error("Scene file path cannot be null or empty.");
				return false;
			}
			
			if (Core.Scene == null)
			{
				Debug.Error("No active scene to save.");
				return false;
			}
			
			try
			{
				// Ensure the directory exists
				var directory = Path.GetDirectoryName(sceneFilePath);
				if (!Directory.Exists(directory))
				{
					Directory.CreateDirectory(directory);
				}
				
				// Serialize the scene data
				var sceneData = Core.Scene.SceneData ?? new SceneData();
				var jsonContent = Voltage.Persistence.Json.ToJson(sceneData, new Voltage.Persistence.JsonSettings
				{
					PrettyPrint = true
				});
				
				// Write to file
				File.WriteAllText(sceneFilePath, jsonContent, new System.Text.UTF8Encoding(false));
				
				// Update current scene info
				CurrentScenePath = sceneFilePath;
				CurrentSceneName = Path.GetFileNameWithoutExtension(sceneFilePath);
				
				Debug.Log($"Successfully saved scene: {CurrentSceneName} to {sceneFilePath}");
				OnSceneSaved?.Invoke(sceneFilePath);
				NotificationSystem.ShowTimedNotification($"Scene saved: {CurrentSceneName}");
				
				return true;
			}
			catch (Exception ex)
			{
				Debug.Error($"Failed to save scene to '{sceneFilePath}': {ex.Message}");
				Debug.Error($"Stack trace: {ex.StackTrace}");
				NotificationSystem.ShowTimedNotification($"Failed to save scene: {ex.Message}");
				return false;
			}
		}
		
		/// <summary>
		/// Saves the current scene to its current file path.
		/// If no current path exists, prompts for a new name.
		/// </summary>
		public bool SaveCurrentScene()
		{
			if (string.IsNullOrEmpty(CurrentScenePath))
			{
				Debug.Warn("No current scene path. Cannot save without a file path.");
				return false;
			}
			
			return SaveScene(CurrentScenePath);
		}
		
		/// <summary>
		/// Saves the current scene with a new name in the current project's Scenes folder.
		/// </summary>
		public bool SaveSceneAs(string sceneName)
		{
			var projectManager = ProjectManager.Instance;
			if (!projectManager.HasActiveProject)
			{
				Debug.Error("No active project. Cannot save scene.");
				return false;
			}
			
			var sceneFilePath = Path.Combine(projectManager.CurrentProject.ScenesFolder, $"{sceneName}.json");
			return SaveScene(sceneFilePath);
		}
		
		/// <summary>
		/// Reloads the currently loaded scene from its file.
		/// </summary>
		public bool ReloadCurrentScene()
		{
			if (!HasLoadedScene)
			{
				Debug.Warn("No scene is currently loaded from a file.");
				return false;
			}

			return LoadScene(CurrentScenePath);
		}
		
		/// <summary>
		/// Clears the current scene path without saving.
		/// Used when a project is unloaded.
		/// </summary>
		public void ClearCurrentScene()
		{
			CurrentScenePath = null;
			CurrentSceneName = null;
			Debug.Log("Cleared current scene reference");
		}
		
		#endregion
	}
}