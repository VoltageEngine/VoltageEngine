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
		/// Prefers the local EngineLibs copy when a project is loaded, falling back
		/// to the live assemblies in the AppDomain if EngineLibs is not available.
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

			// Try to use the project's local EngineLibs copy for Voltage and MonoGame references.
			// This ensures the compiler sees the exact same API surface the editor is running with,
			// even if the on-disk build output DLLs are stale.
			var projectPath = ProjectManager.Instance?.CurrentProject?.ProjectPath;
			bool usedEngineLibs = false;

			if (!string.IsNullOrEmpty(projectPath) && EngineLibsSync.IsReady(projectPath))
			{
				var engineLibsPath = EngineLibsSync.GetEngineLibsPath(projectPath);
				var dlls = Directory.GetFiles(engineLibsPath, "*.dll");

				foreach (var dll in dlls)
				{
					// Skip the source generator DLL - it is a netstandard2.0 Roslyn analyzer
					// and must not be added as a runtime metadata reference.
					if (Path.GetFileName(dll).Equals(EngineLibsSync.SourceGeneratorDllName, StringComparison.OrdinalIgnoreCase))
						continue;

					if (addedPaths.Add(dll))
					{
						references.Add(MetadataReference.CreateFromFile(dll));
					}
				}

				usedEngineLibs = dlls.Length > 0;

				if (usedEngineLibs)
					EditorDebug.Log($"Using EngineLibs references from: {engineLibsPath}", "ScriptCompilation");
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
		/// Cached source generator instances loaded from the Voltage.SourceGenerators DLL.
		/// Loaded once on first compilation and reused for all subsequent compilations.
		/// </summary>
		private static IReadOnlyList<ISourceGenerator> _cachedGenerators;

		/// <summary>
		/// Runs the Voltage.SourceGenerators source generator against the compilation.
		/// The generator is loaded from the Voltage.SourceGenerators.dll located next to
		/// the editor executable. This is the same DLL that gets synced to EngineLibs for
		/// standalone project builds via the Analyzer MSBuild item.
		///
		/// If the generator DLL cannot be found or loaded, compilation proceeds without it
		/// (a warning is logged). Generator-produced diagnostics with Error severity are
		/// added to the errors list.
		/// </summary>
		private static CSharpCompilation RunSourceGenerators(CSharpCompilation compilation, List<string> errors)
		{
			var generators = GetSourceGenerators();
			if (generators == null || generators.Count == 0)
				return compilation;

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
		/// The DLL is located next to the editor executable. Results are cached.
		/// </summary>
		private static IReadOnlyList<ISourceGenerator> GetSourceGenerators()
		{
			if (_cachedGenerators != null)
				return _cachedGenerators;

			var editorDir = Path.GetDirectoryName(typeof(ScriptCompiler).Assembly.Location);
			if (string.IsNullOrEmpty(editorDir))
			{
				EditorDebug.Warn("Cannot determine editor directory for source generator loading.", "ScriptCompilation");
				_cachedGenerators = Array.Empty<ISourceGenerator>();
				return _cachedGenerators;
			}

			var generatorDllPath = Path.Combine(editorDir, EngineLibsSync.SourceGeneratorDllName);
			if (!File.Exists(generatorDllPath))
			{
				EditorDebug.Warn($"Source generator DLL not found at: {generatorDllPath}. " +
					"ComponentData will not be auto-generated for editor scripts.", "ScriptCompilation");
				_cachedGenerators = Array.Empty<ISourceGenerator>();
				return _cachedGenerators;
			}

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

				_cachedGenerators = generators;
				EditorDebug.Log($"Loaded {generators.Count} source generator(s) from {EngineLibsSync.SourceGeneratorDllName}.", "ScriptCompilation");
			}
			catch (Exception ex)
			{
				EditorDebug.Error($"Failed to load source generators from {generatorDllPath}: {ex.Message}", "ScriptCompilation");
				_cachedGenerators = Array.Empty<ISourceGenerator>();
			}

			return _cachedGenerators;
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