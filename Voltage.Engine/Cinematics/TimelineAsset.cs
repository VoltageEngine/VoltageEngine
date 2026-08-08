using System;
using System.Collections.Generic;

namespace Voltage.Cinematics
{
	/// <summary>
	/// A reusable, scene-agnostic cinematic sequence — the data behind a <c>.vtimeline</c> asset. It binds
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

		/// <summary>
		/// Named points for navigation — "jump to the choice", "the door opens here".
		/// </summary>
		public List<TimelineMarker> Markers = new();

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

		/// <summary>
		/// The last moment anything on this timeline happens, ignoring <see cref="Duration"/>.
		/// </summary>
		public float ContentEndTime()
		{
			var end = 0f;

			foreach (var track in ParameterTracks)
				end = Math.Max(end, track?.ContentEndTime() ?? 0f);

			foreach (var e in Events)
				end = Math.Max(end, e.Time + Math.Max(0f, e.Duration));

			foreach (var s in SpawnClips)
				end = Math.Max(end, s.Time + Math.Max(0f, s.Duration));

			foreach (var m in Markers)
				end = Math.Max(end, m?.Time ?? 0f);

			return end;
		}

		/// <summary>The marker with this name, or null.</summary>
		public TimelineMarker FindMarker(string name)
		{
			if (string.IsNullOrEmpty(name))
				return null;

			foreach (var m in Markers)
			{
				if (m != null && string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase))
					return m;
			}

			return null;
		}
	}
}
