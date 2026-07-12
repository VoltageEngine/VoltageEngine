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
			csproj.AppendLine("  <!-- Point these HintPaths at your Voltage editor build output. -->");
			csproj.AppendLine("  <ItemGroup>");
			csproj.AppendLine("    <Reference Include=\"Voltage.Editor\"><HintPath>PATH\\TO\\Voltage.Editor.dll</HintPath></Reference>");
			csproj.AppendLine("    <Reference Include=\"Voltage\"><HintPath>PATH\\TO\\Voltage.dll</HintPath></Reference>");
			csproj.AppendLine("    <Reference Include=\"ImGui.NET\"><HintPath>PATH\\TO\\ImGui.NET.dll</HintPath></Reference>");
			csproj.AppendLine("  </ItemGroup>");
			csproj.AppendLine("</Project>");

			File.WriteAllText(Path.Combine(editorSrc, ns + ".Editor.csproj"), csproj.ToString());
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
