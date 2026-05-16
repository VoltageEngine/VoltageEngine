using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Voltage.Data;

namespace Voltage
{
	public partial class Scene
	{
		/// <summary>
		/// add the Entity to this Scene, and return it
		/// </summary>
		/// <returns></returns>
		public Entity SimpleCreateEntity<TData>(string name, Entity.InstanceType type) where TData : EntityData, new()
		{
			var entity = new Entity(name, type);
			if (entity.EntityData == null)
				entity.EntityData = new TData();

			return AddEntity(entity);
		}

		/// <summary>
		/// add the Entity to this Scene at position, and return it
		/// </summary>
		/// <returns>The entity.</returns>
		/// <param name="name">Name.</param>
		/// <param name="position">Position.</param>
		public Entity SimpleCreateEntity<TData>(string name, Vector2 position) where TData : EntityData, new()
		{
			var entity = new Entity(name);
			entity.Transform.Position = position;
			if (entity.EntityData == null)
				entity.EntityData = new TData();

			return AddEntity(entity);
		}

		/// <summary>
		/// searches for and returns the first Entity with a matching PersistentId. If none are found returns null.
		/// </summary>
		/// <param name="persistentId">The persistent GUID of the entity.</param>
		public Entity FindEntityByPersistentId(Guid persistentId)
		{
			return Entities.FindEntityByPersistentId(persistentId);
		}

		public Entity SimpleCreateEntity(string name, Entity.InstanceType type)
		{
			var entity = new Entity(name, type);
			return AddEntity(entity);
		}

		#region Wait for Entity Added

		/// <summary>
		/// Registers a callback that will be invoked whenever an entity with the specified name is added to the scene.
		/// <para>
		/// This is useful for systems or scripts that need to automatically act on specific entities as they are added,
		/// such as setting up components, event hooks, or performing initialization logic.
		/// </para>
		/// </summary>
		/// <param name="entityName">The name of the entity to listen for.</param>
		/// <param name="onAdded">
		/// The function to execute when an entity with the specified name is added to the scene.
		/// The entity instance will be passed as the parameter.
		/// </param>
		public void OnEntityAddedByName(string entityName, Action<Entity> onAdded)
		{
			if (!_entityAddedByNameCallbacks.TryGetValue(entityName, out var list))
			{
				list = new List<Delegate>();
				_entityAddedByNameCallbacks[entityName] = list;
			}

			list.Add(onAdded);
		}

		/// <summary>
		/// Registers a callback that will be called **once** for the first entity with the specified name added to the scene,
		/// then the callback is automatically removed.
		/// </summary>
		/// <param name="entityName">The name of the entity to listen for.</param>
		/// <param name="onAdded">The callback to invoke when the entity is added.</param>
		public void OnEntityAddedByNameOnce(string entityName, Action<Entity> onAdded)
		{
			var oneShot = new OneShotDelegate<Entity>(onAdded);
			OnEntityAddedByName(entityName, oneShot.Invoke);
		}

		/// <summary>
		/// Registers a callback that will be invoked whenever an entity with the specified tag is added to the scene.
		/// <para>
		/// This is useful for systems or scripts that need to automatically act on entities with a specific tag,
		/// such as setting up components, event hooks, or performing initialization logic.
		/// </para>
		/// </summary>
		/// <param name="tag">The tag to listen for.</param>
		/// <param name="onAdded">
		/// The function to execute when an entity with the specified tag is added to the scene.
		/// The entity instance will be passed as the parameter.
		/// </param>
		public void OnEntityAddedByTag(int tag, Action<Entity> onAdded)
		{
			if (!_entityAddedByTagCallbacks.TryGetValue(tag, out var list))
			{
				list = new List<Delegate>();
				_entityAddedByTagCallbacks[tag] = list;
			}

			list.Add(onAdded);
		}

		/// <summary>
		/// Registers a callback that will be called **once** for the first entity with the specified tag added to the scene,
		/// then the callback is automatically removed.
		/// </summary>
		/// <param name="tag">The tag to listen for.</param>
		/// <param name="onAdded">The callback to invoke when the entity is added.</param>
		public void OnEntityAddedByTagOnce(int tag, Action<Entity> onAdded)
		{
			var oneShot = new OneShotDelegate<Entity>(onAdded);
			OnEntityAddedByTag(tag, oneShot.Invoke);
		}

		private void TriggerEntityAddedCallbacks(Entity entity)
		{
			var delegatesToRemove = new List<(string, Delegate)>();

			// Trigger callbacks registered by name
			if (_entityAddedByNameCallbacks.TryGetValue(entity.Name, out var nameCallbacks))
			{
				foreach (var del in nameCallbacks.ToArray()) // ToArray avoids modification during enumeration
				{
					del.DynamicInvoke(entity);

					// Remove if this is a one-shot delegate
					if (del.Target is IOneShotDelegate)
						delegatesToRemove.Add((entity.Name, del));
				}
			}

			// Remove one-shot delegates after invoking (by name)
			foreach (var (name, d) in delegatesToRemove)
				_entityAddedByNameCallbacks[name].Remove(d);

			delegatesToRemove.Clear();
			var tagDelegatesToRemove = new List<(int, Delegate)>();

			// Trigger callbacks registered by tag
			if (_entityAddedByTagCallbacks.TryGetValue(entity.Tag, out var tagCallbacks))
			{
				foreach (var del in tagCallbacks.ToArray())
				{
					del.DynamicInvoke(entity);

					// Remove if this is a one-shot delegate
					if (del.Target is IOneShotDelegate)
						tagDelegatesToRemove.Add((entity.Tag, del));
				}
			}

			// Remove one-shot delegates after invoking (by tag)
			foreach (var (tag, d) in tagDelegatesToRemove)
				_entityAddedByTagCallbacks[tag].Remove(d);
		}

