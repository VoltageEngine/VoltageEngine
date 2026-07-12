using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Voltage.Editor.DebugUtils;
using Voltage.Editor.ProjectFile;
using Voltage.Utils;

namespace Voltage.Editor.Scripting
{
	/// <summary>
	/// Compiles C# script files at runtime
	/// </summary>
	public class ScriptCompiler
	{
		/// <summary>
		/// Compiles a collection of C# script files into an assembly
		/// </summary>
		public static CompilationResult Compile(IEnumerable<string> scriptPaths, string assemblyName = "DynamicScripts")
		{
			EditorDebug.Log($"Starting compilation of {scriptPaths.Count()} scripts", "ScriptCompilation");
			
			var syntaxTrees = new List<SyntaxTree>();
			var errors = new List<string>();

			// Parse all script files
			foreach (var path in scriptPaths)
			{
				if (!File.Exists(path))
				{
					errors.Add($"Script file not found: {path}");
					continue;
				}

				try
				{
					var code = File.ReadAllText(path);
					var syntaxTree = CSharpSyntaxTree.ParseText(code, path: path);
					syntaxTrees.Add(syntaxTree);
				}
				catch (Exception ex)
				{
					errors.Add($"Error parsing {path}: {ex.Message}");
				}
			}

			if (errors.Any())
			{
				return new CompilationResult
				{
					Success = false,
					Errors = errors,
					Assembly = null
				};
			}

			var references = GetMetadataReferences();

			var compilation = CSharpCompilation.Create(
				assemblyName,
				syntaxTrees: syntaxTrees,
				references: references,
				options: new CSharpCompilationOptions(
					OutputKind.DynamicallyLinkedLibrary,
					optimizationLevel: OptimizationLevel.Debug,
					allowUnsafe: true
				)
			);

			// Stamp a stable [ComponentId] identity onto any component/scene-component that
			// lacks one, so scenes can reference it by id instead of by (renameable) type
			// name. Re-parse the touched files so the generator sees the new attribute and
			// registers the id in its bootstrap. Cache-installed plugin sources are read-only:
			// stamping there is refused and reported as a compile error instead.
			var stampViolations = new List<string>();
			var stampedFiles = ComponentIdStamper.StampMissing(compilation,
				allowStamp: path => !Plugins.PluginManager.Instance.IsReadOnlyPluginSource(path),
				violations: stampViolations);
			if (stampViolations.Count > 0)
			{
				return new CompilationResult
				{
					Success = false,
					Errors = stampViolations,
					Assembly = null
				};
			}
			if (stampedFiles.Count > 0)
				compilation = ReparseChangedTrees(compilation, stampedFiles);

			// Run the Voltage.SourceGenerators source generator to produce ComponentData
			// overrides for partial Component subclasses. This mirrors what MSBuild does
			// during a normal project build via the <Analyzer> item.
			compilation = RunSourceGenerators(compilation, errors);
			if (errors.Any())
			{
				return new CompilationResult
				{
					Success = false,
					Errors = errors,
					Assembly = null
				};
			}

			// Emit to memory stream
			using var ms = new MemoryStream();
			EmitResult result = compilation.Emit(ms);

			if (!result.Success)
			{
				var compilationErrors = result.Diagnostics
					.Where(d => d.Severity == DiagnosticSeverity.Error)
					.Select(d => $"{d.Location.GetLineSpan().Path}({d.Location.GetLineSpan().StartLinePosition.Line + 1}): {d.GetMessage()}")
					.ToList();

				EditorDebug.Error($"Compilation failed with {compilationErrors.Count} errors", "ScriptCompilation");
				foreach (var error in compilationErrors)
				{
					EditorDebug.Error(error, "ScriptCompilation");
				}

				return new CompilationResult
				{
					Success = false,
					Errors = compilationErrors,
					Assembly = null
				};
			}

			// Load assembly from memory
			ms.Seek(0, SeekOrigin.Begin);
			var assembly = Assembly.Load(ms.ToArray());

			EditorDebug.Log("Compilation succeeded", "ScriptCompilation");
			
			return new CompilationResult
			{
				Success = true,
				Errors = new List<string>(),
				Assembly = assembly
			};
		}

