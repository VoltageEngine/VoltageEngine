namespace Voltage.Audio
{
	/// <summary>Playback state of an <see cref="IAudioHandle"/> (backend-agnostic mirror of MonoGame's <c>SoundState</c>).</summary>
	public enum AudioPlayState
	{
		Stopped,
		Playing,
		Paused,
	}

	/// <summary>
	/// A single controllable playing sound — the abstraction <see cref="AudioManager"/> and
	/// <see cref="AudioMixer"/> drive. The default backend wraps a MonoGame <c>SoundEffectInstance</c>;
	/// an FMOD backend would wrap an FMOD event instance.
	///
	/// <para><b>Volume/Pan are the raw output values sent to hardware.</b> Bus gain and positional
	/// attenuation are computed by <see cref="AudioManager"/> and written here every frame for
	/// controlled voices — don't treat <see cref="Volume"/> as a "base" volume.</para>
	/// </summary>
	public interface IAudioHandle
	{
		/// <summary>Raw output volume 0..1 (post bus/positional). Clamped by the backend.</summary>
		float Volume { get; set; }

		/// <summary>Pitch shift in XNA units, -1 (down one octave) .. 1 (up one octave).</summary>
		float Pitch { get; set; }

		/// <summary>Stereo pan, -1 (left) .. 1 (right).</summary>
		float Pan { get; set; }

		/// <summary>Whether the sound loops. Set before <see cref="Play"/> — some backends forbid changing it mid-play.</summary>
		bool IsLooped { get; set; }

		AudioPlayState State { get; }

		bool IsDisposed { get; }

		void Play();
		void Pause();
		void Resume();
		void Stop();

		/// <summary>Releases the underlying platform voice. The handle must not be used afterwards.</summary>
		void Dispose();
	}
}
