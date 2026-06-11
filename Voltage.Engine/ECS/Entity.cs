using Microsoft.Xna.Framework;
using Voltage.Persistence;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Voltage.Data;
using Voltage.Serialization.Registries;
using Voltage.Utils.Coroutines;

namespace Voltage;

public sealed class Entity : IComparable<Entity>
{
	public enum InstanceType
	{
		/// <summary>
		/// Only created via code (e.g., Temporary child-entity during run-time through code.). 
		/// Usually reserved for entities that exist only once in a scene, or those that
		/// need tight data integration with other entities.
		/// </summary>
		NonSerialized,

		/// <summary>
		/// Default entities. Can be created at runtime via the Editor, and can be later turned into Prefabs.
		/// </summary>
		Serialized,

		/// <summary>
		/// A reusable template based on an Entity and its components/children that we created in the Editor.
		/// Used to create (e.g. based on the initial Platform entity we can create: 1) Platform_Short.prefab, 2) Platform_Big.prefab).
		/// </summary>
		SerializedPrefab,

		/// <summary>
		/// An entity that is always present in a scene and cannot be deleted.
		/// Serialized like <see cref="Serialized"/> (transform, components, children are saved/loaded),
		/// but with additional constraints:
		///   - Cannot be destroyed or removed from the scene.
		///   - Only one entity of a given name with this type may exist per scene.
		///   - When a prefab targets an existing SceneRequired entity, the entity is
		///     merge-replaced (components/transform/children replaced) rather than duplicated.
		/// Currently used for the main Camera entity; future candidates include AudioListener, GameManager, etc.
		/// </summary>
		SceneRequired,
	}

	public InstanceType Type;

	private static uint _idGenerator;

	#region properties and fields

	/// <summary>
	/// the scene this entity belongs to
	/// </summary>
	[JsonExclude]
	public Scene Scene;

	/// <summary>
	/// entity name. useful for doing scene-wide searches for an entity
	/// </summary>
	public string Name
	{
		get => _name;
		set
		{
			if (Scene != null)
			{
				_name = Scene.GetUniqueEntityName(value, this);
			}
			else
			{
				_name = value;
			}
		}
	}

	public string OriginalPrefabName
	{
		get => _originalPrefabName;
		set
		{
			if (Type != InstanceType.SerializedPrefab)
				return;

			_originalPrefabName = value;
		}
	}

	/// <summary>
	/// Unique identifer for this Entity
	/// </summary>
	public readonly uint Id;

	/// <summary>
	/// Used for reliable ComponentReference resolution across save/load cycles.
	/// </summary>

	public Guid PersistentId = Guid.NewGuid();

	/// <summary>
	/// Encapsulates the Entity's position/rotation/scale and allows setting up a hieararchy
	/// </summary>
	public readonly Transform Transform;

	/// <summary>
	/// List of all the components currently attached to this entity
	/// </summary>
	[JsonExclude]
	public readonly ComponentList Components;

	/// <summary>
	/// Only affects the selection by mouse in Editor
	/// </summary>
	public bool CanBeSelected = true;

	[JsonExclude] 
	public List<Component> ComponentsToAdd => Components.ComponentsToAdd;

	/// <summary>
	/// use this however you want to. It can later be used to query the scene for all Entities with a specific tag
	/// </summary>
	[EntityTag]
	public int Tag
	{
		get => _tag;
		set => SetTag(value);
	}

	/// <summary>
	/// specifies how often this entitys update method should be called. 1 means every frame, 2 is every other, etc
	/// </summary>
	public uint UpdateInterval = 1;

	/// <summary>
	/// enables/disables the Entity. When disabled colliders are removed from the Physics system and components methods will not be called
	/// </summary>
	public bool Enabled
	{
		get => _enabled;
		set => SetEnabled(value);
	}

	public bool DebugRenderEnabled
	{
		get => _debugRenderEnabled;
		set => _debugRenderEnabled = value;
	}