		/// <summary>
		/// Get metadata references from currently loaded assemblies for compilation.
		/// Uses the live loaded assemblies from the AppDomain rather than globbing
		/// EngineLibs/*.dll, which would pick up native DLLs that Roslyn cannot read.
		/// </summary>
		private static List<MetadataReference> GetMetadataReferences()
		{
			var references = new List<MetadataReference>();
			var addedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

			// Add core .NET references
			var coreAssemblies = new[]
			{
				typeof(object).Assembly, // System.Private.CoreLib
				typeof(System.Console).Assembly, // System.Console
				typeof(System.Linq.Enumerable).Assembly, // System.Linq
				typeof(System.Collections.Generic.List<>).Assembly, // System.Collections
			};

			foreach (var assembly in coreAssemblies)
			{
				if (!string.IsNullOrEmpty(assembly.Location) && addedPaths.Add(assembly.Location))
				{
					references.Add(MetadataReference.CreateFromFile(assembly.Location));
				}
			}

			// Use the project's EngineLibs for Voltage and MonoGame references, but only
			// load KNOWN MANAGED DLLs by name  never glob *.dll which picks up native binaries.
			var projectPath = ProjectManager.Instance?.CurrentProject?.ProjectPath;
			bool usedEngineLibs = false;

			if (!string.IsNullOrEmpty(projectPath) && EngineLibsSync.IsReady(projectPath))
			{
				var engineLibsPath = EngineLibsSync.GetEngineLibsPath(projectPath);
				int added = 0;

				foreach (var dllName in EngineLibsSync.ManagedReferenceDlls)
				{
					var dllPath = Path.Combine(engineLibsPath, dllName);
					if (File.Exists(dllPath) && addedPaths.Add(dllPath))
					{
						references.Add(MetadataReference.CreateFromFile(dllPath));
						added++;
					}
				}

				usedEngineLibs = added > 0;

				if (usedEngineLibs)
					EditorDebug.Log($"Using {added} EngineLibs references from: {engineLibsPath}", "ScriptCompilation");
			}

			// Fallback: if EngineLibs is not available, use the live loaded assemblies
			if (!usedEngineLibs)
			{
				EditorDebug.Log("EngineLibs not available, falling back to loaded assembly locations.", "ScriptCompilation");

				// Add MonoGame/FNA references
				var monogameAssemblies = new[]
				{
					typeof(Microsoft.Xna.Framework.Vector2).Assembly,
					typeof(Microsoft.Xna.Framework.Graphics.Texture2D).Assembly,
				};

				foreach (var assembly in monogameAssemblies)
				{
					if (!string.IsNullOrEmpty(assembly.Location) && addedPaths.Add(assembly.Location))
					{
						references.Add(MetadataReference.CreateFromFile(assembly.Location));
					}
				}

				// Add Voltage references
				var voltageAssemblies = AppDomain.CurrentDomain.GetAssemblies()
					.Where(a => a.GetName().Name?.StartsWith("Voltage") == true)
					.ToArray();

				foreach (var assembly in voltageAssemblies)
				{
					if (!string.IsNullOrEmpty(assembly.Location) && addedPaths.Add(assembly.Location))
					{
						references.Add(MetadataReference.CreateFromFile(assembly.Location));
					}
				}
			}

			// Plugin managed assemblies — a parallel tier to EngineLibs so game scripts can use plugin
			// types (e.g. FMOD components). Explicit manifest-listed DLLs only; never glob PluginLibs,
			// which contains native binaries Roslyn cannot read.
			int pluginRefs = 0;
			foreach (var dllPath in Plugins.PluginManager.Instance.GetEditorReferenceAssemblyPaths())
			{
				if (File.Exists(dllPath) && addedPaths.Add(dllPath))
				{
					references.Add(MetadataReference.CreateFromFile(dllPath));
					pluginRefs++;
				}
			}

			if (pluginRefs > 0)
				EditorDebug.Log($"Added {pluginRefs} plugin assembly reference(s).", "ScriptCompilation");

			// Add runtime references
			var runtimePath = Path.GetDirectoryName(typeof(object).Assembly.Location);
			if (!string.IsNullOrEmpty(runtimePath))
			{
				var runtimeReferences = new[]
				{
					"System.Runtime.dll",
					"System.Collections.dll",
					"System.Linq.dll",
					"netstandard.dll"
				};

				foreach (var dllName in runtimeReferences)
				{
					var path = Path.Combine(runtimePath, dllName);
					if (File.Exists(path) && addedPaths.Add(path))
					{
						references.Add(MetadataReference.CreateFromFile(path));
					}
				}
			}

			return references;
		}

