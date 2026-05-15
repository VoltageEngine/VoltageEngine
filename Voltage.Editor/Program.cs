using System;
using System.IO;
using Voltage.Utils;

namespace Voltage.Editor;

public class Program
{
	public static string[] CommandLineArgs { get; private set; }

	public static void Main(string[] args)
	{
		CommandLineArgs = args;

		// Catches unhandled exceptions and logs them to a file & Editor Debug Window
		AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
		System.Threading.Tasks.TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

		using var game = new Editor();
		game.Run();
	}

	private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
	{
		var ex = e.ExceptionObject as Exception;
		var message = ex != null
			? $"{ex.GetType().FullName}: {ex.Message}\n{ex.StackTrace}"
			: e.ExceptionObject?.ToString() ?? "Unknown error";

		var logMessage = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [FATAL] Unhandled exception " +
		                 $"(IsTerminating={e.IsTerminating}):\n{message}\n";

		// Crash Log File
		try
		{
			var logPath = Path.Combine(
				AppContext.BaseDirectory,
				$"crash_{DateTime.Now:yyyyMMdd_HHmmss}.log");
			File.WriteAllText(logPath, logMessage);
		}
		catch { }

		// Editor
		try
		{
			Debug.Error(logMessage);
		}
		catch { }
	}

	private static void OnUnobservedTaskException(
		object sender,
		System.Threading.Tasks.UnobservedTaskExceptionEventArgs e)
	{
		var message = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [ERROR] Unobserved task exception:\n" +
		              $"{e.Exception}\n";

		try { Debug.Error(message); }
		catch { }

		// Mark as observed so the process doesn't terminate.
		e.SetObserved();
	}
}