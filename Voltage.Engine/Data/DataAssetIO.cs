using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Voltage.Assets;
using Voltage.Persistence;

namespace Voltage.Data
{
	/// <summary>
	/// Load/save for <c>.vasset</c> data-container files: <code> { "@assetType": "DifficultyProfile", "@version": 1, "data": { … } } </code> <b>Reading is generated, writing is reflective.</b> <see cref="Load"/> dispatches to the source-generated reader, so it works under NativeAOT; <see cref="Save"/> uses the reflection encoder, which is fine because saving only happens in the editor.
	/// </summary>
	public static class DataAssetIO
	{
		public const string FileExtension = ".vasset";

		internal const string KeyAssetType = "@assetType";
		internal const string KeyVersion = "@version";
		internal const string KeyData = "data";

		/// <summary>Registered with <see cref="AssetFileRegistry"/> by <c>EngineAssetFormats</c>.</summary>
		public static readonly IAssetFileFormat Format = new DataAssetFileFormat();

		private static JsonSettings SaveSettings() => new()
		{
			PrettyPrint = true,
			TypeNameHandling = TypeNameHandling.None,
			PreserveReferencesHandling = false,
		};

		/// <summary>
		/// The asset at <paramref name="path"/>, or null if it is missing, malformed, or names an unregistered type.
		/// </summary>
		public static DataAsset Load(string path)
		{
			if (string.IsNullOrEmpty(path) || !File.Exists(path))
				return null;

			string json;
			try
			{
				json = File.ReadAllText(path);
			}
			catch (Exception ex)
			{
				Debug.Error($"[DataAssetIO] Could not read '{path}': {ex.Message}");
				return null;
			}

			return FromJson(json, path);
		}

		/// <summary><paramref name="originPath"/> is for diagnostics and <see cref="DataAsset.SourcePath"/>; may be null.</summary>
		public static DataAsset FromJson(string json, string originPath = null)
		{
			if (string.IsNullOrEmpty(json))
				return null;

			var where = originPath ?? "<memory>";

			try
			{
				using var r = new JsonTokenReader(json);
				if (!r.BeginObject())
				{
					Debug.Error($"[DataAssetIO] '{where}' is not a JSON object.");
					return null;
				}

				string id = null;
				var version = 1;

				while (r.ReadNextKey(out var key))
				{
					switch (key)
					{
						case KeyAssetType:
							id = r.ReadString();
							break;

						case KeyVersion:
							version = r.ReadInt();
							break;

						case KeyData:
							if (string.IsNullOrEmpty(id))
							{
								Debug.Error(
									$"[DataAssetIO] '{where}': \"{KeyData}\" appears before \"{KeyAssetType}\". " +
									"Re-save the asset from the editor.");
								return null;
							}

							if (!DataAssetRegistry.TryGet(id, out var entry))
							{
								Debug.Error(
									$"[DataAssetIO] '{where}': unknown {KeyAssetType} \"{id}\". The script " +
									"declaring it may have been deleted, renamed without a [FormerlyKnownAs], " +
									"or failed to compile.");
								return null;
							}

							var asset = r.ReadObject(entry.Reader);
							if (asset == null)
								return null;

							asset.SourcePath = originPath;
							asset.LoadedVersion = version;
							asset.OnLoaded();
							return asset;

						default:
							r.SkipValue();
							break;
					}
				}

				Debug.Error($"[DataAssetIO] '{where}' has no \"{KeyData}\" object.");
				return null;
			}
			catch (Exception ex)
			{
				Debug.Error($"[DataAssetIO] Failed to parse '{where}': {ex.Message}");
				return null;
			}
		}

		/// <summary>
		/// Reads only the <c>@assetType</c> header, without materialising the asset — this is what lets the editor filter typed slots across a whole project cheaply.
		/// </summary>
		public static string PeekAssetTypeId(string path)
		{
			if (string.IsNullOrEmpty(path) || !File.Exists(path))
				return null;

			try
			{
				using var r = new JsonTokenReader(File.ReadAllText(path));
				if (!r.BeginObject())
					return null;

				while (r.ReadNextKey(out var key))
				{
					if (key == KeyAssetType)
						return r.ReadString();

					if (key == KeyData)
						return null;

					r.SkipValue();
				}
			}
			catch
			{
			}

			return null;
		}

