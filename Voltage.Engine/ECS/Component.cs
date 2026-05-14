using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.CompilerServices;
using Voltage.Persistence;
using Voltage.Serialization;

namespace Voltage;

/// <summary>
/// ComponentData class for Components. This is used to serialize Component data to JSON.
/// </summary>
[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public abstract class ComponentData
{
	public bool Enabled = true;
}

// Helper struct to store component type and its data as JSON
[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public struct ComponentDataEntry
{
	public string ComponentTypeName;
	public string ComponentName; // In case there are multiple components of the same type on an Entity, this is used to differentiate them.
	public string DataTypeName;
	public string Json;
}

/// <summary>
/// Execution order:
/// - OnStart
/// - OnEnabled
///
/// Removal:
/// - OnRemovedFromEntity
///
/// </summary>
public class Component : IComparable<Component>
{
	public bool IsSerialized { get; protected set; }

	/// <summary>
	/// the Entity this Component is attached to
	/// </summary>
	[JsonExclude]
	public Entity Entity;

	/// <summary>
	/// shortcut to entity.transform
	/// </summary>
	/// <value>The transform.</value>
	[JsonExclude]
	public Transform Transform
	{
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		get => Entity.Transform;
	}

	/// <summary>
	/// Gets or sets the serializable data for this component.
	///
	/// Override priority (highest to lowest):
	/// 1. Manual override in engine components (SpriteRenderer, Collider, lights, etc.)
	/// 2. Source-generated override emitted by Voltage.SourceGenerators for partial Component subclasses.
	///
	/// The base returns null. A component whose class is NOT marked partial will produce no
	/// serialization entry and its fields will reset on scene reload - making the missing
	/// partial keyword visible immediately in the editor rather than silently working via
	/// reflection and then breaking in a published build.
	/// </summary>
	public virtual ComponentData Data
	{
		get => null;
		set { }
	}

	/// <summary>
	/// true if the Component is enabled and the Entity is enabled. When enabled this Components lifecycle methods will be called.
	/// Changes in state result in onEnabled/onDisable being called.
	/// </summary>
	/// <value><c>true</c> if enabled; otherwise, <c>false</c>.</value>
	public bool Enabled
	{
		get => Entity != null ? Entity.Enabled && _enabled : _enabled;
		set => SetEnabled(value);
	}

	/// <summary>
	/// update order of the Components on this Entity
	/// </summary>
	/// <value>The order.</value>
	public int UpdateOrder
	{
		get => _updateOrder;
		set => SetUpdateOrder(value);
	}

	/// <summary>
	/// Show the desired name of the component in the ImGui inspector. If null, the type name will be used.
	/// </summary>
	public string Name { get; set; }

	private bool _enabled = true;

	internal int _updateOrder = 0;

	#region Component Lifecycle

	public Component(string name = null, bool isSerialized = false)
	{
		Name = name ?? GetType().Name;
		IsSerialized = isSerialized;
	}

	public void SetSerialized(bool isOn)
	{
		IsSerialized = isOn;
	}

	/// <summary>
	/// called when this Component has had its Entity assigned but it is NOT yet added to the live Components list of the Entity yet. Useful
	/// for things like physics Components that need to access the Transform to modify collision body properties.
	/// </summary>
	public virtual void Initialize()
	{
	}

	/// <summary>
	/// Called when this component is added to a scene after all pending component changes are committed. At this point, the Entity field
	/// is set and the Entity.Scene is also set.
	/// </summary>
	public virtual void OnStart()
	{
	}

	/// <summary>
	/// Called when this component is removed from its entity. Do all cleanup here.
	/// </summary>
	public virtual void OnRemovedFromEntity()
	{
	}

	/// <summary>
	/// called when the parent Entity or this Component is enabled
	/// </summary>
	public virtual void OnEnabled()
	{
	}

	/// <summary>
	/// called when the parent Entity or this Component is disabled
	/// </summary>
	public virtual void OnDisabled()
	{
	}

	/// <summary>
	/// called when the entity's position changes. This allows components to be aware that they have moved due to the parent
	/// entity moving.
	/// </summary>
	public virtual void OnEntityTransformChanged(Transform.Component comp)
	{
	}

	public virtual void DebugRender(Batcher batcher)
	{
	}

	#endregion

	#region Fluent setters

	public Component SetEnabled(bool isEnabled)
	{
		if (_enabled != isEnabled)
		{
			_enabled = isEnabled;

			if (_enabled)
				OnEnabled();
			else
				OnDisabled();
		}

		return this;
	}

	public Component SetUpdateOrder(int updateOrder)
	{
		if (_updateOrder != updateOrder)
		{
			_updateOrder = updateOrder;
			if (Entity != null)
				Entity.Components.MarkEntityListUnsorted();
		}

		return this;
	}

	#endregion

	/// <summary>
	/// Creates a clone of this component. Override this method in derived classes for proper deep copying.
	/// Default implementation creates a new instance of the same type but doesn't copy any data.
	/// </summary>
	/// <returns>A new component instance</returns>
	public virtual Component Clone()
	{
		var componentType = GetType();
		var clone = (Component)Activator.CreateInstance(componentType);
		
		clone.Name = Name;
		clone.Enabled = Enabled;
		clone.Entity = null;
		
		return clone;
	}

	public int CompareTo(Component other)
	{
		return _updateOrder.CompareTo(other._updateOrder);
	}


	public override string ToString()
	{
		var type = GetType();
		string typeName = type.IsGenericType
			? $"{type.BaseType.Name}<{type.GetGenericArguments()[0].Name}>"
			: type.Name;

		// If the component's name is null or empty, treat it as the type name
		var compName = string.IsNullOrEmpty(Name) ? typeName : Name;

		// Show only type if name matches type, otherwise show "Name (Type)"
		var displayName = compName == typeName ? typeName : $"{compName} ({typeName})";

		// Prepend entity name if available
		if (Entity != null && !string.IsNullOrEmpty(Entity.Name))
			return $"{Entity.Name}.{displayName}";
		else
			return displayName;
	}

	/// <summary>
	/// Returns all component types that must exist on an entity before
	/// <paramref name="componentType"/> can be added, resolved recursively
	/// so dependency chains are fully satisfied. The list is ordered
	/// deepest-dependency-first so callers can add them in sequence.
	/// </summary>
	public static List<Type> ResolveRequiredComponents(Type componentType)
	{
		var result = new List<Type>();
		var visited = new HashSet<Type>();
		CollectRequirements(componentType, result, visited);
		return result;
	}

	private static void CollectRequirements(Type type, List<Type> result, HashSet<Type> visited)
	{
		if (type == null || !visited.Add(type))
			return;

		foreach (var attr in type.GetCustomAttributes(typeof(RequireComponentAttribute), inherit: true)
		                         .Cast<RequireComponentAttribute>())
		{
			CollectRequirements(attr.ComponentType, result, visited); // recurse into dependency's own requirements
			if (!result.Contains(attr.ComponentType))
				result.Add(attr.ComponentType);
		}
	}

	public static List<RequireComponentInChildrenAttribute> ResolveRequiredComponentsInChildren(Type componentType)
	{
		var result = new List<RequireComponentInChildrenAttribute>();
		var visited = new HashSet<Type>();
		CollectChildRequirements(componentType, result, visited);
		return result;
	}

	private static void CollectChildRequirements(
		Type type,
		List<RequireComponentInChildrenAttribute> result,
		HashSet<Type> visited)
	{
		if (type == null || !visited.Add(type))
			return;

		foreach (var attr in type.GetCustomAttributes(typeof(RequireComponentInChildrenAttribute), inherit: true)
			         .Cast<RequireComponentInChildrenAttribute>())
		{
			// Recurse so chains like A-> [RequireInChildren B] -> [RequireInChildren C] are resolved
			CollectChildRequirements(attr.ComponentType, result, visited);

			bool alreadyQueued = result.Any(r => r.ComponentType == attr.ComponentType);
			if (!alreadyQueued)
				result.Add(attr);
		}
	}

	#region Entity shortcut methods

	public T AddComponent<T>(T component, bool allowSameComponentsOnEntity = false) where T : Component
		=> Entity.AddComponent(component, allowSameComponentsOnEntity);

	public T AddComponent<T>() where T : Component, new()
		=> Entity.AddComponent<T>();

	public T GetComponent<T>() where T : class
		=> Entity.GetComponent<T>();

	public T GetComponent<T>(string name) where T : class
		=> Entity.GetComponent<T>(name);

	public Component GetComponent(Type type)
		=> Entity.GetComponent(type);

	public bool TryGetComponent<T>(out T component) where T : class
		=> Entity.TryGetComponent(out component);

	public bool HasComponent<T>() where T : class
		=> Entity.HasComponent<T>();

	public T GetOrCreateComponent<T>() where T : Component, new()
		=> Entity.GetOrCreateComponent<T>();

	public List<T> GetComponents<T>() where T : class
		=> Entity.GetComponents<T>();

	public void GetComponents<T>(List<T> componentList) where T : class
		=> Entity.GetComponents<T>(componentList);

	public T GetComponentInChildren<T>() where T : class
		=> Entity.GetComponentInChildren<T>();

	public List<T> GetComponentsInChildren<T>() where T : class
		=> Entity.GetComponentsInChildren<T>();

	public void RemoveComponent()
		=> Entity.RemoveComponent(this);

	public bool RemoveComponent<T>() where T : Component
		=> Entity.RemoveComponent<T>();

	#endregion
}