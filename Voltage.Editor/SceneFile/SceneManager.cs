using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Voltage.Data;
using Voltage.Editor.Persistence;
using Voltage.Editor.ProjectFile;
using Voltage.Utils;

namespace Voltage.Editor.SceneFile;

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
	/// Gets the current project's scenes folder, with a guard against no active project.
	/// </summary>
	private string GetActiveScenesFolder()
	{
		var folder = ProjectManager.Instance.GetScenesFolder();
		if (string.IsNullOrEmpty(folder))
		{
			Debug.Error("No active project. Cannot resolve scenes folder.");
			return null;
		}
		return folder;
	}

	/// <summary>
	/// Gets all scene JSON files in the Scenes directory
	/// </summary>
	public List<string> GetAllSceneFiles()
	{
		var scenesDir = GetActiveScenesFolder();
		if (scenesDir == null)
			return new List<string>();

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
		return scenePath != null && File.Exists(scenePath);
	}

	#endregion

	#region Scene Loading

	/// <summary>
	/// Loads the last used scene from persistent settings, validating it belongs
	/// to the currently loaded project.
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

		// Validate the persisted scene path actually belongs to this project
		if (!ProjectManager.Instance.IsPathInCurrentProject(lastScenePath))
		{
			Debug.Warn($"Last scene '{lastScenePath}' does not belong to the current project '{project.ProjectName}'. Clearing.");
			PersistentScene.Clear();
			return CreateFallbackScene("Last scene belongs to a different project");
		}

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
	/// Loads a scene from a .vscene file and sets it as the active scene.
	/// Validates the path belongs to the current project before loading.
	/// </summary>
	public Scene LoadScene(string scenePath)
	{
		if (string.IsNullOrWhiteSpace(scenePath))
		{
			Debug.Error("Scene path cannot be null or empty", "SceneManagement");
			return null;
		}

		// Validate the scene belongs to the current project
		if (!ProjectManager.Instance.IsPathInCurrentProject(scenePath))
		{
			Debug.Error($"Cannot load scene: path '{scenePath}' is not inside the current project.");
			return null;
		}

		if (!File.Exists(scenePath))
		{
			Debug.Error($"Scene file not found: {scenePath}");
			return null;
		}

		try
		{
			var scene = Scene.LoadFromFile(scenePath);

			if (scene == null)
			{
				Debug.Error("Failed to load scene - Scene.LoadFromFile returned null");
				return null;
			}


			if (ProjectManager.Instance.HasActiveProject)
			{
				var designRes = ProjectManager.Instance.CurrentProject.Settings.DesignResolution;

				scene.SetDesignResolution(
					designRes.Width,
					designRes.Height,
					designRes.ResolutionPolicy,
					designRes.HorizontalBleed,
					designRes.VerticalBleed
				);

				if (scene.SceneData != null)
				{
					scene.SceneData.DesignResolutionWidth = designRes.Width;
					scene.SceneData.DesignResolutionHeight = designRes.Height;
					scene.SceneData.ResolutionPolicy = designRes.ResolutionPolicy.ToString();
					scene.SceneData.HorizontalBleed = designRes.HorizontalBleed;
					scene.SceneData.VerticalBleed = designRes.VerticalBleed;
				}
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
	/// Loads a scene by name from the current project's Scenes directory
	/// </summary>
	public Scene LoadSceneByName(string sceneName)
	{
		var scenePath = GetScenePath(sceneName);
		if (scenePath == null)
			return null;

		return LoadScene(scenePath);
	}

	private string GetScenePath(string sceneName)
	{
		var scenesFolder = GetActiveScenesFolder();
		if (scenesFolder == null)
			return null;

		return Path.Combine(scenesFolder, $"{sceneName}.vscene");
	}

	#endregion

	#region Scene Saving

	/// <summary>
	/// Saves the current scene to its current file path.
	/// Validates the save target belongs to the current project.
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

		// Validate we're saving to the correct project
		if (!ProjectManager.Instance.IsPathInCurrentProject(CurrentScenePath))
		{
			Debug.Error($"Cannot save scene: target path '{CurrentScenePath}' is not inside the current project. " +
				"This can happen if the project was changed without clearing the scene state.");
			return false;
		}

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
		if (sceneFilePath == null)
			return false;

		if (File.Exists(sceneFilePath))
		{
			Debug.Error($"Scene file already exists: {sceneFilePath}");
			return false;
		}

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

			SetCurrentScenePath(sceneFilePath, sceneName);
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
	}

	#endregion
}