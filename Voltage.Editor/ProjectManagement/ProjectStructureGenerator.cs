using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Voltage.Editor.EditorDebug;
using Voltage.Utils;

namespace Voltage.Editor.ProjectManagement
{
	/// <summary>
	/// Generates Visual Studio solution and project files for game projects
	/// </summary>
	public static class ProjectStructureGenerator
	{
		/// <summary>
		/// Creates a complete Visual Studio solution structure for a game project
		/// </summary>
		public static bool CreateProjectStructure(
			string projectName,
			string projectPath,
			Version version)
		{
			EditorProcessDebugger.LogInfo("=== Starting Project Structure Generation ===", "ProjectStructure");
			EditorProcessDebugger.LogInfo($"Project: {projectName}", "ProjectStructure");
			EditorProcessDebugger.LogInfo($"Path: {projectPath}", "ProjectStructure");
			EditorProcessDebugger.LogInfo($"Version: {version}", "ProjectStructure");
			
			try
			{
				// Create directory structure
				EditorProcessDebugger.LogInfo("Creating directory structure...", "ProjectStructure");
				Directory.CreateDirectory(projectPath);
				
				var scriptsFolder = Path.Combine(projectPath, "Scripts");
				var effectsFolder = Path.Combine(projectPath, "Effects");
				var contentFolder = Path.Combine(projectPath, "Content");
				var propertiesFolder = Path.Combine(projectPath, "Properties");
				
				EditorProcessDebugger.LogInfo($"Creating Scripts folder: {scriptsFolder}", "ProjectStructure");
				Directory.CreateDirectory(scriptsFolder);
				
				EditorProcessDebugger.LogInfo($"Creating Effects folder: {effectsFolder}", "ProjectStructure");
				Directory.CreateDirectory(effectsFolder);
				
				EditorProcessDebugger.LogInfo($"Creating Content folder: {contentFolder}", "ProjectStructure");
				Directory.CreateDirectory(contentFolder);
				
				EditorProcessDebugger.LogInfo($"Creating Properties folder: {propertiesFolder}", "ProjectStructure");
				Directory.CreateDirectory(propertiesFolder);

				// Create .csproj file
				EditorProcessDebugger.LogInfo("Creating .csproj file...", "ProjectStructure");
				CreateProjectFile(projectName, projectPath, version);
				
				// Create .sln file
				EditorProcessDebugger.LogInfo("Creating .sln file...", "ProjectStructure");
				CreateSolutionFile(projectName, projectPath);
				
				// Create Program.cs (entry point)
				EditorProcessDebugger.LogInfo("Creating Program.cs...", "ProjectStructure");
				CreateProgramFile(projectName, projectPath);
				
				// Create AssemblyInfo.cs
				EditorProcessDebugger.LogInfo("Creating AssemblyInfo.cs...", "ProjectStructure");
				CreateAssemblyInfoFile(projectName, propertiesFolder, version);
				
				// Create example script
				EditorProcessDebugger.LogInfo("Creating example script...", "ProjectStructure");
				CreateExampleScript(scriptsFolder, projectName);
				
				// Create .gitignore
				EditorProcessDebugger.LogInfo("Creating .gitignore...", "ProjectStructure");
				CreateGitIgnoreFile(projectPath);
				
				EditorProcessDebugger.LogInfo($"Successfully created project structure at: {projectPath}", "ProjectStructure");
				EditorProcessDebugger.LogInfo("=== Project Structure Generation Complete ===", "ProjectStructure");
				Debug.Log($"Successfully created project structure at: {projectPath}");
				return true;
			}
			catch (Exception ex)
			{
				EditorProcessDebugger.LogError($"Failed to create project structure: {ex.Message}", "ProjectStructure");
				EditorProcessDebugger.LogError($"Stack trace: {ex.StackTrace}", "ProjectStructure");
				Debug.Error($"Failed to create project structure: {ex.Message}");
				return false;
			}
		}

