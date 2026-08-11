using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;

namespace Voltage.Editor.FilePickers
{
	/// <summary>
	/// Windows' modern Explorer file dialogs (IFileDialog, Vista+) - the same picker every other Windows
	/// application shows, with the sidebar, the search box, typing a path, and the usual keyboard handling.
	///
	/// <para>This exists because tinyfiledialogs' Windows path does not use them: it calls
	/// <c>SHBrowseForFolder</c> for a folder and <c>GetOpenFileName</c> for a file, which are the pre-Vista
	/// dialogs - a cramped tree with no address bar and no way to paste a path. They still work, so nothing
	/// looked broken; they just are not the dialog Windows users expect. Windows therefore never goes
	/// through tinyfd, and <see cref="NativeFileDialogs"/> routes here instead.</para>
	///
	/// <para>The dialog runs on its own STA thread. COM apartment-model dialogs want STA, and the editor's
	/// main thread is whatever the game host made it - borrowing a thread is cheaper than constraining the
	/// process. <see cref="Show"/> joins it, so callers keep the blocking behaviour they already had.</para>
	/// </summary>
	[SupportedOSPlatform("windows")]
	internal static class WindowsFileDialogs
	{
		public static bool TryPickFolder(string title, string startPath, out string folder) =>
			Show(ClsidFileOpenDialog, title, startPath, null, null, null,
				FosPickFolders | FosForceFileSystem | FosPathMustExist | FosNoChangeDir, out folder);

		public static bool TryOpenFile(string title, string startPathOrFile, string[] filterPatterns,
			string filterDescription, out string file) =>
			Show(ClsidFileOpenDialog, title, startPathOrFile, SuggestedFileName(startPathOrFile),
				BuildFilters(filterPatterns, filterDescription), DefaultExtension(filterPatterns),
				FosForceFileSystem | FosFileMustExist | FosPathMustExist | FosNoChangeDir, out file);

		public static bool TrySaveFile(string title, string startPathOrFile, string[] filterPatterns,
			string filterDescription, out string file) =>
			Show(ClsidFileSaveDialog, title, startPathOrFile, SuggestedFileName(startPathOrFile),
				BuildFilters(filterPatterns, filterDescription), DefaultExtension(filterPatterns),
				FosForceFileSystem | FosPathMustExist | FosOverwritePrompt | FosNoChangeDir, out file);

		private static bool Show(Guid clsid, string title, string startPath, string fileName,
			FilterSpec[] filters, string defaultExtension, uint options, out string result)
		{
			string picked = null;
			Exception failure = null;

			var thread = new Thread(() =>
			{
				try
				{
					picked = ShowOnStaThread(clsid, title, startPath, fileName, filters, defaultExtension, options);
				}
				catch (Exception ex)
				{
					failure = ex;
				}
			});

			thread.SetApartmentState(ApartmentState.STA);
			thread.IsBackground = true;
			thread.Start();
			thread.Join();

			if (failure != null)
				throw failure;

			result = picked;
			return !string.IsNullOrEmpty(picked);
		}

		private static string ShowOnStaThread(Guid clsid, string title, string startPath, string fileName,
			FilterSpec[] filters, string defaultExtension, uint options)
		{
			var iid = IidFileDialog;
			var hr = CoCreateInstance(ref clsid, IntPtr.Zero, ClsCtxInprocServer, ref iid, out var instance);
			if (hr < 0 || instance is not IFileDialog dialog)
				throw new COMException("Could not create the Windows file dialog.", hr);

			try
			{
				// OR into whatever the dialog already defaults to rather than replacing it: the defaults
				// carry behaviour (like restoring the last folder) that is not ours to discard.
				dialog.GetOptions(out var current);
				dialog.SetOptions(current | options);

				if (!string.IsNullOrEmpty(title))
					dialog.SetTitle(title);

				ApplyStartFolder(dialog, startPath);

				if (!string.IsNullOrEmpty(fileName))
					dialog.SetFileName(fileName);

				// A folder dialog rejects SetFileTypes outright (E_UNEXPECTED), so the mode decides rather
				// than the caller: filters are meaningless when picking a folder anyway.
				if (filters is { Length: > 0 } && (options & FosPickFolders) == 0)
				{
					dialog.SetFileTypes((uint)filters.Length, filters);
					dialog.SetFileTypeIndex(1); // 1-based; the caller's own filter, not "All files".
				}

				if (!string.IsNullOrEmpty(defaultExtension))
					dialog.SetDefaultExtension(defaultExtension);

				// Owned by the editor window so the dialog cannot end up behind it.
				hr = dialog.Show(GetForegroundWindow());
				if (hr == HresultCancelled)
					return null;
				if (hr < 0)
					throw new COMException("The Windows file dialog failed.", hr);

				dialog.GetResult(out var item);
				try
				{
					return DisplayName(item);
				}
				finally
				{
					Release(item);
				}
			}
			finally
			{
				Release(dialog);
			}
		}

