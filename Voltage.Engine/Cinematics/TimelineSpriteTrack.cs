using System.Collections.Generic;
using Voltage.Sprites;

namespace Voltage.Cinematics
{
	/// <summary>One animation played on the target's <see cref="SpriteAnimator"/> for a span.</summary>
	public class TimelineSpriteClip
	{
		public float Time;
		public float Duration;

		/// <summary>Animation name as registered on the animator (an Aseprite tag, typically).</summary>
		public string Animation;

		public SpriteAnimator.LoopMode Loop = SpriteAnimator.LoopMode.Loop;
	}

	/// <summary>
	/// Drives the target entity's <see cref="SpriteAnimator"/> from the timeline — "play <c>walk</c> from 0.5s to 2.0s, then <c>idle</c>".
	/// </summary>
	[TrackTypeId("sprite")]
	public class TimelineSpriteTrack : TimelineParameterTrack
	{
		public List<TimelineSpriteClip> Clips = new();

		public override void Evaluate(float time, ITimelineContext context)
		{
			var animator = context.ResolveRole(TargetRole)?.GetComponent<SpriteAnimator>();
			if (animator == null)
				return;

			var clip = ClipAt(time);
			if (clip == null || string.IsNullOrEmpty(clip.Animation))
				return;

			if (animator.CurrentAnimationName == clip.Animation)
				return;

			if (animator.Animations == null || !animator.Animations.ContainsKey(clip.Animation))
				return;

			animator.Play(clip.Animation, clip.Loop);
		}

		public override void Validate(ITimelineContext context, List<string> problems)
		{
			var animator = context.ResolveRole(TargetRole)?.GetComponent<SpriteAnimator>();
			if (animator == null)
			{
				if (Clips.Count > 0)
					problems.Add($"sprite track: role '{TargetRole}' has no SpriteAnimator.");
				return;
			}

			foreach (var clip in Clips)
			{
				if (clip == null)
					continue;

				if (string.IsNullOrEmpty(clip.Animation))
					problems.Add($"sprite track on '{TargetRole}': a clip at {clip.Time:0.00}s has no animation name.");
				else if (animator.Animations == null || !animator.Animations.ContainsKey(clip.Animation))
					problems.Add($"sprite track on '{TargetRole}': no animation named '{clip.Animation}'.");
			}
		}

		/// <summary>The clip covering <paramref name="time"/>, or null. Later clips win on overlap.</summary>
		public TimelineSpriteClip ClipAt(float time)
		{
			TimelineSpriteClip found = null;
			if (Clips == null)
				return null;

			for (var i = 0; i < Clips.Count; i++)
			{
				var clip = Clips[i];
				if (clip == null)
					continue;

				if (time >= clip.Time && time < clip.Time + clip.Duration)
					found = clip;
			}

			return found;
		}

		public override float ContentEndTime()
		{
			var end = 0f;
			foreach (var c in Clips) if (c != null) end = System.Math.Max(end, c.Time + c.Duration);
			return end;
		}

		public override void CaptureRestoreState(StateSnapshot snapshot, ITimelineContext context)
		{
			var animator = context.ResolveRole(TargetRole)?.GetComponent<SpriteAnimator>();
			if (animator == null)
				return;

			var animation = animator.CurrentAnimationName;
			var loop = animator.CurrentLoopMode;
			snapshot.Add(() =>
			{
				if (!string.IsNullOrEmpty(animation) && animator.Animations != null &&
					animator.Animations.ContainsKey(animation))
				{
					animator.Play(animation, loop);
				}
			});
		}
	}
}
