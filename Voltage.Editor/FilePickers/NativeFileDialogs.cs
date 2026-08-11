using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Voltage.Editor.DebugUtils;

namespace Voltage.Editor.FilePickers
{
	/// <summary>
	/// The editor's one door to the OS-native open-file / save-file / select-folder dialogs. Every entry
	/// point is a <c>Try…</c> that returns <c>false</c> (rather than throwing) when the dialog is cancelled
	/// or unavailable for the current platform — callers then fall back to the editor's built-in ImGui
	/// picker.
	///
	/// <para>Two implementations sit behind it. Windows goes to <see cref="WindowsFileDialogs"/>, which
	/// shows the modern Explorer dialog every other Windows application uses. Everywhere else this is a
	/// thin P/Invoke over the vendored <c>tinyfiledialogs</c> native library, whose GTK/Cocoa paths are the
	/// system dialogs — it is only the Windows half of tinyfd that is the pre-Vista one, which is exactly
	/// why Windows does not go through it. The tinyfd result strings point into a library-owned static
	/// buffer, so we marshal the return as <see cref="IntPtr"/> and copy it.</para>
	/// </summary>
	public static class NativeFileDialogs
	{
		private const string Lib = "tinyfiledialogs";

		/// <summary>Latched once a P/Invoke throws DllNotFound/EntryPointNotFound so we stop retrying.</summary>
		private static bool _unavailable;
		private static bool _initialized;

		/// <summary>Windows always has IFileDialog (Vista+), so there is nothing to probe there.</summary>
		[SupportedOSPlatformGuard("windows")]
		private static bool UseWindowsDialogs => OperatingSystem.IsWindows();

		/// <summary>
		/// A dialog that refuses to open is reported rather than swallowed: IFileDialog is present on every
		/// supported Windows, so a failure here is a real fault, not a platform difference. Voltage.Debug
		/// rather than EditorDebug, which compiles out unless EDITOR_DEBUG is defined.
		/// </summary>
		private static bool WindowsDialogFailed(Exception ex)
		{
			Voltage.Debug.Warn($"The Windows file dialog failed: {ex.Message}");
			return false;
		}

		/// <summary>True when the native dialogs are usable on this platform (probed lazily, once).</summary>
		public static bool IsAvailable
		{
			get
			{
				if (UseWindowsDialogs)
					return true;

				EnsureInitialized();
				return !_unavailable;
			}
		}

		private static void EnsureInitialized()
		{
			if (_initialized)
				return;
			_initialized = true;

			try
			{
				// Reading the version global both proves the library is loadable and forces UTF-8 string
				// handling for the char* APIs on Windows (harmless elsewhere).
				var ver = Marshal.PtrToStringAnsi(GetVersionPtr());
				TrySetWindowsUtf8();
				EditorDebug.Log($"Native file dialogs available (tinyfiledialogs {ver}).", "FileDialogs");
			}
			catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
			{
				_unavailable = true;
				EditorDebug.Log($"Native file dialogs unavailable ({ex.GetType().Name}); using the in-editor picker.", "FileDialogs");
			}
		}

		/// <summary>
		/// Shows the native "select folder" dialog. Returns true with an absolute folder path on success,
		/// false on cancel OR when native dialogs are unavailable — distinguish via <see cref="IsAvailable"/>.
		/// </summary>
		public static bool TryPickFolder(string title, string startPath, out string folder)
		{
			folder = null;

			if (UseWindowsDialogs)
			{
				try
				{
					return WindowsFileDialogs.TryPickFolder(title, startPath, out folder);
				}
				catch (Exception ex)
				{
					return WindowsDialogFailed(ex);
				}
			}

			if (!IsAvailable)
				return false;

			try
			{
				var ptr = tinyfd_selectFolderDialog(title ?? "", startPath ?? "");
				folder = Marshal.PtrToStringUTF8(ptr);
				return !string.IsNullOrEmpty(folder);
			}
			catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
			{
				_unavailable = true;
				return false;
			}
		}

		/// <summary>
		/// Shows the native "open file" dialog. <paramref name="filterPatterns"/> are globs like
		/// <c>"*.png"</c> (null/empty = all files). Returns true with an absolute file path on success.
		/// </summary>
		public static bool TryOpenFile(string title, string startPathOrFile, string[] filterPatterns, string filterDescription, out string file)
		{
			file = null;

			if (UseWindowsDialogs)
			{
				try
				{
					return WindowsFileDialogs.TryOpenFile(title, startPathOrFile, filterPatterns, filterDescription, out file);
				}
				catch (Exception ex)
				{
					return WindowsDialogFailed(ex);
				}
			}

			if (!IsAvailable)
				return false;

			var (patternPtrs, patternArray) = MarshalPatterns(filterPatterns);
			try
			{
				var ptr = tinyfd_openFileDialog(title ?? "", startPathOrFile ?? "",
					patternPtrs.Length, patternArray, filterDescription, 0);
				file = Marshal.PtrToStringUTF8(ptr);
				return !string.IsNullOrEmpty(file);
			}
			catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
			{
				_unavailable = true;
				return false;
			}
			finally
			{
				FreePatterns(patternPtrs, patternArray);
			}
		}

