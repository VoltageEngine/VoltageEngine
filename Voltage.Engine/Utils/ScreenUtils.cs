using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Voltage.UI;
using Voltage.Utils;
using Voltage;

public class ScreenUtils
{
	public static bool IsFullscreen => _isFullscreen;
	public static bool IsBorderless => _isBorderless;
	private static bool _isFullscreen = false;
    private static bool _isBorderless = false;

    public static void SetFullScreenMode()
    {
        _isFullscreen = true;
        _isBorderless = false;
        Core.Instance.Window.Position = Point.Zero;
        ApplyFullscreenChange();
    }

	/// <summary>
	/// Used for the game window, not the editor window.
	/// </summary>
	public static void SetEditorWindowedMode(bool maximizedVersion)
    {
        _isFullscreen = false;
        _isBorderless = false;

	    if (maximizedVersion)
	    {
		    int titleBarHeight = 32;
		    int topBorder = 1;
		    int taskbarHeight = 48;
		    
		    int maxWidth = Screen.MonitorWidth;
		    int maxHeight = Screen.MonitorHeight;
		    
		    Core.Instance.Window.Position = new Point(0, titleBarHeight);
		    Core.Instance.Window.IsBorderless = false;
		    Screen.IsFullscreen = false;
		    Screen.SetSize(maxWidth, maxHeight - titleBarHeight * 2);
		}
	    else
	    {
			int posX = Screen.MonitorWidth / 2;
			int posY = Screen.MonitorHeight / 2;

			Core.Instance.Window.Position = new Point(posX, posY);
		    Core.Instance.Window.IsBorderless = false;
		    Screen.IsFullscreen = false;
		    Screen.SetSize(Screen.MonitorWidth / 4, Screen.MonitorHeight / 4);
		}

		Screen.ApplyChanges();
    }

	public static void SetGameWindowedMode(bool maximizedVersion)
	{
		_isFullscreen = false;
		_isBorderless = false;

		Core.Instance.Window.Position = new Point(Screen.MonitorWidth / 4, Screen.MonitorHeight / 4);
		ApplyFullscreenChange();
	}

	public static void SetBorderlessMode()
	{
		_isFullscreen = false;
		_isBorderless = true;
		Core.Instance.Window.Position = new Point(Screen.ActualMonitorWidth / 4, Screen.ActualMonitorHeight / 4);
		ApplyFullscreenChange();
	}

    private static void ApplyFullscreenChange()
    {
        if (_isFullscreen)
        {
            // Fullscreen mode
            if (_isBorderless)
            {
                // Borderless fullscreen
                Screen.IsFullscreen = false;
                Core.Instance.Window.IsBorderless = true;
                Screen.SetSize(Screen.ActualMonitorWidth, Screen.ActualMonitorHeight);
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
	        Core.Instance.Window.Position = new Point(0, 0);
	        Screen.SetSize(Screen.ActualMonitorWidth, Screen.ActualMonitorHeight);
			Screen.ApplyChanges();
        }
    }
}