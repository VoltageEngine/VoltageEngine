using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Voltage.Editor.DebugUtils;
using Voltage.Editor.Scripting;

namespace Voltage.Editor.ProjectFile
{
	/// <summary>
	/// Generates Visual Studio solution and project files for game projects
	/// </summary>
	public static class ProjectStructureGenerator
	{
		/// <summary>
		/// Returns the MonoGame.Framework.DesktopGL package version currently used by the editor.
		/// Resolved at runtime from the loaded MonoGame assembly so game projects always match.
		/// Falls back to a sensible default if detection fails.
		/// </summary>
		private static string GetMonoGameVersion()
		{
			try
			{
				// The MonoGame assembly is already loaded — grab its version from metadata.
				var monoGameAssembly = AppDomain.CurrentDomain.GetAssemblies()
					.FirstOrDefault(a => a.GetName().Name == "MonoGame.Framework");

				if (monoGameAssembly != null)
				{
					// Try InformationalVersion first — NuGet packages set this to the full semver string
					var infoAttr = monoGameAssembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
					if (infoAttr != null && !string.IsNullOrWhiteSpace(infoAttr.InformationalVersion))
					{
						// Strip any metadata suffix (e.g. "+abc123") that NuGet won't accept
						var version = infoAttr.InformationalVersion;
						var plusIndex = version.IndexOf('+');
						if (plusIndex >= 0)
							version = version[..plusIndex];

						EditorDebug.Log($"Detected MonoGame version from InformationalVersion: {version}", "ProjectStructure");
						return version;
					}

					// Fall back to assembly version
					var assemblyVersion = monoGameAssembly.GetName().Version;
					if (assemblyVersion != null)
					{
						var version = $"{assemblyVersion.Major}.{assemblyVersion.Minor}.{assemblyVersion.Build}";
						EditorDebug.Log($"Detected MonoGame version from AssemblyVersion: {version}", "ProjectStructure");
						return version;
					}
				}
			}
			catch (Exception ex)
			{
				EditorDebug.Warn($"Could not detect MonoGame version: {ex.Message}", "ProjectStructure");
			}

			// Fallback — keep in sync with Voltage.Editor.csproj manually if this is ever reached
			const string fallback = "3.8.5-preview.1";
			EditorDebug.Warn($"Using fallback MonoGame version: {fallback}", "ProjectStructure");
			return fallback;
		}

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

				// Create a settings folder for ProjectSettings.json
				var settingsFolder = Path.Combine(projectPath, "Settings");
				Directory.CreateDirectory(settingsFolder);
				var settingsPath = Path.Combine(settingsFolder, "ProjectSettings.json");
				if (!File.Exists(settingsPath))
				{
					// Create a default settings file if none exists
					File.WriteAllText(settingsPath, "{\n\t\"WindowWidth\": 1280,\n\t\"WindowHeight\": 720\n}");
					EditorDebug.Log("Created default ProjectSettings.json", "ProjectStructure");
				}

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

				// Copy the editor's Icon.ico to the new project
				CopyEditorIcon(projectPath);

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

			var monoGameVersion = GetMonoGameVersion();

			// Build explicit <Reference> items for each managed engine DLL.
			// We NEVER glob EngineLibs\*.dll because that picks up native binaries
			// (SDL2, clretwrc, System.IO.Compression.Native, etc.) which breaks MSBuild
			// reference resolution and causes "System.Void is not defined" errors.
			var referenceItems = new System.Text.StringBuilder();
			foreach (var dllName in EngineLibsSync.ManagedReferenceDlls)
			{
				var assemblyName = Path.GetFileNameWithoutExtension(dllName);
				referenceItems.AppendLine($"    <Reference Include=\"{assemblyName}\">");
				referenceItems.AppendLine($"      <HintPath>$(MSBuildThisFileDirectory)EngineLibs\\{dllName}</HintPath>");
				referenceItems.AppendLine($"      <Private>false</Private>");
				referenceItems.AppendLine($"    </Reference>");
			}

			var csprojContent = $@"<Project Sdk=""Microsoft.NET.Sdk"">

  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <LangVersion>12.0</LangVersion>
    <Nullable>disable</Nullable>
    <AssemblyName>{projectName}</AssemblyName>
    <RootNamespace>{projectName}</RootNamespace>
    <Version>{version}</Version>
    <StartupObject>{projectName}.Program</StartupObject>

