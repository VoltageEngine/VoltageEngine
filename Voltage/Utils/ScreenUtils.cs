using System;
using Voltage;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Voltage.UI;
using Voltage.Utils;

public class ScreenUtils
{
	public static bool IsFullscreen => _isFullscreen;
	public static bool IsBorderless => _isBorderless;
	private static bool _isFullscreen = false;
    private static bool _isBorderless = false;
    private static int _width = 0;
    private static int _height = 0;

    public static void SetFullScreenMode()
    {
        _isFullscreen = true;
        _isBorderless = false;
        Core.Instance.Window.Position = Point.Zero;
        ApplyFullscreenChange(false);
    }

	/// <summary>
	/// Used for the game window, not the editor window.
	/// </summary>
	public static void SetWindowedMode()
    {
        _isFullscreen = false;
        _isBorderless = false;

#if DEBUG
		int defaultWidth = 1920;
		int defaultHeight = 1080;
		Core.Instance.Window.Position = new Point(Screen.MonitorWidth / 6, Screen.MonitorHeight / 6);
		Screen.SetSize(defaultWidth, defaultHeight);
		Core.Instance.Window.IsBorderless = false;
		Screen.IsFullscreen = false;
		Screen.ApplyChanges();
		return;
#endif
		Core.Instance.Window.Position = new Point(Screen.MonitorWidth / 4, Screen.MonitorHeight / 4);
		ApplyFullscreenChange(true);
    }

	public static void SetBorderlessMode()
	{
		_isFullscreen = false;
		_isBorderless = true;
		Core.Instance.Window.Position = new Point(Screen.MonitorWidth / 4, Screen.MonitorHeight / 4);
		ApplyFullscreenChange(true);
	}

    private static void ApplyFullscreenChange(bool oldIsFullscreen)
    {
        if (_isFullscreen)
        {
            // Fullscreen mode
            if (_isBorderless)
            {
                // Borderless fullscreen
                Screen.IsFullscreen = false;
                Core.Instance.Window.IsBorderless = true;
                Screen.SetSize(Screen.MonitorWidth, Screen.MonitorHeight);
                Screen.ApplyChanges();
            }
            else
            {
                // True hardware fullscreen
                Screen.IsFullscreen = true;
                Core.Instance.Window.IsBorderless = false;
                Screen.PreferredBackBufferWidth = GraphicsAdapter.DefaultAdapter.CurrentDisplayMode.Width;
                Screen.PreferredBackBufferHeight = GraphicsAdapter.DefaultAdapter.CurrentDisplayMode.Height;
                Screen.ApplyChanges();
            }
        }
        else
        {
            // Windowed mode
            Screen.IsFullscreen = false;
            Core.Instance.Window.IsBorderless = false;
#if DEBUG
	        Core.Instance.Window.Position = new Point(0, 0);
	        Screen.SetSize(Screen.MonitorWidth, Screen.MonitorHeight);
#else
            Screen.SetSize(_width > 0 ? _width : 1280, _height > 0 ? _height : 720);
#endif
			Screen.ApplyChanges();
        }
    }
}