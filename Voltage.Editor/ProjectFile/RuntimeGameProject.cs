using System;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Xna.Framework.Content;
using Voltage.Editor.DebugUtils;
using Voltage.Editor.SceneFile;
using Voltage.Project;

namespace Voltage.Editor.ProjectFile
{
	/// <summary>
	/// Runtime implementation of IGameProject that loads project data from ProjectMetadata.
	/// Used by the editor to manage loaded projects.
	/// </summary>
	public class RuntimeGameProject : IGameProject
	{
		private readonly ProjectCreatorWindow.ProjectMetadata _metadata;
		private ProjectSettings _settings;

		#region Metadata Access

		/// <summary>
		/// Gets the underlying project metadata.
		/// </summary>
		public ProjectCreatorWindow.ProjectMetadata Metadata => _metadata;

		/// <summary>
		/// Gets the date when the project was created.
		/// </summary>
		public DateTime CreatedDate => _metadata.CreatedDate;

		#endregion

		public RuntimeGameProject(ProjectCreatorWindow.ProjectMetadata metadata)
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
				var version = ReadVersionFromCsproj();
				return version ?? new Version(1, 0, 0);
			}
		}

		/// <summary>
		/// Reads the Version property directly from the game project's .csproj file.
		/// The .csproj is the single source of truth for version — no caching, no duplication.
		/// </summary>
		private Version ReadVersionFromCsproj()
		{
			try
			{
				var csprojFiles = Directory.GetFiles(ProjectPath, "*.csproj", SearchOption.TopDirectoryOnly);
				if (csprojFiles.Length == 0)
					return null;

				var content = File.ReadAllText(csprojFiles[0]);
				var match = Regex.Match(content, @"<Version>(.*?)</Version>");
				if (match.Success && System.Version.TryParse(match.Groups[1].Value, out var parsed))
					return parsed;
			}
			catch (Exception ex)
			{
				Debug.Warn($"Failed to read version from .csproj: {ex.Message}");
			}

			return null;
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

		#region Version

		/// <summary>
		/// Writes the version to the .csproj file. This is the only place version is persisted.
		/// </summary>
		public bool SetVersion(string newVersion)
		{
			if (string.IsNullOrWhiteSpace(newVersion) || !System.Version.TryParse(newVersion, out _))
				return false;

			try
			{
				var csprojFiles = Directory.GetFiles(ProjectPath, "*.csproj", SearchOption.TopDirectoryOnly);
				if (csprojFiles.Length == 0)
				{
					Debug.Warn($"No .csproj file found in '{ProjectPath}'.");
					return false;
				}

				var csprojPath = csprojFiles[0];
				var content = File.ReadAllText(csprojPath);

				if (Regex.IsMatch(content, @"<Version>.*?</Version>"))
					content = Regex.Replace(content, @"<Version>.*?</Version>", $"<Version>{newVersion}</Version>");
				else
					content = Regex.Replace(content, @"(<PropertyGroup[^>]*>)",
						$"$1\n\t\t<Version>{newVersion}</Version>");

				File.WriteAllText(csprojPath, content, new System.Text.UTF8Encoding(false));
				return true;
			}
			catch (Exception ex)
			{
				Debug.Error($"Failed to save version to .csproj: {ex.Message}");
				return false;
			}
		}

		#endregion

		#region Helper Methods

		private void LoadSettings()
		{
			var settingsPath = Path.Combine(ProjectPath, "ProjectSettings.json");

			if (File.Exists(settingsPath))
			{
				try
				{
					var settingsJson = File.ReadAllText(settingsPath);
					_settings = Voltage.Persistence.Json.FromJson<ProjectSettings>(settingsJson);
					if (_settings != null)
						return;
				}
				catch (Exception ex)
				{
					Debug.Error($"Failed to load ProjectSettings.json: {ex.Message}");
				}
			}

			// No settings file found — create defaults and persist
			_settings = new ProjectSettings();
			try
			{
				var json = Voltage.Persistence.Json.ToJson(_settings, new Voltage.Persistence.JsonSettings { PrettyPrint = true });
				File.WriteAllText(settingsPath, json, new System.Text.UTF8Encoding(false));
			}
			catch (Exception ex)
			{
				Debug.Error($"Failed to create ProjectSettings.json: {ex.Message}");
			}
		}


		private void ApplyGameSettings()
		{
			if (Settings == null)
				return;

			try
			{
				if (Settings.Display != null)
				{
					EditorDebug.Log($"Applying display settings: {Settings.Display.ScreenWidth}x{Settings.Display.ScreenHeight}, " +
					                $"Fullscreen: {Settings.Display.IsFullscreen}, VSync: {Settings.Display.EnableVSync}", "RuntimeGameProject");
				}

				if (Settings.DesignResolution != null)
				{
					EditorDebug.Log($"Design resolution loaded: {Settings.DesignResolution.Width}x{Settings.DesignResolution.Height} " +
					                $"({Settings.DesignResolution.ResolutionPolicy})", "RuntimeGameProject");
				}

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