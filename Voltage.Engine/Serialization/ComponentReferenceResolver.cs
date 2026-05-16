using System;
using System.Collections.Generic;
using System.Reflection;

namespace Voltage.Serialization;

public static class ComponentReferenceResolver
{
	public static void ResolveAll()
	{
		var scene = Core.Scene;

		// Live entities
		for (var e = 0; e < scene.Entities.Count; e++)
		{
			var entity = scene.Entities[e];

			for (var c = 0; c < entity.Components.Count; c++)
				ResolveOnComponent(entity.Components[c], scene);

			var pendingComps = entity.Components.GetComponentsToAddList();
			for (var c = 0; c < pendingComps.Count; c++)
				ResolveOnComponent(pendingComps[c], scene);
		}

		// Entities pending add
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

		var dataType = data.GetType();
		var componentType = target.GetType();

		foreach (var dataField in dataType.GetFields(BindingFlags.Public | BindingFlags.Instance))
		{
			if (dataField.FieldType == typeof(ComponentReference))
			{
				var reference = (ComponentReference)dataField.GetValue(data);

				if (!reference.IsValid)
					continue;

				var liveField = componentType.GetField(
					dataField.Name,
					BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

				if (liveField == null || !typeof(Component).IsAssignableFrom(liveField.FieldType))
				{
					Debug.Warn($"[ComponentReferenceResolver] '{target}' — no matching live field '{dataField.Name}' of Component type found.");
					continue;
				}

				var resolved = FindComponent(scene, reference);
				if (resolved != null)
				{
					liveField.SetValue(target, resolved);
				}
				else
				{
					Debug.Error($"[ComponentReferenceResolver] Could not resolve ComponentReference '{reference}' " +
					            $"for field '{dataField.Name}' on '{target}'. " +
					            "Check that the entity and component still exist in the scene.");
				}

				continue;
			}

			if (dataField.FieldType == typeof(EntityReference))
			{
				var reference = (EntityReference)dataField.GetValue(data);

				if (!reference.IsValid)
					continue;

				var liveField = componentType.GetField(
					dataField.Name,
					BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

				if (liveField == null)
				{
					continue;
				}

				var resolvedEntity = FindEntity(scene, reference);
				if (resolvedEntity == null)
				{
					Debug.Error($"[ComponentReferenceResolver] Could not resolve EntityReference '{reference}' " +
					            $"for field '{dataField.Name}' on '{target}'. " +
					            "Check that the entity still exists in the scene.");
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

	private static Component FindComponent(Scene scene, ComponentReference reference)
	{
		var type = ResolveType(reference.ComponentTypeName);
		if (type == null)
		{
			Debug.Warn($"[ComponentReferenceResolver] FindComponent — type '{reference.ComponentTypeName}' could not be resolved.");
			return null;
		}

		var persistentId = reference.GetPersistentId();
		if (persistentId != Guid.Empty)
		{
			var entity = FindEntityById(scene, persistentId);
			if (entity != null)
			{
				var comp = FindComponentOnEntity(entity, type, reference.ComponentName);
				if (comp != null)
				{
					return comp;
				}
			}
		}

		if (!string.IsNullOrEmpty(reference.EntityName))
		{
			var entity = FindEntityByName(scene, reference.EntityName);
			if (entity != null)
			{
				var comp = FindComponentOnEntity(entity, type, reference.ComponentName);
				if (comp != null)
				{
					return comp;
				}
			}
		}

		Debug.Error($"[ComponentReferenceResolver] Could not resolve ComponentReference '{reference}'. " +
		            "Check that the entity and component still exist in the scene.");
		return null;
	}

	private static Component FindComponentOnEntity(Entity entity, Type type, string componentName)
	{
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
			{
				return entity;
			}
		}

		if (!string.IsNullOrEmpty(reference.EntityName))
		{
			var entity = FindEntityByName(scene, reference.EntityName);
			if (entity != null)
			{
				return entity;
			}
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

		if (Core.LatestScriptAssembly != null)
		{
			type = Core.LatestScriptAssembly.GetType(typeName);
			if (type != null)
				return type;
		}

		foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
		{
			var assemblyName = asm.GetName().Name;
			if (assemblyName != null && assemblyName.StartsWith("DynamicScripts"))
				continue;

			type = asm.GetType(typeName);
			if (type != null)
				return type;
		}

		return null;
	}
}