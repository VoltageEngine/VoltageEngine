using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Voltage.Persistence;

namespace Voltage.Editor.Plugins
{
	/// <summary>
	/// Scaffolds a brand-new plugin package on disk from a few user inputs (Plugin Manager → "Create New
	/// Plugin"). Writes a valid <c>plugin.json</c> plus starter code for the chosen kinds:
	/// a source-form <c>Component</c> under <c>src/</c> for gameplay, and a ready-to-build editor project
	/// with a sample <see cref="IEditorPlugin"/> under <c>editor-src/</c> for editor plugins.
	/// The result is immediately installable as a local-folder plugin.
	/// </summary>
	public static class PluginScaffolder
	{
		/// <summary>User-supplied options for a new plugin. Validated by <see cref="Create"/>.</summary>
		public class Options
		{
			/// <summary>Parent folder the new plugin folder is created inside.</summary>
			public string Location;

			public string Name = "My Plugin";
			public string Id = "com.example.myplugin";
			public string Version = "1.0.0";
			public string Description = "";
			public string Author = "";

			public bool Gameplay = true;
			public bool Editor;

			/// <summary>
			/// Emit the files a standalone repository needs — .gitignore, a release workflow, and a PackagePlugin target.
			/// </summary>
			public bool RepositoryFiles = true;

			/// <summary>
			/// Absolute path to a Voltage build the generated projects reference.
			/// </summary>
			public string EngineLibsPath;
		}

		/// <summary>Outcome of a scaffold: the created plugin root folder, or a user-facing error.</summary>
		public class Result
		{
			public bool Success;
			public string Message;
			public string PluginRoot;
		}

