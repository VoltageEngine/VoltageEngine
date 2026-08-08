using System.Collections.Generic;

namespace Voltage.Cinematics
{
	/// <summary>
	/// Base class for the "evaluable" track family — tracks that are a pure function of time (transform,
	/// alpha, sprite frame, camera, arbitrary property). Unlike event tracks, these can be sampled at any
	/// time and re-applied, which is what makes scrubbing and seeking deterministic.
	///
	/// Concrete tracks must be <b>stateless</b>: an asset is shared and may be played by several directors
	/// at once, so per-playback state on a track would corrupt across them. Where a track needs "have I
	/// already done this?", it asks the target's current state instead.
	/// </summary>
	public abstract class TimelineParameterTrack
	{
		/// <summary>Which role's resolved entity this track drives.</summary>
		public string TargetRole;

		/// <summary>
		/// Samples the track at <paramref name="time"/> (seconds) and applies the result to the resolved
		/// target. Must be a pure function of time with no side effects beyond writing the target's state.
		/// A no-op if the role does not resolve (e.g. a spawnable not yet spawned).
		/// </summary>
		public abstract void Evaluate(float time, ITimelineContext context);

		/// <summary>
		/// Captures the target's current values for the fields this track will modify, so the director can
		/// restore them on <see cref="TimelineDirector.Cancel"/>. Default no-op; concrete tracks override.
		/// </summary>
		public virtual void CaptureRestoreState(StateSnapshot snapshot, ITimelineContext context) { }

		/// <summary>
		/// Reports anything this track references that will not resolve at play time — a missing animation, an unassigned clip.
		/// </summary>
		public virtual void Validate(ITimelineContext context, List<string> problems) { }

		/// <summary>
		/// The last moment this track does anything, used by <see cref="TimelineAsset.ContentEndTime"/>.
		/// </summary>
		public virtual float ContentEndTime() => 0f;

		/// <summary>
		/// Called when the playhead moves <b>forward</b> across <c>(previous, next]</c>, on the same pass that fires events.
		/// </summary>
		public virtual void OnCrossForward(float previous, float next, ITimelineContext context) { }

		/// <summary>
		/// Called when playback stops, is cancelled, or seeks, so a track can silence or reset anything it started in <see cref="OnCrossForward"/>.
		/// </summary>
		public virtual void OnPlaybackInterrupted(ITimelineContext context) { }

	}
}
