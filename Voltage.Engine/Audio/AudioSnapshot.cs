using System;
using System.Collections.Generic;

namespace Voltage.Audio
{
	/// <summary>
	/// A captured set of mixer bus volumes — a named "mood" the mixer can smoothly transition to. Excludes
	/// Master by default so a mood never clobbers the player's global volume. Mute/solo aren't part of a
	/// snapshot; use a bus volume of 0 to silence a bus within a mood.
	/// </summary>
	public sealed class AudioSnapshot
	{
		// Bus name -> target volume 0..1 (case-insensitive, matching AudioMixer.GetBus).
		private readonly Dictionary<string, float> _busVolumes;

		public AudioSnapshot(Dictionary<string, float> busVolumes)
		{
			_busVolumes = busVolumes ?? new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
		}

		/// <summary>The captured (bus name → volume 0..1) pairs.</summary>
		public IReadOnlyDictionary<string, float> BusVolumes => _busVolumes;
	}
}
