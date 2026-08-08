using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace Voltage.Cinematics
{
	/// <summary>
	/// Opts a public component field or property into <see cref="TimelinePropertyTrack"/>'s dropdown and its AOT-safe accessor table.
	/// </summary>
	[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
	public sealed class TimelinePropertyAttribute : Attribute
	{
		/// <summary>Optional label for the editor dropdown; defaults to the member name.</summary>
		public string DisplayName { get; set; }
	}

	public enum TimelinePropertyKind
	{
		Float,
		Vector2,
		Color,
	}

	/// <summary>
	/// Get/set accessors for animatable component properties, keyed by <c>(componentId, property)</c> — the property-shaped counterpart to <see cref="TimelineDispatch"/>.
	/// </summary>
	public static class TimelinePropertyRegistry
	{
		private readonly struct Key : IEquatable<Key>
		{
			public readonly string ComponentId;
			public readonly string Property;

			public Key(string componentId, string property)
			{
				ComponentId = componentId;
				Property = property;
			}

			public bool Equals(Key other) =>
				string.Equals(ComponentId, other.ComponentId, StringComparison.Ordinal) &&
				string.Equals(Property, other.Property, StringComparison.Ordinal);

			public override bool Equals(object obj) => obj is Key k && Equals(k);

			public override int GetHashCode() =>
				(ComponentId?.GetHashCode() ?? 0) * 397 ^ (Property?.GetHashCode() ?? 0);
		}

		private static readonly Dictionary<Key, TimelinePropertyKind> _kinds = new();
		private static readonly Dictionary<Key, (Func<Component, float> Get, Action<Component, float> Set)> _floats = new();
		private static readonly Dictionary<Key, (Func<Component, Vector2> Get, Action<Component, Vector2> Set)> _vectors = new();
		private static readonly Dictionary<Key, (Func<Component, Color> Get, Action<Component, Color> Set)> _colors = new();

		public static void RegisterFloat(string componentId, string property,
			Func<Component, float> get, Action<Component, float> set)
		{
			if (!Validate(componentId, property, get, set))
				return;

			var key = new Key(componentId, property);
			_floats[key] = (get, set);
			_kinds[key] = TimelinePropertyKind.Float;
		}

		public static void RegisterVector2(string componentId, string property,
			Func<Component, Vector2> get, Action<Component, Vector2> set)
		{
			if (!Validate(componentId, property, get, set))
				return;

			var key = new Key(componentId, property);
			_vectors[key] = (get, set);
			_kinds[key] = TimelinePropertyKind.Vector2;
		}

		public static void RegisterColor(string componentId, string property,
			Func<Component, Color> get, Action<Component, Color> set)
		{
			if (!Validate(componentId, property, get, set))
				return;

			var key = new Key(componentId, property);
			_colors[key] = (get, set);
			_kinds[key] = TimelinePropertyKind.Color;
		}

		private static bool Validate<T>(string componentId, string property, Func<Component, T> get, Action<Component, T> set) =>
			!string.IsNullOrEmpty(componentId) && !string.IsNullOrEmpty(property) && get != null && set != null;

		public static bool TryGetKind(string componentId, string property, out TimelinePropertyKind kind) =>
			_kinds.TryGetValue(new Key(componentId, property), out kind);

		public static bool TryGetFloat(string componentId, string property,
			out Func<Component, float> get, out Action<Component, float> set)
		{
			if (_floats.TryGetValue(new Key(componentId, property), out var pair))
			{
				get = pair.Get;
				set = pair.Set;
				return true;
			}

			get = null;
			set = null;
			return false;
		}

		public static bool TryGetVector2(string componentId, string property,
			out Func<Component, Vector2> get, out Action<Component, Vector2> set)
		{
			if (_vectors.TryGetValue(new Key(componentId, property), out var pair))
			{
				get = pair.Get;
				set = pair.Set;
				return true;
			}

			get = null;
			set = null;
			return false;
		}

		public static bool TryGetColor(string componentId, string property,
			out Func<Component, Color> get, out Action<Component, Color> set)
		{
			if (_colors.TryGetValue(new Key(componentId, property), out var pair))
			{
				get = pair.Get;
				set = pair.Set;
				return true;
			}

			get = null;
			set = null;
			return false;
		}

		/// <summary>Everything registered — used by the editor to build the property dropdown.</summary>
		public static IEnumerable<(string ComponentId, string Property, TimelinePropertyKind Kind)> Registered()
		{
			foreach (var pair in _kinds)
				yield return (pair.Key.ComponentId, pair.Key.Property, pair.Value);
		}
	}
}