		/// <summary>
		/// Re-parses the given files (modified by <see cref="ComponentIdStamper"/>) and swaps the
		/// updated syntax trees into the compilation so downstream generation sees the stamped
		/// attributes.
		/// </summary>
		private static CSharpCompilation ReparseChangedTrees(
			CSharpCompilation compilation, IReadOnlyList<string> changedFiles)
		{
			var changedSet = new HashSet<string>(changedFiles, StringComparer.OrdinalIgnoreCase);
			foreach (var oldTree in compilation.SyntaxTrees.ToList())
			{
				if (string.IsNullOrEmpty(oldTree.FilePath) || !changedSet.Contains(oldTree.FilePath))
					continue;

				try
				{
					var code = File.ReadAllText(oldTree.FilePath);
					var newTree = CSharpSyntaxTree.ParseText(code, path: oldTree.FilePath);
					compilation = compilation.ReplaceSyntaxTree(oldTree, newTree);
				}
				catch (Exception ex)
				{
					EditorDebug.Error(
						$"Failed to re-parse stamped file {oldTree.FilePath}: {ex.Message}", "ScriptCompilation");
				}
			}
			return compilation;
		}

		/// <summary>
		/// Cached source generator instances loaded from the Voltage.SourceGenerators DLL.
		/// Loaded once on first compilation and reused for all subsequent compilations.
		/// </summary>
		private static IReadOnlyList<ISourceGenerator> _cachedGenerators;

		/// <summary>
		/// Runs the Voltage.SourceGenerators source generator against the compilation.
		/// </summary>
		private static CSharpCompilation RunSourceGenerators(CSharpCompilation compilation, List<string> errors)
		{
			var generators = GetSourceGenerators();
			if (generators == null || generators.Count == 0)
			{
				Debug.Error("[ScriptCompiler] No source generators available! " +
					"Partial Component subclasses will NOT have ComponentData generated. " +
					"Ensure Voltage.SourceGenerators.dll is present next to the editor executable.");
				return compilation;
			}

			try
			{
				var driver = CSharpGeneratorDriver.Create(generators);
				driver = (CSharpGeneratorDriver)driver.RunGeneratorsAndUpdateCompilation(
					compilation, out var updatedCompilation, out var generatorDiagnostics);

				foreach (var diag in generatorDiagnostics)
				{
					if (diag.Severity == DiagnosticSeverity.Error)
					{
						var msg = $"[SourceGenerator] {diag.GetMessage()}";
						errors.Add(msg);
						EditorDebug.Error(msg, "ScriptCompilation");
					}
					else if (diag.Severity == DiagnosticSeverity.Warning)
					{
						EditorDebug.Warn($"[SourceGenerator] {diag.GetMessage()}", "ScriptCompilation");
					}
				}

				var generatedTreeCount = updatedCompilation.SyntaxTrees.Count() - compilation.SyntaxTrees.Count();
				if (generatedTreeCount > 0)
					EditorDebug.Log($"Source generator produced {generatedTreeCount} additional file(s).", "ScriptCompilation");
				else
					EditorDebug.Warn("Source generator ran but produced 0 files. " +
						"Ensure script Component classes are marked 'partial'.", "ScriptCompilation");

				return (CSharpCompilation)updatedCompilation;
			}
			catch (Exception ex)
			{
				EditorDebug.Error($"Failed to run source generators: {ex.Message}", "ScriptCompilation");
				return compilation;
			}
		}

