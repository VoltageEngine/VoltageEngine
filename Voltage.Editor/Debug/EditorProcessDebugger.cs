using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using Voltage.Utils;

namespace Voltage.Editor.EditorDebug
{
	/// <summary>
	/// Debugger for tracking editor processes and operations.
	/// Logs events only to dedicated text files per process type (no console output).
	/// Only active in DEBUG builds.
	/// </summary>
	public static class EditorProcessDebugger
	{
#if DEBUG
		private static readonly string DebugDirectory = Path.Combine(AppContext.BaseDirectory, "Content", "Voltage", "Debug");
		private static readonly object _lockObject = new();
		private static readonly Dictionary<string, List<DebugEntry>> _processLogs = new();

		/// <summary>
		/// Represents a single debug entry for a process.
		/// </summary>
		public class DebugEntry
		{
			public DateTime Timestamp { get; set; }
			public string Message { get; set; }
			public string Severity { get; set; } // Info, Warning, Error

			public DebugEntry(string message, string severity = "Info")
			{
				Timestamp = DateTime.Now;
				Message = message;
				Severity = severity;
			}

			/// <summary>
			/// Formats the entry as a plain text line.
			/// </summary>
			public string ToLogString()
			{
				return $"[{Timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{Severity.ToUpper()}] {Message}";
			}
		}
#endif

		static EditorProcessDebugger()
		{
#if DEBUG
			// Ensure the debug directory exists
			try
			{
				if (!Directory.Exists(DebugDirectory))
				{
					Directory.CreateDirectory(DebugDirectory);
				}
			}
			catch
			{
				// Silently fail if directory creation fails
			}
#endif
		}

		/// <summary>
		/// Gets the debug directory path (DEBUG only).
		/// </summary>
		public static string GetDebugDirectory()
		{
#if DEBUG
			return DebugDirectory;
#else 
			return string.Empty;
#endif
		}

#if DEBUG
		/// <summary>
		/// Gets the calling class name from the stack trace.
		/// </summary>
		private static string GetCallingClassName()
		{
			try
			{
				var stackTrace = new StackTrace();
				// Skip: GetCallingClassName (0), Log/LogInfo/etc (1), calling method (2)
				for (int i = 2; i < stackTrace.FrameCount; i++)
				{
					var frame = stackTrace.GetFrame(i);
					var method = frame?.GetMethod();
					var declaringType = method?.DeclaringType;
					
					// Skip EditorProcessDebugger itself
					if (declaringType != null && declaringType != typeof(EditorProcessDebugger))
					{
						return declaringType.Name;
					}
				}
			}
			catch
			{
				// Fallback if stack trace fails
			}
			
			return "Unknown";
		}
#endif

		/// <summary>
		/// Logs a message for a specific process type.
		/// Only active in DEBUG builds. Writes to text file only (no console output).
		/// </summary>
		/// <param name="text">The message to log</param>
		/// <param name="typeOfProcess">The process category. If null, uses the calling class name.</param>
		/// <param name="severity">Severity level: "Info", "Warning", or "Error"</param>
		[System.Diagnostics.Conditional("DEBUG")]
		public static void Log(string text, string typeOfProcess = null, string severity = "Info")
		{
#if DEBUG
			if (string.IsNullOrWhiteSpace(text))
			{
				return;
			}

			// Auto-detect calling class if typeOfProcess is not provided
			if (string.IsNullOrWhiteSpace(typeOfProcess))
			{
				typeOfProcess = GetCallingClassName();
			}

			lock (_lockObject)
			{
				// Add to in-memory log
				if (!_processLogs.ContainsKey(typeOfProcess))
				{
					_processLogs[typeOfProcess] = new List<DebugEntry>();
				}

				var entry = new DebugEntry(text, severity);
				_processLogs[typeOfProcess].Add(entry);

				// Write to text file only (no console output)
				WriteToFile(typeOfProcess);
			}
#endif
		}

		/// <summary>
		/// Logs an informational message.
		/// Only active in DEBUG builds. Writes to text file only (no console output).
		/// </summary>
		/// <param name="text">The message to log</param>
		/// <param name="typeOfProcess">The process category. If null, uses the calling class name.</param>
		[System.Diagnostics.Conditional("DEBUG")]
		public static void LogInfo(string text, string typeOfProcess = null)
		{
			Log(text, typeOfProcess, "Info");
		}

		/// <summary>
		/// Logs a warning message.
		/// Only active in DEBUG builds. Writes to text file only (no console output).
		/// </summary>
		/// <param name="text">The message to log</param>
		/// <param name="typeOfProcess">The process category. If null, uses the calling class name.</param>
		[System.Diagnostics.Conditional("DEBUG")]
		public static void LogWarning(string text, string typeOfProcess = null)
		{
			Log(text, typeOfProcess, "Warning");
		}

		/// <summary>
		/// Logs an error message.
		/// Only active in DEBUG builds. Writes to text file only (no console output).
		/// </summary>
		/// <param name="text">The message to log</param>
		/// <param name="typeOfProcess">The process category. If null, uses the calling class name.</param>
		[System.Diagnostics.Conditional("DEBUG")]
		public static void LogError(string text, string typeOfProcess = null)
		{
			Log(text, typeOfProcess, "Error");
		}

