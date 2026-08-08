using System;
using System.IO;
using System.Reflection;
using Voltage.Assets;
using Voltage.Data;
using Voltage.Serialization;

namespace Voltage.Editor.Inspectors.TypeInspectors
{
	/// <summary>
	/// Turns <see cref="AssetTypeAttribute"/> on an inspector member into a predicate over asset files, so a slot can filter its picker and reject a mismatched drop.
	/// </summary>
	internal sealed class AssetSlotFilter
	{
		/// <summary>Accepts anything; for members with no constraint.</summary>
		public static readonly AssetSlotFilter None = new();

		private readonly string[] _extensions;   // lower-case, with dot; null = no extension constraint
		private readonly Type _dataAssetType;    // non-null when constraining to a DataAsset subclass

		/// <summary>Shown in an empty slot, e.g. "None (DifficultyProfile)".</summary>
		public string DisplayTypeName { get; } = "AssetReference";

		/// <summary>False when it accepts everything.</summary>
		public bool IsConstrained => _extensions != null || _dataAssetType != null;

		private AssetSlotFilter()
		{
		}

		private AssetSlotFilter(string[] extensions, Type dataAssetType, string displayTypeName)
		{
			_extensions = extensions;
			_dataAssetType = dataAssetType;
			DisplayTypeName = displayTypeName;
		}

		/// <summary><see cref="None"/> when the member carries no constraint.</summary>
		public static AssetSlotFilter For(MemberInfo member)
		{
			var attr = member?.GetCustomAttribute<AssetTypeAttribute>(inherit: true);
			if (attr == null)
				return None;

			if (attr.Extensions is { Length: > 0 })
			{
				var normalized = new string[attr.Extensions.Length];
				for (var i = 0; i < attr.Extensions.Length; i++)
				{
					var ext = attr.Extensions[i] ?? string.Empty;
					if (ext.Length > 0 && ext[0] != '.')
						ext = '.' + ext;
					normalized[i] = ext.ToLowerInvariant();
				}

				return new AssetSlotFilter(normalized, null, string.Join("/", normalized));
			}

			var type = attr.AssetType;
			if (type == null)
				return None;

			if (typeof(DataAsset).IsAssignableFrom(type))
				return new AssetSlotFilter(new[] { DataAssetIO.FileExtension }, type, type.Name);

			foreach (var format in AssetFileRegistry.All)
			{
				if (format.AssetType == type)
					return new AssetSlotFilter(new[] { format.Extension.ToLowerInvariant() }, null, type.Name);
			}

			return new AssetSlotFilter(null, null, type.Name);
		}

		/// <summary>True when the file may be assigned to this slot.</summary>
		public bool Accepts(string absolutePath)
		{
			if (string.IsNullOrEmpty(absolutePath))
				return false;

			if (_extensions != null)
			{
				var ext = Path.GetExtension(absolutePath).ToLowerInvariant();
				var extensionOk = false;
				foreach (var candidate in _extensions)
				{
					if (ext == candidate)
					{
						extensionOk = true;
						break;
					}
				}

				if (!extensionOk)
					return false;
			}

			if (_dataAssetType == null)
				return true;

			var id = DataAssetIO.PeekAssetTypeId(absolutePath);
			if (id == null)
				return false;

			return DataAssetRegistry.TryGet(id, out var entry) && _dataAssetType.IsAssignableFrom(entry.Type);
		}

		/// <summary>
		/// Prefers the <c>AssetDatabase</c>'s cached header id over re-reading the file — the picker asks this once per candidate per frame.
		/// </summary>
		public bool Accepts(string absolutePath, Guid guid)
		{
			if (_dataAssetType == null || guid == Guid.Empty)
				return Accepts(absolutePath);

			if (_extensions != null)
			{
				var ext = Path.GetExtension(absolutePath ?? string.Empty).ToLowerInvariant();
				if (ext != DataAssetIO.FileExtension)
					return false;
			}

			var id = Voltage.Editor.Assets.AssetDatabase.Instance?.GetDataAssetTypeId(guid)
					 ?? DataAssetIO.PeekAssetTypeId(absolutePath);
			if (id == null)
				return false;

			return DataAssetRegistry.TryGet(id, out var entry) && _dataAssetType.IsAssignableFrom(entry.Type);
		}
	}
}
