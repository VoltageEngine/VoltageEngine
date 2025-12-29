using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Voltage.Editor.DebugUtils
{
	/// <summary>
	/// Debug logging system specifically for editor development.
	/// Only available in DEBUG builds of the editor.
	/// Not accessible to game projects.
	/// </summary>
	internal static class EditorDebug
	{
		[Conditional("EDITOR_DEBUG")]
		public static void Error(
			string format,
			[CallerFilePath] string callerFile = "",
			[CallerLineNumber] int callerLine = 0,
			params object[] args)
		{
			Voltage.Debug.Error($"[EDITOR] {format}", callerFile, callerLine, args);
		}

		[Conditional("EDITOR_DEBUG")]
		public static void Warn(
			string format,
			[CallerFilePath] string callerFile = "",
			[CallerLineNumber] int callerLine = 0,
			params object[] args)
		{
			Voltage.Debug.Warn($"[EDITOR] {format}", callerFile, callerLine, args);
		}

		[Conditional("EDITOR_DEBUG")]
		public static void Log(
			string format,
			[CallerFilePath] string callerFile = "",
			[CallerLineNumber] int callerLine = 0,
			params object[] args)
		{
			Voltage.Debug.Log($"[EDITOR] {format}", callerFile, callerLine, args);
		}

		[Conditional("EDITOR_DEBUG")]
		public static void Info(
			string format,
			[CallerFilePath] string callerFile = "",
			[CallerLineNumber] int callerLine = 0,
			params object[] args)
		{
			Voltage.Debug.Info($"[EDITOR] {format}", callerFile, callerLine, args);
		}

		[Conditional("EDITOR_DEBUG")]
		public static void Success(
			string format,
			[CallerFilePath] string callerFile = "",
			[CallerLineNumber] int callerLine = 0,
			params object[] args)
		{
			Voltage.Debug.Success($"[EDITOR] {format}", callerFile, callerLine, args);
		}

		[Conditional("EDITOR_DEBUG")]
		public static void Trace(
			string format,
			[CallerFilePath] string callerFile = "",
			[CallerLineNumber] int callerLine = 0,
			params object[] args)
		{
			Voltage.Debug.Trace($"[EDITOR] {format}", callerFile, callerLine, args);
		}

		/// <summary>
		/// Editor-specific: Log with custom category for filtering
		/// </summary>
		[Conditional("EDITOR_DEBUG")]
		public static void LogCategory(
			string category,
			string format,
			[CallerFilePath] string callerFile = "",
			[CallerLineNumber] int callerLine = 0,
			params object[] args)
		{
			Voltage.Debug.Log($"[{category}] {format}", callerFile, callerLine, args);
		}
	}
}

