using System;
using System.Collections.Generic;

namespace Voltage.Serialization;

public static class ComponentDataSerializationRegistry
{
	private static readonly Dictionary<uint, Type> _idToType = new();
	private static readonly Dictionary<string, uint> _typeNameToId = new(StringComparer.Ordinal);

	public static void Register(uint id, Type type)
	{
		_idToType[id] = type;
		_typeNameToId[type.FullName] = id;
	}

	public static bool TryGetId(Type type, out uint id)
	{
		if (type?.FullName == null)
		{
			id = 0;
			return false;
		}

		return _typeNameToId.TryGetValue(type.FullName, out id);
	}

	public static Type TryGetType(uint id)
	{
		return _idToType.TryGetValue(id, out var t) ? t : null;
	}
}