		/// <summary>Shows the native "save file" dialog. Returns true with the chosen absolute path.</summary>
		public static bool TrySaveFile(string title, string startPathOrFile, string[] filterPatterns, string filterDescription, out string file)
		{
			file = null;

			if (UseWindowsDialogs)
			{
				try
				{
					return WindowsFileDialogs.TrySaveFile(title, startPathOrFile, filterPatterns, filterDescription, out file);
				}
				catch (Exception ex)
				{
					return WindowsDialogFailed(ex);
				}
			}

			if (!IsAvailable)
				return false;

			var (patternPtrs, patternArray) = MarshalPatterns(filterPatterns);
			try
			{
				var ptr = tinyfd_saveFileDialog(title ?? "", startPathOrFile ?? "",
					patternPtrs.Length, patternArray, filterDescription);
				file = Marshal.PtrToStringUTF8(ptr);
				return !string.IsNullOrEmpty(file);
			}
			catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
			{
				_unavailable = true;
				return false;
			}
			finally
			{
				FreePatterns(patternPtrs, patternArray);
			}
		}

		#region Native interop

		// Marshals a managed string[] of globs to a UTF-8 char const* const* the way tinyfd expects.
		private static (IntPtr[] ptrs, IntPtr array) MarshalPatterns(string[] patterns)
		{
			if (patterns == null || patterns.Length == 0)
				return (Array.Empty<IntPtr>(), IntPtr.Zero);

			var ptrs = new IntPtr[patterns.Length];
			for (var i = 0; i < patterns.Length; i++)
				ptrs[i] = Marshal.StringToCoTaskMemUTF8(patterns[i] ?? "");

			var array = Marshal.AllocCoTaskMem(IntPtr.Size * patterns.Length);
			Marshal.Copy(ptrs, 0, array, patterns.Length);
			return (ptrs, array);
		}

		private static void FreePatterns(IntPtr[] ptrs, IntPtr array)
		{
			foreach (var p in ptrs)
				if (p != IntPtr.Zero)
					Marshal.FreeCoTaskMem(p);
			if (array != IntPtr.Zero)
				Marshal.FreeCoTaskMem(array);
		}

		/// <summary>On Windows, opt the char* APIs into UTF-8 (default already, but explicit). No-op elsewhere.</summary>
		private static void TrySetWindowsUtf8()
		{
			if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
				return;
			try
			{
				var ptr = GetWinUtf8Ptr();
				if (ptr != IntPtr.Zero)
					Marshal.WriteInt32(ptr, 1);
			}
			catch (EntryPointNotFoundException)
			{
				// Older native without the global — ignore.
			}
		}

		[DllImport(Lib, EntryPoint = "tinyfd_selectFolderDialog", CallingConvention = CallingConvention.Cdecl)]
		private static extern IntPtr tinyfd_selectFolderDialog(
			[MarshalAs(UnmanagedType.LPUTF8Str)] string aTitle,
			[MarshalAs(UnmanagedType.LPUTF8Str)] string aDefaultPath);

		[DllImport(Lib, EntryPoint = "tinyfd_openFileDialog", CallingConvention = CallingConvention.Cdecl)]
		private static extern IntPtr tinyfd_openFileDialog(
			[MarshalAs(UnmanagedType.LPUTF8Str)] string aTitle,
			[MarshalAs(UnmanagedType.LPUTF8Str)] string aDefaultPathAndOrFile,
			int aNumOfFilterPatterns,
			IntPtr aFilterPatterns,
			[MarshalAs(UnmanagedType.LPUTF8Str)] string aSingleFilterDescription,
			int aAllowMultipleSelects);

		[DllImport(Lib, EntryPoint = "tinyfd_saveFileDialog", CallingConvention = CallingConvention.Cdecl)]
		private static extern IntPtr tinyfd_saveFileDialog(
			[MarshalAs(UnmanagedType.LPUTF8Str)] string aTitle,
			[MarshalAs(UnmanagedType.LPUTF8Str)] string aDefaultPathAndOrFile,
			int aNumOfFilterPatterns,
			IntPtr aFilterPatterns,
			[MarshalAs(UnmanagedType.LPUTF8Str)] string aSingleFilterDescription);

		[DllImport(Lib, EntryPoint = "tinyfd_getGlobalChar", CallingConvention = CallingConvention.Cdecl)]
		private static extern IntPtr tinyfd_getGlobalChar([MarshalAs(UnmanagedType.LPUTF8Str)] string aParam);

		// tinyfd_version is an exported global char[8]; resolve its address via the module.
		private static IntPtr GetVersionPtr()
		{
			var handle = NativeLibrary.Load(Lib, typeof(NativeFileDialogs).Assembly, null);
			return NativeLibrary.GetExport(handle, "tinyfd_version");
		}

		private static IntPtr GetWinUtf8Ptr()
		{
			var handle = NativeLibrary.Load(Lib, typeof(NativeFileDialogs).Assembly, null);
			return NativeLibrary.TryGetExport(handle, "tinyfd_winUtf8", out var ptr) ? ptr : IntPtr.Zero;
		}

		#endregion
	}
}
