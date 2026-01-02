using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Voltage.Editor.DebugUtils
{
	/// <summary>
	/// Debug logging system specifically for editor development.
	/// Only available in EDITOR_DEBUG configuration of the editor.
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
			Debug.Error($"[EDITOR] {format}", callerFile, callerLine, args);
		}

		[Conditional("EDITOR_DEBUG")]
		public static void Warn(
			string format,
			[CallerFilePath] string callerFile = "",
			[CallerLineNumber] int callerLine = 0,
			params object[] args)
		{
			Debug.Warn($"[EDITOR] {format}", callerFile, callerLine, args);
		}

		[Conditional("EDITOR_DEBUG")]
		public static void Log(
			string format,
			[CallerFilePath] string callerFile = "",
			[CallerLineNumber] int callerLine = 0,
			params object[] args)
		{
			Debug.Log($"[EDITOR] {format}", callerFile, callerLine, args);
		}

		[Conditional("EDITOR_DEBUG")]
		public static void Info(
			string format,
			[CallerFilePath] string callerFile = "",
			[CallerLineNumber] int callerLine = 0,
			params object[] args)
		{
			Debug.Info($"[EDITOR] {format}", callerFile, callerLine, args);
		}

		[Conditional("EDITOR_DEBUG")]
		public static void Success(
			string format,
			[CallerFilePath] string callerFile = "",
			[CallerLineNumber] int callerLine = 0,
			params object[] args)
		{
			Debug.Success($"[EDITOR] {format}", callerFile, callerLine, args);
		}

		[Conditional("EDITOR_DEBUG")]
		public static void Trace(
			string format,
			[CallerFilePath] string callerFile = "",
			[CallerLineNumber] int callerLine = 0,
			params object[] args)
		{
			Debug.Trace($"[EDITOR] {format}", callerFile, callerLine, args);
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
			Debug.Log($"[{category}] {format}", callerFile, callerLine, args);
		}
	}
}

