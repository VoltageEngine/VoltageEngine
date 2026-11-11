using System;
using System.Runtime.InteropServices;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Voltage.Editor.Styling;

/// <summary>MonoGame never asks SDL for a Retina drawable, so the editor turns one on for the Cocoa view and keeps the back buffer in its pixels.</summary>
internal static class MacRetina
{
	private const string ObjC = "/usr/lib/libobjc.A.dylib";
	private const int CocoaSubsystem = 4;

	[StructLayout(LayoutKind.Sequential, Size = 72)]
	private struct WindowInfo
	{
		public byte Major, Minor, Patch;
		public int Subsystem;
		public IntPtr Window;
	}

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	private delegate int GetWindowInfo(IntPtr window, ref WindowInfo info);

	[DllImport(ObjC)]
	private static extern IntPtr sel_registerName([MarshalAs(UnmanagedType.LPUTF8Str)] string name);

	[DllImport(ObjC)]
	private static extern IntPtr objc_getClass([MarshalAs(UnmanagedType.LPUTF8Str)] string name);

	[DllImport(ObjC, EntryPoint = "objc_msgSend")]
	private static extern IntPtr SendPointer(IntPtr receiver, IntPtr selector);

	[DllImport(ObjC, EntryPoint = "objc_msgSend")]
	private static extern void SendBool(IntPtr receiver, IntPtr selector, [MarshalAs(UnmanagedType.I1)] bool value);

	[DllImport(ObjC, EntryPoint = "objc_msgSend")]
	[return: MarshalAs(UnmanagedType.I1)]
	private static extern bool GetBool(IntPtr receiver, IntPtr selector);

	[DllImport(ObjC, EntryPoint = "objc_msgSend")]
	private static extern double GetDouble(IntPtr receiver, IntPtr selector);

	private static IntPtr _window, _view, _openGlContextClass;
	private static IntPtr _wantsBest, _setWantsBest, _backingScale, _currentContext, _update;

	/// <summary>Back-buffer pixels per window point; 1 until <see cref="Enable"/> finds a Cocoa window.</summary>
	internal static float Scale { get; private set; } = 1;

	internal static bool Active => _view != IntPtr.Zero;

	/// <summary>Finds the editor's NSWindow and syncs once; a no-op off macOS or when SDL hides the Cocoa handles.</summary>
	internal static void Enable(GameWindow window)
	{
		if (!OperatingSystem.IsMacOS() || Active || window == null)
			return;

		try
		{
			if (!Utils.SdlNative.TryGet<GetWindowInfo>("SDL_GetWindowWMInfo", out var getInfo))
				return;

			var info = new WindowInfo { Major = 2 };
			if (getInfo(window.Handle, ref info) == 0 || info.Subsystem != CocoaSubsystem || info.Window == IntPtr.Zero)
				return;

			_wantsBest = sel_registerName("wantsBestResolutionOpenGLSurface");
			_setWantsBest = sel_registerName("setWantsBestResolutionOpenGLSurface:");
			_backingScale = sel_registerName("backingScaleFactor");
			_currentContext = sel_registerName("currentContext");
			_update = sel_registerName("update");
			_openGlContextClass = objc_getClass("NSOpenGLContext");
			_window = info.Window;
			_view = SendPointer(_window, sel_registerName("contentView"));
			Sync();
		}
		catch (Exception ex)
		{
			_view = IntPtr.Zero;
			Debug.Warn($"Retina rendering unavailable, the editor stays at window resolution: {ex.Message}");
		}
	}

	/// <summary>Restores the pixel back buffer, viewport and mouse scale that MonoGame resets to window points on every resize.</summary>
	internal static void Sync()
	{
		if (!Active)
			return;

		if (!GetBool(_view, _wantsBest))
		{
			SendBool(_view, _setWantsBest, true);
			var context = SendPointer(_openGlContextClass, _currentContext);
			if (context != IntPtr.Zero)
				SendPointer(context, _update);
		}

		var device = Core.GraphicsDevice;
		var client = Core.Instance.Window.ClientBounds;
		if (device == null || client.Width <= 0 || client.Height <= 0)
			return;

		Scale = (float)Math.Max(1, GetDouble(_window, _backingScale));
		Input.WindowPixelScale = Scale;
		var width = (int)MathF.Round(client.Width * Scale);
		var height = (int)MathF.Round(client.Height * Scale);
		var pp = device.PresentationParameters;
		if (pp.BackBufferWidth == width && pp.BackBufferHeight == height)
			return;

		var oldBounds = new Rectangle(0, 0, pp.BackBufferWidth, pp.BackBufferHeight);
		pp.BackBufferWidth = width;
		pp.BackBufferHeight = height;
		if (device.GetRenderTargets().Length > 0)
			return;

		if (device.ScissorRectangle == oldBounds)
			device.ScissorRectangle = new Rectangle(0, 0, width, height);
		device.Viewport = new Viewport(0, 0, width, height);
	}
}
