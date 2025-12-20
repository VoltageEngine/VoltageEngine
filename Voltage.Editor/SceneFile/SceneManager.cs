using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Voltage.Data;
using Voltage.Editor.EditorDebug;
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
        /// Gets the standard path for scene files
        /// </summary>
        private string GetScenePath(string sceneName)
        {
            return Path.Combine(ProjectManager.Instance.CurrentProject.ScenesFolder, $"{sceneName}.vscene");
        }
        
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
        
        #region Scene Creation
        
        /// <summary>
        /// Creates a new blank scene with default settings
        /// </summary>
        public Scene CreateNewScene(string sceneName)
        {
            EditorProcessDebugger.LogInfo($"=== Creating New Scene ===", "SceneManagement");
            EditorProcessDebugger.LogInfo($"Scene name: {sceneName}", "SceneManagement");
            
            try
            {
                var scene = new Scene();
                
                // Initialize with default SceneData
                scene.SceneData = new SceneData
                {
                    Name = sceneName,
                    CreatedAt = DateTime.Now,
                    ModifiedAt = DateTime.Now,
                    ClearColor = Color.CornflowerBlue,
                    LetterboxColor = Color.Black,
                    ResolutionPolicy = "BestFit",
                    DesignResolutionWidth = 1920,
                    DesignResolutionHeight = 1080,
                    HorizontalBleed = 0,
                    VerticalBleed = 0,
                    EnablePostProcessing = true,
                    Entities = new List<SceneData.SceneEntityData>()
                };
                
                scene.SetDesignResolution(1920, 1080, Scene.SceneResolutionPolicy.BestFit);
                
                CurrentSceneName = sceneName;
                CurrentScenePath = null; // Not saved yet
                OnSceneCreated?.Invoke(sceneName);
                
                return scene;
            }
            catch (Exception ex)
            {
                Debug.Error($"Failed to create new scene: {ex.Message}");
                return null;
            }
        }
        
        #endregion
        
        #region Scene Loading
        
        /// <summary>
        /// Loads a scene from a .vscene file and sets it as the active scene
        /// </summary>
        public bool LoadScene(string scenePath)
        {
            EditorProcessDebugger.LogInfo($"=== Loading Scene ===", "SceneManagement");
            EditorProcessDebugger.LogInfo($"Scene file: {scenePath}", "SceneManagement");
            
            if (string.IsNullOrWhiteSpace(scenePath))
            {
                EditorProcessDebugger.LogError("Scene path cannot be null or empty", "SceneManagement");
                return false;
            }
            
            if (!File.Exists(scenePath))
            {
                Debug.Error($"Scene file not found: {scenePath}");
                return false;
            }
            
            try
            {
                // Load scene from file
                var scene = Scene.LoadFromFile(scenePath);
                
                if (scene == null)
                {
                    Debug.Error("Failed to load scene - Scene.LoadFromFile returned null");
                    return false;
                }
                
                // Update current scene info
                CurrentScenePath = scenePath;
                CurrentSceneName = scene.SceneData?.Name ?? Path.GetFileNameWithoutExtension(scenePath);
                
                // Set as active scene
                Core.Scene = scene;
                Debug.Log($"Successfully loaded scene: {CurrentSceneName}");
                OnSceneLoaded?.Invoke(scenePath);
                
                return true;
            }
            catch (Exception ex)
            {
                Debug.Error($"Failed to load scene from '{scenePath}': {ex.Message}");
                Debug.Error($"Stack trace: {ex.StackTrace}");
                return false;
            }
        }
        
        /// <summary>
        /// Loads a scene by name from the Scenes directory
        /// </summary>
        public bool LoadSceneByName(string sceneName)
        {
            var scenePath = GetScenePath(sceneName);
            return LoadScene(scenePath);
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
            return LoadScene(CurrentScenePath);
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
        
        /// <summary>
        /// Duplicates a scene file with a new name
        /// </summary>
        public bool DuplicateScene(string sourceSceneName, string newSceneName)
        {
            var sourcePath = GetScenePath(sourceSceneName);
            var destPath = GetScenePath(newSceneName);
            
            if (!File.Exists(sourcePath))
            {
                Debug.Error($"Source scene not found: {sourceSceneName}");
                return false;
            }
            
            if (File.Exists(destPath))
            {
                Debug.Error($"Destination scene already exists: {newSceneName}");
                return false;
            }
            
            try
            {
                // Load source scene data
                var jsonContent = File.ReadAllText(sourcePath);
                var sceneData = Voltage.Persistence.Json.FromJson<SceneData>(jsonContent);
                
                // Update name and timestamps
                sceneData.Name = newSceneName;
                sceneData.CreatedAt = DateTime.Now;
                sceneData.ModifiedAt = DateTime.Now;
                
                // Save to new file
                var jsonSettings = new Voltage.Persistence.JsonSettings
                {
                    PrettyPrint = true,
                    TypeNameHandling = Voltage.Persistence.TypeNameHandling.Auto
                };
                
                var newJsonContent = Voltage.Persistence.Json.ToJson(sceneData, jsonSettings);
                
                var directory = Path.GetDirectoryName(destPath);
                Directory.CreateDirectory(directory);
                
                File.WriteAllText(destPath, newJsonContent);
                
                Debug.Log($"Duplicated scene '{sourceSceneName}' to '{newSceneName}'");
                OnSceneCreated?.Invoke(newSceneName);
                NotificationSystem.ShowTimedNotification($"Scene duplicated: {newSceneName}");
                
                return true;
            }
            catch (Exception ex)
            {
                Debug.Error($"Failed to duplicate scene: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// Deletes a scene file
        /// </summary>
        public bool DeleteScene(string sceneName)
        {
            var scenePath = GetScenePath(sceneName);
            
            if (!File.Exists(scenePath))
            {
                Debug.Error($"Scene file not found: {sceneName}");
                return false;
            }
            
            try
            {
                File.Delete(scenePath);
                
                Debug.Log($"Deleted scene: {sceneName}");
                NotificationSystem.ShowTimedNotification($"Scene deleted: {sceneName}");
                
                return true;
            }
            catch (Exception ex)
            {
                Debug.Error($"Failed to delete scene: {ex.Message}");
                return false;
            }
        }
        
        #endregion
    }
}