	/// <summary>
	/// update order of this Entity. updateOrder is also used to sort tag lists on scene.entities
	/// </summary>
	/// <value>The order.</value>
	public int UpdateOrder
	{
		get => _updateOrder;
		set => SetUpdateOrder(value);
	}

	/// <summary>
	/// if destroy was called, this will be true until the next time Entitys are processed
	/// </summary>
	public bool IsDestroyed => _isDestroyed;

	/// <summary>
	/// flag indicating if destroy was called on this Entity
	/// </summary>
	internal bool _isDestroyed;

	private int _tag = 0;
	private bool _enabled = true;
	private bool _debugRenderEnabled = true;
	internal int _updateOrder = 0;
	private string _name;
	private string _originalPrefabName;

	#region Event Subscription
	public event Action AddedToScene;
	public event Action RemovedFromScene;
	#endregion

	#region Serialization data structs
	private readonly Dictionary<Type, List<Delegate>> _componentAddedCallbacks = new();
	private readonly Dictionary<Type, List<Delegate>> _childAddedCallbacks = new();

	/// <summary>
	/// Entity-specific data for serialization.
	/// </summary>
	public EntityData EntityData = new EntityData();

	/// <summary>
	/// Override this in derived classes to provide entity-specific data serialization.
	/// This is called when the entity needs to serialize its current state.
	/// </summary>
	public EntityData GetEntityData()
	{
		return EntityData;
	}

	/// <summary>
	/// Override this in derived classes to apply loaded entity data.
	/// This is called when entity data is loaded from JSON.
	/// </summary>
	public void SetEntityData(EntityData data)
	{
		if (data != null)
		{
			EntityData = data;
		}
	}

	#endregion


	#endregion


	#region Transform passthroughs

	public Transform Parent
	{
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		get => Transform.Parent;
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		set => Transform.SetParent(value);
	}

	public int ChildCount
	{
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		get => Transform.ChildCount;
	}

	public Vector2 Position
	{
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		get => Transform.Position;
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		set => Transform.SetPosition(value);
	}

	public Vector2 LocalPosition
	{
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		get => Transform.LocalPosition;
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		set => Transform.SetLocalPosition(value);
	}

	public float Rotation
	{
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		get => Transform.Rotation;
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		set => Transform.SetRotation(value);
	}

	public float RotationDegrees
	{
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		get => Transform.RotationDegrees;
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		set => Transform.SetRotationDegrees(value);
	}

	public float LocalRotation
	{
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		get => Transform.LocalRotation;
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		set => Transform.SetLocalRotation(value);
	}

	public float LocalRotationDegrees
	{
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		get => Transform.LocalRotationDegrees;
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		set => Transform.SetLocalRotationDegrees(value);
	}

	public Vector2 Scale
	{
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		get => Transform.Scale;
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		set => Transform.SetScale(value);
	}

	public Vector2 LocalScale
	{
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		get => Transform.LocalScale;
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		set => Transform.SetLocalScale(value);
	}

	public Matrix2D WorldInverseTransform
	{
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		get => Transform.WorldInverseTransform;
	}

	public Matrix2D LocalToWorldTransform
	{
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		get => Transform.LocalToWorldTransform;
	}

	public Matrix2D WorldToLocalTransform
	{
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		get => Transform.WorldToLocalTransform;
	}

	#endregion


	public Entity(string name, InstanceType type = InstanceType.NonSerialized)
	{
		Components = new ComponentList(this);
		Transform = new Transform(this);
		_name = name; 
		Id = _idGenerator++;
		DebugRenderEnabled = Core.DebugRenderEnabled;
		Type = type;

		// Since NonSerialized entities cannot be created in the Editor, we set this to false
		if (Type == InstanceType.NonSerialized) 
			CanBeSelected = false;
	}

	public Entity() : this(Utils.Utils.RandomString(8))
	{
		if (Type == InstanceType.NonSerialized)
			CanBeSelected = false;
	}

	internal void OnTransformChanged(Transform.Component comp)
	{
		// notify our children of our changed position
		Components.OnEntityTransformChanged(comp);
	}

