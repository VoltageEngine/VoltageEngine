using Microsoft.Xna.Framework.Content;
using System;
using System.IO;
using Voltage.Editor.EditorDebug;
using Voltage.Editor.Scenes;
using Voltage.Utils;

namespace Voltage.Editor.ProjectManagement
{
	/// <summary>
	/// Runtime implementation of IGameProject that loads project data from ProjectMetadata.
	/// Used by the editor to manage loaded projects.
	/// </summary>
	public class RuntimeGameProject : IGameProject
	{
		private readonly ProjectCreator.ProjectMetadata _metadata;
		private GameSettings _settings;

		#region Metadata Access

		/// <summary>
		/// Gets the underlying project metadata.
		/// </summary>
		public ProjectCreator.ProjectMetadata Metadata => _metadata;

		/// <summary>
		/// Gets the date when the project was created.
		/// </summary>
		public DateTime CreatedDate => _metadata.CreatedDate;

		#endregion

		public RuntimeGameProject(ProjectCreator.ProjectMetadata metadata)
		{
			_metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
			LoadSettings();
		}
		
		#region IGameProject Implementation
		
		public string ProjectName => _metadata.ProjectName;
		
		public string ProjectPath => _metadata.ProjectPath;
		
		public GameSettings Settings => _settings;
		
		public Version Version
		{
			get
			{
				if (System.Version.TryParse(_metadata.Version, out var version))
				{
					return version;
				}
				Debug.Warn($"Failed to parse version '{_metadata.Version}', returning default.");

				return new Version(1, 0, 0);
			}
		}
		
		public string ScriptsFolder => Path.Combine(ProjectPath, _metadata.ScriptsFolder);
		
		public string EffectsFolder => Path.Combine(ProjectPath, _metadata.EffectsFolder);
		
		public string ContentsFolder => Path.Combine(ProjectPath, _metadata.ContentsFolder);
		
		public string DataFolder => Path.Combine(ProjectPath, _metadata.DataFolder);
		
		public string ScenesFolder => Path.Combine(ProjectPath, _metadata.ScenesFolder);
		
		public string PrefabsFolder => Path.Combine(ProjectPath, _metadata.PrefabsFolder);
		

		public void Initialize()
		{
			EditorProcessDebugger.LogInfo($"Initializing project: {ProjectName}", "RuntimeGameProject");
			EditorProcessDebugger.LogInfo($"  Version: {Version}", "RuntimeGameProject");
			EditorProcessDebugger.LogInfo($"  Path: {ProjectPath}", "RuntimeGameProject");
			EditorProcessDebugger.LogInfo($"  Scripts: {ScriptsFolder}", "RuntimeGameProject");
			EditorProcessDebugger.LogInfo($"  Effects: {EffectsFolder}", "RuntimeGameProject");
			EditorProcessDebugger.LogInfo($"  Content: {ContentsFolder}", "RuntimeGameProject");
			EditorProcessDebugger.LogInfo($"  Data: {DataFolder}", "RuntimeGameProject");
			EditorProcessDebugger.LogInfo($"  Scenes: {ScenesFolder}", "RuntimeGameProject");
			EditorProcessDebugger.LogInfo($"  Prefabs: {PrefabsFolder}", "RuntimeGameProject");

			if (Settings != null)
			{
				ApplyGameSettings();
			}
			else
			{
				Debug.Warn("Project has no settings to apply.");
			}
		}
		
		public Scene CreateInitialScene()
		{
			EditorProcessDebugger.LogInfo($"Creating initial GameScene for project: {ProjectName}", "RuntimeGameProject");
			return new GameScene();
		}
		
		public void LoadContent(ContentManager content)
		{
			if (content == null)
			{
				EditorProcessDebugger.LogError("ContentManager is null, cannot load project content.", "RuntimeGameProject");

				return;
			}
			
			EditorProcessDebugger.LogInfo($"Loading content for project: {ProjectName}", "RuntimeGameProject");
			
			// Set the content root directory to the project's content folder
			if (Directory.Exists(ContentsFolder))
			{
				content.RootDirectory = ContentsFolder;
				EditorProcessDebugger.LogInfo($"Set content root directory to: {ContentsFolder}", "RuntimeGameProject");
			}
			else
			{
				EditorProcessDebugger.LogInfo($"Content folder does not exist: {ContentsFolder}", "RuntimeGameProject");
			}
		}
		
		public void UnloadContent()
		{
			EditorProcessDebugger.LogInfo($"Unloading content for project: {ProjectName}", "RuntimeGameProject");
		}
		
		#endregion
		
		#region Helper Methods
		
