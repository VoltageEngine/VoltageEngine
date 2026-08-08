using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Voltage.SourceGenerators;

/// <summary>
/// Pipeline 3 of <see cref="ComponentDataGenerator"/>: a reflection-free reader, an AOT factory and a stable-id registration for every concrete <c>Voltage.Data.DataAsset</c> subclass.
/// </summary>
public partial class ComponentDataGenerator
{
	private const string DataAssetBaseFullName = "Voltage.Data.DataAsset";
	private const string AssetTypeIdAttributeFullName = "Voltage.AssetTypeIdAttribute";
	private const string CloneOnLoadAttributeFullName = "Voltage.Data.CloneOnLoadAttribute";
	private const string AssetVersionAttributeFullName = "Voltage.Data.AssetVersionAttribute";

	// [AssetTypeId] is engine-declared, not generator-emitted: emitting it would cost every game
	// project a CS0436 warning, since Voltage.dll runs this generator too and carries its own copy.

	private struct DataAssetModel
	{
		public string ClassName;
		public string FullyQualifiedName;
		public string FullNamespace;
		public string AssetTypeId;
		public string DisplayName;
		public int Version;
		public bool CloneOnLoad;
		public bool IsPartial;
		public bool HasPublicParameterlessCtor;
		public bool HasExplicitId;
		public List<MemberModel> Members;
		public List<string> NonPublicSerializedNames;
		public List<string> StructsWithExplicitCtorFields;
		public List<string> FormerNames;
		public Location DiagnosticLocation;
	}

	private static DataAssetModel? GetDataAssetModel(GeneratorSyntaxContext ctx, System.Threading.CancellationToken ct)
	{
		var classDecl = (ClassDeclarationSyntax)ctx.Node;
		var symbol = ctx.SemanticModel.GetDeclaredSymbol(classDecl, ct);

		if (symbol is null || symbol.IsAbstract || symbol.IsStatic)
			return null;

		if (!DerivesFrom(symbol, DataAssetBaseFullName))
			return null;

		var declarations = symbol.DeclaringSyntaxReferences;
		if (declarations.Length > 1 && declarations[0].GetSyntax(ct) != classDecl)
			return null;

		var isPartial = classDecl.Modifiers.Any(SyntaxKind.PartialKeyword);

		var hasPublicCtor = symbol.InstanceConstructors.Any(c =>
			c.DeclaredAccessibility == Accessibility.Public && c.Parameters.Length == 0);

		var members = CollectSerializableMembers(symbol, ctx.SemanticModel.Compilation,
			out _, out var structsWithExplicitCtor, DataAssetBaseFullName);

		var nonPublic = new List<string>();
		var kept = new List<MemberModel>(members.Count);
		foreach (var m in members)
		{
			if (m.IsPublic)
				kept.Add(m);
			else
				nonPublic.Add(m.Name);
		}

		string assetTypeId = null;
		var version = 1;
		var cloneOnLoad = false;
		List<string> formerNames = null;

		foreach (var attr in symbol.GetAttributes())
		{
			var name = attr.AttributeClass?.ToDisplayString();

			if (name == AssetTypeIdAttributeFullName)
			{
				if (attr.ConstructorArguments.Length > 0 &&
					attr.ConstructorArguments[0].Value is string id && !string.IsNullOrWhiteSpace(id))
				{
					assetTypeId = id;
				}
			}
			else if (name == CloneOnLoadAttributeFullName)
			{
				cloneOnLoad = true;
			}
			else if (name == AssetVersionAttributeFullName)
			{
				if (attr.ConstructorArguments.Length > 0 && attr.ConstructorArguments[0].Value is int v && v > 0)
					version = v;
			}
			else if (name == "Voltage.Serialization.FormerlyKnownAsAttribute")
			{
				foreach (var arg in attr.ConstructorArguments)
				{
					if (arg.Kind == TypedConstantKind.Array)
					{
						foreach (var elem in arg.Values)
						{
							if (elem.Value is string s && !string.IsNullOrEmpty(s))
								(formerNames ??= new List<string>()).Add(s);
						}
					}
					else if (arg.Value is string single && !string.IsNullOrEmpty(single))
					{
						(formerNames ??= new List<string>()).Add(single);
					}
				}
			}
		}

		var hasExplicitId = assetTypeId != null;

		return new DataAssetModel
		{
			ClassName = symbol.Name,
			FullyQualifiedName = symbol.ToDisplayString(),
			FullNamespace = symbol.ContainingNamespace.IsGlobalNamespace
				? null
				: symbol.ContainingNamespace.ToDisplayString(),
			AssetTypeId = assetTypeId ?? symbol.Name,
			DisplayName = SplitPascalCase(symbol.Name),
			Version = version,
			CloneOnLoad = cloneOnLoad,
			IsPartial = isPartial,
			HasPublicParameterlessCtor = hasPublicCtor,
			HasExplicitId = hasExplicitId,
			Members = kept,
			NonPublicSerializedNames = nonPublic,
			StructsWithExplicitCtorFields = structsWithExplicitCtor,
			FormerNames = formerNames,
			DiagnosticLocation = classDecl.Identifier.GetLocation(),
		};
	}