	public void FinishInit()
	{

	}

	#region Fluent setters

	/// <summary>
	/// sets the tag for the Entity
	/// </summary>
	/// <returns>The tag.</returns>
	/// <param name="tag">Tag.</param>
	public Entity SetTag(int tag)
	{
		if (_tag != tag)
		{
			// we only call through to the entityTagList if we already have a scene. if we dont have a scene yet we will be
			// added to the entityTagList when we do
			if (Scene != null)
				Scene.Entities.RemoveFromTagList(this);
			_tag = tag;
			if (Scene != null)
				Scene.Entities.AddToTagList(this);
		}

		return this;
	}

	/// <summary>
	/// sets the enabled state of the Entity. When disabled colliders are removed from the Physics system and components methods will not be called
	/// </summary>
	/// <returns>The enabled.</returns>
	/// <param name="isEnabled">If set to <c>true</c> is enabled.</param>
	public Entity SetEnabled(bool isEnabled)
	{
		if (_enabled != isEnabled)
		{
			_enabled = isEnabled;

			if (_enabled)
				Components.OnEntityEnabled();
			else
				Components.OnEntityDisabled();
		}

		return this;
	}

	/// <summary>
	/// sets the update order of this Entity. updateOrder is also used to sort tag lists on scene.entities
	/// </summary>
	/// <returns>The update order.</returns>
	/// <param name="updateOrder">Update order.</param>
	public Entity SetUpdateOrder(int updateOrder)
	{
		if (_updateOrder != updateOrder)
		{
			_updateOrder = updateOrder;
			if (Scene != null)
			{
				Scene.Entities.MarkEntityListUnsorted();
				Scene.Entities.MarkTagUnsorted(Tag);
			}
		}

		return this;
	}

	#endregion


	/// <summary>
	/// removes the Entity from the scene and destroys all children.
	/// SceneRequired entities cannot be destroyed — the call is silently ignored.
	/// </summary>
	public void Destroy()
	{
		if (Scene == null)
			return;

		if (Type == InstanceType.SceneRequired)
		{
			Debug.Warn($"Cannot destroy SceneRequired entity '{Name}'. SceneRequired entities must always be present in the scene.");
			return;
		}

		_isDestroyed = true;
		Scene.Entities.Remove(this);
		Transform.Parent = null;

		// destroy any children we have
		for (var i = Transform.ChildCount - 1; i >= 0; i--)
		{
			var child = Transform.GetChild(i);
			child.Entity.Destroy();
		}
	}

	public void Destroy(float timer)
	{
		Core.StartCoroutine(DestroyAfterDelay(timer));
	}

	private IEnumerator DestroyAfterDelay(float timer)
	{
		yield return Coroutine.WaitForSeconds(timer);
		Destroy();
	}
	
	/// <summary>
	/// detaches the Entity from the scene.
	/// the following lifecycle method will be called on the Entity: OnRemovedFromScene
	/// the following lifecycle method will be called on the Components: OnRemovedFromEntity
	/// </summary>
	public void DetachFromScene()
	{
        Scene.Entities.Remove(this);
		Components.DeregisterAllComponents();

		for (var i = 0; i < Transform.ChildCount; i++)
			Transform.GetChild(i).Entity.DetachFromScene();
	}

	/// <summary>
	/// attaches an Entity that was previously detached to a new scene
	/// </summary>
	/// <param name="newScene">New scene.</param>
	public void AttachToScene(Scene newScene)
	{
		Scene = newScene;
		newScene.Entities.Add(this);
		Components.RegisterAllComponents();

		for (var i = 0; i < Transform.ChildCount; i++)
			Transform.GetChild(i).Entity.AttachToScene(newScene);
	}