		public static Result Create(Options opt)
		{
			if (opt == null)
				return Fail("No options provided.");
			if (string.IsNullOrWhiteSpace(opt.Location) || !Directory.Exists(opt.Location))
				return Fail("Pick an existing location folder for the new plugin.");
			if (string.IsNullOrWhiteSpace(opt.Name))
				return Fail("Enter a plugin name.");
			if (string.IsNullOrWhiteSpace(opt.Id))
				return Fail("Enter a plugin id (e.g. com.you.myplugin).");
			if (opt.Id.Any(char.IsWhiteSpace) || opt.Id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
				return Fail("The plugin id cannot contain spaces or characters invalid in a folder name.");
			if (!opt.Gameplay && !opt.Editor)
				return Fail("Choose at least one plugin kind (Gameplay and/or Editor).");
			if (string.IsNullOrWhiteSpace(opt.Version))
				opt.Version = "1.0.0";

			var folderName = SanitizeFolderName(opt.Name);
			var pluginRoot = Path.Combine(opt.Location, folderName);

			if (Directory.Exists(pluginRoot) && Directory.EnumerateFileSystemEntries(pluginRoot).Any())
				return Fail($"A non-empty folder already exists at:\n{pluginRoot}");

			try
			{
				Directory.CreateDirectory(pluginRoot);

				WriteManifest(opt, pluginRoot);

				if (opt.Gameplay)
					WriteGameplayStarter(opt, pluginRoot);
				if (opt.Editor)
					WriteEditorStarter(opt, pluginRoot);

				if (opt.RepositoryFiles)
				{
					WriteGitIgnore(pluginRoot);
					WritePackagingProject(opt, pluginRoot);
					WriteReleaseWorkflow(opt, pluginRoot);
				}

				WriteReadme(opt, pluginRoot);

				return new Result
				{
					Success = true,
					PluginRoot = pluginRoot,
					Message = $"Created plugin '{opt.Id}' at {pluginRoot}.",
				};
			}
			catch (Exception ex)
			{
				return Fail($"Failed to create plugin: {ex.Message}");
			}
		}

		private static void WriteManifest(Options opt, string pluginRoot)
		{
			var kinds = new List<string>();
			if (opt.Gameplay) kinds.Add(PluginManifest.KindGameplay);
			if (opt.Editor) kinds.Add(PluginManifest.KindEditor);

			var manifest = new PluginManifest
			{
				SchemaVersion = 1,
				Id = opt.Id.Trim(),
				Name = opt.Name.Trim(),
				Version = opt.Version.Trim(),
				Description = opt.Description?.Trim() ?? "",
				Author = opt.Author?.Trim() ?? "",
				Kinds = kinds,
				EngineVersion = ">=" + VoltageVersion.Engine,
			};

			if (opt.Gameplay)
			{
				manifest.Gameplay = new PluginGameplaySection
				{
					SourceRoots = new List<string> { "src" },
					RootTypes = new List<string> { $"{NamespaceOf(opt)}.{TypeNameOf(opt)}" },
				};
			}

			if (opt.Editor)
			{
				manifest.Editor = new PluginEditorSection
				{
					// Points at where the editor project's build output should land (see editor-src/README).
					Assemblies = new List<string> { $"editor/{NamespaceOf(opt)}.Editor.dll" },
				};
				manifest.EditorPluginApiVersion = EditorPluginApi.Version;
			}

			File.WriteAllText(Path.Combine(pluginRoot, PluginManifest.FileName), Json.ToJson(manifest, prettyPrint: true));
		}

		private static void WriteGameplayStarter(Options opt, string pluginRoot)
		{
			var ns = NamespaceOf(opt);
			var type = TypeNameOf(opt);
			var componentId = SanitizeId(opt.Id) + ".example";

			var srcDir = Path.Combine(pluginRoot, "src");
			Directory.CreateDirectory(srcDir);

			var sb = new StringBuilder();
			sb.AppendLine("using Voltage;");
			sb.AppendLine("using Voltage.Serialization;");
			sb.AppendLine();
			sb.AppendLine($"namespace {ns}");
			sb.AppendLine("{");
			sb.AppendLine($"\t/// <summary>{EscapeXml(FirstLine(opt.Description, opt.Name + " component."))}</summary>");
			sb.AppendLine("\t// The class is `partial` so the ComponentData source generator emits AOT-safe");
			sb.AppendLine("\t// serialization for it. The [ComponentId] is its stable on-disk identity — keep it");
			sb.AppendLine("\t// even if you rename the class, so saved scenes keep resolving this component.");
			sb.AppendLine($"\t[ComponentId(\"{componentId}\")]");
			sb.AppendLine($"\tpublic partial class {type} : Component");
			sb.AppendLine("\t{");
			sb.AppendLine("\t\t/// <summary>An example serialized field — replace with your own.</summary>");
			sb.AppendLine("\t\tpublic float ExampleValue = 1f;");
			sb.AppendLine("\t}");
			sb.AppendLine("}");

			File.WriteAllText(Path.Combine(srcDir, type + ".cs"), sb.ToString());
		}

		private static void WriteEditorStarter(Options opt, string pluginRoot)
		{
			var ns = NamespaceOf(opt);
			var editorSrc = Path.Combine(pluginRoot, "editor-src");
			Directory.CreateDirectory(editorSrc);

			var plugin = new StringBuilder();
			plugin.AppendLine("using ImGuiNET;");
			plugin.AppendLine("using Voltage.Editor.Plugins;");
			plugin.AppendLine();
			plugin.AppendLine($"namespace {ns}.Editor");
			plugin.AppendLine("{");
			plugin.AppendLine($"\t/// <summary>{EscapeXml(FirstLine(opt.Description, opt.Name + " editor tools."))}</summary>");
			plugin.AppendLine($"\tpublic class {ns}Plugin : IEditorPlugin");
			plugin.AppendLine("\t{");
			plugin.AppendLine("\t\tprivate class ToolWindow : EditorPluginWindow");
			plugin.AppendLine("\t\t{");
			plugin.AppendLine("\t\t\tpublic override void Draw()");
			plugin.AppendLine("\t\t\t{");
			plugin.AppendLine("\t\t\t\tif (ImGui.Begin(Title, ref IsOpen))");
			plugin.AppendLine($"\t\t\t\t\tImGui.Text(\"Hello from {EscapeCs(opt.Name)}!\");");
			plugin.AppendLine("\t\t\t\tImGui.End();");
			plugin.AppendLine("\t\t\t}");
			plugin.AppendLine("\t\t}");
			plugin.AppendLine();
			plugin.AppendLine("\t\tpublic void Initialize(IEditorPluginContext context)");
			plugin.AppendLine("\t\t{");
			plugin.AppendLine($"\t\t\tvar window = new ToolWindow {{ Title = \"{EscapeCs(opt.Name)}\" }};");
			plugin.AppendLine("\t\t\tcontext.RegisterWindow(window);");
			plugin.AppendLine($"\t\t\tcontext.AddMenuItem(\"{EscapeCs(opt.Name)}/Open\", () => window.IsOpen = true);");
			plugin.AppendLine("\t\t}");
			plugin.AppendLine();
			plugin.AppendLine("\t\tpublic void Shutdown() { }");
			plugin.AppendLine("\t}");
			plugin.AppendLine("}");

			File.WriteAllText(Path.Combine(editorSrc, ns + "Plugin.cs"), plugin.ToString());

			var csproj = new StringBuilder();
			csproj.AppendLine("<Project Sdk=\"Microsoft.NET.Sdk\">");
			csproj.AppendLine("  <PropertyGroup>");
			csproj.AppendLine("    <TargetFramework>net8.0</TargetFramework>");
			csproj.AppendLine($"    <AssemblyName>{ns}.Editor</AssemblyName>");
			csproj.AppendLine("    <Nullable>disable</Nullable>");
			csproj.AppendLine("    <!-- Build output goes to ../editor so plugin.json's Editor.Assemblies resolves. -->");
			csproj.AppendLine("    <OutputPath>..\\editor\\</OutputPath>");
			csproj.AppendLine("    <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>");
			csproj.AppendLine("  </PropertyGroup>");
			AppendEnginePathBlock(csproj, opt, "  ");
			csproj.AppendLine("  <ItemGroup>");
			foreach (var dll in new[] { "Voltage.Editor", "Voltage", "Voltage.Persistence", "ImGui.NET" })
			{
				csproj.AppendLine($"    <Reference Include=\"{dll}\">");
				csproj.AppendLine($"      <HintPath>$(VoltageEnginePath)\\{dll}.dll</HintPath>");
				csproj.AppendLine("      <Private>false</Private>");
				csproj.AppendLine("    </Reference>");
			}
			csproj.AppendLine("  </ItemGroup>");
			csproj.AppendLine("</Project>");

			File.WriteAllText(Path.Combine(editorSrc, ns + ".Editor.csproj"), csproj.ToString());
		}

		/// <summary>
		/// Emits the VoltageEnginePath property block shared by every generated project.
		/// </summary>
		private static void AppendEnginePathBlock(StringBuilder sb, Options opt, string indent)
		{
			var fallback = string.IsNullOrWhiteSpace(opt.EngineLibsPath)
				? AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar)
				: opt.EngineLibsPath.TrimEnd(Path.DirectorySeparatorChar);

			sb.AppendLine($"{indent}<!-- Where to find the Voltage assemblies. Override with -p:VoltageEnginePath=<dir>");
			sb.AppendLine($"{indent}     or the VOLTAGE_ENGINE_PATH environment variable; the default below points at the");
			sb.AppendLine($"{indent}     editor that generated this project, so it builds as-is on this machine. -->");
			sb.AppendLine($"{indent}<PropertyGroup>");
			sb.AppendLine($"{indent}  <VoltageEnginePath Condition=\"'$(VoltageEnginePath)' == '' And '$(VOLTAGE_ENGINE_PATH)' != ''\">$(VOLTAGE_ENGINE_PATH)</VoltageEnginePath>");
			sb.AppendLine($"{indent}  <VoltageEnginePath Condition=\"'$(VoltageEnginePath)' == ''\">{EscapeXml(fallback)}</VoltageEnginePath>");
			sb.AppendLine($"{indent}</PropertyGroup>");
			sb.AppendLine($"{indent}<Target Name=\"VerifyVoltageEnginePath\" BeforeTargets=\"ResolveAssemblyReferences\">");
			sb.AppendLine($"{indent}  <Error Condition=\"!Exists('$(VoltageEnginePath)\\Voltage.dll')\" Text=\"Voltage.dll not found at '$(VoltageEnginePath)'. Set VoltageEnginePath or VOLTAGE_ENGINE_PATH to a Voltage build.\" />");
			sb.AppendLine($"{indent}</Target>");
		}

