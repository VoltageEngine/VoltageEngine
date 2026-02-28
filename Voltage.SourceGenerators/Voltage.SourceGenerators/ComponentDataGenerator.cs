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
/// Roslyn incremental source generator that emits a concrete ComponentData subclass and
/// Data property override for every partial Component subclass that doesn't already have
/// a manual Data override.
///
/// The generated code is AOT-safe: zero reflection, direct field access only.
///
/// Serialization rules (matching editor inspector / AutoComponentData):
/// - Public fields: always serialized (unless [HideAttributeInInspector] or [JsonExclude])
/// - Public properties with public getter + setter: always serialized
/// - Private/protected fields with [SerializedField] or [Inspectable]: serialized
/// - Members declared on Component base or above: excluded
/// </summary>
[Generator]
public class ComponentDataGenerator : IIncrementalGenerator
{
    // Fully qualified attribute names used for filtering
    private const string HideAttribute = "Voltage.HideAttributeInInspector";
    private const string JsonExcludeAttribute = "Voltage.Persistence.JsonExcludeAttribute";
    private const string SerializedFieldAttribute = "Voltage.SerializedFieldAttribute";
    private const string InspectableAttribute = "Voltage.InspectableAttribute";
    private const string ComponentBaseFullName = "Voltage.Component";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Find all partial class declarations that derive from Component
        var candidates = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => IsPartialClassCandidate(node),
                transform: static (ctx, ct) => GetComponentClassOrNull(ctx, ct))
            .Where(static x => x is not null)
            .Select(static (x, _) => x!.Value);

        context.RegisterSourceOutput(candidates, static (spc, model) => Execute(spc, model));
    }

    /// <summary>
    /// Quick syntactic filter: is this a partial class declaration?
    /// </summary>
    private static bool IsPartialClassCandidate(SyntaxNode node)
    {
        return node is ClassDeclarationSyntax cds &&
               cds.Modifiers.Any(SyntaxKind.PartialKeyword);
    }

    /// <summary>
    /// Semantic filter: does this partial class derive from Voltage.Component,
    /// and does it NOT already override the Data property?
    /// </summary>
    private static ComponentModel? GetComponentClassOrNull(GeneratorSyntaxContext ctx, System.Threading.CancellationToken ct)
    {
        var classDecl = (ClassDeclarationSyntax)ctx.Node;
        var symbol = ctx.SemanticModel.GetDeclaredSymbol(classDecl, ct);
        if (symbol is null || symbol.IsAbstract)
            return null;

        // Check if it derives from Voltage.Component
        if (!DerivesFrom(symbol, ComponentBaseFullName))
            return null;

        // Check if it already overrides the Data property (manual override takes priority)
        if (HasDataOverride(symbol))
            return null;

        // Collect serializable members
        var members = CollectSerializableMembers(symbol);
        if (members.Count == 0)
            return null;

        return new ComponentModel
        {
            ClassName = symbol.Name,
            FullNamespace = symbol.ContainingNamespace.IsGlobalNamespace
                ? null
                : symbol.ContainingNamespace.ToDisplayString(),
            Members = members
        };
    }

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

    /// <summary>
    /// Checks if this type (not a base) explicitly overrides the Data property.
    /// </summary>
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
    /// Collects all members that should be serialized, using the same rules as
    /// AutoComponentData.GetSerializableMembers and TypeInspectorUtils.GetInspectableProperties.
    /// </summary>
    private static List<MemberModel> CollectSerializableMembers(INamedTypeSymbol componentType)
    {
        var members = new List<MemberModel>();
        var componentBase = componentType.BaseType;

        // Walk the type hierarchy from this type up (but stop at Component)
        var currentType = componentType;
        while (currentType is not null && currentType.ToDisplayString() != ComponentBaseFullName)
        {
            // Fields
            foreach (var member in currentType.GetMembers())
            {
                if (member is IFieldSymbol field)
                {
                    if (field.IsStatic || field.IsConst || field.IsImplicitlyDeclared)
                        continue;

                    // Skip compiler-generated backing fields
                    if (field.Name.StartsWith("<"))
                        continue;

                    if (HasAttribute(field, HideAttribute) || HasAttribute(field, JsonExcludeAttribute))
                        continue;

                    bool hasSerializedField = HasAttribute(field, SerializedFieldAttribute);
                    bool hasInspectable = HasAttributeOrDerived(field, InspectableAttribute);

                    if (field.DeclaredAccessibility == Accessibility.Public)
                    {
                        members.Add(new MemberModel
                        {
                            Name = field.Name,
                            TypeFullName = field.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                            IsProperty = false,
                            IsPublic = true
                        });
                    }
                    else if (hasSerializedField || hasInspectable)
                    {
                        members.Add(new MemberModel
                        {
                            Name = field.Name,
                            TypeFullName = field.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                            IsProperty = false,
                            IsPublic = false
                        });
                    }
                }
                else if (member is IPropertySymbol prop)
                {
                    if (prop.IsStatic || prop.IsImplicitlyDeclared || prop.IsIndexer)
                        continue;

                    if (prop.Name == "Data" || prop.Name == "Enabled" || prop.Name == "UpdateOrder" ||
                        prop.Name == "Name" || prop.Name == "IsSerialized" || prop.Name == "Transform" ||
                        prop.Name == "Entity")
                        continue;

                    if (HasAttribute(prop, HideAttribute) || HasAttribute(prop, JsonExcludeAttribute))
                        continue;

                    if (prop.GetMethod is null)
                        continue;

                    bool hasSerializedField = HasAttribute(prop, SerializedFieldAttribute);
                    bool hasInspectable = HasAttributeOrDerived(prop, InspectableAttribute);
                    bool hasPublicGetter = prop.GetMethod.DeclaredAccessibility == Accessibility.Public;
                    bool hasPublicSetter = prop.SetMethod?.DeclaredAccessibility == Accessibility.Public;

                    if (hasPublicGetter && hasPublicSetter)
                    {
                        members.Add(new MemberModel
                        {
                            Name = prop.Name,
                            TypeFullName = prop.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                            IsProperty = true,
                            IsPublic = true
                        });
                    }
                    else if ((hasSerializedField || hasInspectable) && prop.SetMethod is not null)
                    {
                        members.Add(new MemberModel
                        {
                            Name = prop.Name,
                            TypeFullName = prop.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                            IsProperty = true,
                            IsPublic = false
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

    /// <summary>
    /// Generates the source code for a component's Data class and property override.
    /// </summary>
    private static void Execute(SourceProductionContext spc, ComponentModel model)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable disable");
        sb.AppendLine();

        if (model.FullNamespace is not null)
        {
            sb.AppendLine($"namespace {model.FullNamespace}");
            sb.AppendLine("{");
        }

        var indent = model.FullNamespace is not null ? "    " : "";

        // Generate the partial class with Data override
        sb.AppendLine($"{indent}partial class {model.ClassName}");
        sb.AppendLine($"{indent}{{");

        // Inner ComponentData class
        var dataClassName = $"{model.ClassName}GeneratedData";
        sb.AppendLine($"{indent}    public sealed class {dataClassName} : global::Voltage.ComponentData");
        sb.AppendLine($"{indent}    {{");

        foreach (var m in model.Members)
        {
            // Use a clean data field name (strip leading underscore for private fields)
            var dataFieldName = GetDataFieldName(m.Name);
            sb.AppendLine($"{indent}        public {m.TypeFullName} {dataFieldName};");
        }

        sb.AppendLine($"{indent}    }}");
        sb.AppendLine();

        // --- Data property override ---
        sb.AppendLine($"{indent}    public override global::Voltage.ComponentData Data");
        sb.AppendLine($"{indent}    {{");

        // Getter
        sb.AppendLine($"{indent}        get");
        sb.AppendLine($"{indent}        {{");
        sb.AppendLine($"{indent}            var _data = new {dataClassName}();");
        sb.AppendLine($"{indent}            _data.Enabled = this.Enabled;");
        foreach (var m in model.Members)
        {
            var dataFieldName = GetDataFieldName(m.Name);
            sb.AppendLine($"{indent}            _data.{dataFieldName} = this.{m.Name};");
        }
        sb.AppendLine($"{indent}            return _data;");
        sb.AppendLine($"{indent}        }}");

        // Setter
        sb.AppendLine($"{indent}        set");
        sb.AppendLine($"{indent}        {{");
        sb.AppendLine($"{indent}            if (value is {dataClassName} _d)");
        sb.AppendLine($"{indent}            {{");
        sb.AppendLine($"{indent}                this.Enabled = _d.Enabled;");
        foreach (var m in model.Members)
        {
            var dataFieldName = GetDataFieldName(m.Name);
            sb.AppendLine($"{indent}                this.{m.Name} = _d.{dataFieldName};");
        }
        sb.AppendLine($"{indent}            }}");

        sb.AppendLine($"{indent}        }}");
        sb.AppendLine($"{indent}    }}");

        sb.AppendLine($"{indent}}}");

        if (model.FullNamespace is not null)
            sb.AppendLine("}");

        spc.AddSource($"{model.ClassName}.ComponentData.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
    }

    /// <summary>
    /// Converts a member name to a clean data field name.
    /// Private fields like "_speed" become "Speed", public ones stay as-is.
    /// </summary>
    private static string GetDataFieldName(string memberName)
    {
        if (memberName.StartsWith("_") && memberName.Length > 1)
        {
            return char.ToUpperInvariant(memberName[1]) + memberName.Substring(2);
        }
        return memberName;
    }

    // --- Models ---

    private struct ComponentModel
    {
        public string ClassName;
        public string? FullNamespace;
        public List<MemberModel> Members;
    }

    private struct MemberModel
    {
        public string Name;
        public string TypeFullName;
        public bool IsProperty;
        public bool IsPublic;
    }
}
