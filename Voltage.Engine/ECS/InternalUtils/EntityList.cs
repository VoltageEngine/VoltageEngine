using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Voltage;
using Voltage.Utils;
using Voltage.Utils.Collections;


namespace Voltage;

public class EntityList : IEnumerable<Entity>
{
	public Scene Scene;

	/// <summary>
	/// list of entities added to the scene
	/// </summary>
	private FastList<Entity> _entities = new();

	public FastList<Entity> EntityFastList => _entities;

	/// <summary>
	/// Read-only access to entities pending addition. Used by LoadSceneEntitiesData
	/// to find SceneRequired entities before UpdateLists has been called.
	/// </summary>
	public IReadOnlyCollection<Entity> EntitiesToAdd => _entitiesToAdd;

	/// <summary>
	/// The list of entities that were added this frame. Used to group the entities so we can process them simultaneously
	/// </summary>
	private HashSet<Entity> _entitiesToAdd = new();

	/// <summary>
	/// The list of entities that were marked for removal this frame. Used to group the entities so we can process them simultaneously
	/// </summary>
	private HashSet<Entity> _entitiesToRemove = new();

	/// <summary>
	/// flag used to determine if we need to sort our entities this frame
	/// </summary>
	private bool _isEntityListUnsorted;

	/// <summary>
	/// tracks entities by tag for easy retrieval
	/// </summary>
	private Dictionary<int, FastList<Entity>> _entityDict = new();

	private HashSet<int> _unsortedTags = new();

	// used in updateLists to double buffer so that the original lists can be modified elsewhere
	private HashSet<Entity> _tempEntityList = new();

	private bool _isSceneStarted = false;

	public EntityList(Scene scene)
	{
		Scene = scene;
		Scene.OnSceneBegin += SceneStarted;
	}

	~EntityList()
	{
		Scene.OnSceneBegin -= SceneStarted;
	}

	private void SceneStarted()
	{
		_isSceneStarted = true;
	}

	#region array access

	public int Count => _entities.Length;

	public Entity this[int index] => _entities.Buffer[index];

	#endregion

	public void MarkEntityListUnsorted()
	{
		_isEntityListUnsorted = true;
	}

	internal void MarkTagUnsorted(int tag)
	{
		_unsortedTags.Add(tag);
	}

	/// <summary>
	/// adds an Entity to the list. All lifecycle methods will be called in the next frame.
	/// </summary>
	/// <param name="entity">Entity.</param>
	public void Add(Entity entity)
	{
		_entitiesToAdd.Add(entity);
	}

	/// <summary>
	/// Immediately moves an already-live entity to a specific index within the live entity buffer.
	/// Used for visual reordering of root entities in the scene graph.
	/// </summary>
	public void MoveEntityToIndex(Entity entity, int targetIndex)
	{
		int currentIndex = _entities.IndexOf(entity);
		if (currentIndex < 0 || currentIndex == targetIndex)
			return;

		_entities.RemoveAt(currentIndex);

		// After removal the target index may need adjusting
		if (targetIndex > currentIndex)
			targetIndex--;

		targetIndex = Math.Clamp(targetIndex, 0, _entities.Length);
		_entities.Insert(targetIndex, entity);
	}

	/// <summary>
	/// removes an Entity from the list. All lifecycle methods will be called in the next frame.
	/// </summary>
	/// <param name="entity">Entity.</param>
	public void Remove(Entity entity)
	{
		Debug.WarnIf(_entitiesToRemove.Contains(entity),
			"You are trying to remove an entity ({0}) that you already removed", entity.Name);

		// guard against adding and then removing an Entity in the same frame
		if (_entitiesToAdd.Contains(entity))
		{
			_entitiesToAdd.Remove(entity);
			return;
		}

		if (!_entitiesToRemove.Contains(entity))
			_entitiesToRemove.Add(entity);
	}

	/// <summary>
	/// removes all entities from the entities list
	/// </summary>
	public void RemoveAllEntities()
	{
		// clear lists we don't need anymore
		_unsortedTags.Clear();
		_entitiesToAdd.Clear();
		_isEntityListUnsorted = false;

		// why do we update lists here? Mainly to deal with Entities that were detached before a Scene switch. They will still
		// be in the _entitiesToRemove list which will get handled by updateLists.
		UpdateLists();

		for (var i = 0; i < _entities.Length; i++)
		{
			_entities.Buffer[i]._isDestroyed = true;
			_entities.Buffer[i].OnRemovedFromScene();
			_entities.Buffer[i].Scene = null;
		}

		_entities.Clear();
		_entityDict.Clear();
	}

