using System;
using System.Diagnostics;

namespace Voltage.Editor.Utils
{
	/// <summary>
	/// Progress state for a project load, reported by the steps doing the work and drawn by
	/// <see cref="ImGuiCore.ImGuiManager"/>. The load runs on the UI thread, so it drives the frame
	/// itself through <see cref="FramePump"/> rather than waiting for the next one.
	/// </summary>
	public static class ProjectLoadProgress
	{
		/// <summary>Renders and presents one overlay frame. Installed by the editor at startup.</summary>
		public static Action FramePump;

		public static bool IsActive { get; private set; }
		public static string Title { get; private set; }
		public static string Phase { get; private set; }
		public static string Detail { get; private set; }
		public static int PhaseIndex { get; private set; }
		public static int PhaseCount { get; private set; }

		private static readonly Stopwatch _sinceLastFrame = new();
		private const int MinFrameIntervalMs = 40;

		public static IDisposable Begin(string title, int phaseCount)
		{
			Title = title;
			PhaseCount = Math.Max(1, phaseCount);
			PhaseIndex = 0;
			Phase = "Starting";
			Detail = null;
			IsActive = true;
			_sinceLastFrame.Restart();
			Pump(force: true);
			return new Scope();
		}

		/// <summary>Moves to the next phase. Always draws, so no phase can flash past unseen.</summary>
		public static void BeginPhase(string phase)
		{
			if (!IsActive)
				return;

			PhaseIndex++;
			Phase = phase;
			Detail = null;
			Pump(force: true);
		}

		/// <summary>
		/// The item currently being processed - a file, a plugin id. Throttled: a per-file redraw
		/// would cost more than the work being reported on.
		/// </summary>
		public static void Report(string detail)
		{
			if (!IsActive)
				return;

			Detail = detail;
			Pump(force: false);
		}

		public static void End()
		{
			IsActive = false;
			Title = Phase = Detail = null;
			_sinceLastFrame.Reset();
		}

		private static void Pump(bool force)
		{
			if (FramePump == null)
				return;

			if (!force && _sinceLastFrame.IsRunning && _sinceLastFrame.ElapsedMilliseconds < MinFrameIntervalMs)
				return;

			_sinceLastFrame.Restart();

			try
			{
				FramePump();
			}
			catch
			{
				// A load must never fail because its progress bar could not draw.
			}
		}

		private sealed class Scope : IDisposable
		{
			public void Dispose() => End();
		}
	}
}
