using System;
using System.Reflection;

namespace Voltage.Serialization;

/// <summary>
/// Resolves <see cref="ComponentReference"/> fields on all components in a scene
/// back into live Component references after all entities have been instantiated.
/// </summary>
public static class ComponentReferenceResolver
{
	/// <summary>
	/// Scans every component in the scene for <see cref="ComponentReference"/> fields
	/// in their ComponentData, then wires the resolved live reference back onto
	/// the matching field of the component itself.
	/// </summary>
	public static void ResolveAll(Scene scene)
	{
		for (var e = 0; e < scene.Entities.Count; e++)
		{
			var entity = scene.Entities[e];
			for (var c = 0; c < entity.Components.Count; c++)
				ResolveOnComponent(entity.Components[c], scene);
		}
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
			// ComponentReference fields 
			if (dataField.FieldType == typeof(ComponentReference))
			{
				var reference = (ComponentReference)dataField.GetValue(data);
				if (!reference.IsValid)
					continue;

				var liveField = componentType.GetField(
					dataField.Name,
					BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

				if (liveField == null || !typeof(Component).IsAssignableFrom(liveField.FieldType))
					continue;

				var resolved = FindComponent(scene, reference);
				if (resolved != null)
					liveField.SetValue(target, resolved);
				else
					Debug.Log($"[ComponentReferenceResolver] Could not resolve component '{reference}' for '{target}'.");

				continue;
			}

			// EntityReference fields (Entity or Transform typed on the component)
			if (dataField.FieldType == typeof(EntityReference))
			{
				var reference = (EntityReference)dataField.GetValue(data);
				if (!reference.IsValid)
					continue;

				var liveField = componentType.GetField(
					dataField.Name,
					BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

				if (liveField == null)
					continue;

				var resolvedEntity = scene.FindEntity(reference.EntityName);
				if (resolvedEntity == null)
				{
					Debug.Log($"[ComponentReferenceResolver] Could not resolve entity '{reference.EntityName}' for '{target}'.");
					continue;
				}

				if (liveField.FieldType == typeof(Entity))
				{
					liveField.SetValue(target, resolvedEntity);
				}
				else if (liveField.FieldType == typeof(Transform))
				{
					liveField.SetValue(target, resolvedEntity.Transform);
				}
			}
		}

		target._pendingLoadedData = null;
	}

	private static Component FindComponent(Scene scene, ComponentReference reference)
	{
		var entity = scene.FindEntity(reference.EntityName);
		if (entity == null)
			return null;

		var type = ResolveType(reference.ComponentTypeName);
		if (type == null)
			return null;

		for (var i = 0; i < entity.Components.Count; i++)
		{
			var comp = entity.Components[i];
			if (!type.IsAssignableFrom(comp.GetType()))
				continue;
			if (string.IsNullOrEmpty(reference.ComponentName) || comp.Name == reference.ComponentName)
				return comp;
		}

		var pending = entity.Components.GetComponentsToAddList();
		for (var i = 0; i < pending.Count; i++)
		{
			var comp = pending[i];
			if (!type.IsAssignableFrom(comp.GetType()))
				continue;
			if (string.IsNullOrEmpty(reference.ComponentName) || comp.Name == reference.ComponentName)
				return comp;
		}

		return null;
	}

	/// <summary>
	/// Mirrors Scene.Serialization.cs ResolveType: checks Core.LatestScriptAssembly first,
	/// then all loaded assemblies, skipping stale DynamicScripts assemblies.
	/// </summary>
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