		private static void WriteGitIgnore(string pluginRoot)
		{
			var sb = new StringBuilder();
			sb.AppendLine("# Build output");
			sb.AppendLine("bin/");
			sb.AppendLine("obj/");
			sb.AppendLine();
			sb.AppendLine("# Staged plugin package - release artifacts produced by `-t:PackagePlugin`.");
			sb.AppendLine("# PluginResolver reads this layout from a release zip, not from the repository,");
			sb.AppendLine("# so committing built DLLs would only add binary churn to git history.");
			sb.AppendLine("lib/");
			sb.AppendLine("editor/");
			sb.AppendLine();
			sb.AppendLine("# IDE / OS");
			sb.AppendLine(".vs/");
			sb.AppendLine(".idea/");
			sb.AppendLine("*.user");
			sb.AppendLine(".DS_Store");

			File.WriteAllText(Path.Combine(pluginRoot, ".gitignore"), sb.ToString());
		}

		/// <summary>
		/// Emits a tiny MSBuild project whose only job is <c>PackagePlugin</c>: stage the manifest and any built assemblies into the layout <c>PluginResolver</c> expects, ready to zip into a release.
		/// </summary>
		private static void WritePackagingProject(Options opt, string pluginRoot)
		{
			var ns = NamespaceOf(opt);
			var sb = new StringBuilder();

			sb.AppendLine("<Project Sdk=\"Microsoft.NET.Sdk\">");
			sb.AppendLine();
			sb.AppendLine("  <!--");
			sb.AppendLine("    Packaging-only project: it compiles nothing itself. `PackagePlugin` produces the");
			sb.AppendLine("    plugin.json + assemblies layout PluginResolver reads. Zip the staged files and attach");
			sb.AppendLine("    the archive to a release; the workflow in .github/workflows does this for a v* tag.");
			sb.AppendLine();
			sb.AppendLine("        dotnet msbuild package.proj -t:PackagePlugin");
			sb.AppendLine("  -->");
			sb.AppendLine();
			sb.AppendLine("  <PropertyGroup>");
			sb.AppendLine("    <TargetFramework>net8.0</TargetFramework>");
			sb.AppendLine("    <NoBuild>true</NoBuild>");
			sb.AppendLine("    <IncludeBuildOutput>false</IncludeBuildOutput>");
			sb.AppendLine("  </PropertyGroup>");
			sb.AppendLine();
			sb.AppendLine("  <Target Name=\"PackagePlugin\">");

			if (opt.Editor)
			{
				sb.AppendLine("    <!-- The editor assembly is loaded by the editor, so it needs the EDITOR-flavoured build. -->");
				sb.AppendLine($"    <MSBuild Projects=\"editor-src/{ns}.Editor.csproj\" Targets=\"Build\" Properties=\"Configuration=Release\" />");
			}

			if (opt.Gameplay)
			{
				sb.AppendLine("    <!-- Gameplay ships as source (see plugin.json SourceRoots): it is compiled into the game");
				sb.AppendLine("         project, which is what lets NativeAOT see and trim it correctly. Nothing to build here. -->");
			}

			sb.AppendLine("    <Message Importance=\"high\" Text=\"Plugin package staged. Zip plugin.json and the src/editor folders into a release asset.\" />");
			sb.AppendLine("  </Target>");
			sb.AppendLine();
			sb.AppendLine("</Project>");

			File.WriteAllText(Path.Combine(pluginRoot, "package.proj"), sb.ToString());
		}

