using System.Collections.Generic;

namespace Voltage.Cinematics
{
	/// <summary>
	/// Keyframes an arbitrary <c>[TimelineProperty]</c> member on a component — a lantern's intensity, a shader parameter, a custom gameplay value.
	/// </summary>
	[TrackTypeId("property")]
	public class TimelinePropertyTrack : TimelineParameterTrack
	{
		/// <summary>Stable <c>[ComponentId]</c> of the component owning the property.</summary>
		public string TargetComponentId;

		/// <summary>Member name as registered by <c>[TimelineProperty]</c>.</summary>
		public string Property;

		public List<FloatKeyframe> FloatKeys = new();
		public List<Vector2Keyframe> Vector2Keys = new();
		public List<ColorKeyframe> ColorKeys = new();

		public override void Evaluate(float time, ITimelineContext context)
		{
			var component = context.ResolveComponent(TargetRole, TargetComponentId);
			if (component == null)
				return;

			if (!TimelinePropertyRegistry.TryGetKind(TargetComponentId, Property, out var kind))
				return;   // reported by Validate at Play()

			switch (kind)
			{
				case TimelinePropertyKind.Float when FloatKeys.Count > 0:
					if (TimelinePropertyRegistry.TryGetFloat(TargetComponentId, Property, out _, out var setF))
						setF(component, TimelineTransformTrack.SampleFloat(FloatKeys, time));
					break;

				case TimelinePropertyKind.Vector2 when Vector2Keys.Count > 0:
					if (TimelinePropertyRegistry.TryGetVector2(TargetComponentId, Property, out _, out var setV))
						setV(component, TimelineTransformTrack.SampleVector2(Vector2Keys, time));
					break;

				case TimelinePropertyKind.Color when ColorKeys.Count > 0:
					if (TimelinePropertyRegistry.TryGetColor(TargetComponentId, Property, out _, out var setC))
						setC(component, TimelineTintTrack.SampleColor(ColorKeys, time));
					break;
			}
		}

		public override void Validate(ITimelineContext context, List<string> problems)
		{
			var label = $"property track '{TargetComponentId}.{Property}'";

			if (string.IsNullOrEmpty(TargetComponentId) || string.IsNullOrEmpty(Property))
			{
				problems.Add("a property track has no component or property selected.");
				return;
			}

			if (!TimelinePropertyRegistry.TryGetKind(TargetComponentId, Property, out var kind))
			{
				problems.Add($"{label}: no [TimelineProperty] with that name is registered — it may have been " +
							 "renamed or removed.");
				return;
			}

			if (context.ResolveComponent(TargetRole, TargetComponentId) == null)
				problems.Add($"{label}: role '{TargetRole}' has no component with id '{TargetComponentId}'.");

			var hasKeys = kind switch
			{
				TimelinePropertyKind.Float => FloatKeys.Count > 0,
				TimelinePropertyKind.Vector2 => Vector2Keys.Count > 0,
				TimelinePropertyKind.Color => ColorKeys.Count > 0,
				_ => false,
			};

			if (!hasKeys)
				problems.Add($"{label}: no {kind} keyframes, so the track does nothing.");
		}

		public override float ContentEndTime()
		{
			var end = 0f;
			foreach (var k in FloatKeys) end = System.Math.Max(end, k.Time);
			foreach (var k in Vector2Keys) end = System.Math.Max(end, k.Time);
			foreach (var k in ColorKeys) end = System.Math.Max(end, k.Time);
			return end;
		}

		public override void CaptureRestoreState(StateSnapshot snapshot, ITimelineContext context)
		{
			var component = context.ResolveComponent(TargetRole, TargetComponentId);
			if (component == null || !TimelinePropertyRegistry.TryGetKind(TargetComponentId, Property, out var kind))
				return;

			switch (kind)
			{
				case TimelinePropertyKind.Float
					when TimelinePropertyRegistry.TryGetFloat(TargetComponentId, Property, out var getF, out var setF):
				{
					var value = getF(component);
					snapshot.Add(() => setF(component, value));
					break;
				}

				case TimelinePropertyKind.Vector2
					when TimelinePropertyRegistry.TryGetVector2(TargetComponentId, Property, out var getV, out var setV):
				{
					var value = getV(component);
					snapshot.Add(() => setV(component, value));
					break;
				}

				case TimelinePropertyKind.Color
					when TimelinePropertyRegistry.TryGetColor(TargetComponentId, Property, out var getC, out var setC):
				{
					var value = getC(component);
					snapshot.Add(() => setC(component, value));
					break;
				}
			}
		}
	}
}
