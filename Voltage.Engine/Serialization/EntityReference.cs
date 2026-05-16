using System;
using System.Diagnostics.CodeAnalysis;

namespace Voltage.Serialization;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public struct EntityReference
{
	public string EntityPersistentId;
	public string EntityName;

	public readonly bool IsValid =>
		!string.IsNullOrEmpty(EntityPersistentId) || !string.IsNullOrEmpty(EntityName);

	// Parses EntityPersistentId to a Guid for resolver use. Returns Guid.Empty if unparseable.
	public readonly Guid GetPersistentId() =>
		Guid.TryParse(EntityPersistentId, out var id) ? id : Guid.Empty;

	public static EntityReference From(Entity entity)
	{
		if (entity == null)
			return default;

		// Non-serialized entities are runtime-only and must not be persisted.
		if (entity.Type == Entity.InstanceType.NonSerialized)
			return default;

		return new EntityReference
		{
			EntityPersistentId = entity.PersistentId.ToString(),
			EntityName = entity.Name
		};
	}

	public static EntityReference From(Transform transform)
	{
		if (transform?.Entity == null)
			return default;

		if (transform.Entity.Type == Entity.InstanceType.NonSerialized)
			return default;

		return new EntityReference
		{
			EntityPersistentId = transform.Entity.PersistentId.ToString(),
			EntityName = transform.Entity.Name
		};
	}

	public override readonly string ToString() =>
		IsValid ? $"{EntityName}({EntityPersistentId})" : "(None)";
}