		/// <summary>
		/// Loads the source generator instances from the Voltage.SourceGenerators.dll.
		/// </summary>
		private static IReadOnlyList<ISourceGenerator> GetSourceGenerators()
		{
			if (_cachedGenerators != null)
				return _cachedGenerators;

			var generatorDllPath = FindSourceGeneratorDll();
			if (generatorDllPath == null)
			{
				Debug.Error($"[ScriptCompiler] {EngineLibsSync.SourceGeneratorDllName} not found in any search path. " +
					"ComponentData will NOT be auto-generated for script components.");
				_cachedGenerators = Array.Empty<ISourceGenerator>();
				return _cachedGenerators;
			}

			EditorDebug.Log($"Loading source generator from: {generatorDllPath}", "ScriptCompilation");

			try
			{
				var generatorAssembly = Assembly.LoadFrom(generatorDllPath);
				var generators = new List<ISourceGenerator>();

				foreach (var type in generatorAssembly.GetTypes())
				{
					if (typeof(IIncrementalGenerator).IsAssignableFrom(type) && !type.IsAbstract)
					{
						var instance = (IIncrementalGenerator)Activator.CreateInstance(type);
						generators.Add(instance.AsSourceGenerator());
						EditorDebug.Log($"Loaded source generator: {type.FullName}", "ScriptCompilation");
					}
				}

				if (generators.Count == 0)
				{
					Debug.Error($"[ScriptCompiler] {EngineLibsSync.SourceGeneratorDllName} was loaded but " +
						$"contains 0 IIncrementalGenerator implementations. " +
						$"This may indicate a Microsoft.CodeAnalysis version mismatch. " +
						$"Editor Roslyn: {typeof(CSharpCompilation).Assembly.GetName().Version}, " +
						$"Generator assembly: {generatorAssembly.GetName().Version}");

					var allTypes = generatorAssembly.GetTypes();
					foreach (var t in allTypes)
					{
						var interfaces = t.GetInterfaces().Select(i => i.FullName);
						EditorDebug.Log($"  Type: {t.FullName} implements: [{string.Join(", ", interfaces)}]", "ScriptCompilation");
					}
				}

				_cachedGenerators = generators;
				EditorDebug.Log($"Loaded {generators.Count} source generator(s) from {EngineLibsSync.SourceGeneratorDllName}.", "ScriptCompilation");
			}
			catch (Exception ex)
			{
				Debug.Error($"[ScriptCompiler] Failed to load source generators from {generatorDllPath}: {ex.Message}");
				if (ex.InnerException != null)
					Debug.Error($"  Inner: {ex.InnerException.Message}");
				_cachedGenerators = Array.Empty<ISourceGenerator>();
			}

			return _cachedGenerators;
		}

		/// <summary>
		/// Searches for the Voltage.SourceGenerators.dll in multiple locations.
		/// </summary>
		private static string FindSourceGeneratorDll()
		{
			var dllName = EngineLibsSync.SourceGeneratorDllName;
			var searchPaths = new List<string>();

			// Strategy 1: Next to the editor assembly
			var asmLocation = typeof(ScriptCompiler).Assembly.Location;
			if (!string.IsNullOrEmpty(asmLocation))
			{
				var dir = Path.GetDirectoryName(asmLocation);
				if (!string.IsNullOrEmpty(dir))
					searchPaths.Add(dir);
			}

			// Strategy 2: AppContext.BaseDirectory
			var baseDir = AppContext.BaseDirectory;
			if (!string.IsNullOrEmpty(baseDir))
				searchPaths.Add(baseDir);

			// Strategy 3: Current working directory
			var cwd = Environment.CurrentDirectory;
			if (!string.IsNullOrEmpty(cwd))
				searchPaths.Add(cwd);

			// Strategy 4: Project EngineLibs folder
			var projectPath = ProjectManager.Instance?.CurrentProject?.ProjectPath;
			if (!string.IsNullOrEmpty(projectPath))
				searchPaths.Add(EngineLibsSync.GetEngineLibsPath(projectPath));

			// Strategy 5: Source generator project build output (during development)
			var solutionDir = FindSolutionDir();
			if (solutionDir != null)
			{
				var generatorProjectDir = Path.Combine(
					solutionDir, "Voltage.SourceGenerators", "Voltage.SourceGenerators");
				searchPaths.Add(Path.Combine(generatorProjectDir, "bin", "Debug", "netstandard2.0"));
				searchPaths.Add(Path.Combine(generatorProjectDir, "bin", "Release", "netstandard2.0"));
			}

			// Deduplicate and search
			var searched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (var dir in searchPaths)
			{
				if (string.IsNullOrEmpty(dir) || !searched.Add(dir))
					continue;

				var candidate = Path.Combine(dir, dllName);
				EditorDebug.Log($"Searching for source generator at: {candidate}", "ScriptCompilation");
				if (File.Exists(candidate))
					return candidate;
			}

			Debug.Error($"[ScriptCompiler] Searched {searched.Count} paths for {dllName}: " +
				string.Join(", ", searched));

			return null;
		}

		/// <summary>
		/// Finds the solution root directory by walking up from the editor's base directory.
		/// </summary>
		private static string FindSolutionDir()
		{
			var dir = AppContext.BaseDirectory;
			var di = new DirectoryInfo(dir);
			while (di != null)
			{
				if (Directory.Exists(Path.Combine(di.FullName, "Voltage.Engine")))
					return di.FullName;
				di = di.Parent;
			}

			return null;
		}
	}

	/// <summary>
	/// Result of script compilation
	/// </summary>
	public class CompilationResult
	{
		public bool Success { get; set; }
		public List<string> Errors { get; set; }
		public Assembly Assembly { get; set; }
	}
}