	/// <summary>
	/// checks to see if the Entity is presently managed by this EntityList
	/// </summary>
	/// <param name="entity">Entity.</param>
	public bool Contains(Entity entity)
	{
		return _entities.Contains(entity) || _entitiesToAdd.Contains(entity);
	}

	private FastList<Entity> GetTagList(int tag)
	{
		FastList<Entity> list;
		if (!_entityDict.TryGetValue(tag, out list))
		{
			list = new FastList<Entity>();
			_entityDict[tag] = list;
		}

		return _entityDict[tag];
	}

	internal void AddToTagList(Entity entity)
	{
		var list = GetTagList(entity.Tag);
		if (!list.Contains(entity))
		{
			list.Add(entity);
			_unsortedTags.Add(entity.Tag);
		}
	}

	internal void RemoveFromTagList(Entity entity)
	{
		FastList<Entity> list;
		if (_entityDict.TryGetValue(entity.Tag, out list))
			list.Remove(entity);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal void Update()
	{
		for (var i = 0; i < _entities.Length; i++)
		{
			var entity = _entities.Buffer[i];

			if (entity.UpdateInterval == 0)
				continue;

			if (entity.Enabled && (entity.UpdateInterval == 1 || Time.FrameCount % entity.UpdateInterval == 0))
				entity.Update();
		}
	}

	public void UpdateLists()
	{
		// handle removals
		if (_entitiesToRemove.Count > 0)
		{
			Utils.Utils.Swap(ref _entitiesToRemove, ref _tempEntityList);
			foreach (var entity in _tempEntityList)
			{
				// handle the tagList
				RemoveFromTagList(entity);

				// handle the regular entity list
				_entities.Remove(entity);
				entity.OnRemovedFromScene();
				entity.Scene = null;
			}

			_tempEntityList.Clear();
		}

		// handle additions
		if (_entitiesToAdd.Count > 0)
		{
			Utils.Utils.Swap(ref _entitiesToAdd, ref _tempEntityList);

			// Register every entity in the live list so they are all visible to
			// GetComponent / FindEntity calls before anything initializes.
			foreach (var entity in _tempEntityList)
			{
				_entities.Add(entity);
				entity.Scene = Scene;
				AddToTagList(entity);
			}

			foreach (var entity in _tempEntityList)
				entity.Components.CommitPendingAdditions();

			foreach (var entity in _tempEntityList)
				entity.OnAddedToScene();

			Serialization.ComponentReferenceResolver.ResolveAll();

			foreach (var entity in _tempEntityList)
				entity.Components.CallOnEnableAndOnStart();

			// Only the first time the scene is loaded
			if (_isSceneStarted)
			{
				Scene.InvokeFinishedAddingEntities();
				_isSceneStarted = false;
			}

			_tempEntityList.Clear();
			_isEntityListUnsorted = true;
		}

		if (_isEntityListUnsorted)
		{
			_entities.Sort();
			_isEntityListUnsorted = false;
		}

		// sort our tagList if needed
		if (_unsortedTags.Count > 0)
		{
			foreach (var tag in _unsortedTags)
				_entityDict[tag].Sort();
			_unsortedTags.Clear();
		}


	}


	#region Entity search

	/// <summary>
	/// returns the first Entity found with a name of name. If none are found returns null.
	/// </summary>
	/// <returns>The entity.</returns>
	/// <param name="name">Name.</param>
	public Entity FindEntity(string name)
	{
		for (var i = 0; i < _entities.Length; i++)
			if (_entities.Buffer[i].Name == name)
				return _entities.Buffer[i];

		foreach (var entity in _entitiesToAdd)
			if (entity.Name == name)
				return entity;

		return null;
	}

	/// <summary>
	/// returns a list of all entities with tag. If no entities have the tag an empty list is returned. The returned List can be put back in the pool via ListPool.free.
	/// </summary>
	/// <returns>The with tag.</returns>
	/// <param name="tag">Tag.</param>
	public List<Entity> EntitiesWithTag(int tag)
	{
		var list = GetTagList(tag);

		var returnList = ListPool<Entity>.Obtain();
		returnList.Capacity = _entities.Length;
		for (var i = 0; i < list.Length; i++) returnList.Add(list[i]);

		return returnList;
	}

	/// <summary>
	/// returns a list of all entities with the given tag name. Looks up the tag int value from ProjectSettings.
	/// If the tag name is not found or no entities have the tag, an empty list is returned.
	/// The returned List can be put back in the pool via ListPool.free.
	/// </summary>
	/// <returns>The entities with tag name.</returns>
	/// <param name="tagName">Tag name.</param>
	public List<Entity> EntitiesWithTag(string tagName)
	{
		var entityTags = Project.ProjectSettings.Instance.Entities.EntityTags;

		if (!entityTags.TryGetValue(tagName, out var tag))
			return ListPool<Entity>.Obtain();

		return EntitiesWithTag(tag);
	}

	/// <summary>
	/// returns a List of all Entities (since Entity is sealed). The returned List can be put back in the pool via ListPool.free.
	/// </summary>
	/// <returns>List of all entities.</returns>
	public List<Entity> EntitiesOfType()
	{
		var list = ListPool<Entity>.Obtain();
		for (var i = 0; i < _entities.Length; i++)
			list.Add(_entities.Buffer[i]);

		foreach (var entity in _entitiesToAdd)
			list.Add(entity);

		return list;
	}

	/// <summary>
	/// returns the first Component found in the Scene of type T
	/// </summary>
	/// <returns>The component of type.</returns>
	/// <typeparam name="T">The 1st type parameter.</typeparam>
	public T FindComponentOfType<T>() where T : Component
	{
		for (var i = 0; i < _entities.Length; i++)
			if (_entities.Buffer[i].Enabled)
			{
				var comp = _entities.Buffer[i].GetComponent<T>();
				if (comp != null)
					return comp;
			}

		foreach (var entity in _entitiesToAdd)
			if (entity.Enabled)
			{
				var comp = entity.GetComponent<T>();
				if (comp != null)
					return comp;
			}

		return null;
	}

	/// <summary>
	/// returns all Components found in the Scene of type T. The returned List can be put back in the pool via ListPool.free.
	/// </summary>
	/// <returns>The components of type.</returns>
	/// <typeparam name="T">The 1st type parameter.</typeparam>
	public List<T> FindComponentsOfType<T>() where T : Component
	{
		var comps = ListPool<T>.Obtain();
		for (var i = 0; i < _entities.Length; i++)
			if (_entities.Buffer[i].Enabled)
				_entities.Buffer[i].GetComponents<T>(comps);

		foreach (var entity in _entitiesToAdd)
			if (entity.Enabled)
				entity.GetComponents<T>(comps);

		return comps;
	}

	/// <summary>
	/// returns the first Component found in the Scene of type T with the specified name
	/// </summary>
	/// <returns>The component with the given name and type.</returns>
	/// <param name="name">Name of the component to find.</param>
	/// <typeparam name="T">The component type.</typeparam>
	public T FindComponentWithName<T>(string name) where T : Component
	{
		for (var i = 0; i < _entities.Length; i++)
			if (_entities.Buffer[i].Enabled)
			{
				var comp = _entities.Buffer[i].GetComponent<T>(name);
				if (comp != null)
					return comp;
			}

		foreach (var entity in _entitiesToAdd)
			if (entity.Enabled)
			{
				var comp = entity.GetComponent<T>(name);
				if (comp != null)
					return comp;
			}

		return null;
	}

	/// <summary>
	/// returns the first Entity found with a matching PersistentId. If none are found returns null.
	/// </summary>
	/// <param name="persistentId">The persistent GUID of the entity.</param>
	public Entity FindEntityByPersistentId(Guid persistentId)
	{
		for (var i = 0; i < _entities.Length; i++)
			if (_entities.Buffer[i].PersistentId == persistentId)
				return _entities.Buffer[i];

		foreach (var entity in _entitiesToAdd)
			if (entity.PersistentId == persistentId)
				return entity;

		return null;
	}

	#endregion

	public IEnumerator<Entity> GetEnumerator()
	{
		for (int i = 0; i < _entities.Length; i++)
			yield return _entities.Buffer[i];
	}

	System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
	{
		return GetEnumerator();
	}
}