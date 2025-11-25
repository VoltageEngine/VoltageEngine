using System;
using System.Collections.Generic;
using Voltage.Data;

namespace Voltage.ECS;

/// <summary>
/// Helps to register and create entities based on their type names.
/// </summary>
public static class EntityFactoryRegistry
{
	private static readonly Dictionary<string, Func<Entity>> _factories = new();
	public static event Action<Entity> OnEntityCreated;
	public static event Action<Entity, SceneData.SceneEntityData> OnDataLoadingStarted;

	public static void InvokeEntityCreated(Entity entity)
	{
		OnEntityCreated?.Invoke(entity);
	}

	public static void InvokeDataLoadedStarted(Entity newEntity, SceneData.SceneEntityData entityData)
	{
		OnDataLoadingStarted?.Invoke(newEntity, entityData);
	}

	/// <summary>
	/// Register a factory for creating an entity of a specific type.
	/// </summary>
	/// <param name="typeName"></param>
	/// <param name="factory"></param>
	public static void Register(string typeName, Func<Entity> factory)
	{
		_factories[typeName] = factory;
	}

	/// <summary>
	/// If the type is registered, create an instance of the entity.
	/// </summary>
	/// <param name="typeName"></param>
	/// <param name="entity"></param>
	/// <returns></returns>
	public static bool TryCreate(string typeName, out Entity entity)
	{
		try
		{
			entity = Create(typeName);
			return true;
		}
		catch
		{
			entity = null;
			return false;
		}
	}

	public static Entity Create(string typeName)
	{
		if (_factories.TryGetValue(typeName, out var factory))
		{
			return factory();
		}

		throw new InvalidOperationException(
			$"EntityFactoryRegistry: Entity type '{typeName}' is not registered in the factory. " +
			$"Did you forget to call EntityFactoryRegistry.Register(\"{typeName}\", ...)?");
	}

	public static IEnumerable<string> GetRegisteredTypes()
	{
		return _factories.Keys;
	}

	/// <summary>
	/// Gets the Type for a registered entity without instantiating it
	/// </summary>
	/// <param name="typeName">The type name or full name of the entity</param>
	/// <returns>The Type if found, null otherwise</returns>
	public static Type GetEntityType(string typeName)
	{
		// First, search through registered factories by creating a temporary instance
		// and getting its type (not ideal but necessary since we only store factory delegates)
		foreach (var kvp in _factories)
		{
			var registeredTypeName = kvp.Key;
			
			// If the registered name matches, create a temporary instance to get the type
			if (registeredTypeName == typeName)
			{
				try
				{
					var tempEntity = kvp.Value();
					var type = tempEntity.GetType();
					
					// Clean up the temporary entity if it was added to a scene
					if (tempEntity.Scene != null)
						tempEntity.Destroy();
					
					return type;
				}
				catch
				{
					continue;
				}
			}
		}

		// Fallback: try Type.GetType for assembly-qualified names
		var fallbackType = Type.GetType(typeName);
		if (fallbackType != null)
			return fallbackType;

		// Last resort: search all loaded assemblies for the type
		foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
		{
			foreach (var type in assembly.GetTypes())
			{
				if (type.Name == typeName || type.FullName == typeName)
				{
					if (typeof(Entity).IsAssignableFrom(type))
						return type;
				}
			}
		}

		return null;
	}
}