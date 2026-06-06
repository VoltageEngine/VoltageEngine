using System;
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
/// Roslyn incremental source generator that:
///
/// 1. For each partial Component subclass WITHOUT a manual Data override:
///    emits a nested ComponentData class, a Data property override, and a fully
///    explicit JsonTokenReader-based Read method for the ComponentData class and for
///    every user-defined struct/IComponentGroup field type it contains (recursively).
///    The registered AOT deserializer calls that reader — zero reflection at runtime,
///    safe under NativeAOT trimming.
///
/// 2. For ALL concrete Component subclasses (including those with manual Data overrides):
///    emits a [ModuleInitializer] bootstrap that registers every Component type in
///    ComponentAotFactory so published builds can instantiate components without reflection.
///
/// The generated code is AOT-safe: zero reflection, direct field access only.
/// </summary>
[Generator]
public class ComponentDataGenerator : IIncrementalGenerator
{
	private const string HideAttribute = "Voltage.HideAttributeInInspector";
	private const string JsonExcludeAttribute = "Voltage.Persistence.JsonExcludeAttribute";
	private const string SerializedFieldAttribute = "Voltage.SerializedFieldAttribute";
	private const string InspectableAttribute = "Voltage.InspectableAttribute";
	private const string ComponentBaseFullName = "Voltage.Component";
	private const string EntityFullName = "Voltage.Entity";
	private const string TransformFullName = "Voltage.Transform";
	private const string ComponentReferenceFullName = "Voltage.Serialization.ComponentReference";
	private const string EntityReferenceFullName = "Voltage.Serialization.EntityReference";
	private const string ComponentGroupInterface = "Voltage.IComponentGroup";

	// Engine struct types whose readers are already hand-written in AotDeserializers.
	// The generator calls those instead of emitting its own reader for these types.
	private static readonly HashSet<string> KnownEngineStructReaders = new HashSet<string>
	{
		"Microsoft.Xna.Framework.Vector2",
		"Microsoft.Xna.Framework.Color",
		"Voltage.RectangleF",
		"Voltage.Serialization.ComponentReference",
		"Voltage.Serialization.EntityReference",
	};

	// Maps a known engine struct full name to the static read call expression the
	// generator should emit (the JsonTokenReader parameter is always named _r).
	private static readonly Dictionary<string, string> KnownEngineStructReadCall =
		new Dictionary<string, string>
		{
			["Microsoft.Xna.Framework.Vector2"]       = "global::Voltage.Serialization.AotDeserializers.ReadVector2(_r)",
			["Microsoft.Xna.Framework.Color"]         = "global::Voltage.Serialization.AotDeserializers.ReadColor(_r)",
			["Voltage.RectangleF"]                    = "global::Voltage.Serialization.AotDeserializers.ReadRectangleF(_r)",
			["Voltage.Serialization.ComponentReference"] = "global::Voltage.Serialization.AotDeserializers.ReadComponentReference(_r)",
			["Voltage.Serialization.EntityReference"]    = "global::Voltage.Serialization.AotDeserializers.ReadEntityReference(_r)",
		};

	public void Initialize(IncrementalGeneratorInitializationContext context)
	{
		var dataGenCandidates = context.SyntaxProvider
			.CreateSyntaxProvider(
				predicate: static (node, _) => IsPartialClassCandidate(node),
				transform: static (ctx, ct) => GetComponentModelForDataGen(ctx, ct))
			.Where(static x => x is not null)
			.Select(static (x, _) => x!.Value);

		context.RegisterSourceOutput(dataGenCandidates, static (spc, model) => EmitDataClass(spc, model));

		// 2) ALL concrete Component subclasses for AOT factory bootstrap
		var allComponents = context.SyntaxProvider
			.CreateSyntaxProvider(
				predicate: static (node, _) => node is ClassDeclarationSyntax,
				transform: static (ctx, ct) => GetComponentForBootstrap(ctx, ct))
			.Where(static x => x is not null)
			.Select(static (x, _) => x!.Value)
			.Collect();

		context.RegisterSourceOutput(allComponents, static (spc, components) =>
		{
			EmitAotBootstrap(spc, components);
		});
	}

	#region Syntactic / Semantic Filters

	private static bool IsPartialClassCandidate(SyntaxNode node)
	{
		return node is ClassDeclarationSyntax cds &&
			   cds.Modifiers.Any(SyntaxKind.PartialKeyword);
	}

	private static ComponentModel? GetComponentModelForDataGen(GeneratorSyntaxContext ctx, System.Threading.CancellationToken ct)
	{
		var classDecl = (ClassDeclarationSyntax)ctx.Node;
		var symbol = ctx.SemanticModel.GetDeclaredSymbol(classDecl, ct);
		if (symbol is null || symbol.IsAbstract)
			return null;

		if (!DerivesFrom(symbol, ComponentBaseFullName))
			return null;

		if (HasDataOverride(symbol))
			return null;

		var members = CollectSerializableMembers(symbol, ctx.SemanticModel.Compilation,
			out var skipped, out var structsWithExplicitCtor);

		return new ComponentModel
		{
			ClassName = symbol.Name,
			FullyQualifiedName = symbol.ToDisplayString(),
			FullNamespace = symbol.ContainingNamespace.IsGlobalNamespace
				? null
				: symbol.ContainingNamespace.ToDisplayString(),
			Members = members,
			SkippedFieldNames = skipped,
			StructsWithExplicitCtorFields = structsWithExplicitCtor,
			HasManualDataOverride = false,
			DiagnosticLocation = classDecl.Identifier.GetLocation()
		};
	}

	/// <summary>
	/// For the AOT bootstrap: find every concrete Component subclass regardless of
	/// whether it's partial or has a Data override. We need factory entries for ALL of them.
	/// </summary>
	private static BootstrapEntry? GetComponentForBootstrap(GeneratorSyntaxContext ctx, System.Threading.CancellationToken ct)
	{
		var classDecl = (ClassDeclarationSyntax)ctx.Node;
		var symbol = ctx.SemanticModel.GetDeclaredSymbol(classDecl, ct);
		if (symbol is null || symbol.IsAbstract)
			return null;

		if (!DerivesFrom(symbol, ComponentBaseFullName))
			return null;

		// Must have a public parameterless constructor
		bool hasPublicCtor = symbol.InstanceConstructors.Any(c =>
			c.DeclaredAccessibility == Accessibility.Public && c.Parameters.Length == 0);
		if (!hasPublicCtor)
			return null;

		// If this component has a manual Data override, capture the return type
		// so EmitAotBootstrap can also register its ComponentDataAotDeserializer.
		// Components WITHOUT a manual override get their deserializer registered
		// by the per-class __RegisterDeserializer_ emitted in EmitDataClass.
		string manualDataTypeName = null;
		var dataProp = GetDataPropertyOverride(symbol);
		if (dataProp != null && dataProp.Type is INamedTypeSymbol dataType
			&& !SymbolEqualityComparer.Default.Equals(dataType, ctx.SemanticModel.Compilation.GetTypeByMetadataName("Voltage.ComponentData")))
		{
			manualDataTypeName = dataType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
		}

		return new BootstrapEntry
		{
			ComponentFullName = symbol.ToDisplayString(),
			Namespace = symbol.ContainingNamespace.IsGlobalNamespace
				? null
				: symbol.ContainingNamespace.ToDisplayString(),
			ManualDataTypeFullName = manualDataTypeName
		};
	}

