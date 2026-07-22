using System;
using System.Runtime.InteropServices;
using Microsoft.Xna.Framework;
using Voltage.Editor.DebugUtils;
using Voltage.Utils;

namespace Voltage.Editor.Utils
{
	/// <summary>
	/// Keeps the editor window inside the monitor's usable area - the desktop minus the taskbar, dock or panels.
	/// </summary>
	/// <remarks>
	/// <see cref="ScreenUtils.SetWindowedMode"/> derives its size from raw monitor bounds minus fixed 32px
	/// constants, which leaves the bottom of the CLIENT area underneath the taskbar. Nothing renders wrong, but
	/// ImGui treats that hidden strip as usable space and will happily put a context menu there - hence menus that
	/// look cut off near the bottom of the screen. Asking the OS for the real usable bounds removes the guesswork.
	/// This is editor-only on purpose: shipped games keep the existing windowed behaviour.
	/// </remarks>
	public static class EditorWindowLayout
	{
		// Position round-trips through SDL, so a pixel or two of disagreement must not start a nudge-per-frame loop.
		private const int Tolerance = 2;

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

		private static int _lastDisplay = -1;
		private static bool _applying;

		/// <summary>
		/// Fills the current monitor's usable area and starts tracking it. A no-op when SDL cannot be reached,
		/// leaving whatever <see cref="ScreenUtils"/> already applied.
		/// </summary>
		public static void Install()
		{
			if (!TryGetUsableArea(out var usable, out var left, out var top, out var right, out var bottom))
				return;

			var window = Core.Instance.Window;

			Apply(usable.X + left, usable.Y + top,
				Math.Max(640, usable.W - left - right),
				Math.Max(480, usable.H - top - bottom));

			window.ClientSizeChanged += OnClientSizeChanged;
		}

		/// <summary>
		/// Call once per frame. MonoGame raises no event when a window is dragged to another monitor, so the
		/// display is polled; a change re-clamps against that monitor's taskbar.
		/// </summary>
		public static void Tick()
		{
			if (_applying)
				return;

			try
			{
				var window = Core.Instance?.Window;
				if (window == null || window.Handle == IntPtr.Zero)
					return;

				if (!TryLoadSdl(out var library) ||
				    !TryGet<GetWindowDisplayIndexDelegate>(library, "SDL_GetWindowDisplayIndex", out var displayIndexOf))
					return;

				var display = displayIndexOf(window.Handle);
				if (display < 0 || display == _lastDisplay)
					return;

				_lastDisplay = display;
				ClampToUsableBounds();
			}
			catch
			{
				// Polling must never be able to break the frame.
			}
		}

		private static void OnClientSizeChanged(object sender, EventArgs e) => ClampToUsableBounds();

		/// <summary>
		/// Shrinks and nudges the window until it fits the usable area - never grows it, so a window the user
		/// deliberately made small stays small. Only the parts that overhang the taskbar are corrected.
		/// </summary>
		public static void ClampToUsableBounds()
		{
			if (_applying)
				return;

			if (!TryGetUsableArea(out var usable, out var left, out var top, out var right, out var bottom))
				return;

			var bounds = Core.Instance.Window.ClientBounds;

			var width = Math.Min(bounds.Width, usable.W - left - right);
			var height = Math.Min(bounds.Height, usable.H - top - bottom);

			var minX = usable.X + left;
			var minY = usable.Y + top;
			var x = Math.Clamp(bounds.X, minX, Math.Max(minX, usable.X + usable.W - right - width));
			var y = Math.Clamp(bounds.Y, minY, Math.Max(minY, usable.Y + usable.H - bottom - height));

			var resized = width != bounds.Width || height != bounds.Height;
			var moved = Math.Abs(x - bounds.X) > Tolerance || Math.Abs(y - bounds.Y) > Tolerance;

			if (!resized && !moved)
				return;

			Apply(x, y, width, height);
		}

		// Guarded because resizing raises ClientSizeChanged, which would re-enter this immediately.
		private static void Apply(int x, int y, int width, int height)
		{
			_applying = true;

			try
			{
				Core.Instance.Window.Position = new Point(x, y);
				Screen.SetSize(width, height);
				Screen.ApplyChanges();
			}
			catch (Exception ex)
			{
				EditorDebug.Log($"EditorWindowLayout: could not fit to the usable area: {ex.Message}", "Editor");
			}
			finally
			{
				_applying = false;
			}
		}

		private static bool TryGetUsableArea(out SdlRect usable, out int left, out int top, out int right, out int bottom)
		{
			usable = default;
			left = top = right = bottom = 0;

			try
			{
				var window = Core.Instance?.Window;
				if (window == null || window.Handle == IntPtr.Zero)
					return false;

				if (!TryLoadSdl(out var library))
					return false;

				if (!TryGet<GetWindowDisplayIndexDelegate>(library, "SDL_GetWindowDisplayIndex", out var displayIndexOf) ||
				    !TryGet<GetDisplayUsableBoundsDelegate>(library, "SDL_GetDisplayUsableBounds", out var usableBoundsOf))
					return false;

				var display = displayIndexOf(window.Handle);
				if (display < 0 || usableBoundsOf(display, out usable) != 0 || usable.W <= 0 || usable.H <= 0)
					return false;

				_lastDisplay = display;

				// Borders are outside the client area, so the client must shrink by them or the window overhangs
				// the usable area by exactly the title bar. A failure here just means no inset.
				if (TryGet<GetWindowBordersSizeDelegate>(library, "SDL_GetWindowBordersSize", out var bordersOf))
					bordersOf(window.Handle, out top, out left, out bottom, out right);

				return true;
			}
			catch (Exception ex)
			{
				// Never let window cosmetics stop the editor from starting.
				EditorDebug.Log($"EditorWindowLayout: could not read the usable area: {ex.Message}", "Editor");
				return false;
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
