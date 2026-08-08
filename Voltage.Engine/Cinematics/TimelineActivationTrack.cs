using System.Collections.Generic;

namespace Voltage.Cinematics
{
	/// <summary>A span during which the target entity is enabled.</summary>
	public class TimelineActiveRange
	{
		public float Time;
		public float Duration;
	}

	/// <summary>
	/// Enables the target entity for the spans in <see cref="Ranges"/> and disables it elsewhere.
	/// </summary>
	[TrackTypeId("activation")]
	public class TimelineActivationTrack : TimelineParameterTrack
	{
		public List<TimelineActiveRange> Ranges = new();

		/// <summary>State applied outside every range. Usually false ("appear for a beat, then vanish").</summary>
		public bool ActiveOutsideRanges;

		public override void Evaluate(float time, ITimelineContext context)
		{
			var entity = context.ResolveRole(TargetRole);
			if (entity == null)
				return;

			entity.SetEnabled(IsActiveAt(time));
		}

		public bool IsActiveAt(float time)
		{
			if (Ranges != null)
			{
				for (var i = 0; i < Ranges.Count; i++)
				{
					var range = Ranges[i];
					if (range == null)
						continue;

					if (time >= range.Time && time < range.Time + range.Duration)
						return !ActiveOutsideRanges;
				}
			}

			return ActiveOutsideRanges;
		}

		public override float ContentEndTime()
		{
			var end = 0f;
			foreach (var r in Ranges) if (r != null) end = System.Math.Max(end, r.Time + r.Duration);
			return end;
		}

		public override void CaptureRestoreState(StateSnapshot snapshot, ITimelineContext context)
		{
			var entity = context.ResolveRole(TargetRole);
			if (entity == null)
				return;

			var enabled = entity.Enabled;
			snapshot.Add(() => entity.SetEnabled(enabled));
		}
	}
}
