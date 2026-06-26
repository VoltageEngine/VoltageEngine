using System;
using System.Collections.Generic;

namespace Voltage.Serialization;

public static class ComponentReferenceResolver
{
	public static void ResolveAll()
	{
		var scene = Core.Scene;

		// Resolve SceneComponent references first: they are scene-level services and must
		// have their references wired up before entity components (which may query them
		// during OnStart). SceneComponents do not extend Component so they have their own
		// pending-data field and resolver method.
		for (var i = 0; i < scene._sceneComponents.Length; i++)
			ResolveOnSceneComponent(scene._sceneComponents.Buffer[i], scene);

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

	/// <summary>
	/// Resolves references on the given entity and all of its Transform descendants.
	/// Only processes components that have a non-null <c>_pendingLoadedData</c> so
	/// already-resolved or data-less components are skipped without side effects.
	/// </summary>
	public static void ResolveEntitySubtree(Entity root, Scene scene)
	{
		ResolveEntityComponents(root, scene);
		foreach (var childTransform in root.Transform.Children)
			ResolveEntitySubtree(childTransform.Entity, scene);
	}

	/// <summary>
	/// Walks the subtree rooted at <paramref name="root"/> (including all Transform descendants)
	/// and calls <see cref="Component.RemapReferences"/> on every component, passing the
	/// <paramref name="remap"/> dictionary. For components that have a source-generated override
	/// this is a zero-reflection direct field operation; for engine components with manual Data
	/// overrides the reflection fallback below is used instead.
	/// Call this BEFORE <see cref="ResolveEntitySubtree"/> so the resolver sees corrected ids.
	/// </summary>
	public static void RemapEntitySubtree(Entity root, Dictionary<Guid, Guid> remap)
	{
		if (root == null || remap == null || remap.Count == 0)
			return;
		RemapEntityComponents(root, remap);
		foreach (var childTransform in root.Transform.Children)
			RemapEntitySubtree(childTransform.Entity, remap);
	}

	private static void RemapEntityComponents(Entity entity, Dictionary<Guid, Guid> remap)
	{
		for (var c = 0; c < entity.Components.Count; c++)
			RemapOnComponent(entity.Components[c], remap);

		var pending = entity.Components.GetComponentsToAddList();
		for (var c = 0; c < pending.Count; c++)
			RemapOnComponent(pending[c], remap);
	}

	private static void RemapOnComponent(Component target, Dictionary<Guid, Guid> remap)
	{
		target.RemapReferences(remap);
		RemapReferencesReflectionFallback(target, remap, typeof(Component));
	}

	/// <summary>
	/// Reflection-based remap fallback for engine components that have manual Data overrides
	/// and therefore no source-generated <c>RemapReferences</c> override. Walks the live fields
	/// on <paramref name="target"/> (up to but not including <paramref name="walkStopAt"/>) and
	/// rewrites <c>EntityPersistentId</c> on any <see cref="EntityReference"/> or
	/// <see cref="ComponentReference"/> field whose stored entity id is a key in <paramref name="remap"/>.
	/// Operates on the live object — no ComponentData involved.
	/// <para>
	/// Under NativeAOT the engine assembly is preserved via TrimmerRoots.xml so reflection
	/// metadata is available for engine types. Script/game components must be <c>partial</c>
	/// so the generator emits <c>RemapReferences</c> for them — that override runs before
	/// this fallback inside <see cref="RemapOnComponent"/>.
	/// </para>
	/// </summary>
	private static void RemapReferencesReflectionFallback(object target, Dictionary<Guid, Guid> remap, Type walkStopAt)
	{
		const System.Reflection.BindingFlags flags =
			System.Reflection.BindingFlags.Public |
			System.Reflection.BindingFlags.NonPublic |
			System.Reflection.BindingFlags.Instance;

		var targetType = target.GetType();
		var type = targetType;

		while (type != null && type != walkStopAt)
		{
			foreach (var field in type.GetFields(flags))
			{
				if (field.IsStatic || field.IsInitOnly)
					continue;

				if (field.FieldType == typeof(EntityReference))
				{
					var r = (EntityReference)field.GetValue(target);
					var remapped = RemapEntityReference(r, remap);
					if (remapped.EntityPersistentId != r.EntityPersistentId)
						field.SetValue(target, remapped);
				}
				else if (field.FieldType == typeof(ComponentReference))
				{
					var r = (ComponentReference)field.GetValue(target);
					var remapped = RemapComponentReference(r, remap);
					if (remapped.EntityPersistentId != r.EntityPersistentId)
						field.SetValue(target, remapped);
				}
				else if (field.FieldType == typeof(System.Collections.Generic.List<EntityReference>))
				{
					var list = (System.Collections.Generic.List<EntityReference>)field.GetValue(target);
					if (list != null)
						for (int i = 0; i < list.Count; i++)
							list[i] = RemapEntityReference(list[i], remap);
				}
				else if (field.FieldType == typeof(System.Collections.Generic.List<ComponentReference>))
				{
					var list = (System.Collections.Generic.List<ComponentReference>)field.GetValue(target);
					if (list != null)
						for (int i = 0; i < list.Count; i++)
							list[i] = RemapComponentReference(list[i], remap);
				}
			}
			type = type.BaseType;
		}
	}

	private static EntityReference RemapEntityReference(EntityReference r, Dictionary<Guid, Guid> remap)
	{
		if (!r.IsValid) return r;
		var oldId = r.GetPersistentId();
		if (oldId != Guid.Empty && remap.TryGetValue(oldId, out var newId))
			r.EntityPersistentId = newId.ToString();
		return r;
	}

	private static ComponentReference RemapComponentReference(ComponentReference r, Dictionary<Guid, Guid> remap)
	{
		if (!r.IsValid) return r;
		var oldId = r.GetPersistentId();
		if (oldId != Guid.Empty && remap.TryGetValue(oldId, out var newId))
			r.EntityPersistentId = newId.ToString();
		return r;
	}

	private static void ResolveEntityComponents(Entity entity, Scene scene)
	{
		for (var c = 0; c < entity.Components.Count; c++)
		{
			var comp = entity.Components[c];
			if (comp._pendingLoadedData != null)
				ResolveOnComponent(comp, scene);
		}

		var pending = entity.Components.GetComponentsToAddList();
		for (var c = 0; c < pending.Count; c++)
		{
			var comp = pending[c];
			if (comp._pendingLoadedData != null)
				ResolveOnComponent(comp, scene);
		}
	}

	private static void ResolveOnComponent(Component target, Scene scene)
	{
		ResolveReferencesReflectionFallback(
			target,
			ref target._pendingLoadedData,
			scene,
			typeof(Component),
			isSceneComponent: false);
	}

	/// <summary>
	/// Resolves ComponentReference / EntityReference fields on a SceneComponent.
	/// Shares its implementation with <see cref="ResolveOnComponent"/> via
	/// <see cref="ResolveReferencesReflectionFallback"/>.
	/// </summary>
	private static void ResolveOnSceneComponent(SceneComponent target, Scene scene)
	{
		ResolveReferencesReflectionFallback(
			target,
			ref target._pendingLoadedData,
			scene,
			typeof(SceneComponent),
			isSceneComponent: true);
	}

	/// <summary>
	/// Unified reflection-fallback resolver shared by <see cref="ResolveOnComponent"/> and
	/// <see cref="ResolveOnSceneComponent"/>. Calls the AOT-safe generator-emitted
	/// <c>ApplyResolvedReferences</c> override first (zero reflection), then walks the live
	/// fields with reflection for engine components that have manual Data overrides.
	/// The walk stops at <paramref name="walkStopAt"/> (Component or SceneComponent).
	/// </summary>
	private static void ResolveReferencesReflectionFallback(
		object target,
		ref ComponentData pendingData,
		Scene scene,
		Type walkStopAt,
		bool isSceneComponent)
	{
		if (pendingData == null)
			return;

		// AOT-safe path: generated override assigns fields directly.
		if (isSceneComponent)
			((SceneComponent)target).ApplyResolvedReferences(pendingData, scene);
		else
			((Component)target).ApplyResolvedReferences(pendingData, scene);

		// Reflection fallback: handles components without a generated ApplyResolvedReferences
		// override (e.g. engine components with manual Data overrides, or script components
		// that were compiled without the source generator).
		//
		// NOTE: This path uses GetFields() + GetValue() which requires reflection metadata.
		// Under NativeAOT the Voltage engine assembly is preserved via TrimmerRoots.xml, so
		// engine components with manual Data overrides work correctly. Script components from
		// game projects must be 'partial' so the source generator emits ApplyResolvedReferences
		// for them — that generated override runs first above and skips this loop.
		var dataType = pendingData.GetType();
		var targetType = target.GetType();
		var label = isSceneComponent ? "SceneComponent" : "Component";

		foreach (var dataField in dataType.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
		{
			if (dataField.FieldType == typeof(ComponentReference))
			{
				var reference = (ComponentReference)dataField.GetValue(pendingData);
				if (!reference.IsValid)
					continue;

				var liveField = FindLiveField(targetType, dataField.Name, typeof(Component), walkStopAt);

				if (liveField == null || !typeof(Component).IsAssignableFrom(liveField.FieldType))
				{
					Debug.Warn($"[ComponentReferenceResolver] '{target}' — no matching live field for data field '{dataField.Name}' of Component type found.");
					continue;
				}

				var resolved = FindComponentAot(scene, reference);
				if (resolved != null)
					liveField.SetValue(target, resolved);
				else
					Debug.Error($"[ComponentReferenceResolver] Could not resolve ComponentReference '{reference}' " +
					            $"for field '{dataField.Name}' on {label} '{target}'.");
			}
			else if (dataField.FieldType == typeof(EntityReference))
			{
				var reference = (EntityReference)dataField.GetValue(pendingData);
				if (!reference.IsValid)
					continue;

				var liveField = FindLiveField(targetType, dataField.Name, null, walkStopAt);
				if (liveField == null)
					continue;

				var resolvedEntity = FindEntityAot(scene, reference);
				if (resolvedEntity == null)
				{
					Debug.Error($"[ComponentReferenceResolver] Could not resolve EntityReference '{reference}' " +
					            $"for field '{dataField.Name}' on {label} '{target}'.");
					continue;
				}

				if (liveField.FieldType == typeof(Entity))
					liveField.SetValue(target, resolvedEntity);
				else if (liveField.FieldType == typeof(Transform))
					liveField.SetValue(target, resolvedEntity.Transform);
			}
		}

		pendingData = null;
	}

	/// <summary>
	/// Finds a live field on <paramref name="targetType"/> (including base types up to but
	/// not including <paramref name="walkStopAt"/>) that corresponds to a data field named
	/// <paramref name="dataFieldName"/>.
	///
	/// The source generator applies <c>GetDataFieldName</c> which strips a leading underscore
	/// and capitalises the next character (e.g. <c>_myRef</c> → <c>MyRef</c>). This method
	/// reverses that transform so both <c>myRef</c> (exact match) and <c>_myRef</c>
	/// (underscore-prefixed variant) are tried.
	///
	/// Used for both Component and SceneComponent hierarchies — pass typeof(Component) or
	/// typeof(SceneComponent) as <paramref name="walkStopAt"/>.
	/// </summary>
	private static System.Reflection.FieldInfo FindLiveField(Type targetType, string dataFieldName, Type mustBeAssignableTo, Type walkStopAt)
	{
		const System.Reflection.BindingFlags flags =
			System.Reflection.BindingFlags.Public |
			System.Reflection.BindingFlags.NonPublic |
			System.Reflection.BindingFlags.Instance;

		// Candidate names: exact match, then underscore-prefixed camelCase variant.
		string camelVariant = null;
		if (dataFieldName.Length > 0 && char.IsUpper(dataFieldName[0]))
			camelVariant = "_" + char.ToLowerInvariant(dataFieldName[0]) + dataFieldName.Substring(1);

		var type = targetType;
		while (type != null && type != walkStopAt)
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
				var comp = FindComponentOnEntityByName(entity, reference.ComponentId, reference.ComponentTypeName, reference.ComponentName);
				if (comp != null)
					return comp;
			}
		}

		if (!string.IsNullOrEmpty(reference.EntityName))
		{
			var entity = FindEntityByName(scene, reference.EntityName);
			if (entity != null)
			{
				var comp = FindComponentOnEntityByName(entity, reference.ComponentId, reference.ComponentTypeName, reference.ComponentName);
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
	/// <para>
	/// Phase 4b seam: when <paramref name="componentTypeName"/> does not match any live
	/// component, consults <see cref="TypeRenameRegistry"/> and retries with the current
	/// name so that <see cref="ComponentReference"/> fields survive a class/namespace rename.
	/// </para>
	/// </summary>
	private static Component FindComponentOnEntityByName(Entity entity, string componentId, string componentTypeName, string componentName)
	{
		// Resolve the target type by its stable [ComponentId] first — this survives a class or
		// namespace rename with no registry of old names. Falls back to the stored type name.
		string targetTypeName = componentTypeName;
		if (!string.IsNullOrEmpty(componentId) &&
		    ComponentIdRegistry.TryGetType(componentId, out var idType) && idType.FullName != null)
		{
			targetTypeName = idType.FullName;
		}

		// Direct match pass.
		for (var i = 0; i < entity.Components.Count; i++)
		{
			var comp = entity.Components[i];
			if (comp.GetType().FullName != targetTypeName)
				continue;
			if (string.IsNullOrEmpty(componentName) || comp.Name == componentName)
				return comp;
		}

		var pending = entity.Components.GetComponentsToAddList();
		for (var i = 0; i < pending.Count; i++)
		{
			var comp = pending[i];
			if (comp.GetType().FullName != targetTypeName)
				continue;
			if (string.IsNullOrEmpty(componentName) || comp.Name == componentName)
				return comp;
		}

		// Rename-registry fallback: if the stored type name is a former name, resolve the
		// current type and retry.  This handles ComponentReference fields in saved data that
		// refer to a component whose class or namespace was renamed after Phase 4a.
		if (TypeRenameRegistry.TryResolve(componentTypeName, out var currentType))
		{
			var currentTypeName = currentType.FullName;

			for (var i = 0; i < entity.Components.Count; i++)
			{
				var comp = entity.Components[i];
				if (comp.GetType().FullName != currentTypeName)
					continue;
				if (string.IsNullOrEmpty(componentName) || comp.Name == componentName)
					return comp;
			}

			for (var i = 0; i < pending.Count; i++)
			{
				var comp = pending[i];
				if (comp.GetType().FullName != currentTypeName)
					continue;
				if (string.IsNullOrEmpty(componentName) || comp.Name == componentName)
					return comp;
			}
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
}