		/// <summary>
		/// Points the dialog at the caller's folder. SetFolder rather than SetDefaultFolder: the caller
		/// passes the path the user is already working in, which should win over wherever the dialog was
		/// left last time.
		/// </summary>
		private static void ApplyStartFolder(IFileDialog dialog, string startPath)
		{
			var folder = ResolveFolder(startPath);
			if (folder == null)
				return;

			var iid = IidShellItem;
			if (SHCreateItemFromParsingName(folder, IntPtr.Zero, ref iid, out var instance) < 0
			    || instance is not IShellItem item)
			{
				return;
			}

			try
			{
				dialog.SetFolder(item);
			}
			catch (COMException)
			{
				// A folder that vanished between the check and the call is not worth failing the dialog for.
			}
			finally
			{
				Release(item);
			}
		}

		/// <summary>The existing directory to open at, given a folder, a file inside one, or nothing.</summary>
		private static string ResolveFolder(string startPath)
		{
			if (string.IsNullOrWhiteSpace(startPath))
				return null;

			try
			{
				if (Directory.Exists(startPath))
					return Path.GetFullPath(startPath);

				var parent = Path.GetDirectoryName(startPath);
				return !string.IsNullOrEmpty(parent) && Directory.Exists(parent) ? Path.GetFullPath(parent) : null;
			}
			catch
			{
				return null; // Malformed path - let the dialog pick its own default.
			}
		}

		/// <summary>The file name to prefill, when the caller passed a full path to a file rather than a folder.</summary>
		private static string SuggestedFileName(string startPathOrFile)
		{
			if (string.IsNullOrWhiteSpace(startPathOrFile) || Directory.Exists(startPathOrFile))
				return null;

			try
			{
				var name = Path.GetFileName(startPathOrFile);
				return string.IsNullOrEmpty(name) ? null : name;
			}
			catch
			{
				return null;
			}
		}

		/// <summary>
		/// One entry for the caller's own patterns plus an "All files" escape hatch. Patterns are accepted
		/// in every shape the call sites use - "png", ".png" and "*.png" all mean the same thing.
		/// </summary>
		private static FilterSpec[] BuildFilters(string[] patterns, string description)
		{
			var normalized = NormalizePatterns(patterns);
			var all = new FilterSpec { Name = "All files (*.*)", Spec = "*.*" };

			if (normalized.Length == 0)
				return new[] { all };

			var spec = string.Join(";", normalized);
			var name = string.IsNullOrWhiteSpace(description) ? spec : $"{description} ({spec})";

			return new[] { new FilterSpec { Name = name, Spec = spec }, all };
		}

		private static string[] NormalizePatterns(string[] patterns)
		{
			if (patterns == null)
				return Array.Empty<string>();

			return patterns
				.Where(p => !string.IsNullOrWhiteSpace(p))
				.Select(p =>
				{
					var trimmed = p.Trim();
					if (trimmed.StartsWith("*.", StringComparison.Ordinal))
						return trimmed;
					return trimmed.StartsWith(".", StringComparison.Ordinal) ? "*" + trimmed : "*." + trimmed;
				})
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.ToArray();
		}

		/// <summary>Extension appended when the user types a bare name, taken from the first pattern.</summary>
		private static string DefaultExtension(string[] patterns)
		{
			var normalized = NormalizePatterns(patterns);
			if (normalized.Length == 0)
				return null;

			var first = normalized[0];
			return first is "*.*" ? null : first.Substring(2);
		}