	private static void EmitDataAsset(SourceProductionContext spc, DataAssetModel model)
	{
		var location = model.DiagnosticLocation ?? Location.None;

		if (!model.IsPartial)
		{
			Report(spc, location, "VLT012", "Data asset must be partial", DiagnosticSeverity.Error,
				$"Data asset '{model.ClassName}' must be declared 'partial' so its JSON reader can be " +
				"generated. Without the reader it would load as all-default values in a published build.");
			return;
		}

		if (!model.HasPublicParameterlessCtor)
		{
			Report(spc, location, "VLT011", "Data asset needs a public parameterless constructor",
				DiagnosticSeverity.Error,
				$"Data asset '{model.ClassName}' has no public parameterless constructor, so it cannot be " +
				"created without reflection. Add one, or give existing constructors default arguments.");
			return;
		}

		if (!model.HasExplicitId)
		{
			Report(spc, location, "VLT015", "Data asset has no [AssetTypeId]", DiagnosticSeverity.Warning,
				$"Data asset '{model.ClassName}' has no [AssetTypeId], so its id defaults to the class name " +
				"and is not yet frozen — renaming the class would orphan every .vasset that uses it. The " +
				"editor stamps one on the next compile; add [AssetTypeId(\"" + model.ClassName + "\")] to " +
				"fix it now.");
		}

		if (model.NonPublicSerializedNames.Count > 0)
		{
			Report(spc, location, "VLT014", "Non-public data asset member will not persist",
				DiagnosticSeverity.Warning,
				$"Data asset '{model.ClassName}' has non-public member(s) marked [Serialize] that will " +
				$"never be written to the .vasset file: {string.Join(", ", model.NonPublicSerializedNames)}. " +
				"Unlike components — which serialize through a generated data class — a data asset is " +
				"written by the reflection encoder, which only emits public members. Make the member public.");
		}

		if (model.StructsWithExplicitCtorFields.Count > 0)
		{
			Report(spc, location, "VLT003", "Struct with explicit constructor will not deserialize in NativeAOT builds",
				DiagnosticSeverity.Error,
				$"Data asset '{model.ClassName}' has struct field(s) with an explicit parameterless " +
				$"constructor: {string.Join(", ", model.StructsWithExplicitCtorFields)}. In a published " +
				"build those fields are never populated from JSON — they silently keep their type defaults. " +
				"Convert the struct to a class implementing IComponentGroup to fix this.");
			return;
		}

		if (model.Members.Count == 0)
		{
			Report(spc, location, "VLT013", "Data asset has no serializable members", DiagnosticSeverity.Warning,
				$"Data asset '{model.ClassName}' has no public fields or properties, so every instance will " +
				"be empty. Add public members, or delete the type.");
		}

		var sb = new StringBuilder();
		sb.AppendLine("// <auto-generated/>");
		sb.AppendLine("#nullable disable");
		sb.AppendLine();

		if (model.FullNamespace is not null)
		{
			sb.AppendLine($"namespace {model.FullNamespace}");
			sb.AppendLine("{");
		}

		var indent = model.FullNamespace is not null ? "\t" : "";

		sb.AppendLine($"{indent}partial class {model.ClassName}");
		sb.AppendLine($"{indent}{{");

		var emittedReaders = new HashSet<string>();
		foreach (var m in model.Members)
		{
			if (m.UserStructTypeSymbol != null)
				EmitStructReaders(sb, indent + "\t", m.UserStructTypeSymbol, emittedReaders);

			if (m.IsComponentGroup && m.UserClassTypeSymbol != null)
				EmitGroupReaders(sb, indent + "\t", m.UserClassTypeSymbol, emittedReaders);

			var collectionElem = m.IsListField ? m.ListElementTypeSymbol
				: m.IsDictionaryField ? m.DictionaryValueTypeSymbol
				: m.IsArrayField ? m.ArrayElementTypeSymbol
				: null;

			if (collectionElem is INamedTypeSymbol namedElem)
			{
				if (IsUserDefinedStruct(collectionElem) &&
					!KnownEngineStructReaders.Contains(collectionElem.ToDisplayString()))
				{
					EmitStructReaders(sb, indent + "\t", namedElem, emittedReaders);
				}
				else if (IsComponentGroupType(collectionElem))
				{
					EmitGroupReaders(sb, indent + "\t", namedElem, emittedReaders);
				}
			}
		}

		EmitDataAssetReader(sb, indent + "\t", model);
		EmitDataAssetRegistration(sb, indent + "\t", model);

		sb.AppendLine($"{indent}}}");

		if (model.FullNamespace is not null)
			sb.AppendLine("}");

		spc.AddSource($"{model.ClassName}.DataAsset.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
	}

	/// <summary>
	/// Writes straight into a live instance (no data-class indirection) and keys on the real member name, because that is what the reflection encoder writes on the way out.
	/// </summary>
	private static void EmitDataAssetReader(StringBuilder sb, string indent, DataAssetModel model)
	{
		sb.AppendLine($"{indent}/// <summary>Generated reflection-free reader. Called with the reader at the start of the file's \"data\" object.</summary>");
		sb.AppendLine($"{indent}internal static global::Voltage.Data.DataAsset __ReadDataAsset(global::Voltage.Persistence.JsonTokenReader _r)");
		sb.AppendLine($"{indent}{{");
		sb.AppendLine($"{indent}\tvar _v = new {model.ClassName}();");
		sb.AppendLine($"{indent}\tif (!_r.BeginObject()) return _v;");
		sb.AppendLine($"{indent}\twhile (_r.ReadNextKey(out var _k))");
		sb.AppendLine($"{indent}\t{{");
		sb.AppendLine($"{indent}\t\tswitch (_k)");
		sb.AppendLine($"{indent}\t\t{{");

		foreach (var m in model.Members)
		{
			string readExpr;
			if (m.IsComponentReference)
			{
				readExpr = "global::Voltage.Serialization.AotDeserializers.ReadComponentReference(_r)";
			}
			else if (m.IsEntityReference || m.IsTransformReference)
			{
				readExpr = "global::Voltage.Serialization.AotDeserializers.ReadEntityReference(_r)";
			}
			else if (m.IsComponentGroup && m.UserClassTypeSymbol != null)
			{
				readExpr = $"{GetStructReaderMethodName(m.UserClassTypeSymbol.ToDisplayString())}(_r)";
			}
			else if (m.IsListField && m.ListElementTypeSymbol != null)
			{
				readExpr = $"_r.ReadList(_er => {GetCollectionElementReadExpr(m, m.ListElementTypeSymbol, "_er")})";
			}
			else if (m.IsDictionaryField && m.DictionaryValueTypeSymbol != null)
			{
				readExpr = $"_r.ReadStringDictionary(_vr => {GetCollectionElementReadExpr(m, m.DictionaryValueTypeSymbol, "_vr")})";
			}
			else if (m.IsArrayField && m.ArrayElementTypeSymbol != null)
			{
				readExpr = $"_r.ReadArray(_er => {GetCollectionElementReadExpr(m, m.ArrayElementTypeSymbol, "_er")})";
			}
			else
			{
				readExpr = GetReadExpressionForMember(m, "_r");
			}

			sb.AppendLine($"{indent}\t\t\tcase \"{m.Name}\": _v.{m.Name} = {readExpr}; break;");
		}

		// Skipped, not rejected: adding or removing a field must not invalidate existing assets.
		sb.AppendLine($"{indent}\t\t\tdefault: _r.SkipValue(); break;");
		sb.AppendLine($"{indent}\t\t}}");
		sb.AppendLine($"{indent}\t}}");
		sb.AppendLine($"{indent}\treturn _v;");
		sb.AppendLine($"{indent}}}");
		sb.AppendLine();
	}

	private static void EmitDataAssetRegistration(StringBuilder sb, string indent, DataAssetModel model)
	{
		var fqn = model.FullyQualifiedName;

		sb.AppendLine($"{indent}[global::System.Runtime.CompilerServices.ModuleInitializer]");
		sb.AppendLine($"{indent}internal static void __RegisterDataAsset_{model.ClassName}()");
		sb.AppendLine($"{indent}{{");
		sb.AppendLine($"{indent}\tglobal::Voltage.Data.DataAssetRegistry.Register(");
		sb.AppendLine($"{indent}\t\t\"{EscapeString(model.AssetTypeId)}\",");
		sb.AppendLine($"{indent}\t\ttypeof(global::{fqn}),");
		sb.AppendLine($"{indent}\t\tstatic () => new global::{fqn}(),");
		sb.AppendLine($"{indent}\t\tstatic _r => __ReadDataAsset(_r),");
		sb.AppendLine($"{indent}\t\t{model.Version},");
		sb.AppendLine($"{indent}\t\t\"{EscapeString(model.DisplayName)}\",");
		sb.AppendLine($"{indent}\t\t{(model.CloneOnLoad ? "true" : "false")});");

		if (model.FormerNames != null)
		{
			foreach (var oldName in model.FormerNames)
			{
				sb.AppendLine();
				sb.AppendLine($"{indent}\tglobal::Voltage.Serialization.TypeRenameRegistry.Register(");
				sb.AppendLine($"{indent}\t\t\"{EscapeString(oldName)}\",");
				sb.AppendLine($"{indent}\t\ttypeof(global::{fqn}));");
			}
		}

		sb.AppendLine($"{indent}}}");
	}

	/// <summary>VLT010 — a shared <c>[AssetTypeId]</c> (usually a copy-paste) makes resolution ambiguous.</summary>
	private static void ReportDuplicateAssetTypeIds(SourceProductionContext spc, ImmutableArray<DataAssetModel> all)
	{
		if (all.IsDefaultOrEmpty)
			return;

		var byId = new Dictionary<string, List<DataAssetModel>>(System.StringComparer.Ordinal);
		foreach (var model in all)
		{
			if (string.IsNullOrEmpty(model.AssetTypeId))
				continue;
			if (!byId.TryGetValue(model.AssetTypeId, out var owners))
				byId[model.AssetTypeId] = owners = new List<DataAssetModel>();
			owners.Add(model);
		}

		foreach (var pair in byId)
		{
			if (pair.Value.Count < 2)
				continue;

			var names = string.Join(", ", pair.Value.Select(m => m.FullyQualifiedName));
			foreach (var dup in pair.Value)
			{
				Report(spc, dup.DiagnosticLocation ?? Location.None, "VLT010", "Duplicate [AssetTypeId]",
					DiagnosticSeverity.Error,
					$"Data assets {names} share the same [AssetTypeId(\"{pair.Key}\")]. Each type needs a " +
					$"unique id — give one of them a different id (e.g. \"{pair.Key}-2\").");
			}
		}
	}

	private static void Report(SourceProductionContext spc, Location location, string id, string title,
		DiagnosticSeverity severity, string message)
	{
		spc.ReportDiagnostic(Diagnostic.Create(
			new DiagnosticDescriptor(id, title, "{0}", "Voltage.Serialization", severity, isEnabledByDefault: true),
			location, message));
	}

	/// <summary>"DifficultyProfile" → "Difficulty Profile". Runs of capitals stay together.</summary>
	private static string SplitPascalCase(string name)
	{
		if (string.IsNullOrEmpty(name))
			return name;

		var sb = new StringBuilder(name.Length + 4);
		for (var i = 0; i < name.Length; i++)
		{
			var c = name[i];
			if (i > 0 && char.IsUpper(c) &&
				(!char.IsUpper(name[i - 1]) || (i + 1 < name.Length && !char.IsUpper(name[i + 1]))))
			{
				sb.Append(' ');
			}
			sb.Append(c);
		}

		return sb.ToString();
	}
}
