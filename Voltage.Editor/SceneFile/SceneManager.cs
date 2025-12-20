using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Voltage.Data;
using Voltage.Editor.EditorDebug;
using Voltage.Editor.Persistence;
using Voltage.Editor.ProjectFile;
using Voltage.Editor.Utils;
using Voltage.Utils;

namespace Voltage.Editor.SceneFile
{
	/// <summary>
	/// Manages scene creation, loading, and saving for the current project.
	/// Scenes are pure JSON data files - no C# files required.
	/// </summary>
	public class SceneManager : GlobalManager
	{
		private static SceneManager _instance;

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

		public string CurrentSceneName { get; private set; }
		public string CurrentScenePath { get; private set; }
		public bool HasLoadedScene => !string.IsNullOrEmpty(CurrentScenePath);

		#endregion

		#region Events

		public event Action<string> OnSceneLoaded;
		public event Action<string> OnSceneSaved;
		public event Action<string> OnSceneCreated;

		public void InvokeSceneCreated(string sceneName)
		{
			OnSceneCreated?.Invoke(sceneName);
		}

		#endregion

		public SceneManager()
		{
			_instance = this;
		}

		#region Scene File Management
		/// <summary>
		/// Gets all scene JSON files in the Scenes directory
		/// </summary>
		public List<string> GetAllSceneFiles()
		{
			var scenesDir = ProjectManager.Instance.CurrentProject.ScenesFolder;

			if (!Directory.Exists(scenesDir))
			{
				Debug.Warn($"Scenes directory does not exist: {scenesDir}");
				return new List<string>();
			}

			return Directory.GetFiles(scenesDir, "*.vscene", SearchOption.TopDirectoryOnly).ToList();
		}

		/// <summary>
		/// Gets all scene names (without extension)
		/// </summary>
		public List<string> GetAllSceneNames()
		{
			return GetAllSceneFiles()
				.Select(path => Path.GetFileNameWithoutExtension(path))
				.OrderBy(name => name)
				.ToList();
		}

		/// <summary>
		/// Checks if a scene file exists
		/// </summary>
		public bool SceneExists(string sceneName)
		{
			var scenePath = GetScenePath(sceneName);
			return File.Exists(scenePath);
		}

		#endregion

		#region Scene Loading
		/// <summary>
		/// Loads the last used scene from persistent settings
		/// </summary>
		public Scene LoadLastUsedScene()
		{
			var project = ProjectManager.Instance.CurrentProject;

			if (project == null)
			{
				return CreateFallbackScene("Cannot load last scene because no project is loaded");
			}

			var lastScenePath = PersistentScene.GetLastScenePath();

			if (string.IsNullOrWhiteSpace(lastScenePath))
			{
				return CreateFallbackScene("No last scene recorded to load");
			}

			if (!Path.IsPathRooted(lastScenePath))
				lastScenePath = Path.Combine(project.ScenesFolder, lastScenePath);

			if (!File.Exists(lastScenePath))
			{
				return CreateFallbackScene($"Last scene file not found: {lastScenePath}");
			}

			var scene = LoadScene(lastScenePath);
			return scene ?? CreateFallbackScene($"Failed to load scene from: {lastScenePath}");
		}

		private Scene CreateFallbackScene(string reason)
		{
			Debug.Warn($"{reason}. Creating fallback empty scene.");
			var fallbackScene = new Scene();

			Core.Scene = fallbackScene;
			CurrentScenePath = null;
			CurrentSceneName = "Untitled Scene";
			PersistentScene.Clear();

			return fallbackScene;
		}

		/// <summary>
		/// Loads a scene from a .vscene file and sets it as the active scene
		/// </summary>
		public Scene LoadScene(string scenePath)
		{
			EditorProcessDebugger.LogInfo($"=== Loading Scene ===", "SceneManagement");
			EditorProcessDebugger.LogInfo($"Scene file: {scenePath}", "SceneManagement");

			if (string.IsNullOrWhiteSpace(scenePath))
			{
				EditorProcessDebugger.LogError("Scene path cannot be null or empty", "SceneManagement");
				return null;
			}

			if (!File.Exists(scenePath))
			{
				Debug.Error($"Scene file not found: {scenePath}");
				return null;
			}

			try
			{
				// Load scene from file
				var scene = Scene.LoadFromFile(scenePath);

				if (scene == null)
				{
					Debug.Error("Failed to load scene - Scene.LoadFromFile returned null");
					return null;
				}

				// Update current scene info
				CurrentScenePath = scenePath;
				CurrentSceneName = scene.SceneData?.Name ?? Path.GetFileNameWithoutExtension(scenePath);

				Core.Scene = scene;
				OnSceneLoaded?.Invoke(scenePath);
				PersistentScene.SetLastScenePath(CurrentScenePath);

				return scene;
			}
			catch (Exception ex)
			{
				Debug.Error($"Failed to load scene from '{scenePath}': {ex.Message}");
				Debug.Error($"Stack trace: {ex.StackTrace}");
				return null;
			}
		}

