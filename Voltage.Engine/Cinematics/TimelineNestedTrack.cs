using System;
using System.Collections.Generic;
using Voltage.Serialization;

namespace Voltage.Cinematics
{
	/// <summary>One nested timeline placed on the parent's clock.</summary>
	public class TimelineNestedClip
	{
		/// <summary>When the nested timeline starts, in parent time.</summary>
		public float Time;

		[AssetType(typeof(TimelineAsset))]
		public AssetReference Timeline;

		/// <summary>Designer-facing label for the lane.</summary>
		public string Name;

		/// <summary>
		/// Playback rate for the nested timeline. 1 = its authored speed.
		/// </summary>
		public float Speed = 1f;
	}

	/// <summary>
	/// Plays other <c>.vtimeline</c> assets as reusable beats inside a longer sequence — Unity's Control Track, Unreal's shot track.
	/// </summary>
	[TrackTypeId("nested")]
	public class TimelineNestedTrack : TimelineParameterTrack
	{
		public List<TimelineNestedClip> Clips = new();

		private static readonly Dictionary<Guid, TimelineAsset> _cache = new();
		private static readonly Dictionary<string, TimelineAsset> _cacheByPath = new(StringComparer.OrdinalIgnoreCase);

		// A timeline that includes itself would recurse forever. The depth is per-thread because evaluation
		// is single-threaded per director but the cache is shared.
		[ThreadStatic] private static int _depth;
		private const int MaxDepth = 8;

		public override void Evaluate(float time, ITimelineContext context)
		{
			if (_depth >= MaxDepth)
				return;

			_depth++;
			try
			{
				foreach (var clip in Clips)
				{
					var nested = Resolve(clip);
					if (nested == null)
						continue;

					if (!TryLocalTime(clip, nested, time, out var local))
						continue;

					foreach (var track in nested.ParameterTracks)
						track?.Evaluate(local, context);
				}
			}
			finally
			{
				_depth--;
			}
		}

		public override void OnCrossForward(float previous, float next, ITimelineContext context)
		{
			if (_depth >= MaxDepth)
				return;

			_depth++;
			try
			{
				foreach (var clip in Clips)
				{
					var nested = Resolve(clip);
					if (nested == null)
						continue;

					var speed = clip.Speed <= 0f ? 1f : clip.Speed;
					var prevLocal = (previous - clip.Time) * speed;
					var nextLocal = (next - clip.Time) * speed;
					if (nextLocal <= 0f || prevLocal >= nested.Duration)
						continue;

					foreach (var track in nested.ParameterTracks)
						track?.OnCrossForward(prevLocal, nextLocal, context);

					FireNestedEvents(nested, prevLocal, nextLocal, context);
				}
			}
			finally
			{
				_depth--;
			}
		}

		/// <summary>
		/// Fires nested events whose time falls in the crossing window.
		/// </summary>
		private static void FireNestedEvents(TimelineAsset nested, float prevLocal, float nextLocal, ITimelineContext context)
		{
			foreach (var e in nested.EventsInOrder())
			{
				if (e == null)
					continue;

				if (e.Time > prevLocal && e.Time <= nextLocal)
					InvokeNested(e, e.BeginMethod, context);

				if (e.Duration > 0f)
				{
					var end = e.Time + e.Duration;
					if (end > prevLocal && end <= nextLocal)
						InvokeNested(e, e.EndMethod, context);
				}
			}
		}

		private static void InvokeNested(TimelineEventClip e, string method, ITimelineContext context)
		{
			if (!string.IsNullOrEmpty(e.BroadcastMessage))
				context.RaiseSignal(e.BroadcastMessage, e.Args);

			if (string.IsNullOrEmpty(method) || string.IsNullOrEmpty(e.TargetComponentId))
				return;

			var component = context.ResolveComponent(e.TargetRole, e.TargetComponentId);
			if (component == null)
				return;

			TimelineDispatch.TryInvoke(e.TargetComponentId, method, component, e.Args);
		}

		public override float ContentEndTime()
		{
			var end = 0f;
			foreach (var clip in Clips)
			{
				var nested = Resolve(clip);
				if (nested == null)
					continue;

				var speed = clip.Speed <= 0f ? 1f : clip.Speed;
				end = Math.Max(end, clip.Time + nested.Duration / speed);
			}

			return end;
		}

		public override void Validate(ITimelineContext context, List<string> problems)
		{
			foreach (var clip in Clips)
			{
				var label = clip?.Name ?? clip?.Timeline.AssetName ?? "a nested clip";

				if (clip == null || !clip.Timeline.IsValid)
				{
					problems.Add($"nested track: {label} has no timeline assigned.");
					continue;
				}

				var nested = Resolve(clip);
				if (nested == null)
				{
					problems.Add($"nested track: '{label}' could not be loaded — the file may have been deleted.");
					continue;
				}

				if (nested.SpawnClips.Count > 0)
				{
					problems.Add($"nested track: '{label}' has {nested.SpawnClips.Count} spawn clip(s), which a " +
								 "nested timeline cannot run — move them to the parent timeline.");
				}

				foreach (var track in nested.ParameterTracks)
					track?.Validate(context, problems);
			}
		}

		/// <summary>Local time inside the nested timeline, or false when the playhead is outside the clip.</summary>
		private static bool TryLocalTime(TimelineNestedClip clip, TimelineAsset nested, float time, out float local)
		{
			var speed = clip.Speed <= 0f ? 1f : clip.Speed;
			local = (time - clip.Time) * speed;
			return local >= 0f && local <= nested.Duration;
		}

		private static TimelineAsset Resolve(TimelineNestedClip clip)
		{
			if (clip == null || !clip.Timeline.IsValid)
				return null;

			if (clip.Timeline.AssetGuid != Guid.Empty && _cache.TryGetValue(clip.Timeline.AssetGuid, out var byGuid))
				return byGuid;

			var path = clip.Timeline.ResolvePath();
			if (string.IsNullOrEmpty(path))
				return null;

			if (_cacheByPath.TryGetValue(path, out var cached))
				return cached;

			TimelineAsset loaded;
			try
			{
				loaded = TimelineAssetIO.Load(path);
			}
			catch (Exception ex)
			{
				Debug.Warn($"[TimelineNestedTrack] Failed to load nested timeline '{path}': {ex.Message}");
				loaded = null;
			}

			if (loaded == null)
				return null;

			_cacheByPath[path] = loaded;
			if (clip.Timeline.AssetGuid != Guid.Empty)
				_cache[clip.Timeline.AssetGuid] = loaded;

			return loaded;
		}

		/// <summary>Drops cached nested assets. The editor calls this after a nested timeline is re-saved.</summary>
		public static void ClearCache()
		{
			_cache.Clear();
			_cacheByPath.Clear();
		}
	}
}
