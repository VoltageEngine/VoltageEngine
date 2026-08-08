using System;
using System.Collections.Generic;
using Voltage.Serialization;

namespace Voltage.Data
{
	/// <summary>One keyed slot in a <see cref="DataAssetSet"/>.</summary>
	public class DataAssetSetEntry : ISerializableData
	{
		/// <summary>The variant this entry answers to, e.g. "Easy", "Hard", "Winter".</summary>
		public string Key;

		[AssetType(typeof(DataAsset))]
		public AssetReference Asset;
	}

	/// <summary>
	/// Maps <b>variant keys to other data assets</b> — the authored way to swap a whole container at runtime.
	/// </summary>
	[AssetTypeId("DataAssetSet")]
	public partial class DataAssetSet : DataAsset
	{
		/// <summary>Key used when <see cref="DataVariant.Active"/> matches no entry.</summary>
		public string DefaultKey = "Default";

		public List<DataAssetSetEntry> Entries = new();

		/// <summary>
		/// Falls back to <see cref="DefaultKey"/>, then to the first entry.
		/// </summary>
		public AssetReference Resolve(string key)
		{
			if (Entries == null || Entries.Count == 0)
				return default;

			if (TryFind(key, out var match))
				return match;

			if (!string.Equals(key, DefaultKey, StringComparison.Ordinal) && TryFind(DefaultKey, out var fallback))
				return fallback;

			return Entries[0].Asset;
		}

		/// <summary>In declaration order.</summary>
		public IEnumerable<string> Keys
		{
			get
			{
				if (Entries == null)
					yield break;

				foreach (var entry in Entries)
				{
					if (entry != null && !string.IsNullOrEmpty(entry.Key))
						yield return entry.Key;
				}
			}
		}

		private bool TryFind(string key, out AssetReference reference)
		{
			if (!string.IsNullOrEmpty(key))
			{
				for (var i = 0; i < Entries.Count; i++)
				{
					var entry = Entries[i];
					if (entry != null && string.Equals(entry.Key, key, StringComparison.OrdinalIgnoreCase))
					{
						reference = entry.Asset;
						return true;
					}
				}
			}

			reference = default;
			return false;
		}
	}

	/// <summary>
	/// The globally-selected variant key <see cref="DataAssetSet"/> resolves against.
	/// </summary>
	public static class DataVariant
	{
		private static string _active = "Default";

		/// <summary>Assigning the same value is a no-op, so this is safe to set unconditionally.</summary>
		public static string Active
		{
			get => _active;
			set
			{
				var next = value ?? "Default";
				if (string.Equals(_active, next, StringComparison.Ordinal))
					return;

				_active = next;
				Changed?.Invoke();
			}
		}

		/// <summary>
		/// <b>Unsubscribe when your component is removed</b> — this is a static event, so a forgotten handler keeps the entity alive.
		/// </summary>
		public static event Action Changed;
	}
}