		private static void CreateProjectFile(string projectName, string projectPath, Version version)
		{
			var projectFilePath = Path.Combine(projectPath, $"{projectName}.csproj");
			EditorProcessDebugger.LogInfo($"Creating project file: {projectFilePath}", "ProjectStructure");
			
			var voltageEditorPath = FindVoltageEditorPath();
			var voltageEnginePath = FindVoltageEnginePath();
			var voltagePersistencePath = FindVoltagePersistencePath();
			
			EditorProcessDebugger.LogInfo($"Voltage Editor path: {voltageEditorPath}", "ProjectStructure");
			EditorProcessDebugger.LogInfo($"Voltage Engine path: {voltageEnginePath}", "ProjectStructure");
			EditorProcessDebugger.LogInfo($"Voltage Persistence path: {voltagePersistencePath}", "ProjectStructure");
			
			// Use relative paths if possible
			var relativeEditorPath = GetRelativePath(projectPath, voltageEditorPath);
			var relativeEnginePath = GetRelativePath(projectPath, voltageEnginePath);
			var relativePersistencePath = GetRelativePath(projectPath, voltagePersistencePath);
			
			EditorProcessDebugger.LogInfo($"Relative Editor path: {relativeEditorPath}", "ProjectStructure");
			EditorProcessDebugger.LogInfo($"Relative Engine path: {relativeEnginePath}", "ProjectStructure");
			EditorProcessDebugger.LogInfo($"Relative Persistence path: {relativePersistencePath}", "ProjectStructure");

			var csprojContent = $@"<Project Sdk=""Microsoft.NET.Sdk"">

  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <LangVersion>12.0</LangVersion>
    <Nullable>disable</Nullable>
    <AssemblyName>{projectName}</AssemblyName>
    <RootNamespace>{projectName}</RootNamespace>
    <Version>{version}</Version>
    <ApplicationIcon>Icon.ico</ApplicationIcon>
    <StartupObject>{projectName}.Program</StartupObject>
  </PropertyGroup>

  <PropertyGroup Condition=""'$(Configuration)|$(Platform)'=='Debug|AnyCPU'"">
    <DefineConstants>TRACE;DEBUG;OS_WINDOWS</DefineConstants>
  </PropertyGroup>

  <PropertyGroup Condition=""'$(Configuration)|$(Platform)'=='Release|AnyCPU'"">
    <DefineConstants>TRACE;OS_WINDOWS</DefineConstants>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include=""MonoGame.Framework.DesktopGL"" Version=""3.8.1.*"" />
    <PackageReference Include=""MonoGame.Content.Builder.Task"" Version=""3.8.1.*"" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include=""{relativeEnginePath}\Voltage.Engine.csproj"" />
    <ProjectReference Include=""{relativeEditorPath}\Voltage.Editor.csproj"" />
    <ProjectReference Include=""{relativePersistencePath}\Voltage.Persistence.csproj"" />
  </ItemGroup>

  <ItemGroup>
    <Folder Include=""Scripts\"" />
    <Folder Include=""Effects\"" />
    <Folder Include=""Content\"" />
  </ItemGroup>

  <ItemGroup>
    <Content Include=""Content\**\*.*"">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </Content>
  </ItemGroup>

  <!-- Compile scripts at build time if needed -->
  <ItemGroup>
    <Compile Include=""Scripts\**\*.cs"" />
  </ItemGroup>

</Project>";

			File.WriteAllText(projectFilePath, csprojContent);
			EditorProcessDebugger.LogInfo($"Project file created successfully", "ProjectStructure");
			Debug.Log($"Created project file: {projectFilePath}");
		}

		private static void CreateSolutionFile(string projectName, string projectPath)
		{
			var solutionFilePath = Path.Combine(projectPath, $"{projectName}.sln");
			var projectGuid = Guid.NewGuid().ToString("B").ToUpperInvariant();

			var slnContent = $@"Microsoft Visual Studio Solution File, Format Version 12.00
# Visual Studio Version 17
VisualStudioVersion = 17.0.31903.59
MinimumVisualStudioVersion = 10.0.40219.1
Project(""{{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}}"") = ""{projectName}"", ""{projectName}.csproj"", ""{projectGuid}""
EndProject
Global
	GlobalSection(SolutionConfigurationPlatforms) = preSolution
		Debug|Any CPU = Debug|Any CPU
		Release|Any CPU = Release|Any CPU
	EndGlobalSection
	GlobalSection(ProjectConfigurationPlatforms) = postSolution
		{projectGuid}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
		{projectGuid}.Debug|Any CPU.Build.0 = Debug|Any CPU
		{projectGuid}.Release|Any CPU.ActiveCfg = Release|Any CPU
		{projectGuid}.Release|Any CPU.Build.0 = Release|Any CPU
	EndGlobalSection
	GlobalSection(SolutionProperties) = preSolution
		HideSolutionNode = FALSE
	EndGlobalSection
EndGlobal";

			// Write solution file with UTF-8 encoding and no BOM
			File.WriteAllText(solutionFilePath, slnContent, new System.Text.UTF8Encoding(false));
		}

