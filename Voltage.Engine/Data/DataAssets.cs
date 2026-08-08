using Voltage.Serialization;

namespace Voltage.Data
{
	/// <summary>
	/// Entry point for loading <c>.vasset</c> data containers from game code.
	/// </summary>
	public static class DataAssets
	{
		/// <summary>
		/// The <b>shared instance</b> behind <paramref name="reference"/> (a fresh copy for <see cref="CloneOnLoadAttribute"/> types), or null if it is empty, unresolvable, or the wrong type.
		/// </summary>
		public static T Load<T>(AssetReference reference) where T : DataAsset =>
			DataAssetCache.Get<T>(reference);

		public static DataAsset Load(AssetReference reference) => DataAssetCache.Get(reference);

		public static T LoadFromPath<T>(string absolutePath) where T : DataAsset =>
			DataAssetCache.GetByPath(absolutePath) as T;

		/// <summary>
		/// Resolves a <see cref="DataAssetSet"/> against <see cref="DataVariant.Active"/> (falling back to the set's default key) and loads the result.
		/// </summary>
		public static T LoadVariant<T>(AssetReference setReference) where T : DataAsset
		{
			var loaded = DataAssetCache.Get(setReference);
			if (loaded == null)
				return null;

			if (loaded is not DataAssetSet set)
			{
				if (loaded is T direct)
					return direct;

				Debug.Warn(
					$"[DataAssets] '{loaded.SourcePath ?? setReference.ToString()}' is a " +
					$"{loaded.GetType().Name}; expected a DataAssetSet or a {typeof(T).Name}.");
				return null;
			}

			var chosen = set.Resolve(DataVariant.Active);
			if (!chosen.IsValid)
			{
				Debug.Warn(
					$"[DataAssets] Set '{set.SourcePath ?? setReference.ToString()}' has no entry for " +
					$"variant '{DataVariant.Active}' and no usable fallback.");
				return null;
			}

			return DataAssetCache.Get<T>(chosen);
		}
	}
}
