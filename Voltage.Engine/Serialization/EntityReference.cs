using System.Diagnostics.CodeAnalysis;

namespace Voltage.Serialization;

/// <summary>
/// Serializable reference to an Entity (or its Transform) in a scene.
/// Stored in ComponentData in place of direct Entity/Transform-typed fields.
/// Passed to a live Entity/Transform after all entities are instantiated by
/// <see cref="ComponentReferenceResolver"/>.
/// </summary>
[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public struct EntityReference
{
	public string EntityName;
	public readonly bool IsValid => !string.IsNullOrEmpty(EntityName);

	public static EntityReference From(Entity entity)
	{
		if (entity == null)
			return default;

		return new EntityReference { EntityName = entity.Name };
	}

	public static EntityReference From(Transform transform)
	{
		if (transform?.Entity == null)
			return default;

		return new EntityReference { EntityName = transform.Entity.Name };
	}

	public override readonly string ToString() =>
		IsValid ? EntityName : "(None)";
}