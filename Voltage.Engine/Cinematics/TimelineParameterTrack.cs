namespace Voltage.Cinematics
{
	/// <summary>
	/// Base class for the "evaluable" track family — tracks that are a pure function of time (transform,
	/// alpha, sprite frame, camera, arbitrary property). Unlike event tracks, these can be sampled at any
	/// time and re-applied, which is what makes scrubbing and seeking deterministic.
	///
	/// Concrete tracks are added in later milestones; this base defines the contract the director drives.
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
	}
}
