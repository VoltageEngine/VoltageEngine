using System;
using System.Collections.Generic;
using Voltage.Persistence;

namespace Voltage.Data
{
	/// <summary>
	/// Every known <see cref="DataAsset"/> type, keyed by its stable <c>[AssetTypeId]</c> rather than its CLR name.
	/// </summary>
	public static class DataAssetRegistry
	{
		/// <summary>Everything needed to create, read, and describe one data asset type.</summary>
		public sealed class Entry
		{
			/// <summary>The value written to a file's <c>@assetType</c>.</summary>
			public string Id { get; init; }

			public Type Type { get; init; }

			/// <summary>AOT-safe <c>() =&gt; new T()</c>. No Activator, no reflection.</summary>
			public Func<DataAsset> Factory { get; init; }

			/// <summary>Generated reader. Called with the reader at the start of the file's <c>data</c> object.</summary>
			public Func<JsonTokenReader, DataAsset> Reader { get; init; }

			public int Version { get; init; }

			/// <summary>Menu/diagnostic name, e.g. "Difficulty Profile".</summary>
			public string DisplayName { get; init; }

			public bool CloneOnLoad { get; init; }
		}

		private static readonly object _lock = new();
		private static readonly Dictionary<string, Entry> _byId = new(StringComparer.Ordinal);
		private static readonly Dictionary<Type, Entry> _byType = new();
		private static Entry[] _snapshot = Array.Empty<Entry>();

		/// <summary>
		/// Bumped on every registration.
		/// </summary>
		public static int Version { get; private set; }

		/// <summary>
		/// Called by generated <c>[ModuleInitializer]</c> code.
		/// </summary>
		public static void Register(
			string id,
			Type type,
			Func<DataAsset> factory,
			Func<JsonTokenReader, DataAsset> reader,
			int version,
			string displayName,
			bool cloneOnLoad)
		{
			if (string.IsNullOrEmpty(id) || type == null || factory == null || reader == null)
				return;

			var entry = new Entry
			{
				Id = id,
				Type = type,
				Factory = factory,
				Reader = reader,
				Version = version <= 0 ? 1 : version,
				DisplayName = string.IsNullOrEmpty(displayName) ? id : displayName,
				CloneOnLoad = cloneOnLoad,
			};

			lock (_lock)
			{
				// A recompiled type keeps its id but arrives as a NEW CLR type; drop the stale type key so
				// TryGetId cannot resolve to an assembly that is no longer loaded.
				if (_byId.TryGetValue(id, out var previous) && previous.Type != type)
					_byType.Remove(previous.Type);

				_byId[id] = entry;
				_byType[type] = entry;

				var snapshot = new Entry[_byId.Count];
				_byId.Values.CopyTo(snapshot, 0);
				_snapshot = snapshot;

				Version++;
			}
		}

		public static bool TryGet(string id, out Entry entry)
		{
			if (string.IsNullOrEmpty(id))
			{
				entry = null;
				return false;
			}

			lock (_lock)
				return _byId.TryGetValue(id, out entry);
		}

		public static bool TryGet(Type type, out Entry entry)
		{
			if (type == null)
			{
				entry = null;
				return false;
			}

			lock (_lock)
				return _byType.TryGetValue(type, out entry);
		}

		/// <summary>The stable id for a CLR type, or null if it is not a registered data asset.</summary>
		public static string TryGetId(Type type) => TryGet(type, out var e) ? e.Id : null;

		public static bool IsRegistered(string id) => TryGet(id, out _);

		/// <summary>A snapshot, safe to enumerate while a background script compile registers more.</summary>
		public static IReadOnlyList<Entry> All
		{
			get
			{
				lock (_lock)
					return _snapshot;
			}
		}
	}
}
