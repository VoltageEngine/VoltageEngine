using ImGuiNET;
using Voltage.Audio;
using Num = System.Numerics;

namespace Voltage.Editor.Windows
{
	/// <summary>
	/// Live read-only view of the software audio mixer's profiling counters (mix time vs. budget, active voices,
	/// underruns). Only meaningful while the software mixing backend is active; otherwise shows a hint.
	/// </summary>
	public class AudioProfilerWindow
	{
		public bool IsOpen;

		private static readonly Num.Vector4 Muted = new(0.6f, 0.6f, 0.6f, 1f);
		private static readonly Num.Vector4 MutedGreen = new(0.4f, 0.7f, 0.45f, 1f);
		private static readonly Num.Vector4 Green = new(0.3f, 1f, 0.4f, 1f);
		private static readonly Num.Vector4 Amber = new(1f, 0.8f, 0.2f, 1f);
		private static readonly Num.Vector4 Red = new(1f, 0.3f, 0.3f, 1f);

		public void Draw()
		{
			if (!IsOpen)
				return;

			ImGui.SetNextWindowSize(new Num.Vector2(360, 220), ImGuiCond.FirstUseEver);
			if (!ImGui.Begin("Audio Profiler ###AudioProfiler", ref IsOpen))
			{
				ImGui.End();
				return;
			}

			if (Core.Audio == null || !Core.Audio.TryGetSoftwareAudioStats(out var stats))
			{
				ImGui.TextColored(Muted, "Software mixing backend not active. Enable");
				ImGui.TextColored(Muted, "AudioManager.PreferSoftwareBackend (see Voltage.Editor/Program.cs)");
				ImGui.TextColored(Muted, "to profile DSP.");
				ImGui.End();
				return;
			}

			var load01 = stats.LoadPercent / 100f;
			var barColor = stats.LoadPercent < 50f ? Green : stats.LoadPercent <= 80f ? Amber : Red;
			ImGui.PushStyleColor(ImGuiCol.PlotHistogram, barColor);
			ImGui.ProgressBar(load01, new Num.Vector2(-1f, 0f), $"{stats.LoadPercent:0}% of budget");
			ImGui.PopStyleColor();

			ImGui.Spacing();

			ImGui.Text($"mix {stats.MixAvgMs:0.00} ms / {stats.BudgetMs:0.0} ms  (peak {stats.MixPeakMs:0.00} ms)");
			ImGui.Text($"Active voices: {stats.ActiveVoices}");
			ImGui.TextColored(stats.Underruns > 0 ? Red : MutedGreen, $"Underruns: {stats.Underruns}");

			ImGui.Spacing();
			ImGui.PushStyleColor(ImGuiCol.Text, Muted);
			ImGui.TextWrapped("Underruns > 0 = audible glitches. Sustained load > ~50% risks them.");
			ImGui.PopStyleColor();

			ImGui.End();
		}
	}
}
