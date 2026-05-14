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
			{
				var component = entity.Components[c];
				var data = component.Data;
				if (data == null)
					continue;

				ResolveOnComponent(component, data, scene);
			}
		}
	}

	private static void ResolveOnComponent(Component target, ComponentData data, Scene scene)
	{
		var dataType = data.GetType();
		var componentType = target.GetType();

		var dataFields = dataType.GetFields(BindingFlags.Public | BindingFlags.Instance);
		foreach (var dataField in dataFields)
		{
			if (dataField.FieldType != typeof(ComponentReference))
				continue;

			var reference = (ComponentReference)dataField.GetValue(data);
			if (!reference.IsValid)
				continue;

			// Find the corresponding field on the live component
			var liveField = componentType.GetField(
				dataField.Name,
				BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

			if (liveField == null || !typeof(Component).IsAssignableFrom(liveField.FieldType))
				continue;

			var resolved = FindComponent(scene, reference);
			if (resolved != null)
				liveField.SetValue(target, resolved);
			else
				Debug.Warn($"[ComponentReferenceResolver] Could not resolve '{reference}' for '{target}'.");
		}
	}

	private static Component FindComponent(Scene scene, ComponentReference reference)
	{
		var entity = scene.FindEntity(reference.EntityName);
		if (entity == null)
			return null;

		var type = Type.GetType(reference.ComponentTypeName);
		if (type == null)
		{
			// Try scanning loaded assemblies as a fallback
			foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
			{
				type = asm.GetType(reference.ComponentTypeName);
				if (type != null)
					break;
			}
		}

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

		return entity.GetComponent(type);
	}
}