		private static void CreateProgramFile(string projectName, string projectPath)
		{
			var programFilePath = Path.Combine(projectPath, "Program.cs");

			var programContent = $@"
using System;
using Voltage;
using Voltage.Editor.ProjectManagement;

namespace {projectName}
{{
	/// <summary>
	/// Entry point for {projectName}
	/// </summary>
	public static class Program
	{{
		[STAThread]
		static void Main()
		{{
			using (var game = new {projectName}Game())
			{{
				game.Run();
			}}
		}}
	}}

	/// <summary>
	/// Main game class for {projectName}
	/// </summary>
	public class {projectName}Game : Core
	{{
		public {projectName}Game() : base()
		{{
			// Load game settings
			var settingsPath = System.IO.Path.Combine(
				AppContext.BaseDirectory, 
				""settings.json""
			);
			
			if (System.IO.File.Exists(settingsPath))
			{{
				var json = System.IO.File.ReadAllText(settingsPath);
				var settings = Voltage.Persistence.Json.FromJson<ProjectSettings>(json);
				ProjectSettings.Instance = settings;
			}}
		}}

		protected override void Initialize()
		{{
			base.Initialize();
			
			// Set window title
			Window.Title = ""{projectName}"";
			
			// Load initial scene
			Scene = new MainScene();
		}}
	}}

	/// <summary>
	/// Main game scene
	/// </summary>
	public class MainScene : GameScene
	{{
		public override void Initialize()
		{{
			base.Initialize();
	
			Debug.Info(""MainScene initialized!"");
		}}
	}}
}}
";

			File.WriteAllText(programFilePath, programContent);
		}

		private static void CreateAssemblyInfoFile(string projectName, string propertiesFolder, Version version)
		{
			var assemblyInfoPath = Path.Combine(propertiesFolder, "AssemblyInfo.cs");

			var assemblyInfoContent = $@"using System.Reflection;
using System.Runtime.InteropServices;

[assembly: AssemblyTitle(""{projectName}"")]
[assembly: AssemblyDescription(""Game project created with Voltage Engine"")]
[assembly: AssemblyConfiguration("""")]
[assembly: AssemblyCompany("""")]
[assembly: AssemblyProduct(""{projectName}"")]
[assembly: AssemblyCopyright(""Copyright © {DateTime.Now.Year}"")]
[assembly: AssemblyTrademark("""")]
[assembly: AssemblyCulture("""")]

[assembly: ComVisible(false)]

[assembly: Guid(""{Guid.NewGuid()}"")]

[assembly: AssemblyVersion(""{version}"")]
[assembly: AssemblyFileVersion(""{version}"")]
";

			File.WriteAllText(assemblyInfoPath, assemblyInfoContent);
		}

		private static void CreateExampleScript(string scriptsFolder, string projectName)
		{
			var exampleScriptPath = Path.Combine(scriptsFolder, "ExampleComponent.cs");

			var exampleScriptContent = $@"
using Microsoft.Xna.Framework;
using Voltage;
using Voltage.Utils;

namespace {projectName}.Scripts
{{
	/// <summary>
	/// Example component that can be attached to entities
	/// </summary>
	public class ExampleComponent : Component, IUpdatable
	{{
		public float Speed = 100f;
		
		public override void OnAddedToEntity()
		{{
			base.OnAddedToEntity();
			Debug.Log($""ExampleComponent added to {{Entity.Name}}"");
		}}
		
		public void Update()
		{{
			// Example: Simple movement
			var velocity = Vector2.Zero;
			
			if (Input.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.W))
				velocity.Y -= 1;
			if (Input.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.S))
				velocity.Y += 1;
			if (Input.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.A))
				velocity.X -= 1;
			if (Input.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.D))
				velocity.X += 1;
			
			if (velocity != Vector2.Zero)
			{{
				velocity.Normalize();
				Entity.Transform.Position += velocity * Speed * Time.DeltaTime;
			}}
		}}
	}}
}}
";

			File.WriteAllText(exampleScriptPath, exampleScriptContent);
		}

