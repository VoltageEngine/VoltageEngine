using System;
using System.Runtime.InteropServices;

namespace Voltage.Editor.Utils
{
	/// <summary>
	/// Resolves SDL2 entry points at runtime. MonoGame.Framework.DesktopGL loads SDL under a per-platform file
	/// name without exposing it, so a plain <c>DllImport("SDL2")</c> would only bind on Windows.
	/// </summary>
	public static class SdlNative
	{
		private static IntPtr _library;
		private static bool _probed;

		public static bool IsAvailable => TryLoad(out _);

		/// <summary>Resolves an export as a delegate. False when SDL or the export is unavailable.</summary>
		public static bool TryGet<T>(string export, out T function) where T : Delegate
		{
			function = null;

			if (!TryLoad(out var library))
				return false;

			if (!NativeLibrary.TryGetExport(library, export, out var address) || address == IntPtr.Zero)
				return false;

			function = Marshal.GetDelegateForFunctionPointer<T>(address);
			return true;
		}

		private static bool TryLoad(out IntPtr library)
		{
			if (!_probed)
			{
				_probed = true;

				string[] candidates =
				{
					"SDL2.dll", "libSDL2-2.0.so.0", "libSDL2-2.0.0.dylib", "SDL2",
				};

				foreach (var candidate in candidates)
				{
					if (NativeLibrary.TryLoad(candidate, out _library))
						break;
				}
			}

			library = _library;
			return library != IntPtr.Zero;
		}
	}
}
