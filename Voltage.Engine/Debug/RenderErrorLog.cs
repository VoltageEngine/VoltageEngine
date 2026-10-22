using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Voltage;

/// <summary>
/// Render errors the game survived, written in every build to render-errors.log in the game's config folder (the
/// per-user folder named after the game: Roaming AppData on Windows, ~/.config on Linux and the Steam Deck). Each distinct
/// error is written once with its stack and the GL driver, then only its running count at 10, 100, 1000..., so a draw
/// that fails every frame cannot grow the file every frame.
/// </summary>
public static class RenderErrorLog
{
	public static string Path = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
		Assembly.GetEntryAssembly()?.GetName().Name ?? "Voltage", "render-errors.log");

	private static readonly Dictionary<string, long> _counts = new();

	/// <summary>Counts <paramref name="error"/> and writes it when it is new or its count reaches a power of ten.</summary>
	/// <returns>True the first time this error is seen.</returns>
	public static bool Report(Exception error, string source)
	{
		var stack = error.StackTrace ?? "";
		int end = stack.IndexOf('\n');
		string key = source + ": " + error.GetType().FullName + ": " + error.Message + " @ " + (end < 0 ? stack : stack.Substring(0, end)).Trim();
		_counts.TryGetValue(key, out long count);
		_counts[key] = ++count;
		if (count == 1) Write($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {source} (GL: {GlDriver()})\n{error}\n");
		else if (IsPowerOfTen(count)) Write($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} x{count} {key}\n");
		return count == 1;
	}

	private static bool IsPowerOfTen(long n)
	{
		while (n >= 10 && n % 10 == 0) n /= 10;
		return n == 1;
	}

	private static void Write(string text)
	{
		try
		{
			Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path));
			File.AppendAllText(Path, text);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
	}

	[DllImport("SDL2", EntryPoint = "SDL_GL_GetProcAddress", CallingConvention = CallingConvention.Cdecl)]
	private static extern IntPtr GetProcAddress([MarshalAs(UnmanagedType.LPStr)] string name);

	/// <summary>GL_VENDOR / GL_RENDERER / GL_VERSION of the current context; empty when there is no GL.</summary>
	private static unsafe string GlDriver()
	{
		try
		{
			var getString = (delegate* unmanaged<uint, IntPtr>)GetProcAddress("glGetString");
			if (getString == null) return "";
			return string.Join(" | ", Marshal.PtrToStringAnsi(getString(0x1F00)), Marshal.PtrToStringAnsi(getString(0x1F01)),
				Marshal.PtrToStringAnsi(getString(0x1F02)));
		}
		catch (Exception) { return ""; }
	}
}
