using System;
using System.Collections.Generic;

namespace Voltage.Serialization
{
	/// <summary>
	/// Maps formerly-used fully-qualified type names to the current <see cref="Type"/> after
	/// a C# class or namespace rename, so serialized scenes and prefabs continue to load
	/// after component classes are moved or renamed.
	///
	/// Entries are registered at module initialization time by source-generated
	/// [ModuleInitializer] methods emitted for every <c>[FormerlyKnownAs]</c>-annotated
	/// component. The registry is consulted only on the miss path (after the primary AOT
	/// factory and reflection type lookups both fail), so it adds zero overhead for the
	/// common case where no rename has occurred.
	///
	/// Thread safety: all registrations must complete before any scene is loaded.
	/// The registry is initialized by [ModuleInitializer] methods which run before user
	/// code, so concurrent writes at startup are not possible in practice. Reads are
	/// lock-free (Dictionary is read-only after init). If you call <see cref="Register"/>
	/// at a later point from multiple threads, wrap in your own synchronization.
	/// </summary>
	public static class TypeRenameRegistry
	{
		// Maps old FQN → current Type (already resolved; never null once registered).
		private static readonly Dictionary<string, Type> _oldToNewType =
			new Dictionary<string, Type>(StringComparer.Ordinal);

		// Maps old FQN → current FQN (for diagnostics / tooling queries).
		private static readonly Dictionary<string, string> _oldToNewName =
			new Dictionary<string, string>(StringComparer.Ordinal);

		/// <summary>
		/// Registers <paramref name="oldFullyQualifiedName"/> as a former name for
		/// <paramref name="currentType"/>. Source-generated [ModuleInitializer] methods call
		/// this for every <c>[FormerlyKnownAs]</c> attribute found at compile time.
		/// Multiple calls with the same <paramref name="oldFullyQualifiedName"/> are silently
		/// idempotent (last registration wins if they disagree, which should never happen).
		/// </summary>
		/// <param name="oldFullyQualifiedName">
		/// The exact fully-qualified type name as it appeared in serialized JSON before the rename,
		/// e.g. <c>"Jolt.Scripts.Enemies.DroneComponent"</c>.
		/// </param>
		/// <param name="currentType">The renamed (current) <see cref="Type"/>.</param>
		public static void Register(string oldFullyQualifiedName, Type currentType)
		{
			if (string.IsNullOrEmpty(oldFullyQualifiedName))
				throw new ArgumentNullException(nameof(oldFullyQualifiedName));
			if (currentType == null)
				throw new ArgumentNullException(nameof(currentType));

			_oldToNewType[oldFullyQualifiedName] = currentType;
			_oldToNewName[oldFullyQualifiedName] = currentType.FullName ?? currentType.Name;
		}

		/// <summary>
		/// Attempts to resolve an old fully-qualified type name to the current <see cref="Type"/>.
		/// Returns <c>true</c> and sets <paramref name="currentType"/> when a rename mapping
		/// is found; returns <c>false</c> when the name is unknown to the registry.
		/// </summary>
		public static bool TryResolve(string oldFullyQualifiedName, out Type currentType)
		{
			if (string.IsNullOrEmpty(oldFullyQualifiedName))
			{
				currentType = null;
				return false;
			}

			return _oldToNewType.TryGetValue(oldFullyQualifiedName, out currentType);
		}

		/// <summary>
		/// Returns the current fully-qualified name for a formerly-used name, or <c>null</c>
		/// if no mapping is registered. Useful for diagnostics and tooling.
		/// </summary>
		public static string GetCurrentName(string oldFullyQualifiedName)
		{
			if (string.IsNullOrEmpty(oldFullyQualifiedName))
				return null;
			_oldToNewName.TryGetValue(oldFullyQualifiedName, out var current);
			return current;
		}

		/// <summary>
		/// Returns <c>true</c> when any mapping is registered for
		/// <paramref name="oldFullyQualifiedName"/>. Exposed for testing.
		/// </summary>
		public static bool IsRegistered(string oldFullyQualifiedName) =>
			!string.IsNullOrEmpty(oldFullyQualifiedName) &&
			_oldToNewType.ContainsKey(oldFullyQualifiedName);

		/// <summary>
		/// All currently registered old → new name pairs. Read-only snapshot for diagnostics.
		/// </summary>
		public static IReadOnlyDictionary<string, string> AllMappings => _oldToNewName;
	}
}
