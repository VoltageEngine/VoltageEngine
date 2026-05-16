using System;
using System.Diagnostics.CodeAnalysis;

namespace Voltage.Serialization;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public struct ComponentReference
{
	public string EntityPersistentId;
	public string EntityName;
	public string ComponentTypeName;
	public string ComponentName;

	public readonly bool IsValid =>
		!string.IsNullOrEmpty(ComponentTypeName) &&
		(!string.IsNullOrEmpty(EntityPersistentId) || !string.IsNullOrEmpty(EntityName));

	/// <summary>Parses EntityPersistentId to a Guid for resolver use. Returns Guid.Empty if unparseable.</summary>
	public readonly Guid GetPersistentId() =>
		Guid.TryParse(EntityPersistentId, out var id) ? id : Guid.Empty;

	public static ComponentReference From(Component component)
	{
		if (component?.Entity == null)
			return default;

		if (component.Entity.Type == Entity.InstanceType.NonSerialized)
			return default;

		if (!component.IsSerialized)
			return default;

		return new ComponentReference
		{
			EntityPersistentId = component.Entity.PersistentId.ToString(),
			EntityName = component.Entity.Name,
			ComponentTypeName = component.GetType().FullName,
			ComponentName = component.Name
		};
	}

	public override readonly string ToString() =>
		IsValid ? $"{EntityName}(id:{EntityPersistentId}).{ComponentName} ({ComponentTypeName})" : "(None)";
}