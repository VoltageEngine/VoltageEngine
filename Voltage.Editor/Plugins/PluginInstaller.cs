using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Voltage.Editor.DebugUtils;

namespace Voltage.Editor.Plugins
{
	public enum PluginInstallState
	{
		Downloading,

		/// <summary>Downloaded; extracting and hashing.</summary>
		Working,

		/// <summary>Fetched and cached, waiting for the UI thread to record and load it.</summary>
		ReadyToApply,

		Succeeded,
		Failed,
		Cancelled,
	}

	/// <summary>
	/// Ambient progress sink and cancellation for whatever install is running on this call stack. The
	/// resolver publishes here, so the download reports progress without every call site between the
	/// Plugin Manager and <c>ResolveZip</c> having to pass a reporter through.
	/// </summary>
	internal static class PluginDownloadContext
	{
		private static readonly AsyncLocal<PluginInstallJob> _current = new();

		internal static PluginInstallJob Current
		{
			get => _current.Value;
			set => _current.Value = value;
		}

		internal static CancellationToken Token => Current?.Token ?? CancellationToken.None;

		internal static void Report(long bytesRead, long? totalBytes) => Current?.ReportDownload(bytesRead, totalBytes);

		internal static void EnterWorking() => Current?.EnterWorking();
	}

	/// <summary>One in-flight install, safe to read from the UI thread while it runs on another.</summary>
	public class PluginInstallJob
	{
		/// <summary>
		/// No bytes for this long and the download is treated as stalled. A server that accepts the
		/// connection and then never sends counts as hung, and HttpClient's own timeout would not fire for
		/// minutes.
		/// </summary>
		private const int StallSeconds = 20;

		private readonly CancellationTokenSource _cts = new();
		private long _bytesRead;
		private long _totalBytes;
		private long _lastProgressTicks;
		private volatile PluginInstallState _state = PluginInstallState.Downloading;
		private volatile string _message;

		internal PluginInstallJob(string pluginId, string displayName)
		{
			PluginId = pluginId;
			DisplayName = displayName;
			Interlocked.Exchange(ref _lastProgressTicks, DateTime.UtcNow.Ticks);
		}

		public string PluginId { get; }
		public string DisplayName { get; }

		public PluginInstallState State => _state;
		public string Message => _message;

		public long BytesRead => Interlocked.Read(ref _bytesRead);
		public long TotalBytes => Interlocked.Read(ref _totalBytes);

		internal CancellationToken Token => _cts.Token;

		public bool IsFinished =>
			_state is PluginInstallState.Succeeded or PluginInstallState.Failed or PluginInstallState.Cancelled;

		/// <summary>Set by the worker; consumed by the UI thread in <see cref="PluginInstaller.Pump"/>.</summary>
		internal ResolvedPlugin Resolved;

		internal ProjectPluginEntry Entry;

		/// <summary>Re-points an entry already in plugins.json rather than adding a new one.</summary>
		internal bool IsUpdate;

		/// <summary>0..1, or -1 when the server sent no Content-Length and the total is unknown.</summary>
		public float Progress
		{
			get
			{
				var total = TotalBytes;
				return total > 0 ? Math.Clamp((float)BytesRead / total, 0f, 1f) : -1f;
			}
		}

		/// <summary>
		/// Nothing received for a while. Not an error on its own - a slow server recovers - so it is
		/// surfaced as a prompt to cancel rather than a failure.
		/// </summary>
		public bool Stalled
		{
			get
			{
				if (_state != PluginInstallState.Downloading)
					return false;

				var since = new DateTime(Interlocked.Read(ref _lastProgressTicks), DateTimeKind.Utc);
				return (DateTime.UtcNow - since).TotalSeconds > StallSeconds;
			}
		}

		public int SecondsSinceProgress =>
			(int)(DateTime.UtcNow - new DateTime(Interlocked.Read(ref _lastProgressTicks), DateTimeKind.Utc)).TotalSeconds;

		public void Cancel()
		{
			if (!IsFinished)
				_cts.Cancel();
		}

		internal void ReportDownload(long bytesRead, long? totalBytes)
		{
			Interlocked.Exchange(ref _bytesRead, bytesRead);
			if (totalBytes.HasValue)
				Interlocked.Exchange(ref _totalBytes, totalBytes.Value);

			Interlocked.Exchange(ref _lastProgressTicks, DateTime.UtcNow.Ticks);
		}

		internal void EnterWorking()
		{
			_state = PluginInstallState.Working;
			Interlocked.Exchange(ref _lastProgressTicks, DateTime.UtcNow.Ticks);
		}

		internal void EnterReadyToApply()
		{
			_state = PluginInstallState.ReadyToApply;
			Interlocked.Exchange(ref _lastProgressTicks, DateTime.UtcNow.Ticks);
		}

		internal void Finish(PluginInstallState state, string message)
		{
			_message = message;
			_state = state;
		}
	}

	/// <summary>
	/// Runs plugin installs off the UI thread.
	///
	/// <para>Installing used to call straight into <c>AddPlugin</c> from the draw call, so the editor froze
	/// for the length of the download with no way to see progress or give up.</para>
	/// </summary>
	public static class PluginInstaller
	{
		private static readonly List<PluginInstallJob> _jobs = new();
		private static readonly object _lock = new();
		private static bool _sweptStaging;

