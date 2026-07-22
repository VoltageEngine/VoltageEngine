using System;
using System.Runtime.InteropServices;
using Microsoft.Xna.Framework;
using Voltage.Editor.DebugUtils;
using Voltage.Utils;

namespace Voltage.Editor.Utils
{
	/// <summary>
	/// Fits the editor window to the monitor's usable area - the desktop minus the taskbar, dock or panels.
	/// </summary>
	/// <remarks>
	/// <see cref="ScreenUtils.SetWindowedMode"/> derives its size from raw monitor bounds minus fixed 32px
	/// constants, which leaves the bottom of the CLIENT area underneath the taskbar.
	/// This is editor-only on purpose; shipped games keep the existing windowed behaviour.
	/// </remarks>
	public static class EditorWindowLayout
	{
		[StructLayout(LayoutKind.Sequential)]
		private struct SdlRect
		{
			public int X;
			public int Y;
			public int W;
			public int H;
		}

		private delegate int GetWindowDisplayIndexDelegate(IntPtr window);
		private delegate int GetDisplayUsableBoundsDelegate(int displayIndex, out SdlRect rect);
		private delegate int GetWindowBordersSizeDelegate(IntPtr window, out int top, out int left, out int bottom, out int right);

		/// <summary>
		/// Fills the current monitor's usable area. A no-op when SDL cannot be reached or the window is not
		/// windowed, leaving whatever <see cref="ScreenUtils"/> applied.
		/// </summary>
		public static void FitToUsableBounds()
		{
			// Never touch geometry in fullscreen or borderless. The size is only for the Display to decide
			if (Screen.IsFullscreen || ScreenUtils.Mode == ScreenUtils.ScreenMode.FullScreen ||
			    ScreenUtils.Mode == ScreenUtils.ScreenMode.Borderless)
				return;

			try
			{
				var window = Core.Instance?.Window;
				if (window == null || window.Handle == IntPtr.Zero)
					return;

				if (!TryLoadSdl(out var library))
					return;

				if (!TryGet<GetWindowDisplayIndexDelegate>(library, "SDL_GetWindowDisplayIndex", out var displayIndexOf) ||
				    !TryGet<GetDisplayUsableBoundsDelegate>(library, "SDL_GetDisplayUsableBounds", out var usableBoundsOf))
					return;

				var display = displayIndexOf(window.Handle);
				if (display < 0 || usableBoundsOf(display, out var usable) != 0 || usable.W <= 0 || usable.H <= 0)
					return;

				// Borders are outside the client area, so the client must shrink by them or the window overhangs
				// the usable area by exactly the title bar. A failure here just means no inset.
				int top = 0, left = 0, bottom = 0, right = 0;
				if (TryGet<GetWindowBordersSizeDelegate>(library, "SDL_GetWindowBordersSize", out var bordersOf))
					bordersOf(window.Handle, out top, out left, out bottom, out right);

				window.Position = new Point(usable.X + left, usable.Y + top);

				Screen.SetSize(
					Math.Max(640, usable.W - left - right),
					Math.Max(480, usable.H - top - bottom));
			}
			catch (Exception ex)
			{
				EditorDebug.Log($"EditorWindowLayout: could not fit to the usable area: {ex.Message}", "Editor");
			}
		}

		private static IntPtr _library;
		private static bool _libraryProbed;

		// MonoGame.Framework.DesktopGL loads SDL dynamically under a per-platform file name rather than exposing
		// it, so probe the same names instead of a DllImport that would only resolve on Windows.
		private static bool TryLoadSdl(out IntPtr library)
		{
			if (!_libraryProbed)
			{
				_libraryProbed = true;

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

		private static bool TryGet<T>(IntPtr library, string export, out T function) where T : Delegate
		{
			function = null;

			if (!NativeLibrary.TryGetExport(library, export, out var address) || address == IntPtr.Zero)
				return false;

			function = Marshal.GetDelegateForFunctionPointer<T>(address);
			return true;
		}
	}
}
