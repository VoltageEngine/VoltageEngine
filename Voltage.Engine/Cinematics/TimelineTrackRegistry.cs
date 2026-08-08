using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Voltage.Cinematics
{
	/// <summary>
	/// Stable, rename-proof identity for a <see cref="TimelineParameterTrack"/> type.
	/// </summary>
	[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
	public sealed class TrackTypeIdAttribute : Attribute
	{
		public string Id { get; }

		public TrackTypeIdAttribute(string id) => Id = id;
	}

	/// <summary>
	/// Maps timeline track types to their stable ids, in both directions.
	/// </summary>
	public static class TimelineTrackRegistry
	{
		private static readonly object _lock = new();
		private static readonly Dictionary<string, Type> _byId = new(StringComparer.Ordinal);
		private static readonly Dictionary<Type, string> _byType = new();

		/// <summary>Registers <paramref name="trackType"/> under its <see cref="TrackTypeIdAttribute"/>.</summary>
		/// <exception cref="InvalidOperationException">The type has no <see cref="TrackTypeIdAttribute"/>.</exception>
		public static void Register(Type trackType)
		{
			var id = (Attribute.GetCustomAttribute(trackType, typeof(TrackTypeIdAttribute)) as TrackTypeIdAttribute)?.Id;
			if (string.IsNullOrEmpty(id))
			{
				throw new InvalidOperationException(
					$"Timeline track '{trackType?.FullName}' has no [TrackTypeId]. Give it a short, permanent " +
					"id — it is what .vtimeline files store, and it is what keeps them working when the class " +
					"is renamed.");
			}

			Register(id, trackType);
		}

		public static void Register(string id, Type trackType)
		{
			if (string.IsNullOrEmpty(id) || trackType == null)
				return;

			lock (_lock)
			{
				_byId[id] = trackType;
				_byType[trackType] = id;
			}
		}

		/// <summary>The stored id for a track type, or null when it is not registered.</summary>
		public static string IdFor(Type trackType)
		{
			if (trackType == null)
				return null;

			lock (_lock)
				return _byType.TryGetValue(trackType, out var id) ? id : null;
		}

		/// <summary>The track type for a stored id, or null when it is not registered.</summary>
		public static Type TypeFor(string id)
		{
			if (string.IsNullOrEmpty(id))
				return null;

			lock (_lock)
				return _byId.TryGetValue(id, out var type) ? type : null;
		}

		/// <summary>
		/// Writer hook.
		/// </summary>
		internal static string RequireId(Type trackType) =>
			IdFor(trackType) ?? throw new InvalidOperationException(
				$"Timeline track '{trackType?.FullName}' is not registered, so it cannot be saved. Add " +
				"[TrackTypeId(\"…\")] and register it via TimelineTrackRegistry.Register from a [ModuleInitializer].");

		/// <summary>
		/// Reader hook.
		/// </summary>
		internal static Type RequireType(string id) =>
			TypeFor(id) ?? throw new InvalidOperationException(
				$"Unknown timeline track id '{id}'. The track type may have been deleted, or its plugin is " +
				$"not loaded. Registered ids: {string.Join(", ", RegisteredIds)}.");

		public static IReadOnlyCollection<string> RegisteredIds
		{
			get
			{
				lock (_lock)
					return new List<string>(_byId.Keys);
			}
		}

		[ModuleInitializer]
		internal static void RegisterEngineTracks()
		{
			Register(typeof(TimelineTransformTrack));
			Register(typeof(TimelineCameraTrack));
			Register(typeof(TimelineTintTrack));
			Register(typeof(TimelineSpriteTrack));
			Register(typeof(TimelineActivationTrack));
			Register(typeof(TimelineAudioTrack));
			Register(typeof(TimelinePropertyTrack));
			Register(typeof(TimelineNestedTrack));
		}
	}
}
