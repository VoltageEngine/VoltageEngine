using System.Diagnostics.CodeAnalysis;

namespace Voltage.Serialization;

/// <summary>
/// Serializable reference to a Component on a specific Entity.
/// Stored in ComponentData in place of direct Component-type fields.
/// Resolved to a live Component after all entities are instantiated by
/// <see cref="ComponentReferenceResolver"/>.
/// </summary>
[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public struct ComponentReference
{
	public string EntityName;
	public string ComponentTypeName;
	public string ComponentName;

	public readonly bool IsValid =>
		!string.IsNullOrEmpty(EntityName) && !string.IsNullOrEmpty(ComponentTypeName);

	public static ComponentReference From(Component component)
	{
		if (component?.Entity == null)
			return default;

		return new ComponentReference
		{
			EntityName = component.Entity.Name,
			ComponentTypeName = component.GetType().FullName,
			ComponentName = component.Name
		};
	}

	public override readonly string ToString() =>
		IsValid ? $"{EntityName}.{ComponentName} ({ComponentTypeName})" : "(None)";
}