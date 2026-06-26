using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Voltage.Editor.DebugUtils;

namespace Voltage.Editor.Scripting
{
	/// <summary>
	/// Gives every concrete component (and scene-component) script a stable, rename-proof identity by
	/// stamping a <c>[Voltage.ComponentId("…")]</c> attribute on it the first time it is compiled
	/// without one. Scenes reference components by this id rather than by their C# type name, so
	/// renaming the class or moving its namespace no longer breaks the scenes that use it.
	///
	/// The id is a human-readable alias (Orleans-style), defaulting to the class's simple name at the
	/// moment of stamping. It is then <b>frozen</b>: the stamper never rewrites an existing id, so a
	/// later class rename leaves the id untouched — which is exactly what keeps references stable.
	/// Developers are free to hand-write any id string they prefer.
	///
	/// Detection is semantic (it walks the real inheritance chain via the Roslyn compilation), so
	/// indirect subclasses such as <c>BonkerComponent : EnemyComponent : Component</c> are handled.
	/// Stamping is idempotent — a class that already has the attribute is never touched, so this
	/// causes a one-time edit per component and no churn thereafter.
	/// </summary>
	internal static class ComponentIdStamper
	{
		private const string ComponentBase     = "Voltage.Component";
		private const string SceneComponentBase = "Voltage.SceneComponent";

		/// <summary>
		/// Scans every syntax tree in <paramref name="compilation"/> for concrete component classes
		/// lacking a <c>[ComponentId]</c>, writes the attribute into the corresponding source files,
		/// and returns the absolute paths of the files that were modified (empty when nothing changed).
		/// </summary>
		public static IReadOnlyList<string> StampMissing(CSharpCompilation compilation)
		{
			var changedFiles = new List<string>();

			var componentBase = compilation.GetTypeByMetadataName(ComponentBase);
			var sceneComponentBase = compilation.GetTypeByMetadataName(SceneComponentBase);
			if (componentBase == null && sceneComponentBase == null)
			{
				// Engine reference missing — cannot classify components; skip silently.
				return changedFiles;
			}

			foreach (var tree in compilation.SyntaxTrees)
			{
				var path = tree.FilePath;
				if (string.IsNullOrEmpty(path) || !File.Exists(path))
					continue;

				var model = compilation.GetSemanticModel(tree);
				var root = tree.GetRoot();

				// Collect insertion points: (token start of the class, default id).
				var insertions = new List<(int position, string id)>();
				var usedIdsInFile = new HashSet<string>(StringComparer.Ordinal);

				foreach (var classDecl in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
				{
					var symbol = model.GetDeclaredSymbol(classDecl);
					if (symbol == null || symbol.IsAbstract)
						continue;
					if (!DerivesFrom(symbol, componentBase) && !DerivesFrom(symbol, sceneComponentBase))
						continue;
					// Detect an existing [ComponentId] SYNTACTICALLY. The attribute type is emitted by
					// the generator, which has not run yet at stamp time, so a semantic lookup would
					// fail to resolve it and we'd stamp a duplicate on every compile.
					if (HasComponentIdSyntax(classDecl))
						continue;

					// Default the id to the class's simple name. If a file declares two components
					// with the same simple name (rare) disambiguate the second within the file so the
					// generator's duplicate-id diagnostic doesn't fire on a fresh stamp.
					var id = symbol.Name;
					int n = 2;
					while (!usedIdsInFile.Add(id))
						id = $"{symbol.Name}-{n++}";

					insertions.Add((classDecl.SpanStart, id));
				}

				if (insertions.Count == 0)
					continue;

				try
				{
					var text = tree.GetText();
					var changes = new List<TextChange>(insertions.Count + 1);

					// The attribute is written unqualified ([ComponentId]) and lives in namespace
					// Voltage, so the file needs `using Voltage;`. Component scripts virtually always
					// have it (they derive from Voltage.Component), but guarantee it for the rare file
					// that fully-qualifies its base type instead.
					bool hasVoltageUsing = root.DescendantNodes().OfType<UsingDirectiveSyntax>()
						.Any(u => u.Name?.ToString() == "Voltage");
					if (!hasVoltageUsing)
					{
						var cu = root as CompilationUnitSyntax;
						int usingPos = (cu != null && cu.Usings.Count > 0) ? cu.Usings.Last().FullSpan.End : 0;
						changes.Add(new TextChange(new TextSpan(usingPos, 0), "using Voltage;\r\n"));
					}

					foreach (var (position, id) in insertions)
					{
						var indent = GetIndentAt(text, position);
						var insert = $"[ComponentId(\"{id}\")]\r\n{indent}";
						changes.Add(new TextChange(new TextSpan(position, 0), insert));
					}

					var newText = text.WithChanges(changes);
					File.WriteAllText(path, newText.ToString(), new UTF8Encoding(false));
					changedFiles.Add(path);

					EditorDebug.Log(
						$"Stamped {insertions.Count} [ComponentId] attribute(s) into {Path.GetFileName(path)}.",
						"ComponentId");
				}
				catch (Exception ex)
				{
					Debug.Error($"[ComponentIdStamper] Failed to stamp '{path}': {ex.Message}");
				}
			}

			return changedFiles;
		}

		/// <summary>Returns the leading whitespace of the line that contains <paramref name="position"/>.</summary>
		private static string GetIndentAt(SourceText text, int position)
		{
			var line = text.Lines.GetLineFromPosition(position);
			var sb = new StringBuilder();
			for (int i = line.Start; i < position; i++)
			{
				char c = text[i];
				if (c == ' ' || c == '\t')
					sb.Append(c);
				else
					break;
			}
			return sb.ToString();
		}

		private static bool DerivesFrom(INamedTypeSymbol symbol, INamedTypeSymbol baseType)
		{
			if (baseType == null)
				return false;
			for (var current = symbol.BaseType; current != null; current = current.BaseType)
			{
				if (SymbolEqualityComparer.Default.Equals(current, baseType))
					return true;
			}
			return false;
		}

		/// <summary>
		/// True when the class declaration already carries a <c>[ComponentId]</c> attribute, checked
		/// against the attribute's <b>syntax</b> (its written name) rather than a resolved symbol.
		/// This is deliberate: the attribute type is generator-emitted and is not yet part of the
		/// compilation when the stamper runs, so a semantic check would never see an existing id and
		/// would re-stamp a duplicate on every compile.
		/// </summary>
		private static bool HasComponentIdSyntax(ClassDeclarationSyntax classDecl)
		{
			foreach (var list in classDecl.AttributeLists)
			{
				foreach (var attr in list.Attributes)
				{
					var name = attr.Name.ToString();           // e.g. "Voltage.ComponentId" or "ComponentId"
					int dot = name.LastIndexOf('.');
					if (dot >= 0)
						name = name.Substring(dot + 1);
					if (name == "ComponentId" || name == "ComponentIdAttribute")
						return true;
				}
			}
			return false;
		}
	}
}