		/// <summary>
		/// Loads settings from settings.json, falling back to metadata if not found.
		/// </summary>
		private void LoadSettings()
		{
			var settingsPath = Path.Combine(ProjectPath, "settings.json");
			
			// Try to load from settings.json first (the source of truth)
			if (File.Exists(settingsPath))
			{
				try
				{
					var settingsJson = File.ReadAllText(settingsPath);
					_settings = Voltage.Persistence.Json.FromJson<GameSettings>(settingsJson);
					
					if (_settings != null)
					{
						EditorProcessDebugger.LogInfo($"Loaded settings from: {settingsPath}", "RuntimeGameProject");
						EditorProcessDebugger.LogInfo($"  Design Resolution: {_settings.DesignResolution.Width}x{_settings.DesignResolution.Height} ({_settings.DesignResolution.ResolutionPolicy})", "RuntimeGameProject");
						return;
					}
				}
				catch (Exception ex)
				{
					EditorProcessDebugger.LogError($"Failed to load settings.json: {ex.Message}", "RuntimeGameProject");
				}
			}
			
			// Fall back to metadata settings
			if (_metadata.Settings != null)
			{
				_settings = _metadata.Settings;
				EditorProcessDebugger.LogInfo("Using settings from project metadata", "RuntimeGameProject");
				
				// Save to settings.json for future use
				try
				{
					var settingsJson = Voltage.Persistence.Json.ToJson(_settings, new Voltage.Persistence.JsonSettings
					{
						PrettyPrint = true
					});
					File.WriteAllText(settingsPath, settingsJson, new System.Text.UTF8Encoding(false));
					EditorProcessDebugger.LogInfo($"Created settings.json at: {settingsPath}", "RuntimeGameProject");
				}
				catch (Exception ex)
				{
					EditorProcessDebugger.LogError($"Failed to create settings.json: {ex.Message}", "RuntimeGameProject");
				}
			}
			else
			{
				// Create default settings if nothing exists
				_settings = CreateDefaultSettings();
				EditorProcessDebugger.LogWarning("No settings found, created default settings", "RuntimeGameProject");
			}
		}
		
		/// <summary>
		/// Creates default settings as a last resort.
		/// </summary>
		private GameSettings CreateDefaultSettings()
		{
			return new GameSettings
			{
				Display = new GameSettings.DisplaySettings
				{
					ScreenWidth = 1280,
					ScreenHeight = 720,
					IsFullscreen = false,
					EnableVSync = true
				},
				Audio = new GameSettings.AudioSettings
				{
					MasterVolume = 1.0f,
					MusicVolume = 0.8f,
					SFXVolume = 1.0f
				},
				DesignResolution = new GameSettings.DesignResolutionSettings
				{
					Width = 1280,
					Height = 720,
					ResolutionPolicy = Scene.SceneResolutionPolicy.BestFit,
					HorizontalBleed = 0,
					VerticalBleed = 0
				},
				Physics = new GameSettings.PhysicsSettings(),
				Rendering = new GameSettings.RenderingSettings(),
				Entities = new GameSettings.EntitySettings(),
				ContentDirectory = "Content"
			};
		}
		
		private void ApplyGameSettings()
		{
			if (Settings == null)
				return;
			
			try
			{
				// Apply display settings
				if (Settings.Display != null)
				{
					EditorProcessDebugger.LogInfo($"Applying display settings: {Settings.Display.ScreenWidth}x{Settings.Display.ScreenHeight}, " +
					                              $"Fullscreen: {Settings.Display.IsFullscreen}, VSync: {Settings.Display.EnableVSync}", "RuntimeGameProject");
				}
				
				// Apply design resolution settings
				if (Settings.DesignResolution != null)
				{
					EditorProcessDebugger.LogInfo($"Design resolution loaded: {Settings.DesignResolution.Width}x{Settings.DesignResolution.Height} " +
					                              $"({Settings.DesignResolution.ResolutionPolicy})", "RuntimeGameProject");
				}
				
				// Apply audio settings
				if (Settings.Audio != null)
				{
					EditorProcessDebugger.LogInfo($"Audio settings loaded: Master={Settings.Audio.MasterVolume}, " +
					                              $"Music={Settings.Audio.MusicVolume}, SFX={Settings.Audio.SFXVolume}", "RuntimeGameProject");
				}
				
				// Apply content directory
				if (!string.IsNullOrWhiteSpace(Settings.ContentDirectory))
				{
					EditorProcessDebugger.LogInfo($"Content directory set to: {Settings.ContentDirectory}", "RuntimeGameProject");
				}
			}
			catch (Exception ex)
			{
				EditorProcessDebugger.LogError($"Failed to apply game settings: {ex.Message}", "RuntimeGameProject");
			}
		}
		
		#endregion
	}
}