		/// <summary>Serializes <paramref name="asset"/> to the <c>.vasset</c> document form.</summary>
		public static string ToJson(DataAsset asset)
		{
			if (asset == null)
				throw new ArgumentNullException(nameof(asset));

			if (!DataAssetRegistry.TryGet(asset.GetType(), out var entry))
			{
				throw new InvalidOperationException(
					$"[DataAssetIO] '{asset.GetType().FullName}' is not a registered data asset. It must be " +
					"a concrete, partial DataAsset subclass with a public parameterless constructor so the " +
					"source generator can emit its reader.");
			}

			var payload = Json.ToJson(asset, SaveSettings()) ?? "{}";

			var sb = new StringBuilder();
			sb.Append("{\n");
			sb.Append("  \"").Append(KeyAssetType).Append("\": \"").Append(Escape(entry.Id)).Append("\",\n");
			sb.Append("  \"").Append(KeyVersion).Append("\": ").Append(entry.Version).Append(",\n");
			sb.Append("  \"").Append(KeyData).Append("\": ").Append(Indent(payload, "  ")).Append('\n');
			sb.Append("}\n");
			return sb.ToString();
		}

		/// <summary>Writes <paramref name="asset"/> to <paramref name="path"/>.</summary>
		public static void Save(DataAsset asset, string path) => File.WriteAllText(path, ToJson(asset));

		/// <summary>A fresh instance of the type with that stable id, or null when it is unknown.</summary>
		public static DataAsset CreateDefault(string assetTypeId) =>
			DataAssetRegistry.TryGet(assetTypeId, out var entry) ? entry.Factory() : null;

		/// <summary>Writes a fresh default asset of that type to <paramref name="path"/> and returns it.</summary>
		public static DataAsset CreateAndSave(string assetTypeId, string path)
		{
			var asset = CreateDefault(assetTypeId);
			if (asset == null)
			{
				Debug.Error($"[DataAssetIO] Cannot create '{assetTypeId}' — no such data asset type is registered.");
				return null;
			}

			Save(asset, path);
			return asset;
		}

		private static string Indent(string json, string indent)
		{
			if (string.IsNullOrEmpty(json) || json.IndexOf('\n') < 0)
				return json;

			return json.Replace("\n", "\n" + indent);
		}

		private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
	}

	/// <summary>
	/// <see cref="IAssetFileFormat"/> for <c>.vasset</c>.
	/// </summary>
	internal sealed class DataAssetFileFormat : IAssetFileFormat
	{
		private IReadOnlyList<AssetCreateOption> _cached = Array.Empty<AssetCreateOption>();
		private int _cachedRegistryVersion = -1;

		public string Extension => DataAssetIO.FileExtension;
		public string DisplayName => "Data Asset";
		public Type AssetType => typeof(DataAsset);

		public object LoadObject(string path) => DataAssetIO.Load(path);

		public void SaveObject(object asset, string path) => DataAssetIO.Save((DataAsset)asset, path);

		public IReadOnlyList<AssetCreateOption> CreateOptions
		{
			get
			{
				var version = DataAssetRegistry.Version;
				if (version == _cachedRegistryVersion)
					return _cached;

				var entries = DataAssetRegistry.All;
				var options = new AssetCreateOption[entries.Count];
				for (var i = 0; i < entries.Count; i++)
				{
					var id = entries[i].Id;
					options[i] = new AssetCreateOption(
						entries[i].DisplayName,
						"New" + entries[i].Type.Name,
						path => DataAssetIO.CreateAndSave(id, path));
				}

				_cached = options;
				_cachedRegistryVersion = version;
				return _cached;
			}
		}
	}
}