	/// <summary>
	/// copies the properties, components and colliders of Entity to this instance
	/// </summary>
	/// <param name="entity">Entity.</param>
	public void CopyEntityFrom(Entity entity, string customName = null, InstanceType type = InstanceType.Serialized)
	{
		Type = type;
		Name = customName ?? entity.Name;
		Tag = entity.Tag;
		UpdateOrder = entity.UpdateOrder;
		Enabled = entity.Enabled;
		Transform.Position = entity.Transform.Position;
		Transform.Rotation = entity.Transform.Rotation;
		Transform.Scale = entity.Transform.Scale;

		if(Type == InstanceType.SerializedPrefab)
			OriginalPrefabName = entity.OriginalPrefabName;

		for (var i = 0; i < entity.Components.Count; i++)
		{
			var sourceComponent = entity.Components[i];
			
			// Create new component instance
			var componentType = sourceComponent.GetType();
			Component clonedComponent;

			try
			{
				var typeId = componentType.FullName ?? componentType.Name;
				clonedComponent = ComponentAotFactory.IsRegistered(typeId)
					? (Component)ComponentAotFactory.Create(typeId)
					: (Component)Activator.CreateInstance(componentType);
			}
			catch (Exception ex)
			{
				Debug.Error($"Failed to create component {componentType.Name}: {ex.Message}");
				continue;
			}

			clonedComponent.Name = sourceComponent.Name;
			clonedComponent.Enabled = sourceComponent.Enabled;

			AddComponent(clonedComponent);

			// Use JSON serialization for reliable component data copying
			if (sourceComponent.Data != null)
			{
				try
				{
					var componentJsonSettings = new JsonSettings
					{
						PrettyPrint = false,
						TypeNameHandling = TypeNameHandling.Auto,
						PreserveReferencesHandling = false
					};
					
					var json = Json.ToJson(sourceComponent.Data, componentJsonSettings);
					var clonedData = (ComponentData)Json.FromJson(json, sourceComponent.Data.GetType());
					clonedComponent.Data = clonedData;
				}
				catch (Exception ex)
				{
					Debug.Warn($"Failed to copy component data via JSON for {sourceComponent.GetType().Name}: {ex.Message}");
				}
			}
		}
	}


	/// <summary>
	/// Find a component in this entity that has the same type and name as the source component, and then copies it using JSON serialization
	/// </summary>
	/// <param name="entity"></param>
	public void CopySameComponentFromEntity(Entity entity)
	{
		foreach (var sourceComponent in entity.Components)
		{
			// Try to find a matching component in this entity (by type and name)
			var targetComponent = Components.FirstOrDefault(c => c.GetType() == sourceComponent.GetType() && c.Name == sourceComponent.Name);

			if (targetComponent != null)
			{
				// Use the same approach as component paste with JSON serialization
				if (sourceComponent.Data != null)
				{
					try
					{
						// Use JSON serialization for reliable component data copying (same as paste)
						var componentJsonSettings = new JsonSettings
						{
							PrettyPrint = false,
							TypeNameHandling = TypeNameHandling.Auto,
							PreserveReferencesHandling = false
						};

						var json = Json.ToJson(sourceComponent.Data, componentJsonSettings);
						var clonedData = (ComponentData)Json.FromJson(json, sourceComponent.Data.GetType());
						targetComponent.Data = clonedData;

					}
					catch (Exception ex)
					{
						Debug.Error(
							$"Failed to copy component data via JSON for {sourceComponent.GetType().Name}: {ex.Message}");

						// Fallback to Clone method
						try
						{
							var fallbackClone = sourceComponent.Clone();
							if (fallbackClone?.Data != null)
							{
								targetComponent.Data = fallbackClone.Data;
								Debug.Info(
									$"Used Clone() fallback for component: {sourceComponent.GetType().Name}");
							}
						}
						catch (Exception cloneEx)
						{
							Debug.Error(
								$"Clone fallback also failed for {sourceComponent.GetType().Name}: {cloneEx.Message}");
						}
					}
				}
			}
			else
			{
				// No existing component of this type, create a new one using JSON serialization
				var componentType = sourceComponent.GetType();
				Component newComponent;

				try
				{
					var typeId = componentType.FullName ?? componentType.Name;
					newComponent = ComponentAotFactory.IsRegistered(typeId)
						? (Component)ComponentAotFactory.Create(typeId)
						: (Component)Activator.CreateInstance(componentType);
				}
				catch (Exception ex)
				{
					Debug.Error($"Failed to create component {componentType.Name}: {ex.Message}");
					continue;
				}

				// Copy basic component properties
				newComponent.Name = sourceComponent.Name;
				newComponent.Enabled = sourceComponent.Enabled;

				// Add the component first
				AddComponent(newComponent);

				// Use JSON serialization for reliable component data copying
				if (sourceComponent.Data != null)
				{
					try
					{
						var componentJsonSettings = new JsonSettings
						{
							PrettyPrint = false,
							TypeNameHandling = TypeNameHandling.Auto,
							PreserveReferencesHandling = false
						};
						
						var json = Json.ToJson(sourceComponent.Data, componentJsonSettings);
						var clonedData = (ComponentData)Json.FromJson(json, sourceComponent.Data.GetType());
						newComponent.Data = clonedData;
					}
					catch (Exception ex)
					{
						Debug.Error($"Failed to copy component data via JSON for {sourceComponent.GetType().Name}: {ex.Message}");
						
						// Fallback to Clone method
						try
						{
							var fallbackClone = sourceComponent.Clone();
							if (fallbackClone?.Data != null)
							{
								newComponent.Data = fallbackClone.Data;
							}
						}
						catch (Exception cloneEx)
						{
							Debug.Error($"Clone fallback also failed for {sourceComponent.GetType().Name}: {cloneEx.Message}");
						}
					}
				}
			}
		}
	}


