using System.Collections.Generic;

namespace Voltage.Audio
{
	/// <summary>
	/// Placeholder for a future per-bus DSP effect (reverb, EQ, filter). Declared but unused by design — the
	/// hook exists so the bus API won't change if an effect-capable backend (e.g. FMOD) is added later.
	/// </summary>
	public interface IAudioEffect
	{
	}

	/// <summary>
	/// A named mixer group. Sounds routed to a bus have their output scaled by the bus gain times its parent
	/// chain. Pure arithmetic (no threads, no DSP), so it is AOT-safe and portable to every MonoGame target.
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

		// Future-DSP hook. Never populated by the native system.
		internal readonly List<IAudioEffect> Effects = new();

		// Transient ducking 0..1, driven by AudioManager.Duck — an immediate dip that recovers toward 1.
		internal float TransientDuck = 1f;

		// Sustained ducking 0..1, driven by auto-duck-on-dialogue — held down while dialogue plays. Multiplies
		// with TransientDuck.
		internal float SustainedDuck = 1f;

		public AudioBus(string name, AudioBus parent = null)
		{
			Name = name;
			Parent = parent;
		}

		// This bus's own gain contribution (volume, mute, both ducks) — excludes parents and solo.
		internal float LocalGain => (Mute ? 0f : Volume) * TransientDuck * SustainedDuck;
	}
}
