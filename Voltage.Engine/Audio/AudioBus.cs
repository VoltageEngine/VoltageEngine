using System.Collections.Generic;

namespace Voltage.Audio
{
	/// <summary>
	/// Placeholder for a future per-bus DSP effect (reverb, EQ, filter). The native system does
	/// <b>not</b> implement effects — this hook exists only so the bus API does not have to change if
	/// an effect-capable backend (e.g. FMOD) is added later. Declared, unused by design.
	/// </summary>
	public interface IAudioEffect
	{
	}

	/// <summary>
	/// A named mixer group. Sounds are routed to a bus; the bus's gain (times its parent chain) scales
	/// their output volume. Pure arithmetic — no threads, no DSP — so it is AOT-safe and portable to
	/// every platform MonoGame targets, including consoles.
	/// </summary>
	public sealed class AudioBus
	{
		/// <summary>Stable bus name (e.g. "Master", "Music", "SFX").</summary>
		public string Name { get; }

		/// <summary>Parent bus, or <c>null</c> for the Master bus.</summary>
		public AudioBus Parent { get; }

		/// <summary>User-facing volume 0..1 (the value an options-menu slider drives).</summary>
		public float Volume = 1f;

		public bool Mute;

		/// <summary>When any bus is soloed, non-soloed sibling buses are silenced (mixer-level rule).</summary>
		public bool Solo;

		/// <summary>Future-DSP hook. Never populated by the native system.</summary>
		internal readonly List<IAudioEffect> Effects = new();

		/// <summary>
		/// Transient ducking multiplier 0..1, driven by <see cref="AudioManager.Duck"/>. Recovers toward 1.
		/// </summary>
		internal float DuckMultiplier = 1f;

		public AudioBus(string name, AudioBus parent = null)
		{
			Name = name;
			Parent = parent;
		}

		/// <summary>This bus's own gain contribution (volume, mute, duck) — excludes parents and solo.</summary>
		internal float LocalGain => (Mute ? 0f : Volume) * DuckMultiplier;
	}
}
