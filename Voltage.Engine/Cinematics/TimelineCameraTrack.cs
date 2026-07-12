using System.Collections.Generic;

namespace Voltage.Cinematics
{
	/// <summary>
	/// An evaluable track that keyframes a camera's zoom over time. The camera is just an entity with a
	/// <see cref="Camera"/> component, so its position/rotation are driven by an ordinary
	/// <see cref="TimelineTransformTrack"/> bound to the same role; this track adds the camera-specific
	/// <see cref="Camera.RawZoom"/> channel.
	/// </summary>
	public class TimelineCameraTrack : TimelineParameterTrack
	{
		/// <summary>Keyframes for <see cref="Camera.RawZoom"/>. Empty = don't animate zoom.</summary>
		public List<FloatKeyframe> Zoom = new();

		public override void Evaluate(float time, ITimelineContext context)
		{
			var camera = context.ResolveRole(TargetRole)?.GetComponent<Camera>();
			if (camera == null)
				return;

			if (Zoom is { Count: > 0 })
				camera.RawZoom = TimelineTransformTrack.SampleFloat(Zoom, time);
		}

		public override void CaptureRestoreState(StateSnapshot snapshot, ITimelineContext context)
		{
			var camera = context.ResolveRole(TargetRole)?.GetComponent<Camera>();
			if (camera == null)
				return;

			var zoom = camera.RawZoom;
			snapshot.Add(() => camera.RawZoom = zoom);
		}
	}
}
