using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Voltage
{
	/// <summary>
	/// Remaps the "SDL2.dll" P/Invoke name to the correct native library on non-Windows
	/// platforms. MonoGame installs a DllImportResolver for its own assembly only, so our
	/// direct SDL2 P/Invokes (Clipboard here, SdlFileDropWatcher in the editor) resolve the
	/// raw "SDL2.dll" name and fail on Linux/macOS without their own resolver.
	/// </summary>
	public static class SdlNative
	{
		/// <summary>
		/// Installs the resolver for the given assembly. Safe to call more than once.
		/// </summary>
		public static void Register(Assembly assembly)
		{
			try { NativeLibrary.SetDllImportResolver(assembly, Resolve); }
			catch (InvalidOperationException) { /* a resolver is already set for this assembly */ }
		}

		[ModuleInitializer]
		internal static void RegisterEngineAssembly() => Register(typeof(SdlNative).Assembly);

		private static IntPtr Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
		{
			// On Windows the default resolution already finds SDL2.dll; only intervene elsewhere.
			if (libraryName != "SDL2.dll" || OperatingSystem.IsWindows())
				return IntPtr.Zero;

			string[] candidates = OperatingSystem.IsMacOS()
				? new[] { "libSDL2-2.0.0.dylib", "libSDL2.dylib" }
				: new[] { "libSDL2-2.0.so.0", "libSDL2.so" };

			foreach (var candidate in candidates)
			{
				if (NativeLibrary.TryLoad(candidate, assembly, searchPath, out var handle))
					return handle;
			}

			return IntPtr.Zero;
		}
	}
}