	#region Entity lifecycle methods

	public void OnAddedToScene()
	{
		AddedToScene?.Invoke();
	}

	/// <summary>
	/// Called when this entity is removed from a scene
	/// </summary>
	public void OnRemovedFromScene()
	{
		// if we were destroyed, remove our components. If we were just detached we need to keep our components on the Entity.
		if (_isDestroyed)
			Components.RemoveAllComponents();

		RemovedFromScene?.Invoke();
	}

	/// <summary>
	/// called each frame as long as the Entity is enabled
	/// </summary>
	public void Update()
	{
		Components.Update();
	}

	/// <summary>
	/// called if Core.debugRenderEnabled is true by the default renderers. Custom renderers can choose to call it or not.
	/// </summary>
	/// <param name="batcher">Batcher.</param>
	public void DebugRender(Batcher batcher)
	{
		Components.DebugRender(batcher);
	}

	#endregion


	#region Component Management

	/// <summary>
	/// Adds a Component to the components list. Returns the Component.
	/// Ensures a unique name for multiple components of the same type. 
	/// </summary>
	/// <returns>Scene.</returns>
	/// <param name="component">Component.</param>
	/// <typeparam name="T">The 1st type parameter.</typeparam>
	public T AddComponent<T>(T component, bool allowSameComponentsOnEntity = false) where T : Component
	{
		var type = component.GetType();
		var existingComponents = new List<T>();
		Components.GetComponents(existingComponents);

		foreach (var comp in existingComponents)
		{
			if (!allowSameComponentsOnEntity && comp.GetType() == type)
			{
				Debug.Error($"Can't add the same Component more than once on Entity: {this.Name}");
				return comp;
			}

			if (comp.GetType() == type && comp.Name == component.Name)
			{
				Debug.Error($"Can't add two components with the same name: {comp.Name}, on Entity: {this.Name}");
				return comp;
			}
		}

		int maxIndex = -1;
		foreach (var comp in existingComponents)
		{
			if (comp.GetType() == type)
			{
				if (!string.IsNullOrEmpty(comp.Name))
				{
					// Check for pattern: TypeName or TypeName_N
					if (comp.Name == type.Name)
					{
						maxIndex = Math.Max(maxIndex, 0);
					}
					else if (comp.Name.StartsWith(type.Name + "_"))
					{
						var suffix = comp.Name.Substring(type.Name.Length + 1);
						if (int.TryParse(suffix, out int idx))
							maxIndex = Math.Max(maxIndex, idx);
					}
				}
				else
				{
					maxIndex = Math.Max(maxIndex, 0);
				}
			}
		}

		string componentName = null;

		// Assign unique name if needed
		if (maxIndex >= 0)
		{
			componentName = $"{type.Name}_{maxIndex + 1}";
		}
		else if (string.IsNullOrEmpty(component.Name))
		{
			componentName = type.Name;
		}

		if (!string.IsNullOrEmpty(componentName))
			component.Name = componentName;

		component.Entity = this;
		Components.Add(component);
		try
		{
			// called before it's added to live list
			component.OnAddedToEntity();
		}
		catch (Exception ex)
		{
			Debug.Error($"Exception in OnAddedToEntity for component '{component.GetType().Name}' on entity '{component.Entity.Name}': {ex.Message}\n{ex.StackTrace}");
		}

		TriggerComponentAddedCallbacks(component);

		return component;
	}

