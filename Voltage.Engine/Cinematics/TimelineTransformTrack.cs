using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Voltage.Utils.Tweens.Easing;

namespace Voltage.Cinematics
{
	/// <summary>
	/// An evaluable parameter track that keyframes an entity's transform — position, rotation (radians),
	/// and/or scale — over time, easing between keyframes. Each channel is independent: leave a channel's
	/// keyframe list empty to not animate it. Being a pure function of time, it scrubs and seeks cleanly.
	///
	/// Keyframe lists are expected to be sorted ascending by <see cref="Vector2Keyframe.Time"/> (the editor
	/// keeps them sorted on edit).
	/// </summary>
	public class TimelineTransformTrack : TimelineParameterTrack
	{
		public List<Vector2Keyframe> Position = new();
		public List<FloatKeyframe> Rotation = new();
		public List<Vector2Keyframe> Scale = new();

		public override void Evaluate(float time, ITimelineContext context)
		{
			var entity = context.ResolveRole(TargetRole);
			if (entity == null)
				return;

			if (Position is { Count: > 0 })
				entity.Position = SampleVector2(Position, time);
			if (Rotation is { Count: > 0 })
				entity.Rotation = SampleFloat(Rotation, time);
			if (Scale is { Count: > 0 })
				entity.Scale = SampleVector2(Scale, time);
		}

		public override void CaptureRestoreState(StateSnapshot snapshot, ITimelineContext context)
		{
			var entity = context.ResolveRole(TargetRole);
			if (entity == null)
				return;

			var pos = entity.Position;
			var rot = entity.Rotation;
			var scale = entity.Scale;
			snapshot.Add(() =>
			{
				entity.Position = pos;
				entity.Rotation = rot;
				entity.Scale = scale;
			});
		}

		/// <summary>
		/// Samples a sorted Vector2 keyframe list at <paramref name="time"/>: clamps outside the range,
		/// eases between the two surrounding keyframes otherwise. The destination keyframe's
		/// <see cref="Vector2Keyframe.Ease"/> controls the segment's curve.
		/// </summary>
		public static Vector2 SampleVector2(List<Vector2Keyframe> keys, float time)
		{
			if (keys.Count == 1 || time <= keys[0].Time)
				return keys[0].Value;

			var last = keys[keys.Count - 1];
			if (time >= last.Time)
				return last.Value;

			for (var i = 1; i < keys.Count; i++)
			{
				if (time > keys[i].Time)
					continue;

				var a = keys[i - 1];
				var b = keys[i];
				var duration = b.Time - a.Time;
				return duration <= 0f
					? b.Value
					: Lerps.Ease(b.Ease, a.Value, b.Value, time - a.Time, duration);
			}

			return last.Value;
		}

		/// <summary>Scalar analogue of <see cref="SampleVector2"/> (rotation/alpha/property).</summary>
		public static float SampleFloat(List<FloatKeyframe> keys, float time)
		{
			if (keys.Count == 1 || time <= keys[0].Time)
				return keys[0].Value;

			var last = keys[keys.Count - 1];
			if (time >= last.Time)
				return last.Value;

			for (var i = 1; i < keys.Count; i++)
			{
				if (time > keys[i].Time)
					continue;

				var a = keys[i - 1];
				var b = keys[i];
				var duration = b.Time - a.Time;
				return duration <= 0f
					? b.Value
					: Lerps.Ease(b.Ease, a.Value, b.Value, time - a.Time, duration);
			}

			return last.Value;
		}
	}
}
