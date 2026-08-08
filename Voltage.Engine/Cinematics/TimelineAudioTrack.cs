using System.Collections.Generic;
using Voltage.Serialization;

namespace Voltage.Cinematics
{
	/// <summary>One sound fired at a point on the timeline.</summary>
	public class TimelineAudioClip
	{
		public float Time;

		[AssetType(".wav", ".ogg", ".mp3")]
		public AssetReference Clip;

		public float Volume = 1f;

		/// <summary>Semitone-style offset passed to the mixer; 0 = unmodified.</summary>
		public float Pitch;

		/// <summary>-1 (left) to 1 (right).</summary>
		public float Pan;

		/// <summary>Mixer bus, e.g. "SFX", "Music", "Dialogue".</summary>
		public string Bus = "SFX";

		/// <summary>Designer-facing label for the lane.</summary>
		public string Name;
	}

	/// <summary>
	/// Fires sounds at points on the timeline — VO lines, stings, foley.
	/// </summary>
	[TrackTypeId("audio")]
	public class TimelineAudioTrack : TimelineParameterTrack
	{
		public List<TimelineAudioClip> Clips = new();

		public override void Validate(ITimelineContext context, List<string> problems)
		{
			foreach (var clip in Clips)
			{
				if (clip == null)
					continue;

				if (!clip.Clip.IsValid)
					problems.Add($"audio track: the clip at {clip.Time:0.00}s has no sound assigned.");
				else if (clip.Clip.ResolvePath() == null)
					problems.Add($"audio track: '{clip.Name ?? clip.Clip.ToString()}' at {clip.Time:0.00}s cannot be resolved.");
			}
		}

		/// <summary>No-op: audio is not a function of time. See the class remarks.</summary>
		public override void Evaluate(float time, ITimelineContext context)
		{
		}

		public override float ContentEndTime()
		{
			var end = 0f;
			foreach (var c in Clips) if (c != null) end = System.Math.Max(end, c.Time);
			return end;
		}

		public override void OnCrossForward(float previous, float next, ITimelineContext context)
		{
			if (Clips == null || Core.Audio == null)
				return;

			for (var i = 0; i < Clips.Count; i++)
			{
				var clip = Clips[i];
				if (clip == null || clip.Time <= previous || clip.Time > next)
					continue;

				if (!clip.Clip.IsValid)
					continue;

				var loaded = Core.Audio.LoadClip(clip.Clip);
				if (loaded == null)
				{
					Debug.Warn($"[TimelineAudioTrack] Could not load '{clip.Name ?? clip.Clip.ToString()}'.");
					continue;
				}

				Core.Audio.PlaySfx(loaded, clip.Bus ?? "SFX", clip.Volume, clip.Pitch, clip.Pan);
			}
		}
	}
}
