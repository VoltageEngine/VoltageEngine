using System;
using System.Runtime.InteropServices;
using Voltage.Editor.Utils;

namespace Voltage.Editor.Styling;

internal static class EditorDisplayScale
{
	[StructLayout(LayoutKind.Sequential, Size = 72)]
	private struct WindowInfo
	{
		public byte Major, Minor, Patch;
		public int Subsystem;
		public IntPtr Window;
	}
	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	private delegate int GetWindowInfo(IntPtr window, ref WindowInfo info);
	[DllImport("user32.dll")]
	private static extern uint GetDpiForWindow(IntPtr window);
	private static GetWindowInfo _getWindowInfo;

	internal static float GetScale()
	{
		if (Core.Instance?.Window == null) return 1;
		if (OperatingSystem.IsWindows())
		{
			_getWindowInfo ??= Utils.SdlNative.TryGet<GetWindowInfo>("SDL_GetWindowWMInfo", out var function) ? function : null;
			var info = new WindowInfo { Major = 2 };
			if (_getWindowInfo != null && _getWindowInfo(Core.Instance.Window.Handle, ref info) != 0 && info.Subsystem == 1)
			{
				var dpi = GetDpiForWindow(info.Window);
				if (dpi > 0) return dpi / 96f;
			}
		}
		// Retina drawables may contain more pixels than their window's mouse coordinates.
		var bounds = Core.Instance.Window.ClientBounds;
		return bounds.Width > 0 ? Math.Max(1, Core.GraphicsDevice.PresentationParameters.BackBufferWidth / (float)bounds.Width) : 1;
	}
}