	private static IPropertySymbol GetDataPropertyOverride(INamedTypeSymbol symbol)
	{
		foreach (var member in symbol.GetMembers())
		{
			if (member is IPropertySymbol prop &&
				prop.Name == "Data" &&
				prop.IsOverride &&
				SymbolEqualityComparer.Default.Equals(prop.ContainingType, symbol))
			{
				return prop;
			}
		}
		return null;
	}

	#endregion

	#region Data Class Emission (Pipeline 1)

	private static void EmitDataClass(SourceProductionContext spc, ComponentModel model)
	{
		if (model.Members.Count == 0)
		{
			// Determine whether there are public fields that were silently skipped —
			// that indicates a type-recognition failure (e.g. IComponentGroup not found).
			bool hasSkippedFields = model.SkippedFieldNames.Count > 0;

			var diagId = hasSkippedFields ? "VLT002" : "VLT001";
			var diagTitle = hasSkippedFields
				? "Component fields skipped — IComponentGroup not recognized"
				: "Component has no serializable members";
			var diagMsg = hasSkippedFields
				? $"Component '{{0}}' has public fields that were skipped because their types were not recognized. " +
				  $"Skipped fields: {string.Join(", ", model.SkippedFieldNames)}. " +
				  $"Ensure Voltage.IComponentGroup is defined and the Voltage.Engine project compiles without errors."
				: "Component '{0}' has no serializable members. " +
				  "If you expect scene data to be loaded, add public fields or mark private ones with [Serialize].";

			// VLT003 — struct fields whose type has an explicit parameterless constructor.
			// In a NativeAOT build the AOT deserializer cannot call the constructor and the
			// reflection-based JSON path is trimmed away, so the struct fields are never
			// populated from JSON. This data assignment only works in the Editor (reflection is available)
			if (model.StructsWithExplicitCtorFields.Count > 0)
			{
				spc.ReportDiagnostic(Diagnostic.Create(
					new DiagnosticDescriptor(
						"VLT003",
						"Struct with explicit constructor will not deserialize in NativeAOT builds",
						"Component '{0}' has struct field(s) with an explicit parameterless constructor: {1}. " +
						"In a NativeAOT/published build these fields are never populated from JSON — they silently " +
						"keep their type defaults even when the save file contains different values. " +
						"This works in the Editor (reflection available) but breaks in the build, making it a silent data loss trap. " +
						"Convert the struct to a class implementing IComponentGroup to fix this.",
						"Voltage.Serialization",
						DiagnosticSeverity.Error,
						isEnabledByDefault: true),
					model.DiagnosticLocation ?? Location.None,
					model.ClassName,
					string.Join(", ", model.StructsWithExplicitCtorFields)));
			}

			spc.ReportDiagnostic(Diagnostic.Create(
				new DiagnosticDescriptor(
					diagId, diagTitle, diagMsg,
					"Voltage.Serialization",
					hasSkippedFields ? DiagnosticSeverity.Error : DiagnosticSeverity.Warning,
					isEnabledByDefault: true),
				model.DiagnosticLocation ?? Location.None,
				model.ClassName));

			return;
		}

		var sb = new StringBuilder();
		sb.AppendLine("// <auto-generated/>");
		sb.AppendLine("#nullable disable");
		sb.AppendLine();

		bool hasComponentReferences = model.Members.Any(m => m.IsComponentReference);
		bool hasEntityReferences = model.Members.Any(m => m.IsEntityReference || m.IsTransformReference);

		if (hasComponentReferences || hasEntityReferences)
			sb.AppendLine("using Voltage.Serialization;");

		sb.AppendLine();

		if (model.FullNamespace is not null)
		{
			sb.AppendLine($"namespace {model.FullNamespace}");
			sb.AppendLine("{");
		}

		var indent = model.FullNamespace is not null ? "\t" : "";
		var dataClassName = $"{model.ClassName}GeneratedData";

		sb.AppendLine($"{indent}partial class {model.ClassName}");
		sb.AppendLine($"{indent}{{");

		// ── Inner ComponentData class ────────────────────────────────────────
		sb.AppendLine($"{indent}\tpublic sealed class {dataClassName} : global::Voltage.ComponentData");
		sb.AppendLine($"{indent}\t{{");
		foreach (var m in model.Members)
		{
			var dataFieldName = GetDataFieldName(m.Name);
			if (m.IsComponentReference)
				sb.AppendLine($"{indent}\t\tpublic global::{ComponentReferenceFullName} {dataFieldName};");
			else if (m.IsEntityReference || m.IsTransformReference)
				sb.AppendLine($"{indent}\t\tpublic global::{EntityReferenceFullName} {dataFieldName};");
			else
				sb.AppendLine($"{indent}\t\tpublic {m.TypeFullName} {dataFieldName};");
		}
		sb.AppendLine($"{indent}\t}}");
		sb.AppendLine();

		// ── Data property override ───────────────────────────────────────────
		sb.AppendLine($"{indent}\tpublic override global::Voltage.ComponentData Data");
		sb.AppendLine($"{indent}\t{{");

		// Getter
		sb.AppendLine($"{indent}\t\tget");
		sb.AppendLine($"{indent}\t\t{{");
		sb.AppendLine($"{indent}\t\t\tvar _data = new {dataClassName}();");
		sb.AppendLine($"{indent}\t\t\t_data.Enabled = this.Enabled;");
		sb.AppendLine($"{indent}\t\t\t_data.CanBeSelected = this.CanBeSelected;");
		sb.AppendLine($"{indent}\t\t\t_data.UpdateOrder = this.UpdateOrder;");
		foreach (var m in model.Members)
		{
			var dataFieldName = GetDataFieldName(m.Name);
			if (m.IsComponentReference)
				sb.AppendLine($"{indent}\t\t\t_data.{dataFieldName} = global::{ComponentReferenceFullName}.From(this.{m.Name});");
			else if (m.IsEntityReference)
				sb.AppendLine($"{indent}\t\t\t_data.{dataFieldName} = global::{EntityReferenceFullName}.From(this.{m.Name});");
			else if (m.IsTransformReference)
				sb.AppendLine($"{indent}\t\t\t_data.{dataFieldName} = global::{EntityReferenceFullName}.From(this.{m.Name});");
			else if (m.IsComponentGroup && m.UserClassTypeSymbol != null)
			{
				var fqn = m.UserClassTypeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
				var cloneName = GetCloneMethodName(m.UserClassTypeSymbol.ToDisplayString());
				// Guard: field-level = new() is the intended pattern; warn if someone forgot it.
				sb.AppendLine($"{indent}\t\t\tif (this.{m.Name} == null) global::Voltage.Debug.Error($\"[{{GetType().Name}}] IComponentGroup field '{m.Name}' is null. Initialize it with '= new {fqn.Split(':').Last().Trim()}()' at declaration.\");");
				sb.AppendLine($"{indent}\t\t\t_data.{dataFieldName} = {cloneName}(this.{m.Name});");
			}
			else
				sb.AppendLine($"{indent}\t\t\t_data.{dataFieldName} = this.{m.Name};");
		}
		sb.AppendLine($"{indent}\t\t\treturn _data;");
		sb.AppendLine($"{indent}\t\t}}");

		// Setter
		sb.AppendLine($"{indent}\t\tset");
		sb.AppendLine($"{indent}\t\t{{");
		sb.AppendLine($"{indent}\t\t\tif (value is {dataClassName} _d)");
		sb.AppendLine($"{indent}\t\t\t{{");
		sb.AppendLine($"{indent}\t\t\t\tthis.Enabled = _d.Enabled;");
		sb.AppendLine($"{indent}\t\t\t\tthis.CanBeSelected = _d.CanBeSelected;");
		sb.AppendLine($"{indent}\t\t\t\tthis.UpdateOrder = _d.UpdateOrder;");
		foreach (var m in model.Members)
		{
			var dataFieldName = GetDataFieldName(m.Name);
			if (m.IsComponentReference)
				sb.AppendLine($"{indent}\t\t\t\t// {m.Name} is a Component reference — resolved post-load by ComponentReferenceResolver.");
			else if (m.IsEntityReference || m.IsTransformReference)
				sb.AppendLine($"{indent}\t\t\t\t// {m.Name} is an Entity/Transform reference — resolved post-load by ComponentReferenceResolver.");
			else if (m.IsComponentGroup && m.UserClassTypeSymbol != null)
			{
				var cloneName = GetCloneMethodName(m.UserClassTypeSymbol.ToDisplayString());
				sb.AppendLine($"{indent}\t\t\t\tthis.{m.Name} = {cloneName}(_d.{dataFieldName});");
			}
			else
				sb.AppendLine($"{indent}\t\t\t\tthis.{m.Name} = _d.{dataFieldName};");
		}
		sb.AppendLine($"{indent}\t\t\t}}");
		sb.AppendLine($"{indent}\t\t}}");
		sb.AppendLine($"{indent}\t}}");
		sb.AppendLine();

		// ── JsonTokenReader-based readers (AOT-safe deserialization) ─────────
		//
		// For every user-defined struct/IComponentGroup field type (nested included),
		// emit the required static helper methods. These are the ONLY deserializers
		// used at runtime — no reflection, no JsonDecoder.
		//
		// Known engine types (Vector2, Color, RectangleF) already have readers
		// in AotDeserializers, so we just call those.
		var emittedReaders = new HashSet<string>(); // shared guard for both struct and group helpers
		foreach (var m in model.Members)
		{
			if (m.UserStructTypeSymbol != null)
				EmitStructReaders(sb, indent + "\t", m.UserStructTypeSymbol, emittedReaders);

			if (m.IsComponentGroup && m.UserClassTypeSymbol != null)
				EmitGroupReaders(sb, indent + "\t", m.UserClassTypeSymbol, emittedReaders);
		}

		// Top-level ComponentData reader
		EmitComponentDataReader(sb, indent + "\t", dataClassName, model.Members);
		//
		// // ── ResolveReferences override (AOT-safe reference resolution) ───────
		// //
		// // Called by ComponentReferenceResolver instead of reflection so that
		// // ComponentReference / EntityReference fields are restored in NativeAOT builds.
		// bool hasAnyReferences = model.Members.Any(m =>
		// 	m.IsComponentReference || m.IsEntityReference || m.IsTransformReference);
		//
		// if (hasAnyReferences)
		// {
		// 	sb.AppendLine($"{indent}\tpublic override void ResolveReferences(");
		// 	sb.AppendLine($"{indent}\t\tglobal::System.Action<string, global::Voltage.Serialization.ComponentReference> resolveComponent,");
		// 	sb.AppendLine($"{indent}\t\tglobal::System.Action<string, global::Voltage.Serialization.EntityReference> resolveEntity)");
		// 	sb.AppendLine($"{indent}\t{{");
		//
		// 	foreach (var m in model.Members)
		// 	{
		// 		var dataFieldName = GetDataFieldName(m.Name);
		// 		if (m.IsComponentReference)
		// 			sb.AppendLine($"{indent}\t\tresolveComponent(\"{m.Name}\", this.{dataFieldName});");
		// 		else if (m.IsEntityReference || m.IsTransformReference)
		// 			sb.AppendLine($"{indent}\t\tresolveEntity(\"{m.Name}\", this.{dataFieldName});");
		// 	}
		//
		// 	sb.AppendLine($"{indent}\t}}");
		// 	sb.AppendLine();
		// }

		// ── ApplyResolvedReferences override (AOT-safe reference resolution) ─
		//
		// Direct field assignment — no reflection. Called by ComponentReferenceResolver
		// after scene load so Component/Entity/Transform references are restored
		// correctly in both Editor and NativeAOT published builds.
		bool hasAnyReferences = model.Members.Any(m =>
			m.IsComponentReference || m.IsEntityReference || m.IsTransformReference);

		if (hasAnyReferences)
		{
			sb.AppendLine($"{indent}\tpublic override void ApplyResolvedReferences(global::Voltage.ComponentData _data, global::Voltage.Scene _scene)");
			sb.AppendLine($"{indent}\t{{");
			sb.AppendLine($"{indent}\t\tif (_data is not {dataClassName} _d) return;");
			sb.AppendLine();

			foreach (var m in model.Members)
			{
				var dataFieldName = GetDataFieldName(m.Name);
				if (m.IsComponentReference)
				{
					sb.AppendLine($"{indent}\t\tif (_d.{dataFieldName}.IsValid)");
					sb.AppendLine($"{indent}\t\t{{");
					sb.AppendLine($"{indent}\t\t\tvar _resolved_{m.Name} = global::Voltage.Serialization.ComponentReferenceResolver.FindComponentAot(_scene, _d.{dataFieldName});");
					sb.AppendLine($"{indent}\t\t\tif (_resolved_{m.Name} is {m.TypeFullName} _typed_{m.Name})");
					sb.AppendLine($"{indent}\t\t\t\tthis.{m.Name} = _typed_{m.Name};");
					sb.AppendLine($"{indent}\t\t\telse");
					sb.AppendLine($"{indent}\t\t\t\tglobal::Voltage.Debug.Error($\"[{{GetType().Name}}] Could not resolve ComponentReference for field '{m.Name}' — component not found in scene.\");");
					sb.AppendLine($"{indent}\t\t}}");
				}
				else if (m.IsEntityReference)
				{
					sb.AppendLine($"{indent}\t\tif (_d.{dataFieldName}.IsValid)");
					sb.AppendLine($"{indent}\t\t{{");
					sb.AppendLine($"{indent}\t\t\tvar _resolvedEntity_{m.Name} = global::Voltage.Serialization.ComponentReferenceResolver.FindEntityAot(_scene, _d.{dataFieldName});");
					sb.AppendLine($"{indent}\t\t\tif (_resolvedEntity_{m.Name} != null)");
					sb.AppendLine($"{indent}\t\t\t\tthis.{m.Name} = _resolvedEntity_{m.Name};");
					sb.AppendLine($"{indent}\t\t\telse");
					sb.AppendLine($"{indent}\t\t\t\tglobal::Voltage.Debug.Error($\"[{{GetType().Name}}] Could not resolve EntityReference for field '{m.Name}' — entity not found in scene.\");");
					sb.AppendLine($"{indent}\t\t}}");
				}
				else if (m.IsTransformReference)
				{
					sb.AppendLine($"{indent}\t\tif (_d.{dataFieldName}.IsValid)");
					sb.AppendLine($"{indent}\t\t{{");
					sb.AppendLine($"{indent}\t\t\tvar _resolvedTransform_{m.Name} = global::Voltage.Serialization.ComponentReferenceResolver.FindEntityAot(_scene, _d.{dataFieldName});");
					sb.AppendLine($"{indent}\t\t\tif (_resolvedTransform_{m.Name} != null)");
					sb.AppendLine($"{indent}\t\t\t\tthis.{m.Name} = _resolvedTransform_{m.Name}.Transform;");
					sb.AppendLine($"{indent}\t\t\telse");
					sb.AppendLine($"{indent}\t\t\t\tglobal::Voltage.Debug.Error($\"[{{GetType().Name}}] Could not resolve TransformReference for field '{m.Name}' — entity not found in scene.\");");
					sb.AppendLine($"{indent}\t\t}}");
				}
			}

			sb.AppendLine($"{indent}\t}}");
			sb.AppendLine();
		}

		// ── [ModuleInitializer] registration ────────────────────────────────
		//
		// Registers the AOT deserializer so TryDeserialize can find this type at runtime.
		// Uses the JsonTokenReader-based reader above — zero reflection, NativeAOT-safe.
		sb.AppendLine($"{indent}\t[System.Runtime.CompilerServices.ModuleInitializer]");
		sb.AppendLine($"{indent}\tinternal static void __RegisterDeserializer_{model.ClassName}()");
		sb.AppendLine($"{indent}\t{{");
		sb.AppendLine($"{indent}\t\tglobal::Voltage.Serialization.ComponentDataAotDeserializer.Register(");
		sb.AppendLine($"{indent}\t\t\ttypeof({dataClassName}).FullName,");
		sb.AppendLine($"{indent}\t\t\tstatic _json =>");
		sb.AppendLine($"{indent}\t\t\t{{");
		sb.AppendLine($"{indent}\t\t\t\tusing var _r = new global::Voltage.Persistence.JsonTokenReader(_json);");
		sb.AppendLine($"{indent}\t\t\t\treturn Read_{dataClassName}(_r);");
		sb.AppendLine($"{indent}\t\t\t}});");
		sb.AppendLine($"{indent}\t}}");

		sb.AppendLine($"{indent}}}");

		if (model.FullNamespace is not null)
			sb.AppendLine("}");

		spc.AddSource($"{model.ClassName}.ComponentData.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
	}

	/// <summary>
	/// Emits a private static Read_TypeName(JsonTokenReader) method for a user-defined
	/// struct type and all user-defined struct types it contains (depth-first).
	/// Skips known engine types that already have readers in AotDeserializers.
	/// </summary>
	private static void EmitStructReaders(StringBuilder sb, string indent,
		INamedTypeSymbol structType, HashSet<string> emitted)
	{
		var fullName = structType.ToDisplayString();
		if (emitted.Contains(fullName))
			return;
		emitted.Add(fullName);

		// Recurse into nested struct fields first (depth-first)
		foreach (var member in structType.GetMembers())
		{
			if (member is IFieldSymbol f && !f.IsStatic && !f.IsConst && !f.IsImplicitlyDeclared
				&& f.Type is INamedTypeSymbol ft
				&& ft.IsValueType && !ft.IsPrimitive() && ft.EnumUnderlyingType == null
				&& !KnownEngineStructReaders.Contains(ft.ToDisplayString()))
			{
				EmitStructReaders(sb, indent, ft, emitted);
			}
		}

		var readerName = GetStructReaderMethodName(fullName);

		sb.AppendLine($"{indent}private static {structType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)} {readerName}(global::Voltage.Persistence.JsonTokenReader _r)");
		sb.AppendLine($"{indent}{{");
		sb.AppendLine($"{indent}\tvar _v = new {structType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}();");
		sb.AppendLine($"{indent}\tif (!_r.BeginObject()) return _v;");
		sb.AppendLine($"{indent}\twhile (_r.ReadNextKey(out var _k))");
		sb.AppendLine($"{indent}\t{{");
		sb.AppendLine($"{indent}\t\tswitch (_k)");
		sb.AppendLine($"{indent}\t\t{{");

		foreach (var member in structType.GetMembers())
		{
			if (member is not IFieldSymbol field)
				continue;
			if (field.IsStatic || field.IsConst || field.IsImplicitlyDeclared)
				continue;
			if (field.DeclaredAccessibility != Accessibility.Public)
				continue;

			var readExpr = GetReadExpression(field.Type, "_r");
			sb.AppendLine($"{indent}\t\t\tcase \"{field.Name}\": _v.{field.Name} = {readExpr}; break;");
		}

		sb.AppendLine($"{indent}\t\t\tdefault: _r.SkipValue(); break;");
		sb.AppendLine($"{indent}\t\t}}");
		sb.AppendLine($"{indent}\t}}");
		sb.AppendLine($"{indent}\treturn _v;");
		sb.AppendLine($"{indent}}}");
		sb.AppendLine();
	}

	/// <summary>
	/// Returns true when a user-defined struct has an explicit (non-compiler-generated)
	/// parameterless constructor — i.e. the user set default field values inside it.
	/// The AOT reader uses <c>new T()</c> which ignores those defaults entirely.
	/// </summary>
	private static bool HasExplicitParameterlessConstructor(INamedTypeSymbol structType)
	{
		foreach (var ctor in structType.InstanceConstructors)
		{
			if (!ctor.IsImplicitlyDeclared && ctor.Parameters.Length == 0)
				return true;
		}
		return false;
	}

	/// <summary>
	/// Emits two static helpers for each <see cref="IComponentGroup"/> class type encountered
	/// (depth-first, recursing into nested groups and user structs):
	/// <list type="bullet">
	///   <item><c>Clone_X(X src) → X</c> — deep-copies the group for snapshot storage in ComponentData.</item>
	///   <item><c>Read_X(JsonTokenReader) → X</c> — AOT-safe deserializer called by the ComponentData reader.</item>
	/// </list>
	/// </summary>
	private static void EmitGroupReaders(StringBuilder sb, string indent,
		INamedTypeSymbol groupType, HashSet<string> emitted)
	{
		var fullName = groupType.ToDisplayString();
		if (emitted.Contains(fullName))
			return;
		emitted.Add(fullName);

		// Recurse into nested IComponentGroup fields first (depth-first)
		foreach (var member in groupType.GetMembers())
		{
			if (member is IFieldSymbol f && !f.IsStatic && !f.IsConst && !f.IsImplicitlyDeclared
				&& f.Type is INamedTypeSymbol ft && IsComponentGroupType(ft))
			{
				EmitGroupReaders(sb, indent, ft, emitted);
			}
		}

		// Also emit readers for any user-defined struct fields within the group
		foreach (var member in groupType.GetMembers())
		{
			if (member is IFieldSymbol f && !f.IsStatic && !f.IsConst && !f.IsImplicitlyDeclared
				&& f.Type is INamedTypeSymbol ft
				&& ft.IsValueType && !ft.IsPrimitive() && ft.EnumUnderlyingType == null
				&& !KnownEngineStructReaders.Contains(ft.ToDisplayString()))
			{
				EmitStructReaders(sb, indent, ft, emitted);
			}
		}

		var fqn = groupType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
		var readerName = GetStructReaderMethodName(fullName);
		var cloneName = GetCloneMethodName(fullName);

		// ── Clone_ method ────────────────────────────────────────────────────
		sb.AppendLine($"{indent}private static {fqn} {cloneName}({fqn} _src)");
		sb.AppendLine($"{indent}{{");
		sb.AppendLine($"{indent}\tif (_src == null) return null;");
		sb.AppendLine($"{indent}\tvar _v = new {fqn}();");

		foreach (var member in groupType.GetMembers())
		{
			if (member is not IFieldSymbol field) continue;
			if (field.IsStatic || field.IsConst || field.IsImplicitlyDeclared) continue;
			if (field.DeclaredAccessibility != Accessibility.Public) continue;

			if (IsComponentGroupType(field.Type))
			{
				var nestedClone = GetCloneMethodName(field.Type.ToDisplayString());
				sb.AppendLine($"{indent}\t_v.{field.Name} = {nestedClone}(_src.{field.Name});");
			}
			else
			{
				// Value types (structs, primitives) and strings are copied directly
				sb.AppendLine($"{indent}\t_v.{field.Name} = _src.{field.Name};");
			}
		}

		sb.AppendLine($"{indent}\treturn _v;");
		sb.AppendLine($"{indent}}}");
		sb.AppendLine();

		// ── Read_ method ─────────────────────────────────────────────────────
		sb.AppendLine($"{indent}private static {fqn} {readerName}(global::Voltage.Persistence.JsonTokenReader _r)");
		sb.AppendLine($"{indent}{{");
		sb.AppendLine($"{indent}\tvar _v = new {fqn}();");
		sb.AppendLine($"{indent}\tif (!_r.BeginObject()) return _v;");
		sb.AppendLine($"{indent}\twhile (_r.ReadNextKey(out var _k))");
		sb.AppendLine($"{indent}\t{{");
		sb.AppendLine($"{indent}\t\tswitch (_k)");
		sb.AppendLine($"{indent}\t\t{{");

		foreach (var member in groupType.GetMembers())
		{
			if (member is not IFieldSymbol field) continue;
			if (field.IsStatic || field.IsConst || field.IsImplicitlyDeclared) continue;
			if (field.DeclaredAccessibility != Accessibility.Public) continue;

			var readExpr = GetReadExpression(field.Type, "_r");
			sb.AppendLine($"{indent}\t\t\tcase \"{field.Name}\": _v.{field.Name} = {readExpr}; break;");
		}

		sb.AppendLine($"{indent}\t\t\tdefault: _r.SkipValue(); break;");
		sb.AppendLine($"{indent}\t\t}}");
		sb.AppendLine($"{indent}\t}}");
		sb.AppendLine($"{indent}\treturn _v;");
		sb.AppendLine($"{indent}}}");
		sb.AppendLine();
	}

	/// <summary>
	/// Emits the top-level Read_XxxGeneratedData(JsonTokenReader) for the ComponentData class.
	/// </summary>
	private static void EmitComponentDataReader(StringBuilder sb, string indent,
		string dataClassName, List<MemberModel> members)
	{
		sb.AppendLine($"{indent}private static {dataClassName} Read_{dataClassName}(global::Voltage.Persistence.JsonTokenReader _r)");
		sb.AppendLine($"{indent}{{");
		sb.AppendLine($"{indent}\tvar _d = new {dataClassName}();");
		sb.AppendLine($"{indent}\tif (!_r.BeginObject()) return _d;");
		sb.AppendLine($"{indent}\twhile (_r.ReadNextKey(out var _k))");
		sb.AppendLine($"{indent}\t{{");
		sb.AppendLine($"{indent}\t\tswitch (_k)");
		sb.AppendLine($"{indent}\t\t{{");
		sb.AppendLine($"{indent}\t\t\tcase \"Enabled\": _d.Enabled = _r.ReadBool(); break;");
		sb.AppendLine($"{indent}\t\t\tcase \"CanBeSelected\": _d.CanBeSelected = _r.ReadBool(); break;");
		sb.AppendLine($"{indent}\t\t\tcase \"UpdateOrder\": _d.UpdateOrder = _r.ReadInt(); break;");

		foreach (var m in members)
		{
			var dataFieldName = GetDataFieldName(m.Name);
			string readExpr;
			if (m.IsComponentReference || m.IsEntityReference || m.IsTransformReference)
			{
				// ComponentReference and EntityReference are structs with string fields —
				// they are handled by dedicated readers in AotDeserializers.
				var knownCall = m.IsComponentReference
					? "global::Voltage.Serialization.AotDeserializers.ReadComponentReference(_r)"
					: "global::Voltage.Serialization.AotDeserializers.ReadEntityReference(_r)";
				readExpr = knownCall;
			}
			else if (m.IsComponentGroup && m.UserClassTypeSymbol != null)
			{
				var readerMethod = GetStructReaderMethodName(m.UserClassTypeSymbol.ToDisplayString());
				readExpr = $"{readerMethod}(_r)";
			}
			else
			{
				readExpr = GetReadExpressionForMember(m, "_r");
			}
			sb.AppendLine($"{indent}\t\t\tcase \"{dataFieldName}\": _d.{dataFieldName} = {readExpr}; break;");
		}

		sb.AppendLine($"{indent}\t\t\tdefault: _r.SkipValue(); break;");
		sb.AppendLine($"{indent}\t\t}}");
		sb.AppendLine($"{indent}\t}}");
		sb.AppendLine($"{indent}\treturn _d;");
		sb.AppendLine($"{indent}}}");
		sb.AppendLine();
	}

	/// <summary>
	/// Returns the read expression for a field/property based on its type symbol.
	/// </summary>
	private static string GetReadExpression(ITypeSymbol type, string readerParam)
	{
		// Primitives and string
		switch (type.SpecialType)
		{
			case SpecialType.System_Boolean: return $"{readerParam}.ReadBool()";
			case SpecialType.System_Byte:
			case SpecialType.System_SByte:
			case SpecialType.System_Int16:
			case SpecialType.System_UInt16:
			case SpecialType.System_Int32:
			case SpecialType.System_UInt32: return $"{readerParam}.ReadInt()";
			case SpecialType.System_Int64:
			case SpecialType.System_UInt64: return $"{readerParam}.ReadLong()";
			case SpecialType.System_Single: return $"{readerParam}.ReadFloat()";
			case SpecialType.System_Double: return $"{readerParam}.ReadDouble()";
			case SpecialType.System_String: return $"{readerParam}.ReadString()";
		}

		var fullName = type.ToDisplayString();

		// Enum
		if (type is INamedTypeSymbol { EnumUnderlyingType: not null } enumType)
			return $"{readerParam}.ReadEnum<{enumType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}>()";

		// Known engine struct types with pre-existing readers in AotDeserializers
		if (KnownEngineStructReadCall.TryGetValue(fullName, out var knownCall))
			return knownCall.Replace("_r", readerParam);

		// User-defined value type (struct) — call the generated reader
		if (type.IsValueType)
		{
			var readerMethod = GetStructReaderMethodName(fullName);
			return $"{readerMethod}({readerParam})";
		}

		// IComponentGroup class type — call the generated class reader
		if (IsComponentGroupType(type))
		{
			var readerMethod = GetStructReaderMethodName(fullName);
			return $"{readerMethod}({readerParam})";
		}

		// Fallback: skip unrecognised types
		return $"default /* unsupported type: {fullName} */";
	}

	/// <summary>
	/// Overload that works from a <see cref="MemberModel"/> (already has pre-computed type info).
	/// </summary>
	private static string GetReadExpressionForMember(MemberModel m, string readerParam)
	{
		if (m.IsComponentGroup && m.UserClassTypeSymbol != null)
		{
			var readerMethod = GetStructReaderMethodName(m.UserClassTypeSymbol.ToDisplayString());
			return $"{readerMethod}({readerParam})";
		}

		if (m.UserStructTypeSymbol != null)
		{
			// User-defined struct — call the generated reader
			var readerMethod = GetStructReaderMethodName(m.UserStructTypeSymbol.ToDisplayString());
			return $"{readerMethod}({readerParam})";
		}

		// Delegate to the type-symbol based overload using a lightweight probe
		// We encode the type kind in TypeFullName via the existing field
		return GetReadExpressionFromTypeName(m.TypeFullName, readerParam);
	}

	/// <summary>
	/// Fallback read-expression resolver based on the stored fully-qualified type name.
	/// Used when a <see cref="MemberModel"/> has no <see cref="MemberModel.UserStructTypeSymbol"/>.
	/// </summary>
	private static string GetReadExpressionFromTypeName(string typeFullName, string readerParam)
	{
		switch (typeFullName)
		{
			case "bool":
			case "global::System.Boolean": return $"{readerParam}.ReadBool()";
			case "int":
			case "global::System.Int32": return $"{readerParam}.ReadInt()";
			case "uint":
			case "global::System.UInt32": return $"(uint){readerParam}.ReadInt()";
			case "long":
			case "global::System.Int64": return $"{readerParam}.ReadLong()";
			case "float":
			case "global::System.Single": return $"{readerParam}.ReadFloat()";
			case "double":
			case "global::System.Double": return $"{readerParam}.ReadDouble()";
			case "string":
			case "global::System.String": return $"{readerParam}.ReadString()";
			case "global::Microsoft.Xna.Framework.Vector2": return $"global::Voltage.Serialization.AotDeserializers.ReadVector2({readerParam})";
			case "global::Microsoft.Xna.Framework.Color": return $"global::Voltage.Serialization.AotDeserializers.ReadColor({readerParam})";
			case "global::Voltage.RectangleF": return $"global::Voltage.Serialization.AotDeserializers.ReadRectangleF({readerParam})";
		}

		// Generic enum detection is not possible from name alone; leave as skip
		return $"default /* unsupported type: {typeFullName} */";
	}

	/// <summary>
	/// Derives a valid C# method name from a type's full name.
	/// e.g. "Jolt.Scripts.PlayerData+Movement" → "Read_PlayerData_Movement"
	/// </summary>
	private static string GetStructReaderMethodName(string typeFullName)
	{
		// Replace namespace dots and nested-type "+" separator with "_"
		var name = typeFullName
			.Replace('.', '_')
			.Replace('+', '_')
			.Replace('<', '_')
			.Replace('>', '_')
			.Replace(',', '_')
			.Replace(' ', '_');
		return "Read_" + name;
	}

	/// <summary>
	/// Derives the Clone_ method name from a type's full name.
	/// e.g. "Jolt.Scripts.PlayerData+Movement" → "Clone_PlayerData_Movement"
	/// </summary>
	private static string GetCloneMethodName(string typeFullName)
	{
		var name = typeFullName
			.Replace('.', '_')
			.Replace('+', '_')
			.Replace('<', '_')
			.Replace('>', '_')
			.Replace(',', '_')
			.Replace(' ', '_');
		return "Clone_" + name;
	}

	#endregion

	#region AOT Bootstrap Emission (Pipeline 2)

	/// <summary>
	/// Emits a [ModuleInitializer] that registers every concrete Component in
	/// ComponentAotFactory. This is all that's needed — serialization is handled
	/// by Voltage.Persistence.Json + the generated Data property, not STJ.
	/// </summary>
	private static void EmitAotBootstrap(SourceProductionContext spc, ImmutableArray<BootstrapEntry> components)
	{
		if (components.IsDefaultOrEmpty)
			return;

		var unique = components
			.GroupBy(c => c.ComponentFullName)
			.Select(g => g.First())
			.ToList();

		var sb = new StringBuilder();
		sb.AppendLine("// <auto-generated/>");
		sb.AppendLine("#nullable disable");
		sb.AppendLine("using Voltage.Serialization.Registries;");
		sb.AppendLine();

		var namespaces = unique
			.Select(c => c.Namespace)
			.Where(n => n != null)
			.Distinct()
			.OrderBy(n => n);
		foreach (var ns in namespaces)
			sb.AppendLine($"using {ns};");

		sb.AppendLine();
		sb.AppendLine("namespace Voltage.Serialization.Generated");
		sb.AppendLine("{");
		sb.AppendLine("\tinternal static class ComponentDataAotBootstrap");
		sb.AppendLine("\t{");
		sb.AppendLine("\t\tprivate static bool _inited;");
		sb.AppendLine();
		sb.AppendLine("\t\t[System.Runtime.CompilerServices.ModuleInitializer]");
		sb.AppendLine("\t\tinternal static void AutoRegister()");
		sb.AppendLine("\t\t{");
		sb.AppendLine("\t\t\tif (_inited) return;");
		sb.AppendLine("\t\t\t_inited = true;");
		sb.AppendLine();

		foreach (var entry in unique)
		{
			// Always register the component factory
			sb.AppendLine($"\t\t\tComponentAotFactory.Register(");
			sb.AppendLine($"\t\t\t\ttypeof(global::{entry.ComponentFullName}).FullName,");
			sb.AppendLine($"\t\t\t\t() => new global::{entry.ComponentFullName}());");

			// Only emit deserializer for components with a MANUAL Data override.
			// Auto-generated components register their own deserializer via
			// __RegisterDeserializer_ emitted by EmitDataClass — which now uses
			// the JsonTokenReader-based reader, so it is fully NativeAOT-safe.
			if (entry.ManualDataTypeFullName != null)
			{
				sb.AppendLine();
				sb.AppendLine($"\t\t\tglobal::Voltage.Serialization.ComponentDataAotDeserializer.Register(");
				sb.AppendLine($"\t\t\t\ttypeof({entry.ManualDataTypeFullName}).FullName,");
				sb.AppendLine($"\t\t\t\tstatic _json => (global::Voltage.ComponentData)global::Voltage.Persistence.Json.FromJson(_json, typeof({entry.ManualDataTypeFullName})));");
			}

			sb.AppendLine();
		}

		sb.AppendLine("\t\t}");
		sb.AppendLine("\t}");
		sb.AppendLine("}");

		spc.AddSource("ComponentDataAotBootstrap.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
	}

	#endregion

	#region Helpers

	private static bool DerivesFrom(INamedTypeSymbol symbol, string baseFullName)
	{
		var current = symbol.BaseType;
		while (current is not null)
		{
			if (current.ToDisplayString() == baseFullName)
				return true;
			current = current.BaseType;
		}
		return false;
	}

	private static bool HasDataOverride(INamedTypeSymbol symbol)
	{
		foreach (var member in symbol.GetMembers())
		{
			if (member is IPropertySymbol prop &&
				prop.Name == "Data" &&
				prop.IsOverride &&
				SymbolEqualityComparer.Default.Equals(prop.ContainingType, symbol))
			{
				return true;
			}
		}
		return false;
	}

	/// <summary>
	/// Returns true when the type implements <c>Voltage.IComponentGroup</c>.
	/// Checks by fully-qualified name first, then falls back to name + namespace
	/// to handle cases where the interface symbol is unresolved at generator time.
	/// </summary>
	private static bool IsComponentGroupType(ITypeSymbol type)
	{
		if (type == null || type.IsValueType) return false;
		if (type is not INamedTypeSymbol named) return false;

		foreach (var iface in named.AllInterfaces)
		{
			// Primary: fully-qualified match
			if (iface.ToDisplayString() == ComponentGroupInterface)
				return true;

			// Fallback: match by name + containing namespace in case the symbol
			// is unresolved or its display string differs (e.g. error type).
			if (iface.Name == "IComponentGroup" &&
				iface.ContainingNamespace?.ToDisplayString() == "Voltage")
				return true;
		}

		return false;
	}

	private static bool IsComponentType(ITypeSymbol type, Compilation compilation)
	{
		if (type == null || type.IsValueType || type.SpecialType == SpecialType.System_String)
			return false;

		var componentSymbol = compilation.GetTypeByMetadataName(ComponentBaseFullName);
		if (componentSymbol == null)
			return false;

		var current = type;
		while (current != null)
		{
			if (SymbolEqualityComparer.Default.Equals(current, componentSymbol))
				return true;
			current = current.BaseType;
		}
		return false;
	}

	private static bool IsEntityType(ITypeSymbol type, Compilation compilation)
	{
		if (type == null || type.IsValueType)
			return false;

		var entitySymbol = compilation.GetTypeByMetadataName(EntityFullName);
		return entitySymbol != null && SymbolEqualityComparer.Default.Equals(type, entitySymbol);
	}

	private static bool IsTransformType(ITypeSymbol type, Compilation compilation)
	{
		if (type == null || type.IsValueType)
			return false;

		var transformSymbol = compilation.GetTypeByMetadataName(TransformFullName);
		return transformSymbol != null && SymbolEqualityComparer.Default.Equals(type, transformSymbol);
	}

	/// <summary>
	/// Returns true when the type is a user-defined struct that needs a generated reader.
	/// Excludes primitives, enums, strings, and known engine struct types.
	/// </summary>
	private static bool IsUserDefinedStruct(ITypeSymbol type)
	{
		if (type == null || !type.IsValueType)
			return false;
		if (type.SpecialType != SpecialType.None)
			return false; // primitive or System.* special type
		if (type is INamedTypeSymbol { EnumUnderlyingType: not null } enumType)
			return false; // enum
		if (KnownEngineStructReaders.Contains(type.ToDisplayString()))
			return false;
		return true;
	}

	private static List<MemberModel> CollectSerializableMembers(
		INamedTypeSymbol componentType, Compilation compilation,
		out List<string> skippedFieldNames,
		out List<string> structsWithExplicitCtorFields)
	{
		var members = new List<MemberModel>();
		skippedFieldNames = new List<string>();
		structsWithExplicitCtorFields = new List<string>();
		var currentType = componentType;

		while (currentType is not null && currentType.ToDisplayString() != ComponentBaseFullName)
		{
			foreach (var member in currentType.GetMembers())
			{
				if (member is IFieldSymbol field)
				{
					if (field.IsStatic || field.IsConst || field.IsImplicitlyDeclared)
						continue;
					if (field.Name.StartsWith("<"))
						continue;
					if (HasAttribute(field, HideAttribute) || HasAttribute(field, JsonExcludeAttribute))
						continue;

					bool hasSerializedField = HasAttribute(field, SerializedFieldAttribute);
					bool hasInspectable = HasAttributeOrDerived(field, InspectableAttribute);
					bool isComponentRef = IsComponentType(field.Type, compilation);
					bool isEntityRef = IsEntityType(field.Type, compilation);
					bool isTransformRef = IsTransformType(field.Type, compilation);
					bool isComponentGroup = IsComponentGroupType(field.Type);

					// Skip class references that are not Component/Entity/Transform/IComponentGroup —
					// they cannot be reliably serialized and must not appear in ComponentData.
					if (!field.Type.IsValueType &&
						field.Type.SpecialType != SpecialType.System_String &&
						!isComponentRef && !isEntityRef && !isTransformRef && !isComponentGroup)
					{
						if (field.DeclaredAccessibility == Accessibility.Public)
							skippedFieldNames.Add($"{field.Name} ({field.Type.ToDisplayString()})");
						continue;
					}

					INamedTypeSymbol userStructSymbol = IsUserDefinedStruct(field.Type)
						? field.Type as INamedTypeSymbol
						: null;

					// VLT003: user-defined struct with an explicit parameterless constructor.
					// The AOT reader calls new T() and then reads JSON fields into it — any
					// defaults set inside the constructor body are silently discarded.
					// The correct pattern is to use IComponentGroup (a class) instead.
					if (userStructSymbol != null && HasExplicitParameterlessConstructor(userStructSymbol))
					{
						structsWithExplicitCtorFields.Add(
							$"{field.Name} ({userStructSymbol.ToDisplayString()})");
					}

					INamedTypeSymbol userClassSymbol = isComponentGroup
						? field.Type as INamedTypeSymbol
						: null;

					if (field.DeclaredAccessibility == Accessibility.Public)
					{
						members.Add(new MemberModel
						{
							Name = field.Name,
							TypeFullName = field.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
							IsProperty = false,
							IsPublic = true,
							IsComponentReference = isComponentRef,
							IsEntityReference = isEntityRef,
							IsTransformReference = isTransformRef,
							IsComponentGroup = isComponentGroup,
							UserStructTypeSymbol = userStructSymbol,
							UserClassTypeSymbol = userClassSymbol
						});
					}
					else if (hasSerializedField || hasInspectable)
					{
						members.Add(new MemberModel
						{
							Name = field.Name,
							TypeFullName = field.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
							IsProperty = false,
							IsPublic = false,
							IsComponentReference = isComponentRef,
							IsEntityReference = isEntityRef,
							IsTransformReference = isTransformRef,
							IsComponentGroup = isComponentGroup,
							UserStructTypeSymbol = userStructSymbol,
							UserClassTypeSymbol = userClassSymbol
						});
					}
				}
				else if (member is IPropertySymbol prop)
				{
					if (prop.IsStatic || prop.IsImplicitlyDeclared || prop.IsIndexer)
						continue;
					if (prop.Name is "Data" or "Enabled" or "UpdateOrder" or "Name" or "IsSerialized"
								 or "Transform" or "Entity" or "CanBeSelected")
						continue;
					if (HasAttribute(prop, HideAttribute) || HasAttribute(prop, JsonExcludeAttribute))
						continue;
					if (prop.GetMethod is null)
						continue;

					bool hasSerializedField = HasAttribute(prop, SerializedFieldAttribute);
					bool hasInspectable = HasAttributeOrDerived(prop, InspectableAttribute);
					bool hasPublicGetter = prop.GetMethod.DeclaredAccessibility == Accessibility.Public;
					bool hasPublicSetter = prop.SetMethod?.DeclaredAccessibility == Accessibility.Public;
					bool isComponentRef = IsComponentType(prop.Type, compilation);
					bool isEntityRef = IsEntityType(prop.Type, compilation);
					bool isTransformRef = IsTransformType(prop.Type, compilation);
					bool isComponentGroup = IsComponentGroupType(prop.Type);

					if (!prop.Type.IsValueType &&
						prop.Type.SpecialType != SpecialType.System_String &&
						!isComponentRef && !isEntityRef && !isTransformRef && !isComponentGroup)
						continue;

					INamedTypeSymbol userStructSymbol = IsUserDefinedStruct(prop.Type)
						? prop.Type as INamedTypeSymbol
						: null;

					if (userStructSymbol != null && HasExplicitParameterlessConstructor(userStructSymbol))
					{
						structsWithExplicitCtorFields.Add(
							$"{prop.Name} ({userStructSymbol.ToDisplayString()})");
					}

					INamedTypeSymbol userClassSymbol = isComponentGroup
						? prop.Type as INamedTypeSymbol
						: null;

					if (hasPublicGetter && hasPublicSetter)
					{
						members.Add(new MemberModel
						{
							Name = prop.Name,
							TypeFullName = prop.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
							IsProperty = true,
							IsPublic = true,
							IsComponentReference = isComponentRef,
							IsEntityReference = isEntityRef,
							IsTransformReference = isTransformRef,
							IsComponentGroup = isComponentGroup,
							UserStructTypeSymbol = userStructSymbol,
							UserClassTypeSymbol = userClassSymbol
						});
					}
					else if ((hasSerializedField || hasInspectable) && prop.SetMethod is not null)
					{
						members.Add(new MemberModel
						{
							Name = prop.Name,
							TypeFullName = prop.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
							IsProperty = true,
							IsPublic = false,
							IsComponentReference = isComponentRef,
							IsEntityReference = isEntityRef,
							IsTransformReference = isTransformRef,
							IsComponentGroup = isComponentGroup,
							UserStructTypeSymbol = userStructSymbol,
							UserClassTypeSymbol = userClassSymbol
						});
					}
				}
			}
			currentType = currentType.BaseType;
		}
		return members;
	}

	private static bool HasAttribute(ISymbol symbol, string fullName)
	{
		return symbol.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == fullName);
	}

	private static bool HasAttributeOrDerived(ISymbol symbol, string baseFullName)
	{
		foreach (var attr in symbol.GetAttributes())
		{
			var attrClass = attr.AttributeClass;
			while (attrClass is not null)
			{
				if (attrClass.ToDisplayString() == baseFullName)
					return true;
				attrClass = attrClass.BaseType;
			}
		}
		return false;
	}

	private static string GetDataFieldName(string memberName)
	{
		if (memberName.StartsWith("_") && memberName.Length > 1)
			return char.ToUpperInvariant(memberName[1]) + memberName.Substring(2);
		return memberName;
	}

	#endregion

	#region Models

	private struct ComponentModel
	{
		public string ClassName;
		public string FullyQualifiedName;
		public string FullNamespace;
		public List<MemberModel> Members;
		/// <summary>
		/// Public fields that were encountered but skipped because their type was
		/// not recognized as serializable. Populated for diagnostic VLT002.
		/// </summary>
		public List<string> SkippedFieldNames;
		/// <summary>
		/// User-defined struct fields that have an explicit parameterless constructor.
		/// Those constructor defaults are lost during AOT deserialization — VLT003.
		/// </summary>
		public List<string> StructsWithExplicitCtorFields;
		public bool HasManualDataOverride;
		public Location DiagnosticLocation;
	}

	private struct MemberModel
	{
		public string Name;
		public string TypeFullName;
		public bool IsProperty;
		public bool IsPublic;
		public bool IsComponentReference;
		public bool IsEntityReference;
		public bool IsTransformReference;
		/// <summary>
		/// True when this member's type implements <c>Voltage.IComponentGroup</c>.
		/// Mutually exclusive with <see cref="UserStructTypeSymbol"/>.
		/// </summary>
		public bool IsComponentGroup;
		/// <summary>
		/// Set when this member's type is a user-defined struct that needs a generated
		/// JsonTokenReader reader. Null for primitives, enums, strings, known engine types,
		/// and IComponentGroup types.
		/// </summary>
		public INamedTypeSymbol UserStructTypeSymbol;
		/// <summary>
		/// Set when <see cref="IsComponentGroup"/> is true. Holds the class symbol so
		/// <c>EmitGroupReaders</c> can iterate the group's fields.
		/// </summary>
		public INamedTypeSymbol UserClassTypeSymbol;
	}

	private struct BootstrapEntry
	{
		public string ComponentFullName;
		public string Namespace;
		public string ManualDataTypeFullName;
	}

	#endregion
}

/// <summary>
/// Extension helpers for <see cref="INamedTypeSymbol"/> that are not available in
/// netstandard2.0 but are trivial to implement.
/// </summary>
internal static class TypeSymbolExtensions
{
	/// <summary>Returns true when the type is a CLR primitive (bool, int, float, etc.).</summary>
	internal static bool IsPrimitive(this INamedTypeSymbol type)
	{
		switch (type.SpecialType)
		{
			case SpecialType.System_Boolean:
			case SpecialType.System_Byte:
			case SpecialType.System_SByte:
			case SpecialType.System_Char:
			case SpecialType.System_Int16:
			case SpecialType.System_UInt16:
			case SpecialType.System_Int32:
			case SpecialType.System_UInt32:
			case SpecialType.System_Int64:
			case SpecialType.System_UInt64:
			case SpecialType.System_Single:
			case SpecialType.System_Double:
			case SpecialType.System_Decimal:
				return true;
			default:
				return false;
		}
	}
}