		private static void WriteReleaseWorkflow(Options opt, string pluginRoot)
		{
			var dir = Path.Combine(pluginRoot, ".github", "workflows");
			Directory.CreateDirectory(dir);

			var paths = new List<string> { "plugin.json" };
			if (opt.Gameplay) paths.Add("src");
			if (opt.Editor) paths.Add("editor");

			var sb = new StringBuilder();
			sb.AppendLine("name: Release");
			sb.AppendLine();
			sb.AppendLine("# Packages the plugin and attaches it to a tagged release.");
			sb.AppendLine("#");
			sb.AppendLine("# The engine is checked out and built first, because a plugin binds to a built engine rather");
			sb.AppendLine("# than a NuGet package. Pinning the engine ref keeps a release reproducible.");
			sb.AppendLine("on:");
			sb.AppendLine("  push:");
			sb.AppendLine("    tags: ['v*']");
			sb.AppendLine("  workflow_dispatch:");
			sb.AppendLine();
			sb.AppendLine("env:");
			sb.AppendLine("  ENGINE_REPO: VoltageEngine/VoltageEngine");
			sb.AppendLine("  ENGINE_REF: main");
			sb.AppendLine();
			sb.AppendLine("jobs:");
			sb.AppendLine("  package:");
			sb.AppendLine("    runs-on: ubuntu-latest");
			sb.AppendLine();
			sb.AppendLine("    # Org repositories default the workflow token to read-only, which packages fine and then");
			sb.AppendLine("    # 403s on release creation.");
			sb.AppendLine("    permissions:");
			sb.AppendLine("      contents: write");
			sb.AppendLine();
			sb.AppendLine("    steps:");
			sb.AppendLine("      - uses: actions/checkout@v4");
			sb.AppendLine("        with: { path: plugin }");
			sb.AppendLine("      - uses: actions/checkout@v4");
			sb.AppendLine("        with:");
			sb.AppendLine("          repository: ${{ env.ENGINE_REPO }}");
			sb.AppendLine("          ref: ${{ env.ENGINE_REF }}");
			sb.AppendLine("          path: VoltageEngine");
			sb.AppendLine("      - uses: actions/setup-dotnet@v4");
			sb.AppendLine("        with: { dotnet-version: '8.0.x' }");
			sb.AppendLine();
			sb.AppendLine("      - name: Build the engine");
			sb.AppendLine("        run: dotnet build VoltageEngine/Voltage.Editor/Voltage.Editor.csproj -c Editor-Release");
			sb.AppendLine();
			sb.AppendLine("      # PackagePlugin drives msbuild directly, which does not restore on its own.");
			sb.AppendLine("      - name: Restore the plugin");
			sb.AppendLine("        working-directory: plugin");
			sb.AppendLine("        run: dotnet restore package.proj");
			sb.AppendLine();
			sb.AppendLine("      - name: Stage the package");
			sb.AppendLine("        working-directory: plugin");
			sb.AppendLine("        run: dotnet msbuild package.proj -t:PackagePlugin -p:VoltageEnginePath=$GITHUB_WORKSPACE/VoltageEngine/Voltage.Editor/bin/Editor-Release/net8.0");
			sb.AppendLine();
			sb.AppendLine("      # The manifest version is what the editor displays and what dependency ranges resolve");
			sb.AppendLine("      # against, so a tag that disagrees with it would ship a mislabelled package.");
			sb.AppendLine("      - name: Check plugin.json version matches the tag");
			sb.AppendLine("        if: startsWith(github.ref, 'refs/tags/v')");
			sb.AppendLine("        working-directory: plugin");
			sb.AppendLine("        run: |");
			sb.AppendLine("          TAG=\"${GITHUB_REF_NAME#v}\"");
			sb.AppendLine("          MANIFEST=$(grep -o '\"Version\"[[:space:]]*:[[:space:]]*\"[^\"]*\"' plugin.json | head -1 | sed 's/.*\"\\([^\"]*\\)\"$/\\1/')");
			sb.AppendLine("          [ \"$TAG\" = \"$MANIFEST\" ] || { echo \"::error::tag $GITHUB_REF_NAME != plugin.json $MANIFEST\"; exit 1; }");
			sb.AppendLine();
			sb.AppendLine("      - name: Zip the package");
			sb.AppendLine("        working-directory: plugin");
			sb.AppendLine($"        run: zip -r \"{opt.Id.Trim()}-${{GITHUB_REF_NAME#v}}.zip\" {string.Join(" ", paths)}");
			sb.AppendLine();
			sb.AppendLine("      - name: Attach to the release");
			sb.AppendLine("        if: startsWith(github.ref, 'refs/tags/v')");
			sb.AppendLine("        uses: softprops/action-gh-release@v2");
			sb.AppendLine("        with: { files: plugin/*.zip }");

			File.WriteAllText(Path.Combine(dir, "release.yml"), sb.ToString());
		}

