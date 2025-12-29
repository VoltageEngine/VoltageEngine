using System;
using System.Collections.Generic;
using System.IO;

namespace Voltage.Editor.Assets
{
	//TODO: Rework this completely
	/// <summary>
	/// Lightweight asset registry for editor integration.
	/// Tracks loaded assets without complex pipeline management.
	/// </summary>
	public class AssetManager
	{
		// private readonly Dictionary<string, AssetEntry> _assetCache;
		// private string _contentRootPath;
		//
		// public AssetManager()
		// {
		// 	_assetCache = new Dictionary<string, AssetEntry>();
		// }
		//
		// /// <summary>
		// /// Sets the content root directory (e.g., "Content")
		// /// </summary>
		// public void SetContentRoot(string path)
		// {
		// 	_contentRootPath = path;
		// }
		//
		// /// <summary>
		// /// Registers an asset that was loaded
		// /// </summary>
		// public void RegisterAsset(string relativePath, object asset)
		// {
		// 	_assetCache[relativePath] = new AssetEntry
		// 	{
		// 		Path = relativePath,
		// 		Asset = asset,
		// 		Type = asset.GetType(),
		// 		LastModified = File.Exists(GetFullPath(relativePath)) 
		// 			? File.GetLastWriteTime(GetFullPath(relativePath)) 
		// 			: DateTime.MinValue
		// 	};
		// }
		//
		// /// <summary>
		// /// Gets all registered assets (for simple browser display)
		// /// </summary>
		// public IEnumerable<AssetEntry> GetAllAssets() => _assetCache.Values;
		//
		// /// <summary>
		// /// Simple file enumeration for browser (no MGCB parsing)
		// /// </summary>
		// public IEnumerable<string> EnumerateContentFiles()
		// {
		// 	if (string.IsNullOrEmpty(_contentRootPath) || !Directory.Exists(_contentRootPath))
		// 		yield break;
		//
		// 	foreach (var file in Directory.EnumerateFiles(_contentRootPath, "*.*", SearchOption.AllDirectories))
		// 	{
		// 		// Skip build outputs and MGCB files
		// 		if (file.EndsWith(".xnb") || file.EndsWith(".mgcb"))
		// 			continue;
		//
		// 		yield return Path.GetRelativePath(_contentRootPath, file);
		// 	}
		// }
		//
		// private string GetFullPath(string relativePath)
		// {
		// 	return Path.Combine(_contentRootPath ?? "", relativePath);
		// }
	}

	public class AssetEntry
	{
		public string Path { get; set; }
		public object Asset { get; set; }
		public Type Type { get; set; }
		public DateTime LastModified { get; set; }
	}
}
