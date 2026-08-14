using System;
using System.Collections.Generic;
using Voltage.Editor.DebugUtils;

namespace Voltage.Editor.Plugins
{
	public enum PluginLogLevel
	{
		Info,
		Warning,
		Error,
	}

	public sealed class PluginLogEntry
	{
		public int Id;
		public PluginLogLevel Level;
		public string Text;

		public bool IsError => Level == PluginLogLevel.Error;
	}

	/// <summary>The plugin system's messages, shown in the Plugin Manager. Always compiled, unlike EditorDebug, which is conditional on EDITOR_DEBUG and so was absent from release builds.</summary>
	public static class PluginLog
	{
		/// <summary>Enough to cover a burst of install/restore output; beyond that the oldest go.</summary>
		private const int MaxEntries = 200;

		private static readonly object _lock = new();
		private static readonly List<PluginLogEntry> _entries = new();
		private static int _nextId;

		/// <summary>Snapshot safe to enumerate while a worker thread is logging.</summary>
		public static IReadOnlyList<PluginLogEntry> Entries
		{
			get
			{
				lock (_lock)
					return _entries.ToArray();
			}
		}

		public static int Count
		{
			get
			{
				lock (_lock)
					return _entries.Count;
			}
		}

		public static void Log(string message) => Add(PluginLogLevel.Info, message);

		public static void Warn(string message) => Add(PluginLogLevel.Warning, message);

		public static void Error(string message) => Add(PluginLogLevel.Error, message);

		public static void Clear()
		{
			lock (_lock)
				_entries.Clear();
		}

		public static void Remove(int id)
		{
			lock (_lock)
				_entries.RemoveAll(e => e.Id == id);
		}

		private static void Add(PluginLogLevel level, string message)
		{
			if (string.IsNullOrWhiteSpace(message))
				return;

			lock (_lock)
			{
				_entries.Add(new PluginLogEntry { Id = _nextId++, Level = level, Text = message });

				if (_entries.Count > MaxEntries)
					_entries.RemoveRange(0, _entries.Count - MaxEntries);
			}

			switch (level)
			{
				case PluginLogLevel.Error:
					Debug.Error($"[Plugins] {message}");
					break;
				case PluginLogLevel.Warning:
					Debug.Warn($"[Plugins] {message}");
					break;
				default:
					EditorDebug.Log($"[Plugins] {message}");
					break;
			}
		}
	}
}
