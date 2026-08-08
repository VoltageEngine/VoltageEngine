using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Voltage.SourceGenerators
{
	/// <summary>
	/// Emits AOT-safe get/set accessors for members marked <c>[Voltage.Cinematics.TimelineProperty]</c>, so a <c>TimelinePropertyTrack</c> can animate an arbitrary component property with zero runtime reflection.
	/// </summary>
	[Generator]
	public sealed class TimelinePropertyGenerator : IIncrementalGenerator
	{
		private const string AttributeMetadataName = "Voltage.Cinematics.TimelinePropertyAttribute";
		private const string ComponentBaseFullName = "Voltage.Component";
		private const string ComponentIdAttributeFullName = "Voltage.ComponentIdAttribute";

		public void Initialize(IncrementalGeneratorInitializationContext context)
		{
			var candidates = context.SyntaxProvider.ForAttributeWithMetadataName(
					AttributeMetadataName,
					predicate: static (_, _) => true,
					transform: static (ctx, _) => Extract(ctx))
				.Where(static r => r is not null)
				.Select(static (r, _) => r!.Value);

			context.RegisterSourceOutput(candidates.Collect(), static (spc, items) => Emit(spc, items));
		}

		private readonly struct PropertyReg
		{
			public readonly string ComponentId;
			public readonly string ComponentFullName;   // global::-qualified
			public readonly string Member;
			public readonly string RegisterMethod;      // RegisterFloat / RegisterVector2 / RegisterColor
			public readonly string ClrType;             // global::-qualified value type
			public readonly string Warning;

			public PropertyReg(string componentId, string componentFullName, string member,
				string registerMethod, string clrType, string warning)
			{
				ComponentId = componentId;
				ComponentFullName = componentFullName;
				Member = member;
				RegisterMethod = registerMethod;
				ClrType = clrType;
				Warning = warning;
			}
		}

		private static PropertyReg? Extract(GeneratorAttributeSyntaxContext ctx)
		{
			var symbol = ctx.TargetSymbol;
			var owner = symbol.ContainingType;
			if (owner == null)
				return null;

			var display = $"{owner.ToDisplayString()}.{symbol.Name}";

			ITypeSymbol memberType;
			bool writable;
			switch (symbol)
			{
				case IFieldSymbol field:
					memberType = field.Type;
					writable = !field.IsReadOnly && !field.IsConst;
					break;
				case IPropertySymbol property:
					memberType = property.Type;
					writable = property.SetMethod is { DeclaredAccessibility: Accessibility.Public };
					break;
				default:
					return Reject($"[TimelineProperty] on '{display}' must be a field or property.");
			}

			if (symbol.IsStatic)
				return Reject($"[TimelineProperty] '{display}' must be an instance member.");

			if (symbol.DeclaredAccessibility != Accessibility.Public)
				return Reject($"[TimelineProperty] '{display}' must be public.");

			if (!writable)
				return Reject($"[TimelineProperty] '{display}' must be writable — a timeline has to set it.");

			if (!DerivesFromComponent(owner))
				return Reject($"[TimelineProperty] '{display}' must be declared on a Voltage.Component subclass.");

			var componentId = GetComponentId(owner);
			if (string.IsNullOrEmpty(componentId))
			{
				return Reject($"Component '{owner.ToDisplayString()}' has a [TimelineProperty] member but no " +
							  "[ComponentId]. Add a stable [ComponentId(\"…\")] so the timeline can reference it.");
			}

			string register;
			string clrType;
			switch (memberType.ToDisplayString())
			{
				case "float":
				case "System.Single":
					register = "RegisterFloat";
					clrType = "float";
					break;
				case "Microsoft.Xna.Framework.Vector2":
					register = "RegisterVector2";
					clrType = "global::Microsoft.Xna.Framework.Vector2";
					break;
				case "Microsoft.Xna.Framework.Color":
					register = "RegisterColor";
					clrType = "global::Microsoft.Xna.Framework.Color";
					break;
				default:
					return Reject($"[TimelineProperty] '{display}' has unsupported type " +
								  $"'{memberType.ToDisplayString()}'. Supported: float, Vector2, Color.");
			}

			var ownerFq = owner.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
			return new PropertyReg(componentId, ownerFq, symbol.Name, register, clrType, warning: null);
		}

		private static PropertyReg? Reject(string warning) => new PropertyReg(null, null, null, null, null, warning);

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

		private static void Emit(SourceProductionContext spc, ImmutableArray<PropertyReg> items)
		{
			if (items.IsDefaultOrEmpty)
				return;

			var sb = new StringBuilder();
			sb.AppendLine("// <auto-generated/>");
			sb.AppendLine("// Timeline property accessors, generated from [TimelineProperty] members.");
			sb.AppendLine("#nullable disable");
			sb.AppendLine();

			foreach (var warning in items.Where(i => i.Warning != null).Select(i => i.Warning).Distinct())
				sb.AppendLine($"#warning {warning}");

			var valid = items.Where(i => i.Warning == null).ToList();
			if (valid.Count > 0)
			{
				sb.AppendLine("namespace Voltage.Cinematics.Generated");
				sb.AppendLine("{");
				sb.AppendLine("\tinternal static class TimelinePropertyBootstrap");
				sb.AppendLine("\t{");
				sb.AppendLine("\t\tprivate static bool _inited;");
				sb.AppendLine();
				sb.AppendLine("\t\t[System.Runtime.CompilerServices.ModuleInitializer]");
				sb.AppendLine("\t\tinternal static void AutoRegister()");
				sb.AppendLine("\t\t{");
				sb.AppendLine("\t\t\tif (_inited) return;");
				sb.AppendLine("\t\t\t_inited = true;");
				sb.AppendLine();

				var seen = new HashSet<string>();
				foreach (var reg in valid)
				{
					if (!seen.Add($"{reg.ComponentId}::{reg.Member}"))
						continue;

					sb.AppendLine($"\t\t\tglobal::Voltage.Cinematics.TimelinePropertyRegistry.{reg.RegisterMethod}(");
					sb.AppendLine($"\t\t\t\t\"{Escape(reg.ComponentId)}\",");
					sb.AppendLine($"\t\t\t\t\"{Escape(reg.Member)}\",");
					sb.AppendLine($"\t\t\t\tstatic c => (({reg.ComponentFullName})c).{reg.Member},");
					sb.AppendLine($"\t\t\t\tstatic (c, v) => (({reg.ComponentFullName})c).{reg.Member} = v);");
					sb.AppendLine();
				}

				sb.AppendLine("\t\t}");
				sb.AppendLine("\t}");
				sb.AppendLine("}");
			}

			spc.AddSource("TimelinePropertyBootstrap.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
		}

		private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
	}
}
