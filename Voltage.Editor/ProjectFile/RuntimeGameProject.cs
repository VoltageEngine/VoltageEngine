using System;
using System.IO;
using Microsoft.Xna.Framework.Content;
using Voltage.Editor.DebugUtils;
using Voltage.Editor.SceneFile;

namespace Voltage.Editor.ProjectFile
{
	/// <summary>
	/// Runtime implementation of IGameProject that loads project data from ProjectMetadata.
	/// Used by the editor to manage loaded projects.
	/// </summary>
	public class RuntimeGameProject : IGameProject
	{
		private readonly ProjectCreator.ProjectMetadata _metadata;
		private ProjectSettings _settings;

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
		
		public ProjectSettings Settings => _settings;
		
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
			EditorDebug.Log($"Initializing project: {ProjectName}", "RuntimeGameProject");
			EditorDebug.Log($"  Version: {Version}", "RuntimeGameProject");
			EditorDebug.Log($"  Path: {ProjectPath}", "RuntimeGameProject");
			EditorDebug.Log($"  Scripts: {ScriptsFolder}", "RuntimeGameProject");
			EditorDebug.Log($"  Effects: {EffectsFolder}", "RuntimeGameProject");
			EditorDebug.Log($"  Content: {ContentsFolder}", "RuntimeGameProject");
			EditorDebug.Log($"  Data: {DataFolder}", "RuntimeGameProject");
			EditorDebug.Log($"  Scenes: {ScenesFolder}", "RuntimeGameProject");
			EditorDebug.Log($"  Prefabs: {PrefabsFolder}", "RuntimeGameProject");

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
			EditorDebug.Log($"Creating initial GameScene for project: {ProjectName}", "RuntimeGameProject");
			var scene = new Scene();
			scene.AddSceneComponent<GameSceneComponent>();
			return scene;
		}
		
		public void LoadContent(ContentManager content)
		{
			if (content == null)
			{
				EditorDebug.Error("ContentManager is null, cannot load project content.", "RuntimeGameProject");

				return;
			}
			
			EditorDebug.Log($"Loading content for project: {ProjectName}", "RuntimeGameProject");
			
			// Set the content root directory to the project's content folder
			if (Directory.Exists(ContentsFolder))
			{
				content.RootDirectory = ContentsFolder;
				EditorDebug.Log($"Set content root directory to: {ContentsFolder}", "RuntimeGameProject");
			}
			else
			{
				EditorDebug.Log($"Content folder does not exist: {ContentsFolder}", "RuntimeGameProject");
			}
		}
		
		public void UnloadContent()
		{
			EditorDebug.Log($"Unloading content for project: {ProjectName}", "RuntimeGameProject");
		}
		
		#endregion
		
		#region Helper Methods
		
		/// <summary>
		/// Loads settings from settings.json, falling back to metadata if not found.
		/// </summary>
		private void LoadSettings()
		{
			var settingsPath = Path.Combine(ProjectPath, "ProjectSettings.json");
			
			// Try to load from settings.json first (the source of truth)
			if (File.Exists(settingsPath))
			{
				try
				{
					var settingsJson = File.ReadAllText(settingsPath);
					_settings = Voltage.Persistence.Json.FromJson<ProjectSettings>(settingsJson);
					
					if (_settings != null)
					{
						EditorDebug.Log($"Loaded settings from: {settingsPath}", "RuntimeGameProject");
						EditorDebug.Log($"  Design Resolution: {_settings.DesignResolution.Width}x{_settings.DesignResolution.Height} ({_settings.DesignResolution.ResolutionPolicy})", "RuntimeGameProject");
						return;
					}
				}
				catch (Exception ex)
				{
					EditorDebug.Error($"Failed to load ProjectSettings.json: {ex.Message}", "RuntimeGameProject");
				}
			}
			
			// Fall back to metadata settings
			if (_metadata.Settings != null)
			{
				_settings = _metadata.Settings;
				EditorDebug.Log("Using settings from project metadata", "RuntimeGameProject");
				
				try
				{
					var settingsJson = Voltage.Persistence.Json.ToJson(_settings, new Voltage.Persistence.JsonSettings
					{
						PrettyPrint = true
					});
					File.WriteAllText(settingsPath, settingsJson, new System.Text.UTF8Encoding(false));
					EditorDebug.Log($"Created ProjectSettings.json at: {settingsPath}", "RuntimeGameProject");
				}
				catch (Exception ex)
				{
					EditorDebug.Error($"Failed to create ProjectSettings.json: {ex.Message}", "RuntimeGameProject");
				}
			}
			else
			{
				_settings = new ProjectCreator().CreateDefaultSettings();
				EditorDebug.Warn("No settings found, created default settings", "RuntimeGameProject");
			}
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
					EditorDebug.Log($"Applying display settings: {Settings.Display.ScreenWidth}x{Settings.Display.ScreenHeight}, " +
					                              $"Fullscreen: {Settings.Display.IsFullscreen}, VSync: {Settings.Display.EnableVSync}", "RuntimeGameProject");
				}
				
				// Apply design resolution settings
				if (Settings.DesignResolution != null)
				{
					EditorDebug.Log($"Design resolution loaded: {Settings.DesignResolution.Width}x{Settings.DesignResolution.Height} " +
					                              $"({Settings.DesignResolution.ResolutionPolicy})", "RuntimeGameProject");
				}
				
				// Apply audio settings
				if (Settings.Audio != null)
				{
					EditorDebug.Log($"Audio settings loaded: Master={Settings.Audio.MasterVolume}, " +
					                              $"Music={Settings.Audio.MusicVolume}, SFX={Settings.Audio.SFXVolume}", "RuntimeGameProject");
				}
				
				// Apply content directory
				if (!string.IsNullOrWhiteSpace(Settings.ContentDirectory))
				{
					EditorDebug.Log($"Content directory set to: {Settings.ContentDirectory}", "RuntimeGameProject");
				}
			}
			catch (Exception ex)
			{
				EditorDebug.Error($"Failed to apply game settings: {ex.Message}", "RuntimeGameProject");
			}
		}
		
		#endregion
	}
}