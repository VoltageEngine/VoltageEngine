using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Voltage.SourceGenerators
{
	/// <summary>
	/// Emits AOT-safe dispatch registrations for methods marked <c>[Voltage.Cinematics.TimelineEvent]</c>,
	/// so a cinematic timeline can call a component method by (componentId, methodName) with zero runtime
	/// reflection. For each such method it produces a <c>TimelineDispatch.Register(...)</c> call inside a
	/// <c>[ModuleInitializer]</c>, mirroring how <see cref="ComponentDataGenerator"/> emits its factory
	/// bootstrap. The invoker casts the component and unpacks positional <c>TimelineArg</c>s into the
	/// method's parameters.
	/// </summary>
	[Generator]
	public sealed class TimelineEventGenerator : IIncrementalGenerator
	{
		private const string AttributeMetadataName = "Voltage.Cinematics.TimelineEventAttribute";
		private const string ComponentBaseFullName = "Voltage.Component";
		private const string ComponentIdAttributeFullName = "Voltage.ComponentIdAttribute";

		public void Initialize(IncrementalGeneratorInitializationContext context)
		{
			var candidates = context.SyntaxProvider.ForAttributeWithMetadataName(
					AttributeMetadataName,
					predicate: static (node, _) => node is MethodDeclarationSyntax,
					transform: static (ctx, _) => Extract(ctx))
				.Where(static r => r is not null)
				.Select(static (r, _) => r!.Value);

			context.RegisterSourceOutput(candidates.Collect(), static (spc, items) => Emit(spc, items));
		}

		/// <summary>Value-equatable result of inspecting one [TimelineEvent] method (for incremental caching).</summary>
		private readonly struct EventReg
		{
			public readonly string ComponentId;
			public readonly string ComponentFullName;   // global::-qualified
			public readonly string Method;
			public readonly string ArgsExpression;       // comma-separated arg accessors, or "" for none
			public readonly string Warning;              // non-null when the method was rejected

			public EventReg(string componentId, string componentFullName, string method, string argsExpression, string warning)
			{
				ComponentId = componentId;
				ComponentFullName = componentFullName;
				Method = method;
				ArgsExpression = argsExpression;
				Warning = warning;
			}
		}

		private static EventReg? Extract(GeneratorAttributeSyntaxContext ctx)
		{
			if (ctx.TargetSymbol is not IMethodSymbol method)
				return null;

			var owner = method.ContainingType;
			var ownerName = owner.ToDisplayString();
			var display = $"{ownerName}.{method.Name}";

			if (method.IsStatic)
				return Reject($"[TimelineEvent] method '{display}' must be an instance method.");

			if (method.DeclaredAccessibility != Accessibility.Public)
				return Reject($"[TimelineEvent] method '{display}' must be public.");

			if (!DerivesFromComponent(owner))
				return Reject($"[TimelineEvent] method '{display}' must be declared on a Voltage.Component subclass.");

			var componentId = GetComponentId(owner);
			if (string.IsNullOrEmpty(componentId))
				return Reject($"Component '{ownerName}' has a [TimelineEvent] method but no [ComponentId]. Add a stable [ComponentId(\"…\")] so the timeline can reference it.");

			var argsBuilder = new StringBuilder();
			for (var i = 0; i < method.Parameters.Length; i++)
			{
				var accessor = ArgAccessor(method.Parameters[i], i, out var typeName);
				if (accessor == null)
					return Reject($"[TimelineEvent] method '{display}' has unsupported parameter type '{typeName}'. Supported: float, int, bool, string, EntityReference, ComponentReference, AssetReference, PrefabReference.");

				if (i > 0)
					argsBuilder.Append(", ");
				argsBuilder.Append(accessor);
			}

			var ownerFq = owner.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat); // includes global::
			return new EventReg(componentId, ownerFq, method.Name, argsBuilder.ToString(), warning: null);
		}

		private static EventReg? Reject(string warning) => new EventReg(null, null, null, null, warning);

		/// <summary>Returns the C# expression that reads argument <paramref name="index"/> for this parameter, or null if unsupported.</summary>
		private static string ArgAccessor(IParameterSymbol parameter, int index, out string typeName)
		{
			typeName = parameter.Type.ToDisplayString();
			var special = parameter.Type.SpecialType;

			string call;
			string defaultExpr;
			switch (special)
			{
				case SpecialType.System_Single:
					call = "AsFloat()"; defaultExpr = "default(float)"; break;
				case SpecialType.System_Int32:
					call = "AsInt()"; defaultExpr = "default(int)"; break;
				case SpecialType.System_Boolean:
					call = "AsBool()"; defaultExpr = "default(bool)"; break;
				case SpecialType.System_String:
					call = "AsString()"; defaultExpr = "default(string)"; break;
				default:
					// Serializable reference types (structs in Voltage.Serialization) map to their accessor.
					switch (parameter.Type.ToDisplayString())
					{
						case "Voltage.Serialization.EntityReference":
							call = "AsEntity()";
							defaultExpr = "default(global::Voltage.Serialization.EntityReference)";
							break;
						case "Voltage.Serialization.ComponentReference":
							call = "AsComponentReference()";
							defaultExpr = "default(global::Voltage.Serialization.ComponentReference)";
							break;
						case "Voltage.Serialization.AssetReference":
							call = "AsAssetReference()";
							defaultExpr = "default(global::Voltage.Serialization.AssetReference)";
							break;
						case "Voltage.Serialization.PrefabReference":
							call = "AsPrefabReference()";
							defaultExpr = "default(global::Voltage.Serialization.PrefabReference)";
							break;
						default:
							return null;
					}
					break;
			}

			return $"a.Length > {index} ? a[{index}].{call} : {defaultExpr}";
		}

		private static bool DerivesFromComponent(INamedTypeSymbol type)
		{
			for (var t = type; t != null; t = t.BaseType)
			{
				if (t.ToDisplayString() == ComponentBaseFullName)
					return true;
			}
			return false;
		}

		private static string GetComponentId(INamedTypeSymbol type)
		{
			foreach (var attr in type.GetAttributes())
			{
				if (attr.AttributeClass?.ToDisplayString() != ComponentIdAttributeFullName)
					continue;
				if (attr.ConstructorArguments.Length > 0 &&
					attr.ConstructorArguments[0].Value is string id && !string.IsNullOrWhiteSpace(id))
					return id;
			}
			return null;
		}

		private static void Emit(SourceProductionContext spc, ImmutableArray<EventReg> items)
		{
			if (items.IsDefaultOrEmpty)
				return;

			var sb = new StringBuilder();
			sb.AppendLine("// <auto-generated/>");
			sb.AppendLine("// Timeline event dispatch, generated from [TimelineEvent] methods.");
			sb.AppendLine("#nullable disable");
			sb.AppendLine();

			// Surface rejected methods as build warnings (they won't be callable from a timeline).
			foreach (var warning in items.Where(i => i.Warning != null).Select(i => i.Warning).Distinct())
				sb.AppendLine($"#warning {warning}");

			var valid = items.Where(i => i.Warning == null).ToList();
			if (valid.Count > 0)
			{
				sb.AppendLine("namespace Voltage.Cinematics.Generated");
				sb.AppendLine("{");
				sb.AppendLine("\tinternal static class TimelineEventBootstrap");
				sb.AppendLine("\t{");
				sb.AppendLine("\t\tprivate static bool _inited;");
				sb.AppendLine();
				sb.AppendLine("\t\t[System.Runtime.CompilerServices.ModuleInitializer]");
				sb.AppendLine("\t\tinternal static void AutoRegister()");
				sb.AppendLine("\t\t{");
				sb.AppendLine("\t\t\tif (_inited) return;");
				sb.AppendLine("\t\t\t_inited = true;");
				sb.AppendLine();

				// De-dup identical (componentId, method) registrations that can arise from partials.
				var seen = new HashSet<string>();
				foreach (var reg in valid)
				{
					if (!seen.Add($"{reg.ComponentId}::{reg.Method}"))
						continue;

					var invocation = reg.ArgsExpression.Length == 0
						? $"(({reg.ComponentFullName})c).{reg.Method}()"
						: $"(({reg.ComponentFullName})c).{reg.Method}({reg.ArgsExpression})";

					sb.AppendLine("\t\t\tglobal::Voltage.Cinematics.TimelineDispatch.Register(");
					sb.AppendLine($"\t\t\t\t\"{Escape(reg.ComponentId)}\",");
					sb.AppendLine($"\t\t\t\t\"{Escape(reg.Method)}\",");
					sb.AppendLine($"\t\t\t\tstatic (c, a) => {invocation});");
					sb.AppendLine();
				}

				sb.AppendLine("\t\t}");
				sb.AppendLine("\t}");
				sb.AppendLine("}");
			}

			spc.AddSource("TimelineEventBootstrap.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
		}

		private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
	}
}
