using System;
using System.IO;
using Voltage.Editor.DebugUtils;
using Voltage.Editor.Scripting;
using Voltage.Editor.Scripting;

namespace Voltage.Editor.ProjectFile
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
			try
			{
				// Create directory structure
				EditorDebug.Log("Creating directory structure...", "ProjectStructure");
				Directory.CreateDirectory(projectPath);
				
				var scriptsFolder = Path.Combine(projectPath, "Scripts");
				var effectsFolder = Path.Combine(projectPath, "Effects");
				var contentFolder = Path.Combine(projectPath, "Content");
				var propertiesFolder = Path.Combine(projectPath, "Properties");
				
				Directory.CreateDirectory(scriptsFolder);
				Directory.CreateDirectory(effectsFolder);
				Directory.CreateDirectory(contentFolder);
				Directory.CreateDirectory(propertiesFolder);

				// Sync engine DLLs into the new project before generating files that reference them
				EngineLibsSync.SyncToProject(projectPath);

				// Create .csproj file
				CreateProjectFile(projectName, projectPath, version);
				
				// Create .sln file
				CreateSolutionFile(projectName, projectPath);
				
				// Create Program.cs (entry point)
				CreateProgramFile(projectName, projectPath);
				
				// Create AssemblyInfo.cs
				CreateAssemblyInfoFile(projectName, propertiesFolder, version);
				
				// Create example script
				CreateExampleScript(scriptsFolder, projectName);
				
				// Create .gitignore
				CreateGitIgnoreFile(projectPath);
				
				EditorDebug.Log($"Successfully created project structure at: {projectPath}", "ProjectStructure");
				return true;
			}
			catch (Exception ex)
			{
				EditorDebug.Error($"Failed to create project structure: {ex.Message}", "ProjectStructure");
				EditorDebug.Error($"Stack trace: {ex.StackTrace}", "ProjectStructure");
				return false;
			}
		}

		private static void CreateProjectFile(string projectName, string projectPath, Version version)
		{
			var projectFilePath = Path.Combine(projectPath, $"{projectName}.csproj");
			EditorDebug.Log($"Creating project file: {projectFilePath}", "ProjectStructure");

			// Build DLL references pointing to the local EngineLibs folder.
			// This makes the game project fully self-contained — no dependency on the
			// engine source tree path, so the project works anywhere it's opened.
			var engineLibsPath = EngineLibsSync.GetEngineLibsPath(projectPath);
			var engineLibsRelative = GetRelativePath(projectPath, engineLibsPath);

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

   <!--
   EngineLibs: local copies of the Voltage engine DLLs, auto-synced by the Voltage Editor
   on every project load. Do NOT commit this folder (it is .gitignored).
   If these DLLs are missing, open the project in the Voltage Editor first to regenerate them.
 -->
 <ItemGroup>
   <!-- Runtime engine references. The source generator DLL is excluded here
        because it must be loaded as an Analyzer, not a runtime assembly. -->
   <Reference Include=""$(MSBuildThisFileDirectory)EngineLibs\*.dll""
              Exclude=""$(MSBuildThisFileDirectory)EngineLibs\Voltage.SourceGenerators.dll"" />
 </ItemGroup>

 <!--
   Voltage.SourceGenerators: Roslyn incremental source generator that auto-generates a
   ComponentData subclass and Data property override for every partial Component subclass.
   Produces AOT-safe, zero-reflection serialization code at compile time.
   Must be declared as an Analyzer item, not a Reference.
 -->
 <ItemGroup>
   <Analyzer Include=""$(MSBuildThisFileDirectory)EngineLibs\Voltage.SourceGenerators.dll"" />
 </ItemGroup>

  <ItemGroup>
    <Content Include=""Content\**\*.*"">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </Content>
  </ItemGroup>

</Project>";

			File.WriteAllText(projectFilePath, csprojContent);
			EditorDebug.Log($"Project file created successfully", "ProjectStructure");
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

			var programContent = $@"using System;
using Voltage;

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
				""ProjectSettings.json""
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
	public class MainScene : Scene
	{{
		public override void OnStart()
		{{
			base.OnStart();
			Debug.Log(""MainScene started!"");
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

			var exampleScriptContent = $@"using Microsoft.Xna.Framework;
using Voltage;
using Voltage.Utils;

namespace {projectName}.Scripts
{{
	/// <summary>
	/// Example component that can be attached to entities
	/// </summary>
	public partial class ExampleComponent : Component, IUpdatable
	{{
		public float Speed = 100f;

		public override void OnStart()
		{{
			base.OnStart();
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

# Voltage Engine - auto-synced engine DLLs (regenerated by the Voltage Editor on project load)
EngineLibs/

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

			EditorDebug.Log($"Searching for Voltage.Persistence from: {currentDir}", "ProjectStructure");

			while (di != null)
			{
				var persistenceCsproj = Path.Combine(di.FullName, "Voltage.Persistence", "Voltage.Persistence.csproj");
				if (File.Exists(persistenceCsproj))
				{
					var foundPath = Path.GetDirectoryName(persistenceCsproj);
					EditorDebug.Log($"Found Voltage.Persistence at: {foundPath}", "ProjectStructure");
					return foundPath;
				}

				di = di.Parent;
			}

			// Fallback: assume standard structure
			var fallbackPath = Path.Combine(currentDir, "..", "Voltage.Persistence");
			EditorDebug.Warn($"Voltage.Persistence not found, using fallback: {fallbackPath}", "ProjectStructure");
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