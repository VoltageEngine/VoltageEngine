using System;
using System.Collections.Generic;
using System.IO;
using Voltage.Persistence;

namespace Voltage.Assets
{
	/// <summary>
	/// One entry under the Asset Browser's "Create ▸" menu.
	/// </summary>
	/// <param name="Write">Writes a fresh default asset to the given absolute path and returns it.</param>
	public readonly record struct AssetCreateOption(
		string Label,
		string DefaultFileName,
		Func<string, object> Write);

	/// <summary>
	/// Non-generic view of a format, so registries and editor UI can treat them all uniformly without knowing the asset type.
	/// </summary>
	public interface IAssetFileFormat
	{
		/// <summary>Lower-case extension including the dot, e.g. ".vtileset".</summary>
		string Extension { get; }

		/// <summary>Menu/diagnostic name, e.g. "Tileset".</summary>
		string DisplayName { get; }

		Type AssetType { get; }

		/// <summary>Null when the file is absent.</summary>
		object LoadObject(string path);

		void SaveObject(object asset, string path);

		IReadOnlyList<AssetCreateOption> CreateOptions { get; }
	}

	/// <summary>
	/// Shared implementation of a JSON-backed, single-object asset file — the pattern behind <c>.vtileset</c>, <c>.vtimeline</c> and anything added later.
	/// </summary>
	public sealed class JsonAssetFile<TAsset> : IAssetFileFormat where TAsset : class
	{
		private readonly JsonSettings _settings;
		private readonly Func<string, TAsset> _createDefault;
		private readonly Action<TAsset> _afterLoad;

		private IReadOnlyList<AssetCreateOption> _createOptions;

		public string Extension { get; }
		public string DisplayName { get; }
		public Type AssetType => typeof(TAsset);

		/// <param name="createDefault">
		/// Receives the file name without extension as a naming hint (null outside a file context);
		/// formats that carry no name may ignore it.
		/// </param>
		/// <param name="settings">
		/// Defaults to pretty-printed, no type names, no reference tracking. A format holding polymorphic
		/// members must pass its own.
		/// </param>
		/// <param name="afterLoad">Post-deserialization fixup. Not called when loading yields null.</param>
		public JsonAssetFile(
			string extension,
			string displayName,
			Func<string, TAsset> createDefault,
			JsonSettings settings = null,
			Action<TAsset> afterLoad = null)
		{
			Extension = extension ?? throw new ArgumentNullException(nameof(extension));
			DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
			_createDefault = createDefault ?? throw new ArgumentNullException(nameof(createDefault));
			_afterLoad = afterLoad;
			_settings = settings ?? new JsonSettings
			{
				PrettyPrint = true,
				TypeNameHandling = TypeNameHandling.None,
				PreserveReferencesHandling = false,
			};
		}

		/// <summary><paramref name="nameHint"/> is the intended file name, if known.</summary>
		public TAsset CreateDefault(string nameHint = null) => _createDefault(nameHint);

		public TAsset CreateAndSave(string path)
		{
			var asset = CreateDefault(Path.GetFileNameWithoutExtension(path));
			Save(asset, path);
			return asset;
		}

		public string ToJson(TAsset asset) => Json.ToJson(asset, _settings);

		public TAsset FromJson(string json)
		{
			var asset = Json.FromJson<TAsset>(json, _settings);
			if (asset != null)
				_afterLoad?.Invoke(asset);
			return asset;
		}

		public void Save(TAsset asset, string path) => File.WriteAllText(path, ToJson(asset));

		/// <summary>Null when the file does not exist.</summary>
		public TAsset Load(string path) => File.Exists(path) ? FromJson(File.ReadAllText(path)) : null;

		#region IAssetFileFormat

		object IAssetFileFormat.LoadObject(string path) => Load(path);

		void IAssetFileFormat.SaveObject(object asset, string path) => Save((TAsset)asset, path);

		/// <summary>Cached, so the Asset Browser can read it every frame the menu is open without allocating.</summary>
		public IReadOnlyList<AssetCreateOption> CreateOptions => _createOptions ??= new[]
		{
			new AssetCreateOption(DisplayName, "New" + DisplayName.Replace(" ", string.Empty),
				path => CreateAndSave(path))
		};

		#endregion
	}
}