		private static void WriteReadme(Options opt, string pluginRoot)
		{
			var sb = new StringBuilder();
			sb.AppendLine($"# {opt.Name}");
			sb.AppendLine();
			if (!string.IsNullOrWhiteSpace(opt.Description))
			{
				sb.AppendLine(opt.Description.Trim());
				sb.AppendLine();
			}
			sb.AppendLine("Voltage plugin scaffold. The editor reads `plugin.json`; everything else is declared there.");
			sb.AppendLine();
			if (opt.Gameplay)
			{
				sb.AppendLine("## Gameplay");
				sb.AppendLine("- `src/` — C# that compiles together with the game. Edit the starter component and add more.");
				sb.AppendLine("- Add this folder in the editor (Plugin Manager → Add Plugin → Local folder) with");
				sb.AppendLine("  \"I'm editing this plugin\" ticked to hot-reload your changes while you work.");
				sb.AppendLine();
			}
			if (opt.Editor)
			{
				sb.AppendLine("## Editor tools");
				sb.AppendLine("- `editor-src/` — a C# project for your editor window. Point its Reference HintPaths at");
				sb.AppendLine("  your Voltage editor build, then `dotnet build` it; the DLL lands in `editor/` where");
				sb.AppendLine("  `plugin.json` expects it. Reopen the project (or restart the editor) to load it.");
				sb.AppendLine();
			}
			File.WriteAllText(Path.Combine(pluginRoot, "README.md"), sb.ToString());
		}

