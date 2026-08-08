using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Voltage.Utils.Tweens.Easing;

namespace Voltage.Cinematics
{
	/// <summary>
	/// Keyframes the colour of every <see cref="RenderableComponent"/> on the target entity — alpha and RGB on independent channels, so a plain fade only needs <see cref="Alpha"/>.
	/// </summary>
	[TrackTypeId("tint")]
	public class TimelineTintTrack : TimelineParameterTrack
	{
		/// <summary>0..1 opacity keyframes. Empty = leave alpha alone.</summary>
		public List<FloatKeyframe> Alpha = new();

		/// <summary>RGB keyframes. Empty = leave colour alone.</summary>
		public List<ColorKeyframe> Tint = new();

		public override void Evaluate(float time, ITimelineContext context)
		{
			var entity = context.ResolveRole(TargetRole);
			if (entity == null)
				return;

			var hasAlpha = Alpha is { Count: > 0 };
			var hasTint = Tint is { Count: > 0 };
			if (!hasAlpha && !hasTint)
				return;

			var renderables = entity.GetComponents<RenderableComponent>();
			if (renderables == null || renderables.Count == 0)
				return;

			var alpha = hasAlpha ? MathHelper.Clamp(TimelineTransformTrack.SampleFloat(Alpha, time), 0f, 1f) : 0f;
			var tint = hasTint ? SampleColor(Tint, time) : Color.White;

			for (var i = 0; i < renderables.Count; i++)
			{
				var current = renderables[i].Color;
				renderables[i].Color = new Color(
					hasTint ? tint.R : current.R,
					hasTint ? tint.G : current.G,
					hasTint ? tint.B : current.B,
					hasAlpha ? (byte)(alpha * 255f) : current.A);
			}
		}

		public override float ContentEndTime()
		{
			var end = 0f;
			foreach (var k in Alpha) end = System.Math.Max(end, k.Time);
			foreach (var k in Tint) end = System.Math.Max(end, k.Time);
			return end;
		}

		public override void CaptureRestoreState(StateSnapshot snapshot, ITimelineContext context)
		{
			var entity = context.ResolveRole(TargetRole);
			var renderables = entity?.GetComponents<RenderableComponent>();
			if (renderables == null)
				return;

			for (var i = 0; i < renderables.Count; i++)
			{
				var renderable = renderables[i];
				var colour = renderable.Color;
				snapshot.Add(() => renderable.Color = colour);
			}
		}

		/// <summary>Colour analogue of the transform track's samplers.</summary>
		public static Color SampleColor(List<ColorKeyframe> keys, float time)
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