		private static string DisplayName(IShellItem item)
		{
			item.GetDisplayName(SigdnFileSysPath, out var ptr);
			if (ptr == IntPtr.Zero)
				return null;

			try
			{
				return Marshal.PtrToStringUni(ptr);
			}
			finally
			{
				Marshal.FreeCoTaskMem(ptr);
			}
		}

		private static void Release(object comObject)
		{
			if (comObject != null && Marshal.IsComObject(comObject))
				Marshal.ReleaseComObject(comObject);
		}

		#region Native interop

		private static readonly Guid ClsidFileOpenDialog = new("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7");
		private static readonly Guid ClsidFileSaveDialog = new("C0B4E2F3-BA21-4773-8DBA-335EC946EB8B");
		private static readonly Guid IidFileDialog = new("42F85136-DB7E-439C-85F1-E4075D135FC8");
		private static readonly Guid IidShellItem = new("43826D1E-E718-42EE-BC55-A1E261C37BFE");

		private const uint ClsCtxInprocServer = 1;

		/// <summary>HRESULT_FROM_WIN32(ERROR_CANCELLED) - the user closed the dialog, which is not a failure.</summary>
		private const int HresultCancelled = unchecked((int)0x800704C7);

		private const uint SigdnFileSysPath = 0x80058000;

		// FILEOPENDIALOGOPTIONS
		private const uint FosOverwritePrompt = 0x00000002;
		private const uint FosNoChangeDir = 0x00000008;
		private const uint FosPickFolders = 0x00000020;
		private const uint FosForceFileSystem = 0x00000040;
		private const uint FosPathMustExist = 0x00000800;
		private const uint FosFileMustExist = 0x00001000;

		[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
		private struct FilterSpec
		{
			[MarshalAs(UnmanagedType.LPWStr)] public string Name;
			[MarshalAs(UnmanagedType.LPWStr)] public string Spec;
		}

		// IFileDialog, declared in vtable order (IModalWindow's Show first). IFileOpenDialog and
		// IFileSaveDialog both derive from it, so one declaration drives every dialog here; only Show is
		// PreserveSig, because cancelling is an ordinary outcome rather than an exception.
		[ComImport, Guid("42F85136-DB7E-439C-85F1-E4075D135FC8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
		private interface IFileDialog
		{
			[PreserveSig] int Show(IntPtr parent);

			void SetFileTypes(uint count, [MarshalAs(UnmanagedType.LPArray)] FilterSpec[] filterSpec);
			void SetFileTypeIndex(uint fileType);
			void GetFileTypeIndex(out uint fileType);
			void Advise(IntPtr events, out uint cookie);
			void Unadvise(uint cookie);
			void SetOptions(uint options);
			void GetOptions(out uint options);
			void SetDefaultFolder(IShellItem folder);
			void SetFolder(IShellItem folder);
			void GetFolder(out IShellItem folder);
			void GetCurrentSelection(out IShellItem item);
			void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string name);
			void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string name);
			void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string title);
			void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string text);
			void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string label);
			void GetResult(out IShellItem item);
			void AddPlace(IShellItem item, int placement);
			void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string extension);
			void Close([MarshalAs(UnmanagedType.Error)] int result);
			void SetClientGuid(ref Guid client);
			void ClearClientData();
			void SetFilter(IntPtr filter);
		}

		[ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
		private interface IShellItem
		{
			void BindToHandler(IntPtr bindContext, ref Guid handler, ref Guid riid, out IntPtr result);
			void GetParent(out IShellItem parent);
			void GetDisplayName(uint sigdnName, out IntPtr name);
			void GetAttributes(uint mask, out uint attributes);
			void Compare(IShellItem other, uint hint, out int order);
		}

		[DllImport("ole32.dll")]
		private static extern int CoCreateInstance(ref Guid clsid, IntPtr outer, uint context, ref Guid iid,
			[MarshalAs(UnmanagedType.Interface)] out object instance);

		[DllImport("shell32.dll", CharSet = CharSet.Unicode)]
		private static extern int SHCreateItemFromParsingName([MarshalAs(UnmanagedType.LPWStr)] string path,
			IntPtr bindContext, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object instance);

		[DllImport("user32.dll")]
		private static extern IntPtr GetForegroundWindow();

		#endregion
	}
}
