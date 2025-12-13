using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ImGuiNET;
using Voltage.Editor.FilePickers;
using Voltage.Editor.Utils;
using Voltage.Utils;
using Num = System.Numerics;

namespace Voltage.Editor.ProjectManagement
{
	/// <summary>
	/// Handles the creation of new game projects through an ImGui popup interface
	/// </summary>
	public class ProjectCreator
	{
		private string _projectName = "";
		private string _projectPath = "";
		private bool _showCreateProjectPopup = false;
		private FilePicker _folderPicker;
		private bool _showFolderPicker = false;
		private bool _reopenCreateProjectPopup = false;
		private string _projectNameError = "";
		
		// GameSettings fields
		private int _screenWidth = 1280;
		private int _screenHeight = 720;
		private bool _isFullscreen = false;
		private bool _enableVSync = true;
		private float _masterVolume = 1.0f;
		private float _musicVolume = 1.0f;
		private float _sfxVolume = 1.0f;
		
		// Version fields
		private int _majorVersion = 1;
		private int _minorVersion = 0;
		private int _buildVersion = 0;
		
		private const string ScriptsFolder = "Scripts";
		private const string EffectsFolder = "Effects";
		private const string ContentsFolder = "Content";
		
		public void OpenCreateProjectPopup()
		{
			_showCreateProjectPopup = true;
			
			// Set default project path to user's documents folder
			var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
			_projectPath = Path.Combine(documentsPath, "VoltageProjects");
		}
		
		public void Draw()
		{
			if (_reopenCreateProjectPopup)
			{
				_showCreateProjectPopup = true;
				_reopenCreateProjectPopup = false;
			}
			
			if (_showCreateProjectPopup)
			{
				ImGui.OpenPopup("create-project-popup");
				_showCreateProjectPopup = false;
			}
			
			DrawFolderPickerPopup();
			
			var center = new Num.Vector2(Screen.Width * 0.5f, Screen.Height * 0.5f);
			ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new Num.Vector2(0.5f, 0.5f));
			ImGui.SetNextWindowSize(new Num.Vector2(600, 700), ImGuiCond.Appearing);
			
			bool open = true;
			if (ImGui.BeginPopupModal("create-project-popup", ref open, ImGuiWindowFlags.NoResize))
			{
				ImGui.TextColored(new Num.Vector4(0.2f, 0.8f, 1.0f, 1.0f), "Create New Game Project");
				ImGui.Separator();
				
				VoltageEditorUtils.MediumVerticalSpace();
				
				DrawProjectInfo();
				VoltageEditorUtils.MediumVerticalSpace();
				
				DrawDisplaySettings();
				VoltageEditorUtils.MediumVerticalSpace();
				
				DrawAudioSettings();
				VoltageEditorUtils.MediumVerticalSpace();
				
				DrawVersionInfo();
				VoltageEditorUtils.MediumVerticalSpace();
				
				DrawFolderInfo();
				VoltageEditorUtils.MediumVerticalSpace();
				
				DrawActionButtons();
				
				ImGui.EndPopup();
			}
		}
		
		private void DrawProjectInfo()
		{
			ImGui.TextColored(new Num.Vector4(0.8f, 0.9f, 1.0f, 1.0f), "Project Information");
			ImGui.Separator();
			
			ImGui.Text("Project Name:");
			ImGui.SetNextItemWidth(-1);
			
			// Check for project name conflicts in real-time
			if (ImGui.InputText("##ProjectName", ref _projectName, 100))
			{
				ValidateProjectName();
			}
			
			if (ImGui.IsItemHovered())
			{
				ImGui.SetTooltip("Name of your game project");
			}
			
			// Display error message under the input field if there's an error
			if (!string.IsNullOrWhiteSpace(_projectNameError))
			{
				ImGui.PushStyleColor(ImGuiCol.Text, new Num.Vector4(1.0f, 0.3f, 0.3f, 1.0f));
				ImGui.TextWrapped(_projectNameError);
				ImGui.PopStyleColor();
			}
			
			VoltageEditorUtils.SmallVerticalSpace();
			
			ImGui.Text("Project Path:");
			ImGui.SetNextItemWidth(-250);
			ImGui.InputText("##ProjectPath", ref _projectPath, 500);
			
			ImGui.SameLine();
			
			if (ImGui.Button("Browse...", new Num.Vector2(100, 0)))
			{
				OpenFolderPicker();
			}
			
			if (ImGui.IsItemHovered())
			{
				ImGui.SetTooltip("Directory where the project will be created");
			}
			
			// Show the full project path that will be created
			if (!string.IsNullOrWhiteSpace(_projectName) && !string.IsNullOrWhiteSpace(_projectPath))
			{
				var fullPath = Path.Combine(_projectPath, _projectName);
				ImGui.PushStyleColor(ImGuiCol.Text, new Num.Vector4(0.7f, 0.7f, 0.7f, 1.0f));
				ImGui.TextWrapped($"Full path: {fullPath}");
				ImGui.PopStyleColor();
			}
		}
		
