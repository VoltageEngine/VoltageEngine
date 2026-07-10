using System;
using System.Collections.Generic;

namespace Voltage.Audio
{
	/// <summary>
	/// The global bus tree: <c>Master → { Music, SFX, UI, Ambience, Voice }</c>. Resolves the effective
	/// linear gain for any bus, folding in the parent chain, mute, ducking, solo, and the global master
	/// mute (<see cref="Core.IsAudioOn"/>). The whole "mixing" layer MonoGame lacks — deliberately
	/// volume-only (no DSP); see <see cref="IAudioEffect"/> for the future-effects seam.
	/// </summary>
	public sealed class AudioMixer
	{
		public AudioBus Master { get; }
		public AudioBus Music { get; }
		public AudioBus Sfx { get; }
		public AudioBus Ui { get; }
		public AudioBus Ambience { get; }
		public AudioBus Voice { get; }

		private readonly Dictionary<string, AudioBus> _byName;

		public AudioMixer()
		{
			Master = new AudioBus("Master");
			Music = new AudioBus("Music", Master);
			Sfx = new AudioBus("SFX", Master);
			Ui = new AudioBus("UI", Master);
			Ambience = new AudioBus("Ambience", Master);
			Voice = new AudioBus("Voice", Master);

			_byName = new Dictionary<string, AudioBus>(StringComparer.OrdinalIgnoreCase)
			{
				{ Master.Name, Master },
				{ Music.Name, Music },
				{ Sfx.Name, Sfx },
				{ Ui.Name, Ui },
				{ Ambience.Name, Ambience },
				{ Voice.Name, Voice },
			};
		}

		/// <summary>All buses (order not guaranteed; iterate for tooling only).</summary>
		public IReadOnlyCollection<AudioBus> Buses => _byName.Values;

		/// <summary>Resolves a bus by name; unknown names fall back to SFX so a typo never silently drops the sound.</summary>
		public AudioBus GetBus(string name)
		{
			if (!string.IsNullOrEmpty(name) && _byName.TryGetValue(name, out var bus))
				return bus;
			return Sfx;
		}

		/// <summary>
		/// Effective linear gain 0..1 for <paramref name="bus"/>: parent chain, the solo rule (any soloed
		/// non-master bus silences its non-soloed siblings), and the global master mute.
		/// </summary>
		public float EffectiveGain(AudioBus bus)
		{
			if (bus == null || !Core.IsAudioOn)
				return 0f;

			// Solo: if any non-master bus is soloed, only soloed buses (and Master) pass.
			bool anySolo = false;
			foreach (var b in _byName.Values)
			{
				if (b != Master && b.Solo)
				{
					anySolo = true;
					break;
				}
			}
			if (anySolo && bus != Master && !bus.Solo)
				return 0f;

			float gain = 1f;
			for (var cur = bus; cur != null; cur = cur.Parent)
				gain *= cur.LocalGain;

			return gain;
		}
	}
}