	/// <summary>
	/// Adds a Component to the components list. Returns the Component.
	/// </summary>
	/// <returns>Scene.</returns>
	/// <typeparam name="T">The 1st type parameter.</typeparam>
	public T AddComponent<T>() where T : Component, new()
	{
		var component = new T();
		return AddComponent(component);
	}

	/// <summary>
	/// Gets the first component of type T and returns it. If no components are found returns null.
	/// </summary>
	/// <returns>The component.</returns>
	/// <typeparam name="T">The 1st type parameter.</typeparam>
	public T GetComponent<T>() where T : class
	{
		return Components.GetComponent<T>(false);
	}

	/// <summary>
	/// Gets the first component of type T with the specified name and returns it. If no component is found returns null.
	/// </summary>
	/// <returns>The component with the given name and type.</returns>
	/// <param name="name">Name of the component to find.</param>
	/// <typeparam name="T">The component type.</typeparam>
	public T GetComponent<T>(string name) where T : class
	{
		return Components.GetComponent<T>(name);
	}

	/// <summary>
	/// Tries to get the component of type T. If no components are found returns false.
	/// </summary>
	/// <returns>true if a component has been found.</returns>
	/// <typeparam name="T">The 1st type parameter.</typeparam>
	public bool TryGetComponent<T>(out T component) where T : class
	{
		component = Components.GetComponent<T>(false);
		return component != null;
	}

	/// <summary>
	/// checks to see if the Entity has the component
	/// </summary>
	public bool HasComponent<T>() where T : class
	{
		return Components.GetComponent<T>(false) != null;
	}

	/// <summary>
	/// Gets the first Component of type T and returns it. If no Component is found the Component will be created.
	/// </summary>
	/// <returns>The component.</returns>
	/// <typeparam name="T">The 1st type parameter.</typeparam>
	public T GetOrCreateComponent<T>() where T : Component, new()
	{
		var comp = Components.GetComponent<T>(true);
		if (comp == null)
			comp = AddComponent<T>();

		return comp;
	}

	/// <summary>
	/// Gets the first component of type T and returns it optionally skips checking un-initialized Components (Components who have not yet had their
	/// onAddedToEntity method called). If no components are found returns null.
	/// </summary>
	/// <returns>The component.</returns>
	/// <param name="onlyReturnInitializedComponents">If set to <c>true</c> only return initialized components.</param>
	/// <typeparam name="T">The 1st type parameter.</typeparam>
	public T GetComponent<T>(bool onlyReturnInitializedComponents) where T : class
	{
		return Components.GetComponent<T>(onlyReturnInitializedComponents);
	}

	/// <summary>
	/// Gets all the components of type T without a List allocation
	/// </summary>
	/// <param name="componentList">Component list.</param>
	/// <typeparam name="T">The 1st type parameter.</typeparam>
	public void GetComponents<T>(List<T> componentList) where T : class
	{
		Components.GetComponents(componentList);
	}