		private static void CreateGitIgnoreFile(string projectPath)
		{
			var gitignorePath = Path.Combine(projectPath, ".gitignore");

			var gitignoreContent = @"## Ignore Visual Studio temporary files, build results, and
## files generated by popular Visual Studio add-ons.

# User-specific files
*.suo
*.user
*.userosscache
*.sln.docstates

# Build results
[Dd]ebug/
[Dd]ebugPublic/
[Rr]elease/
[Rr]eleases/
x64/
x86/
bld/
[Bb]in/
[Oo]bj/

# Visual Studio cache/options directory
.vs/

# MSTest test Results
[Tt]est[Rr]esult*/
[Bb]uild[Ll]og.*

# NuGet Packages
*.nupkg
**/packages/*

# MonoGame
Content/bin/
Content/obj/

# Rider
.idea/

# OS generated files
.DS_Store
Thumbs.db
";

			File.WriteAllText(gitignorePath, gitignoreContent);
			Debug.Log($"Created .gitignore: {gitignorePath}");
		}

		private static string FindVoltageEditorPath()
		{
			var currentDir = AppContext.BaseDirectory;
			var di = new DirectoryInfo(currentDir);

			while (di != null)
			{
				var editorCsproj = Path.Combine(di.FullName, "Voltage.Editor", "Voltage.Editor.csproj");
				if (File.Exists(editorCsproj))
					return Path.GetDirectoryName(editorCsproj);
				
				di = di.Parent;
			}

			return Path.Combine(currentDir, "..", "Voltage.Editor");
		}

		private static string FindVoltageEnginePath()
		{
			var currentDir = AppContext.BaseDirectory;
			var di = new DirectoryInfo(currentDir);

			while (di != null)
			{
				var engineCsproj = Path.Combine(di.FullName, "Voltage.Engine", "Voltage.Engine.csproj");
				if (File.Exists(engineCsproj))
					return Path.GetDirectoryName(engineCsproj);
				
				di = di.Parent;
			}

			return Path.Combine(currentDir, "..", "Voltage.Engine");
		}

		private static string FindVoltagePersistencePath()
		{
			var currentDir = AppContext.BaseDirectory;
			var di = new DirectoryInfo(currentDir);

			EditorProcessDebugger.LogInfo($"Searching for Voltage.Persistence from: {currentDir}", "ProjectStructure");

			while (di != null)
			{
				var persistenceCsproj = Path.Combine(di.FullName, "Voltage.Persistence", "Voltage.Persistence.csproj");
				if (File.Exists(persistenceCsproj))
				{
					var foundPath = Path.GetDirectoryName(persistenceCsproj);
					EditorProcessDebugger.LogInfo($"Found Voltage.Persistence at: {foundPath}", "ProjectStructure");
					return foundPath;
				}
				
				di = di.Parent;
			}

			// Fallback: assume standard structure
			var fallbackPath = Path.Combine(currentDir, "..", "Voltage.Persistence");
			EditorProcessDebugger.LogWarning($"Voltage.Persistence not found, using fallback: {fallbackPath}", "ProjectStructure");
			return fallbackPath;
		}

		private static string GetRelativePath(string fromPath, string toPath)
		{
			try
			{
				var fromUri = new Uri(fromPath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar);
				var toUri = new Uri(toPath);
				var relativeUri = fromUri.MakeRelativeUri(toUri);
				var relativePath = Uri.UnescapeDataString(relativeUri.ToString());
				return relativePath.Replace('/', Path.DirectorySeparatorChar);
			}
			catch
			{
				// If relative path fails, return absolute path
				return toPath;
			}
		}
	}
}