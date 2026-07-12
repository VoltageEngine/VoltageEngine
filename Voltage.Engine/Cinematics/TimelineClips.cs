using System;
using Voltage.Serialization;

namespace Voltage.Cinematics
{
	/// <summary>
	/// One event on the timeline's event track. Fires a method on a bound role's component (AOT-safe
	/// dispatch via <see cref="TimelineDispatch"/>) and/or broadcasts a named signal. Instant when
	/// <see cref="Duration"/> is 0; ranged clips fire <see cref="BeginMethod"/> at <see cref="Time"/> and
	/// <see cref="EndMethod"/> at <c>Time + Duration</c>.
	///
	/// Pure serialized asset data — no "has fired" runtime flags live here, because the immutable asset
	/// can be played by many directors at once; each <see cref="TimelineDirector"/> tracks firing itself.
	/// </summary>
	public class TimelineEventClip
	{
		/// <summary>Designer-facing label (e.g. "explosion").</summary>
		public string Name;

		/// <summary>Start time in seconds.</summary>
		public float Time;

		/// <summary>Duration in seconds; 0 = instant (only <see cref="BeginMethod"/> fires).</summary>
		public float Duration;

		// Direct method-call binding (editor: pick role → component → [TimelineEvent] method).

		/// <summary>Which role's resolved entity to invoke on. Null/empty = pure broadcast.</summary>
		public string TargetRole;

		/// <summary>ComponentId of the component on that entity that owns the method.</summary>
		public string TargetComponentId;

		/// <summary>[TimelineEvent] method fired at <see cref="Time"/>.</summary>
		public string BeginMethod;

		/// <summary>Optional [TimelineEvent] method fired at <c>Time + Duration</c> (ranged clips).</summary>
		public string EndMethod;

		/// <summary>Positional arguments unpacked into the method by the generated dispatch.</summary>
		public TimelineArg[] Args = Array.Empty<TimelineArg>();

		// Broadcast alternative (decoupled): emit a named signal the game listens for.

		/// <summary>If set, raises <see cref="TimelineDirector.OnSignal"/> with this name instead of/alongside a call.</summary>
		public string BroadcastMessage;

		/// <summary>Whether this event still fires when the player skips the cutscene.</summary>
		public SkipBehavior OnSkip = SkipBehavior.Skip;
	}

	/// <summary>
	/// Spawns a prefab for a time range and binds it to a role other tracks can target (e.g. spawn an
	/// explosion, then a transform track moves it). The director owns the spawned entity's lifecycle:
	/// created when the playhead enters the range, destroyed when it leaves — so scrubbing spawns/despawns
	/// correctly. On a normal end, spawns are destroyed unless <see cref="KeepAfterTimeline"/> is set.
	/// </summary>
	public class TimelineSpawnClip
	{
		/// <summary>Role name the spawned entity is bound to for the duration (targetable by other tracks).</summary>
		public string SpawnRole;

		/// <summary>Prefab to instantiate.</summary>
		public PrefabReference Prefab;

		/// <summary>Start time in seconds.</summary>
		public float Time;

		/// <summary>How long the spawned entity lives (seconds). Its range is [Time, Time+Duration).</summary>
		public float Duration;

		/// <summary>
		/// When true, the spawned entity survives after the timeline finishes (e.g. a summoned boss);
		/// otherwise it is destroyed on finish. Cancelling the timeline always destroys it.
		/// </summary>
		public bool KeepAfterTimeline;
	}

	/// <summary>A named actor slot declared by a timeline asset, bound to a real entity per scene by the director.</summary>
	public class TimelineRole
	{
		public string Name;

		/// <summary>Optional hint (a ComponentId the bound entity is expected to have) for editor validation.</summary>
		public string ExpectedComponentId;
	}
}