	/// <summary>
	/// Gets all the components of type T. The returned List can be put back in the pool via ListPool.free.
	/// </summary>
	/// <returns>The component.</returns>
	/// <typeparam name="T">The 1st type parameter.</typeparam>
	public List<T> GetComponents<T>() where T : class
	{
		return Components.GetComponents<T>();
	}

	/// <summary>
	/// removes the first Component of type T from the components list
	/// </summary>
	public bool RemoveComponent<T>() where T : Component
	{
		var comp = GetComponent<T>();
		if (comp != null)
		{
			RemoveComponent(comp);
			return true;
		}

		return false;
	}

	/// <summary>
	/// removes a Component from the components list
	/// </summary>
	/// <param name="component">The Component to remove</param>
	public void RemoveComponent(Component component)
	{
		Components.Remove(component);
	}

	/// <summary>
	/// removes all Components from the Entity
	/// </summary>
	public void RemoveAllComponents()
	{
		for (var i = 0; i < Components.Count; i++)
			RemoveComponent(Components[i]);
	}

	/// <summary>
	/// Removes a component from the entity that matches the type and name of the specified component,
	/// then adds the specified component to the entity.
	/// </summary>
	public T ReplaceComponent<T>(T component) where T : Component
	{
		if(HasComponent<T>())
			RemoveComponent<T>();

		return AddComponent(component);
	}

	#endregion

	#region Child Event callbacks
	/// <summary>
	/// Registers a callback that will be invoked whenever a child entity is added to this entity.
	/// </summary>
	public void OnChildAdded(Action<Entity> onAdded)
	{
		var type = typeof(Entity);
		if (!_childAddedCallbacks.TryGetValue(type, out var list))
		{
			list = new List<Delegate>();
			_childAddedCallbacks[type] = list;
		}
		list.Add(onAdded);

		// Immediately call for existing children
		foreach (var child in Transform.Children)
		{
			onAdded(child.Entity);
		}
	}

	/// <summary>
	/// Registers a callback that will be called once for the first child entity of type T added to this entity,
	/// then the callback is automatically removed.
	/// </summary>
	public void OnChildAddedOnce(Action<Entity> onAdded)
	{
		var oneShot = new OneShotDelegate<Entity>(onAdded);
		OnChildAdded(oneShot.Invoke);
	}

	internal void TriggerChildAddedCallbacks(Entity child)
	{
		var type = child.GetType();
		var delegatesToRemove = new List<(Type, Delegate)>();

		foreach (var kvp in _childAddedCallbacks)
		{
			if (kvp.Key.IsAssignableFrom(type))
			{
				foreach (var del in kvp.Value.ToArray())
				{
					del.DynamicInvoke(child);

					if (del.Target is IOneShotDelegate)
						delegatesToRemove.Add((kvp.Key, del));
				}
			}
		}

		// Remove one-shot delegates after invoking
		foreach (var (t, d) in delegatesToRemove)
			_childAddedCallbacks[t].Remove(d);
	}


	#endregion

	#region Component Event callbacks

	/// <summary>
	/// Registers a callback that will be invoked whenever a component of type <typeparamref name="T"/> is added to this entity.
	/// </summary>
	public void OnComponentAdded<T>(Action<T> onAdded) where T : Component
	{
		var type = typeof(T);
		if (!_componentAddedCallbacks.TryGetValue(type, out var list))
		{
			list = new List<Delegate>();
			_componentAddedCallbacks[type] = list;
		}
		list.Add(onAdded);
	}

	/// <summary>
	/// Registers a callback that will be called **once** for the first component of type T added to this entity,
	/// then the callback is automatically removed.
	/// </summary>
	public void OnComponentAddedOnce<T>(Action<T> onAdded) where T : Component
	{
		var oneShot = new OneShotDelegate<T>(onAdded);
		OnComponentAdded<T>(oneShot.Invoke);
	}

