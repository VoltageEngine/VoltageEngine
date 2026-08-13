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

	/// <summary>
	/// Everything the plugin system has to say, kept where the user is already looking: the Plugin
	/// Manager's Messages panel.
	///
	/// <para>This exists because the subsystem used to log through <c>EditorDebug</c>, which is
	/// <c>[Conditional("EDITOR_DEBUG")]</c> - a symbol the shipped Editor-Release build does not define.
	/// Every explanation of why a plugin failed to resolve, load or install was therefore compiled out of
	/// the build people actually run, which is how a failed install came to look like a button that did
	/// nothing. Messages here are always compiled, and mirrored to the console for anyone running from a
	/// terminal.</para>
	/// </summary>
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

			// Mirrored so a terminal launch still shows it, and so it survives the editor closing.
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
