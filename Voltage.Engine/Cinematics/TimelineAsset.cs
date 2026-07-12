using System.Collections.Generic;

namespace Voltage.Cinematics
{
	/// <summary>
	/// A reusable, scene-agnostic cinematic sequence — the data behind a <c>.timeline</c> asset. It binds
	/// to abstract <see cref="Roles"/> (e.g. "Hero", "Camera"), never concrete entities; a
	/// <see cref="TimelineDirector"/> maps those roles to real entities per scene. This separation is what
	/// makes a cutscene reusable across levels.
	///
	/// Tracks fall into two families:
	/// <list type="bullet">
	///   <item><b>Evaluable</b> (<see cref="ParameterTracks"/>) — pure functions of time, scrubbable.</item>
	///   <item><b>Trigger</b> (<see cref="Events"/>) — imperative, fire-once method calls / broadcasts.</item>
	/// </list>
	/// plus <see cref="SpawnClips"/> that own the lifecycle of prefabs instantiated for the cutscene.
	/// </summary>
	public class TimelineAsset
	{
		/// <summary>Total length in seconds.</summary>
		public float Duration = 5f;

		/// <summary>Abstract actor slots this timeline expects to be bound (or spawned) at play time.</summary>
		public List<TimelineRole> Roles = new();

		/// <summary>Evaluable parameter tracks (transform/alpha/frame/property/camera).</summary>
		public List<TimelineParameterTrack> ParameterTracks = new();

		/// <summary>Fire-once event clips (method calls and/or broadcasts).</summary>
		public List<TimelineEventClip> Events = new();

		/// <summary>Prefab spawn clips whose lifecycle the director owns.</summary>
		public List<TimelineSpawnClip> SpawnClips = new();

		private List<TimelineEventClip> _eventsInOrder;

		/// <summary>
		/// Events sorted ascending by <see cref="TimelineEventClip.Time"/>. Cached; call
		/// <see cref="InvalidateEventOrder"/> after editing the event list (the editor does this).
		/// </summary>
		public IReadOnlyList<TimelineEventClip> EventsInOrder()
		{
			if (_eventsInOrder == null || _eventsInOrder.Count != Events.Count)
			{
				_eventsInOrder = new List<TimelineEventClip>(Events);
				_eventsInOrder.Sort((a, b) => a.Time.CompareTo(b.Time));
			}
			return _eventsInOrder;
		}

		/// <summary>Drops the cached event ordering (call after adding/removing/retiming events).</summary>
		public void InvalidateEventOrder() => _eventsInOrder = null;
	}
}