	internal void TriggerComponentAddedCallbacks(Component component)
	{
		var type = component.GetType();
		var delegatesToRemove = new List<(Type, Delegate)>();

		foreach (var kvp in _componentAddedCallbacks)
		{
			if (kvp.Key.IsAssignableFrom(type))
			{
				foreach (var del in kvp.Value.ToArray())
				{
					del.DynamicInvoke(component);

					// Remove if this is a one-shot delegate
					if (del.Target is IOneShotDelegate)
						delegatesToRemove.Add((kvp.Key, del));
				}
			}
		}

		// Remove one-shot delegates after invoking
		foreach (var (t, d) in delegatesToRemove)
			_componentAddedCallbacks[t].Remove(d);
	}

	#endregion
	public int CompareTo(Entity other)
	{
		var compare = _updateOrder.CompareTo(other._updateOrder);
		if (compare == 0)
			compare = Id.CompareTo(other.Id);
		return compare;
	}

	public override string ToString()
	{
		return string.Format("[Entity: name: {0}, tag: {1}, enabled: {2}, depth: {3}]", Name, Tag, Enabled,
			UpdateOrder);
	}

	/// <summary>
	/// Gets the first component whose type is assignable to <paramref name="componentType"/>
	/// and returns it as a <see cref="Component"/>. Returns null if none is found.
	/// Useful when the target type is only known at runtime (e.g. RequireComponent resolution).
	/// </summary>
	public Component GetComponent(Type componentType)
	{
		for (var i = 0; i < Components.Count; i++)
		{
			var component = Components[i];
			if (componentType.IsAssignableFrom(component.GetType()))
				return component;
		}

		for (var i = 0; i < Components.ComponentsToAdd.Count; i++)
		{
			var component = Components.ComponentsToAdd[i];
			if (componentType.IsAssignableFrom(component.GetType()))
				return component;
		}

		return null;
	}

	/// <summary>
	/// Searches this entity and all of its children (recursively, depth-first) for the
	/// first component of type <typeparamref name="T"/>. Returns null if none is found.
	/// </summary>
	public T GetComponentInChildren<T>() where T : class
	{
	var result = GetComponent<T>();
	if (result != null)
		return result;

	for (var i = 0; i < Transform.ChildCount; i++)
	{
		result = Transform.GetChild(i).Entity.GetComponentInChildren<T>();
		if (result != null)
			return result;
	}

	return null;
	}

	/// <summary>
	/// Searches all children (recursively, depth-first) for all components of type
	/// <typeparamref name="T"/> and appends them to <paramref name="results"/>.
	/// Does NOT include components on this entity itself.
	/// </summary>
	public void GetComponentsInChildren<T>(List<T> results) where T : class
	{
	for (var i = 0; i < Transform.ChildCount; i++)
	{
		var child = Transform.GetChild(i).Entity;
		child.GetComponents<T>(results);
		child.GetComponentsInChildren<T>(results);
	}
	}

	/// <summary>
	/// Returns all components of type <typeparamref name="T"/> found on this entity
	/// and all of its children recursively.
	/// </summary>
	public List<T> GetComponentsInChildren<T>() where T : class
	{
		var results = new List<T>();
		GetComponents<T>(results);
		GetComponentsInChildren<T>(results);
		return results;
	}

	/// <summary>
	/// Searches the parent chain of this entity for the first component of type
	/// <typeparamref name="T"/>. Does NOT include components on this entity itself.
	/// Returns null if none is found.
	/// </summary>
	public T GetComponentInParent<T>() where T : class
	{
		var parent = Transform.Parent;
		while (parent != null)
		{
			var result = parent.Entity.GetComponent<T>();
			if (result != null)
				return result;

			parent = parent.Parent;
		}

		return null;
	}

	/// <summary>
	/// Searches the entire parent chain of this entity and collects all components
	/// of type <typeparamref name="T"/>. Does NOT include components on this entity itself.
	/// </summary>
	public List<T> GetComponentsInParents<T>() where T : class
	{
		var results = new List<T>();
		var parent = Transform.Parent;
		while (parent != null)
		{
			parent.Entity.GetComponents<T>(results);
			parent = parent.Parent;
		}

		return results;
	}
}