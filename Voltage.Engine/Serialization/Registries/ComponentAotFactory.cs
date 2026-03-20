using System;
using System.Collections.Generic;

namespace Voltage.Serialization.Registries;

/// <summary>
/// AOT-safe factory that creates component instances by stable type id.
/// Every entry is a simple <c>() => new T()</c> lambda. No Activator, no reflection.
/// </summary>
public static class ComponentAotFactory
{
	private static readonly Dictionary<string, Func<object>> _factories = [];

	public static void Register(string typeId, Func<object> factory)
	{
		_factories[typeId] = factory;
	}

	public static object Create(string typeId)
	{
		if (!_factories.TryGetValue(typeId, out var factory))
			throw new InvalidOperationException($"No AOT factory registered for component '{typeId}'.");
		return factory();
	}

	public static bool IsRegistered(string typeId) => _factories.ContainsKey(typeId);

	public static IReadOnlyCollection<string> RegisteredTypeIds => _factories.Keys;
}