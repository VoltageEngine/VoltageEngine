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
	/// Gives types referenced <i>by identity</i> in serialized data a stable, rename-proof id, by stamping
	/// a marker attribute on the first compile without one: <c>[ComponentId]</c> for components (scenes
	/// reference them by it) and <c>[AssetTypeId]</c> for data assets (a <c>.vasset</c>'s <c>@assetType</c>).
	///
	/// <para>The id defaults to the class's simple name and is then <b>frozen</b> — never rewritten — which
	/// is exactly what keeps references stable across a later rename. Detection is semantic, so indirect
	/// subclasses are handled; stamping is idempotent, so it costs one edit per type and no churn.</para>
	/// </summary>
	internal static class ComponentIdStamper
	{
		private const string ComponentBase = "Voltage.Component";
		private const string SceneComponentBase = "Voltage.SceneComponent";
		private const string DataAssetBase = "Voltage.Data.DataAsset";

		/// <summary>Stamps <c>[ComponentId]</c> onto every concrete component / scene-component lacking one.</summary>
		public static IReadOnlyList<string> StampMissing(CSharpCompilation compilation,
			Func<string, bool> allowStamp = null, List<string> violations = null)
			=> Stamp(compilation, new[] { ComponentBase, SceneComponentBase },
				attributeSimpleName: "ComponentId", kindLabel: "component",
				allowStamp: allowStamp, violations: violations);

		/// <summary>Stamps <c>[AssetTypeId]</c> onto every concrete <c>DataAsset</c> subclass lacking one.</summary>
		public static IReadOnlyList<string> StampMissingAssetTypeIds(CSharpCompilation compilation,
			Func<string, bool> allowStamp = null, List<string> violations = null)
			=> Stamp(compilation, new[] { DataAssetBase },
				attributeSimpleName: "AssetTypeId", kindLabel: "data asset",
				allowStamp: allowStamp, violations: violations);

		/// <summary>
		/// Writes the attribute into every source file declaring a matching type that lacks it, and returns
		/// the paths modified.
		///
		/// <para><paramref name="allowStamp"/> gates which files may be mutated: only sources the user owns.
		/// Cache-installed plugin packages are immutable, so a type there is reported into
		/// <paramref name="violations"/> for the plugin author to fix.</para>
		/// </summary>
		private static IReadOnlyList<string> Stamp(
			CSharpCompilation compilation,
			string[] baseTypeFullNames,
			string attributeSimpleName,
			string kindLabel,
			Func<string, bool> allowStamp,
			List<string> violations)
		{
			var changedFiles = new List<string>();

			var baseTypes = baseTypeFullNames
				.Select(compilation.GetTypeByMetadataName)
				.Where(t => t != null)
				.ToArray();

			if (baseTypes.Length == 0)
			{
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
					if (!baseTypes.Any(b => DerivesFrom(symbol, b)))
						continue;
					// Syntactic check: the generator has not run yet, so a semantic lookup would re-stamp every compile.
					if (HasAttributeSyntax(classDecl, attributeSimpleName))
						continue;

					var id = symbol.Name;
					int n = 2;
					while (!usedIdsInFile.Add(id))
						id = $"{symbol.Name}-{n++}";

					insertions.Add((classDecl.SpanStart, id));
				}

				if (insertions.Count == 0)
					continue;

				if (allowStamp != null && !allowStamp(path))
				{
					violations?.Add(
						$"{path}: {kindLabel}(s) missing a [{attributeSimpleName}] attribute inside a " +
						"read-only plugin package. Published plugins must declare stable " +
						$"[{attributeSimpleName}]s on every {kindLabel}.");
					continue;
				}

				try
				{
					var text = tree.GetText();
					var changes = new List<TextChange>(insertions.Count + 1);

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
						var insert = $"[{attributeSimpleName}(\"{id}\")]\r\n{indent}";
						changes.Add(new TextChange(new TextSpan(position, 0), insert));
					}

					var newText = text.WithChanges(changes);
					File.WriteAllText(path, newText.ToString(), new UTF8Encoding(false));
					changedFiles.Add(path);

					EditorDebug.Log(
						$"Stamped {insertions.Count} [{attributeSimpleName}] attribute(s) into {Path.GetFileName(path)}.",
						attributeSimpleName);
				}
				catch (Exception ex)
				{
					Debug.Error($"[{attributeSimpleName}Stamper] Failed to stamp '{path}': {ex.Message}");
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

		/// <summary>Checked against written syntax rather than a resolved symbol — see the call site.</summary>
		private static bool HasAttributeSyntax(ClassDeclarationSyntax classDecl, string attributeSimpleName)
		{
			foreach (var list in classDecl.AttributeLists)
			{
				foreach (var attr in list.Attributes)
				{
					var name = attr.Name.ToString();           // e.g. "Voltage.ComponentId" or "ComponentId"
					int dot = name.LastIndexOf('.');
					if (dot >= 0)
						name = name.Substring(dot + 1);
					if (name == attributeSimpleName || name == attributeSimpleName + "Attribute")
						return true;
				}
			}
			return false;
		}
	}
}
