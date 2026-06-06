using System;
using System.Collections.Generic;

namespace Voltage.Serialization;

public static class ComponentReferenceResolver
{
	public static void ResolveAll()
	{
		var scene = Core.Scene;

		for (var e = 0; e < scene.Entities.Count; e++)
		{
			var entity = scene.Entities[e];

			for (var c = 0; c < entity.Components.Count; c++)
				ResolveOnComponent(entity.Components[c], scene);

			var pendingComps = entity.Components.GetComponentsToAddList();
			for (var c = 0; c < pendingComps.Count; c++)
				ResolveOnComponent(pendingComps[c], scene);
		}

		foreach (var entity in scene.Entities.EntitiesToAdd)
		{
			for (var c = 0; c < entity.Components.Count; c++)
				ResolveOnComponent(entity.Components[c], scene);

			var pendingComps = entity.Components.GetComponentsToAddList();
			for (var c = 0; c < pendingComps.Count; c++)
				ResolveOnComponent(pendingComps[c], scene);
		}
	}

	public static void ReResolveComponent(Component component, Scene scene)
	{
		if (component._pendingLoadedData == null)
			component._pendingLoadedData = component.Data;

		if (component._pendingLoadedData == null)
			return;

		ResolveOnComponent(component, scene);
	}

	private static void ResolveOnComponent(Component target, Scene scene)
	{
		var data = target._pendingLoadedData;
		if (data == null)
			return;

		// AOT-safe path: generated override on the component assigns fields directly.
		// Works in NativeAOT published builds where reflection is trimmed.
		target.ApplyResolvedReferences(data, scene);

		// Reflection fallback: handles components without a generated ApplyResolvedReferences
		// override (e.g. engine components with manual Data overrides, or script components
		// that were compiled without the source generator).
		var dataType = data.GetType();
		var componentType = target.GetType();

		foreach (var dataField in dataType.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
		{
			if (dataField.FieldType == typeof(ComponentReference))
			{
				var reference = (ComponentReference)dataField.GetValue(data);
				if (!reference.IsValid)
					continue;

				// The generator may have renamed the field (e.g. _myRef → MyRef via GetDataFieldName).
				// Search all instance fields on the component and pick the one whose
				// GetDataFieldName-equivalent matches the data field name.
				var liveField = FindLiveField(componentType, dataField.Name, typeof(Component));

				if (liveField == null || !typeof(Component).IsAssignableFrom(liveField.FieldType))
				{
					Debug.Warn($"[ComponentReferenceResolver] '{target}' — no matching live field for data field '{dataField.Name}' of Component type found.");
					continue;
				}

				var resolved = FindComponent(scene, reference);
				if (resolved != null)
					liveField.SetValue(target, resolved);
				else
					Debug.Error($"[ComponentReferenceResolver] Could not resolve ComponentReference '{reference}' " +
					            $"for field '{dataField.Name}' on '{target}'.");
			}
			else if (dataField.FieldType == typeof(EntityReference))
			{
				var reference = (EntityReference)dataField.GetValue(data);
				if (!reference.IsValid)
					continue;

				var liveField = FindLiveField(componentType, dataField.Name, null);

				if (liveField == null)
					continue;

				var resolvedEntity = FindEntity(scene, reference);
				if (resolvedEntity == null)
				{
					Debug.Error($"[ComponentReferenceResolver] Could not resolve EntityReference '{reference}' " +
					            $"for field '{dataField.Name}' on '{target}'.");
					continue;
				}

				if (liveField.FieldType == typeof(Entity))
					liveField.SetValue(target, resolvedEntity);
				else if (liveField.FieldType == typeof(Transform))
					liveField.SetValue(target, resolvedEntity.Transform);
			}
		}

		target._pendingLoadedData = null;
	}

	/// <summary>
	/// Finds a live field on <paramref name="componentType"/> (including base types) that
	/// corresponds to a data field named <paramref name="dataFieldName"/>.
	///
	/// The source generator applies <c>GetDataFieldName</c> which strips a leading underscore
	/// and capitalises the next character (e.g. <c>_myRef</c> → <c>MyRef</c>). This method
	/// reverses that transform so both <c>myRef</c> (exact match) and <c>_myRef</c>
	/// (underscore-prefixed variant) are tried.
	/// </summary>
	private static System.Reflection.FieldInfo FindLiveField(Type componentType, string dataFieldName, Type mustBeAssignableTo)
	{
		const System.Reflection.BindingFlags flags =
			System.Reflection.BindingFlags.Public |
			System.Reflection.BindingFlags.NonPublic |
			System.Reflection.BindingFlags.Instance;

		// Candidate names: exact match, then underscore-prefixed camelCase variant.
		string camelVariant = null;
		if (dataFieldName.Length > 0 && char.IsUpper(dataFieldName[0]))
			camelVariant = "_" + char.ToLowerInvariant(dataFieldName[0]) + dataFieldName.Substring(1);

		var type = componentType;
		while (type != null && type != typeof(Component))
		{
			var exact = type.GetField(dataFieldName, flags);
			if (exact != null && (mustBeAssignableTo == null || mustBeAssignableTo.IsAssignableFrom(exact.FieldType)))
				return exact;

			if (camelVariant != null)
			{
				var variant = type.GetField(camelVariant, flags);
				if (variant != null && (mustBeAssignableTo == null || mustBeAssignableTo.IsAssignableFrom(variant.FieldType)))
					return variant;
			}

			type = type.BaseType;
		}

		return null;
	}

	/// <summary>
	/// AOT-safe lookup called directly from source-generated <c>ApplyResolvedReferences</c> overrides.
	/// Matches by type full name string — never calls Type.GetType() or reflection.
	/// </summary>
	public static Component FindComponentAot(Scene scene, ComponentReference reference)
	{
		if (!reference.IsValid)
			return null;

		var persistentId = reference.GetPersistentId();
		if (persistentId != Guid.Empty)
		{
			var entity = FindEntityById(scene, persistentId);
			if (entity != null)
			{
				var comp = FindComponentOnEntityByName(entity, reference.ComponentTypeName, reference.ComponentName);
				if (comp != null)
					return comp;
			}
		}

		if (!string.IsNullOrEmpty(reference.EntityName))
		{
			var entity = FindEntityByName(scene, reference.EntityName);
			if (entity != null)
			{
				var comp = FindComponentOnEntityByName(entity, reference.ComponentTypeName, reference.ComponentName);
				if (comp != null)
					return comp;
			}
		}

		Debug.Error($"[ComponentReferenceResolver] Could not resolve ComponentReference '{reference}'. " +
		            "Check that the entity and component still exist in the scene.");
		return null;
	}

	/// <summary>
	/// AOT-safe lookup called directly from source-generated <c>ApplyResolvedReferences</c> overrides.
	/// </summary>
	public static Entity FindEntityAot(Scene scene, EntityReference reference)
		=> FindEntity(scene, reference);

	/// <summary>
	/// Matches a component by its type full name string and optional component name.
	/// Fully AOT-safe — no Type.GetType(), no IsAssignableFrom(), no reflection.
	/// </summary>
	private static Component FindComponentOnEntityByName(Entity entity, string componentTypeName, string componentName)
	{
		for (var i = 0; i < entity.Components.Count; i++)
		{
			var comp = entity.Components[i];
			if (comp.GetType().FullName != componentTypeName)
				continue;
			if (string.IsNullOrEmpty(componentName) || comp.Name == componentName)
				return comp;
		}

		var pending = entity.Components.GetComponentsToAddList();
		for (var i = 0; i < pending.Count; i++)
		{
			var comp = pending[i];
			if (comp.GetType().FullName != componentTypeName)
				continue;
			if (string.IsNullOrEmpty(componentName) || comp.Name == componentName)
				return comp;
		}

		return null;
	}

	private static Component FindComponent(Scene scene, ComponentReference reference)
	{
		var persistentId = reference.GetPersistentId();
		if (persistentId != Guid.Empty)
		{
			var entity = FindEntityById(scene, persistentId);
			if (entity != null)
			{
				var comp = FindComponentOnEntity(entity, ResolveType(reference.ComponentTypeName), reference.ComponentName);
				if (comp != null)
					return comp;
			}
		}

		if (!string.IsNullOrEmpty(reference.EntityName))
		{
			var entity = FindEntityByName(scene, reference.EntityName);
			if (entity != null)
			{
				var comp = FindComponentOnEntity(entity, ResolveType(reference.ComponentTypeName), reference.ComponentName);
				if (comp != null)
					return comp;
			}
		}

		Debug.Error($"[ComponentReferenceResolver] Could not resolve ComponentReference '{reference}'. " +
		            "Check that the entity and component still exist in the scene.");
		return null;
	}

	private static Component FindComponentOnEntity(Entity entity, Type type, string componentName)
	{
		if (type == null)
			return null;

		for (var i = 0; i < entity.Components.Count; i++)
		{
			var comp = entity.Components[i];
			if (!type.IsAssignableFrom(comp.GetType()))
				continue;
			if (string.IsNullOrEmpty(componentName) || comp.Name == componentName)
				return comp;
		}

		var pending = entity.Components.GetComponentsToAddList();
		for (var i = 0; i < pending.Count; i++)
		{
			var comp = pending[i];
			if (!type.IsAssignableFrom(comp.GetType()))
				continue;
			if (string.IsNullOrEmpty(componentName) || comp.Name == componentName)
				return comp;
		}

		return null;
	}

	private static Entity FindEntity(Scene scene, EntityReference reference)
	{
		var persistentId = reference.GetPersistentId();
		if (persistentId != Guid.Empty)
		{
			var entity = FindEntityById(scene, persistentId);
			if (entity != null)
				return entity;
		}

		if (!string.IsNullOrEmpty(reference.EntityName))
		{
			var entity = FindEntityByName(scene, reference.EntityName);
			if (entity != null)
				return entity;
		}

		return null;
	}

	private static Entity FindEntityById(Scene scene, Guid persistentId)
	{
		for (var e = 0; e < scene.Entities.Count; e++)
			if (scene.Entities[e].PersistentId == persistentId)
				return scene.Entities[e];

		foreach (var entity in scene.Entities.EntitiesToAdd)
			if (entity.PersistentId == persistentId)
				return entity;

		return null;
	}

	private static Entity FindEntityByName(Scene scene, string name)
	{
		var entity = scene.FindEntity(name);
		if (entity != null)
			return entity;

		foreach (var pending in scene.Entities.EntitiesToAdd)
			if (string.Equals(pending.Name, name, StringComparison.OrdinalIgnoreCase))
				return pending;

		return null;
	}

	private static Type ResolveType(string typeName)
	{
		if (string.IsNullOrEmpty(typeName))
			return null;

		var type = Type.GetType(typeName);
		if (type != null)
			return type;

		// Check the latest hot-reloaded script assembly first so fresh script types
		// are found even when the same name exists in an older loaded assembly.
		if (Core.LatestScriptAssembly != null)
		{
			type = Core.LatestScriptAssembly.GetType(typeName);
			if (type != null)
				return type;
		}

		foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
		{
			type = asm.GetType(typeName);
			if (type != null)
				return type;
		}

		return null;
	}
}