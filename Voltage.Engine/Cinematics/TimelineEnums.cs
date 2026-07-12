namespace Voltage.Cinematics
{
	/// <summary>Playback state of a <see cref="TimelineDirector"/> director.</summary>
	public enum DirectorState
	{
		/// <summary>Idle — not advancing. Either never played, finished, or cancelled.</summary>
		Stopped,
		Playing,
		Paused,
	}

	/// <summary>What happens when the playhead reaches the end of the timeline.</summary>
	public enum WrapMode
	{
		/// <summary>Stop advancing and hold the final frame's state.</summary>
		Hold,
		/// <summary>Jump back to the start and keep playing (re-fires events each cycle).</summary>
		Loop,
		/// <summary>Reverse direction at each end.</summary>
		PingPong,
		/// <summary>Stop and finish (same as Hold for state, but explicitly ends).</summary>
		Stop,
	}

	/// <summary>
	/// Per-event behavior when the player skips the cutscene. The reason a naive skip breaks games is
	/// consequential events; this lets a designer mark which ones must still apply.
	/// </summary>
	public enum SkipBehavior
	{
		/// <summary>Cosmetic — dropped on skip (camera shake, a gasp SFX, a particle burst).</summary>
		Skip,
		/// <summary>Consequential — fired immediately on skip so its state change still applies
		/// (give an item, spawn a boss, set a story flag).</summary>
		FireImmediately,
	}

	/// <summary>The value kinds a <see cref="TimelineArg"/> can carry (kept to AOT-safe primitives + refs).</summary>
	public enum TimelineArgType
	{
		Float,
		Int,
		Bool,
		String,
		Entity,
		Component,
		Asset,
		Prefab,
	}
}