    <!-- Disable auto-generated assembly info to avoid conflicts with Properties/AssemblyInfo.cs -->
    <GenerateAssemblyInfo>false</GenerateAssemblyInfo>
  </PropertyGroup>

  <!-- Only set the application icon if Icon.ico exists in the project root -->
  <PropertyGroup Condition=""Exists('Icon.ico')"">
    <ApplicationIcon>Icon.ico</ApplicationIcon>
  </PropertyGroup>

  <PropertyGroup Condition=""'$(Configuration)|$(Platform)'=='Debug|AnyCPU'"">
    <DefineConstants>TRACE;DEBUG;OS_WINDOWS</DefineConstants>
  </PropertyGroup>

  <PropertyGroup Condition=""'$(Configuration)|$(Platform)'=='Release|AnyCPU'"">
    <DefineConstants>TRACE;OS_WINDOWS</DefineConstants>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include=""MonoGame.Framework.DesktopGL"" Version=""{monoGameVersion}"" />
    <PackageReference Include=""MonoGame.Content.Builder.Task"" Version=""{monoGameVersion}"" />
  </ItemGroup>

  <!--
    EngineLibs: local copies of the Voltage engine DLLs, auto-synced by the Voltage Editor
    on every project load. Do NOT commit this folder (it is .gitignored).
    If these DLLs are missing, open the project in the Voltage Editor first to regenerate them.

    IMPORTANT: Only managed DLLs are listed here. Never add native binaries (SDL2, OpenAL,
    clretwrc, etc.) — they are resolved automatically by the MonoGame NuGet package.
  -->
  <ItemGroup>
{referenceItems}  </ItemGroup>

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
			EditorDebug.Log($"Project file created with MonoGame version: {monoGameVersion}", "ProjectStructure");
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
using Voltage.Project;

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

			// Load the initial scene configured in ProjectSettings.json
			var settings = ProjectSettings.Instance;
			if (settings != null && !string.IsNullOrEmpty(settings.InitialScene))
			{{
				var scenePath = System.IO.Path.Combine(
					AppContext.BaseDirectory,
					""Data"", ""Scenes"",
					settings.InitialScene + "".vscene""
				);

				if (System.IO.File.Exists(scenePath))
				{{
					var loadedScene = Scene.LoadFromFile(scenePath);
					if (loadedScene != null)
					{{
						Scene = loadedScene;
						return;
					}}
				}}

				Debug.Warn($""Could not load initial scene '{{settings.InitialScene}}' from: {{scenePath}}"");
			}}

			// Fallback: empty scene
			Scene = new Scene();
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

		/// <summary>
		/// Copies the Voltage Editor's Icon.ico to the game project directory so it can be
		/// used as the application icon for the built executable.
		/// </summary>
		private static void CopyEditorIcon(string projectPath)
		{
			try
			{
				// The Icon.ico is an embedded resource but also exists next to the editor executable
				// or in the editor project directory. Try both locations.
				var editorDir = Path.GetDirectoryName(typeof(ProjectStructureGenerator).Assembly.Location);
				var iconSource = editorDir != null ? Path.Combine(editorDir, "Icon.ico") : null;

				// If not found next to the running assembly, search up from BaseDirectory
				// for the Voltage.Editor project folder (works during development)
				if (iconSource == null || !File.Exists(iconSource))
				{
					var di = new DirectoryInfo(AppContext.BaseDirectory);
					while (di != null)
					{
						var candidate = Path.Combine(di.FullName, "Voltage.Editor", "Icon.ico");
						if (File.Exists(candidate))
						{
							iconSource = candidate;
							break;
						}

						// Also check if we're already inside the Voltage.Editor directory
						candidate = Path.Combine(di.FullName, "Icon.ico");
						if (File.Exists(candidate) && File.Exists(Path.Combine(di.FullName, "Voltage.Editor.csproj")))
						{
							iconSource = candidate;
							break;
						}

						di = di.Parent;
					}
				}

				if (iconSource != null && File.Exists(iconSource))
				{
					var iconDest = Path.Combine(projectPath, "Icon.ico");
					File.Copy(iconSource, iconDest, overwrite: true);
					EditorDebug.Log("Copied Icon.ico to game project.", "ProjectStructure");
				}
				else
				{
					EditorDebug.Warn("Icon.ico not found in editor directory. Game project will build without an application icon.", "ProjectStructure");
				}
			}
			catch (Exception ex)
			{
				EditorDebug.Warn($"Could not copy Icon.ico: {ex.Message}", "ProjectStructure");
			}
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