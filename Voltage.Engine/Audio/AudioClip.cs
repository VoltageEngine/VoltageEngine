using Microsoft.Xna.Framework.Audio;

namespace Voltage.Audio
{
	/// <summary>
	/// Backend-agnostic handle to a loaded audio clip. The active backend decides the concrete
	/// representation (MonoGame <see cref="SoundEffect"/> or decoded PCM); callers deal only in
	/// <see cref="AudioClip"/>. Obtain one via <see cref="AudioManager.LoadClip"/>.
	/// </summary>
	public abstract class AudioClip
	{
		/// <summary>Wraps a loaded MonoGame <see cref="SoundEffect"/> as an <see cref="AudioClip"/> (null-safe).</summary>
		public static AudioClip FromSoundEffect(SoundEffect sound)
			=> sound == null ? null : new SoundEffectAudioClip(sound);

		/// <summary>
		/// Builds a PCM clip from interleaved float samples in [-1, 1] (the software-mixing backend's
		/// representation). <paramref name="channels"/> is 1 (mono) or 2 (stereo).
		/// </summary>
		public static AudioClip FromPcm(float[] samples, int channels, int sampleRate)
			=> new PcmAudioClip(samples, channels, sampleRate);
	}

	// The MonoGame backend's clip representation: a loaded SoundEffect.
	internal sealed class SoundEffectAudioClip : AudioClip
	{
		public readonly SoundEffect Sound;
		public SoundEffectAudioClip(SoundEffect sound) => Sound = sound;
	}
}
