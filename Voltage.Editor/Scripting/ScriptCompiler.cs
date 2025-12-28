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
		/// Get metadata references from currently loaded assemblies for compilation
		/// </summary>
		private static List<MetadataReference> GetMetadataReferences()
		{
			var references = new List<MetadataReference>();

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
				if (!string.IsNullOrEmpty(assembly.Location))
				{
					references.Add(MetadataReference.CreateFromFile(assembly.Location));
				}
			}

			// Add MonoGame/FNA references
			var monogameAssemblies = new[]
			{
				typeof(Microsoft.Xna.Framework.Vector2).Assembly,
				typeof(Microsoft.Xna.Framework.Graphics.Texture2D).Assembly,
			};

			foreach (var assembly in monogameAssemblies)
			{
				if (!string.IsNullOrEmpty(assembly.Location))
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
				if (!string.IsNullOrEmpty(assembly.Location))
				{
					references.Add(MetadataReference.CreateFromFile(assembly.Location));
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
					if (File.Exists(path))
					{
						references.Add(MetadataReference.CreateFromFile(path));
					}
				}
			}

			return references;
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