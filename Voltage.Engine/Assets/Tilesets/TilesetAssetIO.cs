using Voltage.Assets;

namespace Voltage.Tilesets
{
	/// <summary>
	/// Load/save for <c>.vtileset</c> assets.
	/// </summary>
	public static class TilesetAssetIO
	{
		public const string FileExtension = ".vtileset";

		/// <summary>Default JSON settings: a tileset holds no polymorphic members.</summary>
		public static readonly JsonAssetFile<TilesetAsset> Format = new(
			FileExtension,
			"Tileset",
			createDefault: name => new TilesetAsset
			{
				Name = name,
				TileWidth = 16,
				TileHeight = 16,
			});

		public static TilesetAsset CreateDefault(string name = null) => Format.CreateDefault(name);

		public static TilesetAsset CreateAndSave(string path) => Format.CreateAndSave(path);

		public static string ToJson(TilesetAsset asset) => Format.ToJson(asset);

		public static TilesetAsset FromJson(string json) => Format.FromJson(json);

		public static void Save(TilesetAsset asset, string path) => Format.Save(asset, path);

		public static TilesetAsset Load(string path) => Format.Load(path);
	}
}
