using System.IO;
using Voltage.Persistence;

namespace Voltage.Tilesets
{
	/// <summary>Load/save for <c>.vtileset</c> assets.</summary>
	public static class TilesetAssetIO
	{
		public const string FileExtension = ".vtileset";

		private static JsonSettings Settings() => new()
		{
			PrettyPrint = true,
			TypeNameHandling = TypeNameHandling.None,
			PreserveReferencesHandling = false,
		};

		public static TilesetAsset CreateDefault(string name = null) => new()
		{
			Name = name,
			TileWidth = 16,
			TileHeight = 16,
		};

		public static TilesetAsset CreateAndSave(string path)
		{
			var asset = CreateDefault(Path.GetFileNameWithoutExtension(path));
			Save(asset, path);
			return asset;
		}

		public static string ToJson(TilesetAsset asset) => Json.ToJson(asset, Settings());

		public static TilesetAsset FromJson(string json) => Json.FromJson<TilesetAsset>(json, Settings());

		public static void Save(TilesetAsset asset, string path) => File.WriteAllText(path, ToJson(asset));

		public static TilesetAsset Load(string path) =>
			File.Exists(path) ? FromJson(File.ReadAllText(path)) : null;
	}
}
