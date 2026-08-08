using System;
using System.Collections.Generic;
using System.IO;

namespace Voltage.Assets
{
	/// <summary>
	/// Extension → <see cref="IAssetFileFormat"/>.
	/// </summary>
	public static class AssetFileRegistry
	{
		private static readonly object _lock = new();

		private static readonly Dictionary<string, IAssetFileFormat> _byExtension =
			new(StringComparer.OrdinalIgnoreCase);

		private static IAssetFileFormat[] _snapshot = Array.Empty<IAssetFileFormat>();

		private static bool _engineFormatsRegistered;

		/// <summary>Replaces any previous format for the same extension. Idempotent.</summary>
		public static void Register(IAssetFileFormat format)
		{
			if (format == null || string.IsNullOrEmpty(format.Extension))
				return;

			lock (_lock)
			{
				_byExtension[format.Extension] = format;

				var snapshot = new IAssetFileFormat[_byExtension.Count];
				_byExtension.Values.CopyTo(snapshot, 0);
				_snapshot = snapshot;
			}
		}

		/// <summary>With or without a leading dot. Null when the extension is unknown.</summary>
		public static IAssetFileFormat ForExtension(string extension)
		{
			EnsureEngineFormatsRegistered();

			if (string.IsNullOrEmpty(extension))
				return null;

			if (extension[0] != '.')
				extension = '.' + extension;

			lock (_lock)
				return _byExtension.TryGetValue(extension, out var format) ? format : null;
		}

		/// <summary>Null when the path's extension is unknown.</summary>
		public static IAssetFileFormat ForPath(string path) =>
			string.IsNullOrEmpty(path) ? null : ForExtension(Path.GetExtension(path));

		/// <summary>True when this registry can load and save that file type.</summary>
		public static bool IsKnownAssetFile(string path) => ForPath(path) != null;

		/// <summary>A stable snapshot, safe to enumerate while other threads register.</summary>
		public static IReadOnlyList<IAssetFileFormat> All
		{
			get
			{
				EnsureEngineFormatsRegistered();
				lock (_lock)
					return _snapshot;
			}
		}

		/// <summary>
		/// Idempotent, and safe to call from a read path: the guard is set <i>before</i> the registrations run, so the <see cref="Register"/> calls it triggers cannot recurse back into it.
		/// </summary>
		internal static void EnsureEngineFormatsRegistered()
		{
			lock (_lock)
			{
				if (_engineFormatsRegistered)
					return;
				_engineFormatsRegistered = true;
			}

			EngineAssetFormats.RegisterAll();
		}
	}
}