		#region Naming helpers

		/// <summary>Suggests a reverse-domain id from a display name, e.g. "My Cool Plugin" → "com.example.mycoolplugin".</summary>
		public static string SuggestId(string name)
		{
			var slug = SanitizeId(name);
			return string.IsNullOrEmpty(slug) ? "com.example.myplugin" : $"com.example.{slug}";
		}

		private static string SanitizeId(string s)
		{
			if (string.IsNullOrWhiteSpace(s))
				return "";
			var chars = s.ToLowerInvariant().Where(c => char.IsLetterOrDigit(c)).ToArray();
			return new string(chars);
		}

		private static string SanitizeFolderName(string name)
		{
			var invalid = Path.GetInvalidFileNameChars();
			var cleaned = new string(name.Trim().Select(c => invalid.Contains(c) ? '_' : c).ToArray());
			return string.IsNullOrWhiteSpace(cleaned) ? "NewPlugin" : cleaned;
		}

		/// <summary>PascalCase C# namespace/type stem from the name (letters/digits only), e.g. "My Plugin" → "MyPlugin".</summary>
		private static string NamespaceOf(Options opt)
		{
			var parts = (opt.Name ?? "")
				.Split(new[] { ' ', '-', '_', '.' }, StringSplitOptions.RemoveEmptyEntries)
				.Select(p => new string(p.Where(char.IsLetterOrDigit).ToArray()))
				.Where(p => p.Length > 0)
				.Select(p => char.ToUpperInvariant(p[0]) + p.Substring(1));

			var ns = string.Concat(parts);
			if (string.IsNullOrEmpty(ns) || !char.IsLetter(ns[0]))
				ns = "MyPlugin";
			return ns;
		}

		private static string TypeNameOf(Options opt) => NamespaceOf(opt) + "Component";

		private static string FirstLine(string text, string fallback)
		{
			if (string.IsNullOrWhiteSpace(text))
				return fallback;
			var line = text.Trim().Split('\n')[0].Trim();
			return line.Length == 0 ? fallback : line;
		}

		private static string EscapeXml(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
		private static string EscapeCs(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

		private static Result Fail(string message) => new() { Success = false, Message = message };

		#endregion
	}
}