		#endregion


		#region Wait For Component Added

		/// <summary>
		/// Registers a callback that will be invoked whenever a component of type <typeparamref name="T"/> is added to any entity in the scene.
		/// </summary>
		public void OnComponentAddedToScene<T>(Action<T> onAdded) where T : Component
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
		/// Registers a callback that will be called **once** for the first component of type T added to the scene,
		/// then the callback is automatically removed.
		/// </summary>
		public void OnComponentAddedToSceneOnce<T>(Action<T> onAdded) where T : Component
		{
			var oneShot = new OneShotDelegate<T>(onAdded);
			OnComponentAddedToScene<T>(oneShot.Invoke);
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

		/// <summary>
		/// adds an Entity to the Scene's Entities list
		/// </summary>
		/// <param name="entity">The Entity to add</param>
		public virtual Entity AddEntity(Entity entity)
		{
			// In PlayMode don't serialize new entities 
			if (!Core.IsEditMode)
				entity.Type = Entity.InstanceType.NonSerialized;

			if (Entities.FindEntity(entity.Name) != null)
				entity.Name = GetUniqueEntityName(entity.Name, entity);

			Entities.Add(entity);
			entity.Scene = this;

			// Recursively add child entities
			for (var i = 0; i < entity.Transform.ChildCount; i++)
			{
				var childEntity = entity.Transform.GetChild(i).Entity;
				AddEntity(childEntity);
			}

			TriggerEntityAddedCallbacks(entity);
			return entity;
		}

		/// <summary>
		/// removes all entities from the scene
		/// </summary>
		public void DestroyAllEntities()
		{
			for (var i = 0; i < Entities.Count; i++)
				Entities[i].Destroy();
		}

		/// <summary>
		/// searches for and returns the first Entity with name
		/// </summary>
		/// <returns>The entity.</returns>
		/// <param name="name">Name.</param>
		public Entity FindEntity(string name)
		{
			return Entities.FindEntity(name);
		}

		/// <summary>
		/// returns all entities with the given tag
		/// </summary>
		/// <returns>The entities by tag.</returns>
		/// <param name="tag">Tag.</param>
		public List<Entity> FindEntitiesWithTag(int tag)
		{
			return Entities.EntitiesWithTag(tag);
		}

		/// <summary>
		/// returns the first enabled loaded component of Type T
		/// </summary>
		/// <returns>The component of type.</returns>
		/// <typeparam name="T">The 1st type parameter.</typeparam>
		public T FindComponentOfType<T>() where T : Component
		{
			return Entities.FindComponentOfType<T>();
		}

		/// <summary>
		/// returns a list of all enabled loaded components of Type T
		/// </summary>
		/// <returns>The components of type.</returns>
		/// <typeparam name="T">The 1st type parameter.</typeparam>
		public List<T> FindComponentsOfType<T>() where T : Component
		{
			return Entities.FindComponentsOfType<T>();
		}

		/// <summary>
		/// returns the first enabled loaded component of Type T with the specified name
		/// </summary>
		/// <returns>The component with the given name and type.</returns>
		/// <param name="name">Name of the component to find.</param>
		/// <typeparam name="T">The component type.</typeparam>
		public T FindComponentWithName<T>(string name) where T : Component
		{
			return Entities.FindComponentWithName<T>(name);
		}

		/// <summary>
		/// Pattern: BaseName + optional separator + optional number at the end e.g. Platform, Platform_1, Platform-1, Platform1
		/// </summary>
		/// <param name="baseName"></param>
		/// <returns></returns>
		public string GetUniqueEntityName(string baseName, Entity entity, IEnumerable<Entity> pendingEntities = null)
		{
			var baseLower = baseName.ToLower();

			var allNames = new List<string>();
			for (var i = 0; i < Entities.Count; i++)
			{
				if (Entities[i] == entity)
					continue;
				allNames.Add(Entities[i].Name.ToLower());
			}

			if (pendingEntities != null)
			{
				foreach (var e in pendingEntities)
				{
					if (e == entity)
						continue;
					allNames.Add(e.Name.ToLower());
				}
			}

			if (!allNames.Contains(baseLower))
				return baseName;

			var inputPattern = @"^(.+?)[_\-]?(\d+)$";
			var inputMatch = System.Text.RegularExpressions.Regex.Match(baseName, inputPattern);

			string actualBaseName;
			int startingNumber;

			if (inputMatch.Success)
			{
				actualBaseName = inputMatch.Groups[1].Value;
				startingNumber = int.Parse(inputMatch.Groups[2].Value);
			}
			else
			{
				actualBaseName = baseName;
				startingNumber = 1;
			}

			var baseNameLower = actualBaseName.ToLower();
			var pattern = @"^" + System.Text.RegularExpressions.Regex.Escape(baseNameLower) + @"(?:[_\-]?(\d+))?$";
			var maxNum = startingNumber - 1;

			foreach (var name in allNames)
			{
				var match = System.Text.RegularExpressions.Regex.Match(name, pattern);
				if (match.Success)
				{
					if (string.IsNullOrEmpty(match.Groups[1].Value))
					{
						maxNum = Math.Max(maxNum, 0);
					}
					else if (int.TryParse(match.Groups[1].Value, out var num))
					{
						maxNum = Math.Max(maxNum, num);
					}
				}
			}

			return $"{actualBaseName}_{maxNum + 1}";
		}
	}
}
