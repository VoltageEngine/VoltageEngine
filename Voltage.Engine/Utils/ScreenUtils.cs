using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Voltage.UI;
using Voltage.Utils;
using Voltage;

public class ScreenUtils
{
	public enum ScreenMode
	{
		FullScreen, // True hardware fullscreen
		Borderless, // Borderless fullscreen
		WindowedMax, // Window mode that fills the entire screen, with account for the taskbar under it 
		Windowed 
	}

	/// <summary>
	/// The offset from the bottom part of the screen that the application must have
	/// </summary>
	public static int TaskBarBottomOffset = 32 * 2;

	public static ScreenMode Mode { get; private set; }
	public static int TitleBarHeight = 32;


	public static void ApplyScreenChange(ScreenMode mode)
	{
		Mode = mode;

		if (Mode == ScreenMode.FullScreen)
		{
			SetFullScreenMode();
		}
		else if (Mode == ScreenMode.Borderless)
		{
			SetBorderlessMode();
		}
		else if (Mode == ScreenMode.WindowedMax)
		{
			SetWindowedMode(true);
		}
		else
		{
			SetWindowedMode(true);
		}
	}

	private static void SetFullScreenMode()
    {
        Core.Instance.Window.Position = Point.Zero;
		Core.Instance.Window.IsBorderless = false;
        Screen.PreferredBackBufferWidth = Screen.ActualMonitorWidth;
        Screen.PreferredBackBufferHeight = Screen.ActualMonitorHeight;
        Screen.IsFullscreen = true;
		Screen.ApplyChanges();
	}

    private static void SetBorderlessMode()
    {
	    Core.Instance.Window.Position = Point.Zero;
		Screen.IsFullscreen = false;
	    Core.Instance.Window.IsBorderless = true;
	    Screen.SetSize(Screen.ActualMonitorWidth, Screen.ActualMonitorHeight);
	    Screen.ApplyChanges();
	}

	/// <summary>
	/// Used for the game window, not the editor window.
	/// </summary>
	private static void SetWindowedMode(bool maximizedVersion)
    {
		if (maximizedVersion)
	    {
			int maxWidth = Screen.MonitorWidth;
		    int maxHeight = Screen.MonitorHeight;
		    
		    Core.Instance.Window.Position = new Point(0, TitleBarHeight);
		    Core.Instance.Window.IsBorderless = false;
		    Screen.IsFullscreen = false;
		    Screen.SetSize(maxWidth, maxHeight - TitleBarHeight * 2);
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
}