		/// <summary>Snapshot safe to enumerate while a job is running.</summary>
		public static IReadOnlyList<PluginInstallJob> Jobs
		{
			get
			{
				lock (_lock)
					return _jobs.ToArray();
			}
		}

		public static bool IsBusy
		{
			get
			{
				lock (_lock)
					return _jobs.Exists(j => !j.IsFinished);
			}
		}

		/// <summary>
		/// Fetches a plugin on a worker and applies it on the next <see cref="Pump"/>. With
		/// <paramref name="isUpdate"/> the entry is expected to be in plugins.json already and its source
		/// is re-pointed instead of appended - the download itself is identical either way, which is why
		/// updates go through here rather than blocking the UI thread for the length of a fetch.
		/// </summary>
		public static PluginInstallJob Start(ProjectPluginEntry entry, string displayName, bool isUpdate = false)
		{
			if (entry == null)
				return null;

			SweepStaleStagingOnce();

			var job = new PluginInstallJob(entry.Id ?? displayName, displayName);
			lock (_lock)
			{
				// One at a time: installs mutate plugins.json and load assemblies, and two of those
				// interleaving is not worth the trouble it would cause.
				if (_jobs.Exists(j => !j.IsFinished))
					return null;

				_jobs.Add(job);
			}

			job.Entry = entry;
			job.IsUpdate = isUpdate;

			Task.Run(() =>
			{
				PluginDownloadContext.Current = job;
				try
				{
					// Only the fetch happens here. Recording and loading the plugin runs on the UI thread
					// in Pump, because loading an editor plugin calls its Initialize, which registers
					// windows and menu items that the UI is enumerating at the same time.
					job.Resolved = PluginManager.Instance.ResolveForAdd(entry);
					job.EnterReadyToApply();
				}
				catch (OperationCanceledException)
				{
					job.Finish(PluginInstallState.Cancelled, "Install cancelled.");
				}
				catch (Exception ex)
				{
					job.Finish(PluginInstallState.Failed, ex.Message);
					EditorDebug.Warn($"Install of '{job.PluginId}' failed: {ex.Message}", "Plugins");
				}
				finally
				{
					PluginDownloadContext.Current = null;
				}
			});

			return job;
		}

		/// <summary>
		/// Call once per frame from the UI thread. Finishes any install whose download has completed, on
		/// this thread, so assembly loading and window registration never race the draw.
		/// </summary>
		public static void Pump()
		{
			PluginInstallJob ready = null;
			lock (_lock)
			{
				foreach (var job in _jobs)
				{
					if (job.State == PluginInstallState.ReadyToApply)
					{
						ready = job;
						break;
					}
				}
			}

			if (ready == null)
				return;

			try
			{
				// Both halves report their outcome in the message they return, so the state is read back
				// off its prefix rather than duplicating the classification here.
				var result = ready.IsUpdate
					? PluginManager.Instance.CompleteUpdate(ready.Entry, ready.Resolved)
					: PluginManager.Instance.CompleteAdd(ready.Entry, ready.Resolved);

				var ok = result != null && (ready.IsUpdate
					? !result.StartsWith("Update failed", StringComparison.Ordinal)
					: result.StartsWith("Added", StringComparison.Ordinal));

				ready.Finish(ok ? PluginInstallState.Succeeded : PluginInstallState.Failed, result);
			}
			catch (Exception ex)
			{
				ready.Finish(PluginInstallState.Failed, ex.Message);
				EditorDebug.Warn($"Install of '{ready.PluginId}' failed while applying: {ex.Message}", "Plugins");
			}
			finally
			{
				ready.Resolved = null;
			}
		}

		public static void Dismiss(PluginInstallJob job)
		{
			if (job == null)
				return;

			lock (_lock)
				_jobs.Remove(job);
		}

		/// <summary>
		/// Deletes staging left behind by an install that never finished - the machine was shut down, the
		/// editor was killed. Those directories are never reused, so anything older than an hour is
		/// abandoned by definition.
		/// </summary>
		public static void SweepStaleStagingOnce()
		{
			if (_sweptStaging)
				return;

			_sweptStaging = true;

			try
			{
				var root = Path.Combine(Path.GetTempPath(), "VoltagePluginStaging");
				if (!Directory.Exists(root))
					return;

				var cutoff = DateTime.UtcNow.AddHours(-1);
				var removed = 0;

				foreach (var dir in Directory.GetDirectories(root))
				{
					if (Directory.GetLastWriteTimeUtc(dir) > cutoff)
						continue;

					try
					{
						Directory.Delete(dir, recursive: true);
						removed++;
					}
					catch
					{
						// Still locked by another editor instance; it will be swept next time.
					}
				}

				// A partial download leaves "<staging>.zip" beside the directory.
				foreach (var file in Directory.GetFiles(root, "*.zip"))
				{
					if (File.GetLastWriteTimeUtc(file) > cutoff)
						continue;

					try
					{
						File.Delete(file);
						removed++;
					}
					catch
					{
						// As above.
					}
				}

				if (removed > 0)
					EditorDebug.Log($"Cleaned {removed} abandoned plugin download(s) from {root}.", "Plugins");
			}
			catch (Exception ex)
			{
				EditorDebug.Warn($"Could not sweep plugin staging: {ex.Message}", "Plugins");
			}
		}
	}
}