		private void ValidateProjectName()
		{
			_projectNameError = "";
			
			if (string.IsNullOrWhiteSpace(_projectName))
			{
				return; // Don't show error for empty field while typing
			}
			
			// Check if path is valid
			if (!string.IsNullOrWhiteSpace(_projectPath))
			{
				var fullProjectPath = Path.Combine(_projectPath, _projectName);
				
				// Check if directory already exists
				if (Directory.Exists(fullProjectPath))
				{
					_projectNameError = $"Error: A project with the name '{_projectName}' already exists at this location.";
				}
			}
			
			// Validate project name for invalid characters
			var invalidChars = Path.GetInvalidFileNameChars();
			if (_projectName.IndexOfAny(invalidChars) >= 0)
			{
				_projectNameError = "Error: Project name contains invalid characters.";
			}
		}
		
		private void OpenFolderPicker()
		{
			// Initialize the folder picker with the current path or a default
			string startingPath = !string.IsNullOrWhiteSpace(_projectPath) 
				? _projectPath 
				: Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
			
			_folderPicker = FilePicker.GetFolderPicker(this, startingPath);
			_folderPicker.DontAllowTraverselBeyondRootFolder = false; // Allow navigation beyond root
			_showFolderPicker = true;
		}
		
		private void DrawFolderPickerPopup()
		{
			if (_showFolderPicker && _folderPicker != null)
			{
				ImGui.OpenPopup("folder-picker-popup");
				_showFolderPicker = false;
			}
			
			var center = new Num.Vector2(Screen.Width * 0.5f, Screen.Height * 0.5f);
			ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new Num.Vector2(0.5f, 0.5f));
			ImGui.SetNextWindowSize(new Num.Vector2(600, 500), ImGuiCond.Appearing);
			
			bool open = true;
			if (ImGui.BeginPopupModal("folder-picker-popup", ref open, ImGuiWindowFlags.NoResize))
			{
				ImGui.TextColored(new Num.Vector4(0.2f, 0.8f, 1.0f, 1.0f), "Select Project Directory");
				ImGui.Separator();
				VoltageEditorUtils.SmallVerticalSpace();
				
				if (_folderPicker != null && _folderPicker.Draw())
				{
					_projectPath = _folderPicker.SelectedFile;
					
					ValidateProjectName();
					
					FilePicker.RemoveFilePicker(_folderPicker);
					_folderPicker = null;
					ImGui.CloseCurrentPopup();
					
					_reopenCreateProjectPopup = true;
				}
				
				ImGui.EndPopup();
			}
			