		/// <summary>
		/// Loads a scene by name from the Scenes directory
		/// </summary>
		public Scene LoadSceneByName(string sceneName)
		{
			var scenePath = GetScenePath(sceneName);
			return LoadScene(scenePath);
		}

		private string GetScenePath(string sceneName)
		{
			return Path.Combine(ProjectManager.Instance.CurrentProject.ScenesFolder, $"{sceneName}.vscene");
		}

		#endregion

		#region Scene Saving

		/// <summary>
		/// Saves the current scene to its current file path
		/// </summary>
		public bool SaveCurrentScene()
		{
			if (Core.Scene == null)
			{
				Debug.Error("No active scene to save");
				return false;
			}

			if (string.IsNullOrEmpty(CurrentScenePath))
			{
				Debug.Warn("No save path set for current scene");
				return false;
			}

			EditorProcessDebugger.LogInfo($"=== Saving Scene ===", "SceneManagement");
			EditorProcessDebugger.LogInfo($"Scene file: {CurrentScenePath}", "SceneManagement");

			try
			{
				bool success = Core.Scene.SaveToFile(CurrentScenePath);

				if (success)
				{
					Debug.Info($"Successfully saved scene: {CurrentSceneName}");
					OnSceneSaved?.Invoke(CurrentScenePath);
					PersistentScene.SetLastScenePath(CurrentScenePath);
				}

				return success;
			}
			catch (Exception ex)
			{
				Debug.Error($"Failed to save scene: {ex.Message}");
				NotificationSystem.ShowTimedNotification($"Failed to save scene: {ex.Message}");
				return false;
			}
		}

		/// <summary>
		/// Sets the current scene path (used when creating new scenes before first save)
		/// </summary>
		public void SetCurrentScenePath(string scenePath, string sceneName = null)
		{
			CurrentScenePath = scenePath;
			CurrentSceneName = sceneName ?? Path.GetFileNameWithoutExtension(scenePath);
		}

		/// <summary>
		/// Creates a new scene data file for the current scene.
		/// This is used when saving a scene that doesn't have an associated file yet.
		/// </summary>
		/// <param name="sceneName">Name for the new scene file (without extension)</param>
		/// <returns>True if successful, false otherwise</returns>
		public bool CreateSceneFile(string sceneName)
		{
			if (Core.Scene == null)
			{
				Debug.Error("No active scene to save");
				return false;
			}

			var sceneFilePath = GetScenePath(sceneName);

			// Check if file already exists
			if (File.Exists(sceneFilePath))
			{
				Debug.Error($"Scene file already exists: {sceneFilePath}");
				return false;
			}

			EditorProcessDebugger.LogInfo($"=== Creating Scene File ===", "SceneManagement");
			EditorProcessDebugger.LogInfo($"Scene name: {sceneName}", "SceneManagement");
			EditorProcessDebugger.LogInfo($"Scene path: {sceneFilePath}", "SceneManagement");

			try
			{
				// Initialize SceneData if needed OR update the name if it exists
				if (Core.Scene.SceneData == null)
				{
					Core.Scene.SceneData = new SceneData
					{
						Name = sceneName,
						CreatedAt = DateTime.Now,
						ModifiedAt = DateTime.Now
					};
				}
				else
				{
					// IMPORTANT: Update the scene name in existing SceneData
					Core.Scene.SceneData.Name = sceneName;
					Core.Scene.SceneData.ModifiedAt = DateTime.Now;

					// If this is the first time saving, set CreatedAt
					if (Core.Scene.SceneData.CreatedAt == default)
					{
						Core.Scene.SceneData.CreatedAt = DateTime.Now;
					}
				}

				// Set current path
				SetCurrentScenePath(sceneFilePath, sceneName);

				// Save the scene
				bool success = SaveCurrentScene();

				if (success)
				{
					OnSceneCreated?.Invoke(sceneName);
				}

				return success;
			}
			catch (Exception ex)
			{
				Debug.Error($"Failed to create scene file: {ex.Message}");
				return false;
			}
		}

		#endregion

		#region Scene Utilities

		/// <summary>
		/// Reloads the current scene from its file
		/// </summary>
		public bool ReloadCurrentScene()
		{
			if (!HasLoadedScene)
			{
				Debug.Warn("No scene is currently loaded from a file");
				return false;
			}

			Debug.Log($"Reloading scene: {CurrentSceneName}");
			var scene = LoadScene(CurrentScenePath);
			return scene != null;
		}

		/// <summary>
		/// Clears the current scene path (when closing a project, etc.)
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