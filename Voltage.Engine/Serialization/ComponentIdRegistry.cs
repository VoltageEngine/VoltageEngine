using System;
using System.Collections.Generic;

namespace Voltage.Serialization
{
	/// <summary>
	/// Maps a stable component identity — the human-readable alias declared by
	/// <c>[ComponentId("…")]</c> — to the current component <see cref="Type"/> and back.
	/// <para></para>
	/// This is the runtime backbone of alias-based component identity: scenes and prefabs store the
	/// id string, and on load the engine resolves it to whatever type now carries that id —
	/// regardless of how the class has since been renamed or which namespace/assembly it moved to.
	/// Because the id lives inside the component's own source (an attribute), it travels with the
	/// declaration automatically, so renames need no editor reconciliation and no central rename map.
	/// </summary>
	public static class ComponentIdRegistry
	{
		// id (ordinal, case-sensitive) -> current component Type.
		private static readonly Dictionary<string, Type> _idToType =
			new Dictionary<string, Type>(StringComparer.Ordinal);

		// current component Type -> id, for the save path.
		private static readonly Dictionary<Type, string> _typeToId =
			new Dictionary<Type, string>();

		/// <summary>
		/// Registers <paramref name="id"/> as the stable identity of <paramref name="componentType"/>.
		/// Called by the source-generated bootstrap for every <c>[ComponentId]</c> component.
		/// Idempotent; the last registration wins if two disagree (which should never happen).
		/// </summary>
		public static void Register(string id, Type componentType)
		{
			if (string.IsNullOrEmpty(id))
				throw new ArgumentNullException(nameof(id));
			if (componentType == null)
				throw new ArgumentNullException(nameof(componentType));

			_idToType[id] = componentType;
			_typeToId[componentType] = id;
		}

		/// <summary>
		/// Resolves a stored component id to the <see cref="Type"/> that currently carries it.
		/// Returns <c>false</c> when the id is unknown (e.g. legacy scene with no id, or the
		/// component was deleted).
		/// </summary>
		public static bool TryGetType(string id, out Type componentType)
		{
			if (string.IsNullOrEmpty(id))
			{
				componentType = null;
				return false;
			}

			return _idToType.TryGetValue(id, out componentType);
		}

		/// <summary>
		/// Returns the stable id for a component <see cref="Type"/>, or <c>null</c> when the type has
		/// no <c>[ComponentId]</c> registration. Used by the save path to stamp the id into
		/// scene/prefab entries.
		/// </summary>
		public static string GetIdForType(Type componentType)
		{
			if (componentType == null)
				return null;
			_typeToId.TryGetValue(componentType, out var id);
			return id;
		}

		/// <summary>True when a mapping is registered for <paramref name="id"/>.</summary>
		public static bool IsRegistered(string id) =>
			!string.IsNullOrEmpty(id) && _idToType.ContainsKey(id);

		/// <summary>Read-only snapshot of all id → current type pairs (diagnostics/tooling).</summary>
		public static IReadOnlyDictionary<string, Type> AllMappings => _idToType;
	}
}