			// Handle cancel/close via X button
			if (!open && _folderPicker != null)
			{
				FilePicker.RemoveFilePicker(_folderPicker);
				_folderPicker = null;
				
				// Reopen the create project popup after this frame
				_reopenCreateProjectPopup = true;
			}
		}
		
		private void DrawDisplaySettings()
		{
			if (ImGui.CollapsingHeader("Display Settings", ImGuiTreeNodeFlags.DefaultOpen))
			{
				ImGui.Indent();
				
				ImGui.Text("Screen Resolution:");
				ImGui.SetNextItemWidth(150);
				ImGui.InputInt("Width##ScreenWidth", ref _screenWidth);
				ImGui.SameLine();
				ImGui.SetNextItemWidth(150);
				ImGui.InputInt("Height##ScreenHeight", ref _screenHeight);
				
				VoltageEditorUtils.SmallVerticalSpace();
				
				ImGui.Checkbox("Fullscreen", ref _isFullscreen);
				ImGui.Checkbox("Enable VSync", ref _enableVSync);
				
				ImGui.Unindent();
			}
		}
		
		private void DrawAudioSettings()
		{
			if (ImGui.CollapsingHeader("Audio Settings", ImGuiTreeNodeFlags.DefaultOpen))
			{
				ImGui.Indent();
				
				ImGui.Text("Master Volume:");
				ImGui.SetNextItemWidth(-1);
				ImGui.SliderFloat("##MasterVolume", ref _masterVolume, 0.0f, 1.0f, "%.2f");
				
				VoltageEditorUtils.SmallVerticalSpace();
				
				ImGui.Text("Music Volume:");
				ImGui.SetNextItemWidth(-1);
				ImGui.SliderFloat("##MusicVolume", ref _musicVolume, 0.0f, 1.0f, "%.2f");
				
				VoltageEditorUtils.SmallVerticalSpace();
				
				ImGui.Text("SFX Volume:");
				ImGui.SetNextItemWidth(-1);
				ImGui.SliderFloat("##SFXVolume", ref _sfxVolume, 0.0f, 1.0f, "%.2f");
				
				ImGui.Unindent();
			}
		}
		
		private void DrawVersionInfo()
		{
			if (ImGui.CollapsingHeader("Version", ImGuiTreeNodeFlags.DefaultOpen))
			{
				ImGui.Indent();
				
				ImGui.SetNextItemWidth(100);
				ImGui.InputInt("Major", ref _majorVersion);
				_majorVersion = Math.Max(0, _majorVersion);
				
				ImGui.SameLine();
				ImGui.SetNextItemWidth(100);
				ImGui.InputInt("Minor", ref _minorVersion);
				_minorVersion = Math.Max(0, _minorVersion);
				
				ImGui.SameLine();
				ImGui.SetNextItemWidth(100);
				ImGui.InputInt("Build", ref _buildVersion);
				_buildVersion = Math.Max(0, _buildVersion);
				
				ImGui.TextColored(new Num.Vector4(0.7f, 0.7f, 0.7f, 1.0f), 
					$"Version: {_majorVersion}.{_minorVersion}.{_buildVersion}");
				
				ImGui.Unindent();
			}
		}
		
		private void DrawFolderInfo()
		{
			if (ImGui.CollapsingHeader("Project Structure", ImGuiTreeNodeFlags.DefaultOpen))
			{
				ImGui.Indent();
				
				ImGui.TextColored(new Num.Vector4(0.7f, 1.0f, 0.7f, 1.0f), "Standard folders that will be created:");
				
				ImGui.BulletText($"{ScriptsFolder}/ - For game scripts and logic");
				ImGui.BulletText($"{EffectsFolder}/ - For shader effects");
				ImGui.BulletText($"{ContentsFolder}/ - For game assets");
				
				ImGui.Unindent();
			}
		}
		
		private void DrawActionButtons()
		{
			ImGui.Separator();
			
			bool canCreate = !string.IsNullOrWhiteSpace(_projectName) && 
			                 !string.IsNullOrWhiteSpace(_projectPath) &&
			                 string.IsNullOrWhiteSpace(_projectNameError) && 
			                 _screenWidth > 0 && 
			                 _screenHeight > 0;
			
			if (!canCreate)
			{
				ImGui.BeginDisabled();
			}
			
			var buttonWidth = 120f;
			var spacing = 10f;
			var totalButtonWidth = (buttonWidth * 2) + spacing;
			var windowWidth = ImGui.GetWindowSize().X;
			var centerStart = (windowWidth - totalButtonWidth) * 0.5f;
			
			ImGui.SetCursorPosX(centerStart);
			
			if (ImGui.Button("Create Project", new Num.Vector2(buttonWidth, 30)))
			{
				if (canCreate)
				{
					CreateProject();
				}
			}
			
			if (!canCreate)
			{
				ImGui.EndDisabled();
			}
			
			ImGui.SameLine();
			
			if (ImGui.Button("Cancel", new Num.Vector2(buttonWidth, 30)))
			{
				ImGui.CloseCurrentPopup();
				ResetFields();
			}
		}
		
		private void CreateProject()
		{
			try
			{
				if (string.IsNullOrWhiteSpace(_projectName))
				{
					_projectNameError = "Error: Project name cannot be empty.";
					return;
				}
				
				var fullProjectPath = Path.Combine(_projectPath, _projectName);
				
				if (Directory.Exists(fullProjectPath))
				{
					_projectNameError = $"Error: A project with the name '{_projectName}' already exists at this location.";
					return;
				}
				
				Directory.CreateDirectory(fullProjectPath);
				
				var scriptsPath = Path.Combine(fullProjectPath, ScriptsFolder);
				var effectsPath = Path.Combine(fullProjectPath, EffectsFolder);
				var contentsPath = Path.Combine(fullProjectPath, ContentsFolder);
				
				Directory.CreateDirectory(scriptsPath);
				Directory.CreateDirectory(effectsPath);
				Directory.CreateDirectory(contentsPath);
				
				var settings = new GameSettings
				{
					Display = new DisplaySettings
					{
						ScreenWidth = _screenWidth,
						ScreenHeight = _screenHeight,
						IsFullscreen = _isFullscreen,
						EnableVSync = _enableVSync
					},
					Audio = new AudioSettings
					{
						MasterVolume = _masterVolume,
						MusicVolume = _musicVolume,
						SFXVolume = _sfxVolume
					},
					Physics = new PhysicsSettings(),
					Rendering = new RenderingSettings(),
					Entities = new EntitySettings(),
					ContentDirectory = "Content"
				};
				
				// Create Version
				var version = new Version(_majorVersion, _minorVersion, _buildVersion);
				
				// Generate project structure (solution, csproj, folders, etc.)
				bool structureCreated = ProjectStructureGenerator.CreateProjectStructure(
					_projectName,
					fullProjectPath,
					version
				);
				
				if (!structureCreated)
				{
					NotificationSystem.ShowTimedNotification("Failed to create project structure!");
					return;
				}
				
				// Save project metadata
				var projectMetadata = new ProjectMetadata
				{
					ProjectName = _projectName,
					ProjectPath = fullProjectPath,
					Settings = settings,
					Version = version.ToString(),
					ScriptsFolder = ScriptsFolder,
					EffectsFolder = EffectsFolder,
					ContentsFolder = ContentsFolder,
					CreatedDate = DateTime.Now
				};
				
				var metadataPath = Path.Combine(fullProjectPath, "project.json");
				
				var metadataJson = Voltage.Persistence.Json.ToJson(projectMetadata, new Voltage.Persistence.JsonSettings
				{
					PrettyPrint = true
				});
				
				File.WriteAllText(metadataPath, metadataJson);
				
				var settingsPath = Path.Combine(fullProjectPath, "settings.json");
				var settingsJson = Voltage.Persistence.Json.ToJson(settings, new Voltage.Persistence.JsonSettings
				{
					PrettyPrint = true
				});
				File.WriteAllText(settingsPath, settingsJson);
				
				Debug.Log($"Successfully created project '{_projectName}' at: {fullProjectPath}");
				NotificationSystem.ShowTimedNotification($"Project '{_projectName}' created successfully!");
				
				// Optionally open the solution in Visual Studio
				var solutionPath = Path.Combine(fullProjectPath, $"{_projectName}.sln");
				if (File.Exists(solutionPath))
				{
					Debug.Log($"Opening solution: {solutionPath}");
					System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
					{
						FileName = solutionPath,
						UseShellExecute = true
					});
				}
				
				ImGui.CloseCurrentPopup();
				ResetFields();
			}
			catch (Exception ex)
			{
				Debug.Error($"Failed to create project: {ex.Message}");
				Debug.Error($"Stack trace: {ex.StackTrace}");
				_projectNameError = $"Error: {ex.Message}";
			}
		}
		
		private void ResetFields()
		{
			_projectName = "";
			_projectPath = "";
			_projectNameError = "";
			_screenWidth = 1280;
			_screenHeight = 720;
			_isFullscreen = false;
			_enableVSync = true;
			_masterVolume = 1.0f;
			_musicVolume = 1.0f;
			_sfxVolume = 1.0f;
			_majorVersion = 1;
			_minorVersion = 0;
			_buildVersion = 0;
			
			if (_folderPicker != null)
			{
				FilePicker.RemoveFilePicker(_folderPicker);
				_folderPicker = null;
			}
		}
	}
	
	public class ProjectMetadata
	{
		public string ProjectName;
		public string ProjectPath;
		public GameSettings Settings;
		public string Version;
		public string ScriptsFolder;
		public string EffectsFolder;
		public string ContentsFolder;
		public DateTime CreatedDate;
	}
}
