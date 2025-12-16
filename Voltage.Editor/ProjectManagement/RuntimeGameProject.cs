using Microsoft.Xna.Framework.Content;
using System;
using System.IO;
using Voltage.Editor.EditorDebug;
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
		}
		
		#region IGameProject Implementation
		
		public string ProjectName => _metadata.ProjectName;
		
		public string ProjectPath => _metadata.ProjectPath;
		
		public GameSettings Settings => _metadata.Settings;
		
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
			// Default implementation - create an empty scene
			EditorProcessDebugger.LogInfo($"Creating initial scene for project: {ProjectName}", "RuntimeGameProject");
			return new Scene();
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