		/// <summary>
		/// Clears all logs for a specific process type.
		/// Only active in DEBUG builds.
		/// </summary>
		[System.Diagnostics.Conditional("DEBUG")]
		public static void ClearLogs(string typeOfProcess)
		{
#if DEBUG
			lock (_lockObject)
			{
				if (_processLogs.ContainsKey(typeOfProcess))
				{
					_processLogs[typeOfProcess].Clear();
					WriteToFile(typeOfProcess);
				}
			}
#endif
		}

		/// <summary>
		/// Clears all logs for all process types.
		/// Only active in DEBUG builds.
		/// </summary>
		[System.Diagnostics.Conditional("DEBUG")]
		public static void ClearAllLogs()
		{
#if DEBUG
			lock (_lockObject)
			{
				foreach (var processType in _processLogs.Keys.ToArray())
				{
					_processLogs[processType].Clear();
					WriteToFile(processType);
				}
			}
#endif
		}

		/// <summary>
		/// Gets all log entries for a specific process type.
		/// Only active in DEBUG builds. Returns empty list in RELEASE.
		/// </summary>
		public static List<DebugEntry> GetLogs(string typeOfProcess)
		{
#if DEBUG
			lock (_lockObject)
			{
				return _processLogs.TryGetValue(typeOfProcess, out var logs) 
					? new List<DebugEntry>(logs) 
					: new List<DebugEntry>();
			}
#else
			return new List<DebugEntry>();
#endif
		}

		/// <summary>
		/// Gets all process types that have been logged.
		/// Only active in DEBUG builds. Returns empty list in RELEASE.
		/// </summary>
		public static List<string> GetProcessTypes()
		{
#if DEBUG
			lock (_lockObject)
			{
				return new List<string>(_processLogs.Keys);
			}
#else
			return new List<string>();
#endif
		}

#if DEBUG
		/// <summary>
		/// Writes the log entries for a process type to its text file.
		/// </summary>
		private static void WriteToFile(string typeOfProcess)
		{
			try
			{
				var filePath = Path.Combine(DebugDirectory, $"{SanitizeFileName(typeOfProcess)}.txt");
				
				if (!_processLogs.TryGetValue(typeOfProcess, out var entries) || entries.Count == 0)
				{
					// Write empty file if no entries
					File.WriteAllText(filePath, $"=== {typeOfProcess} Debug Log ==={Environment.NewLine}No entries.{Environment.NewLine}", new UTF8Encoding(false));
					return;
				}

				// Limit entries to last 500 to prevent file bloat
				var entriesToWrite = entries.Count > 500 
					? entries.Skip(entries.Count - 500).ToList() 
					: entries;

				var sb = new StringBuilder();
				sb.AppendLine($"=== {typeOfProcess} Debug Log ===");
				sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
				sb.AppendLine($"Total Entries: {entriesToWrite.Count}");
				sb.AppendLine(new string('=', 80));
				sb.AppendLine();

				foreach (var entry in entriesToWrite)
				{
					sb.AppendLine(entry.ToLogString());
				}

				sb.AppendLine();
				sb.AppendLine(new string('=', 80));
				sb.AppendLine($"End of {typeOfProcess} Log");

				File.WriteAllText(filePath, sb.ToString(), new UTF8Encoding(false));
			}
			catch
			{
				// Silently fail if file write fails
			}
		}

		/// <summary>
		/// Sanitizes a process type name to be a valid filename.
		/// </summary>
		private static string SanitizeFileName(string fileName)
		{
			var invalidChars = Path.GetInvalidFileNameChars();
			var sanitized = new StringBuilder(fileName.Length);

			foreach (var c in fileName)
			{
				sanitized.Append(Array.IndexOf(invalidChars, c) >= 0 ? '_' : c);
			}

			return sanitized.ToString();
		}
#endif

		/// <summary>
		/// Exports all logs to a single consolidated text file.
		/// Only active in DEBUG builds.
		/// </summary>
		[System.Diagnostics.Conditional("DEBUG")]
		public static void ExportAllLogs(string outputPath = null)
		{
#if DEBUG
			lock (_lockObject)
			{
				try
				{
					outputPath ??= Path.Combine(DebugDirectory, $"EditorDebugLog_{DateTime.Now:yyyyMMdd_HHmmss}.txt");

					var sb = new StringBuilder();
					sb.AppendLine("=== Voltage Editor Debug Log Export ===");
					sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
					sb.AppendLine($"Total Process Types: {_processLogs.Count}");
					sb.AppendLine(new string('=', 80));
					sb.AppendLine();

					foreach (var kvp in _processLogs.OrderBy(k => k.Key))
					{
						var processType = kvp.Key;
						var entries = kvp.Value;

						sb.AppendLine();
						sb.AppendLine($"--- Process: {processType} ---");
						sb.AppendLine($"Entries: {entries.Count}");
						sb.AppendLine(new string('-', 80));

						foreach (var entry in entries)
						{
							sb.AppendLine(entry.ToLogString());
						}

						sb.AppendLine(new string('-', 80));
					}

					sb.AppendLine();
					sb.AppendLine(new string('=', 80));
					sb.AppendLine("=== End of Export ===");

					File.WriteAllText(outputPath, sb.ToString(), new UTF8Encoding(false));
				}
				catch
				{
					// Silently fail if export fails
				}
			}
#endif
		}
	}

#if !DEBUG
	/// <summary>
	/// Dummy DebugEntry class for RELEASE builds to prevent compilation errors.
	/// </summary>
	public class DebugEntry
	{
		public DateTime Timestamp { get; set; }
		public string Message { get; set; }
		public string Severity { get; set; }
	}
#endif
}