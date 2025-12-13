using Microsoft.Xna.Framework.Content;
using System;
using System.IO;
using Voltage.Utils;

namespace Voltage.Editor.ProjectManagement
{
	/// <summary>
	/// Runtime implementation of IGameProject that loads project data from ProjectMetadata.
	/// Used by the editor to manage loaded projects.
	/// </summary>
	public class RuntimeGameProject : IGameProject
	{
		private readonly ProjectMetadata _metadata;
		
		public RuntimeGameProject(ProjectMetadata metadata)
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
		
		public void Initialize()
		{
			Debug.Log($"Initializing project: {ProjectName}");
			Debug.Log($"  Version: {Version}");
			Debug.Log($"  Path: {ProjectPath}");
			Debug.Log($"  Scripts: {ScriptsFolder}");
			Debug.Log($"  Effects: {EffectsFolder}");
			Debug.Log($"  Content: {ContentsFolder}");
			
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
			Debug.Log($"Creating initial scene for project: {ProjectName}");
			return new Scene();
		}
		
		public void LoadContent(ContentManager content)
		{
			if (content == null)
			{
				Debug.Warn("ContentManager is null, cannot load project content.");
				return;
			}
			
			Debug.Log($"Loading content for project: {ProjectName}");
			
			// Set the content root directory to the project's content folder
			if (Directory.Exists(ContentsFolder))
			{
				content.RootDirectory = ContentsFolder;
				Debug.Log($"Set content root directory to: {ContentsFolder}");
			}
			else
			{
				Debug.Warn($"Content folder does not exist: {ContentsFolder}");
			}
			
			// Additional content loading can be implemented here
		}
		
		public void UnloadContent()
		{
			Debug.Log($"Unloading content for project: {ProjectName}");
			// Implement content unloading if needed
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
					Debug.Log($"Applying display settings: {Settings.Display.ScreenWidth}x{Settings.Display.ScreenHeight}, " +
					         $"Fullscreen: {Settings.Display.IsFullscreen}, VSync: {Settings.Display.EnableVSync}");
					
					// Note: Actual application of these settings would require access to GraphicsDeviceManager
					// This should be done through the Core or a dedicated settings manager
				}
				
				// Apply audio settings
				if (Settings.Audio != null)
				{
					Debug.Log($"Audio settings loaded: Master={Settings.Audio.MasterVolume}, " +
					         $"Music={Settings.Audio.MusicVolume}, SFX={Settings.Audio.SFXVolume}");
				}
				
				// Apply content directory
				if (!string.IsNullOrWhiteSpace(Settings.ContentDirectory))
				{
					Debug.Log($"Content directory set to: {Settings.ContentDirectory}");
				}
			}
			catch (Exception ex)
			{
				Debug.Error($"Failed to apply game settings: {ex.Message}");
			}
		}
		
		#endregion
		
		#region Metadata Access
		
		/// <summary>
		/// Gets the underlying project metadata.
		/// </summary>
		public ProjectMetadata Metadata => _metadata;
		
		/// <summary>
		/// Gets the date when the project was created.
		/// </summary>
		public DateTime CreatedDate => _metadata.CreatedDate;
		
		#endregion
	}
}