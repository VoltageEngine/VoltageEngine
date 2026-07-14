using Voltage.Serialization;

namespace Voltage.Audio
{
	/// <summary>
	/// Swappable audio backend — the seam that lets an alternate backend (software-mixing PCM or FMOD) replace
	/// the default <see cref="MonoGameAudioBackend"/> without touching component or mixer code;
	/// <see cref="AudioManager"/> only ever talks to this contract. Each backend owns its clip representation
	/// behind the abstract <see cref="AudioClip"/> (MonoGame <c>SoundEffect</c> vs. decoded PCM).
	/// </summary>
	public interface IAudioBackend
	{
		/// <summary>Called once when the backend becomes active.</summary>
		void Init();

		/// <summary>Called when the backend is torn down or replaced. Must release all resources.</summary>
		void Shutdown();

		/// <summary>
		/// Loads (or resolves from cache) the clip into this backend's <see cref="AudioClip"/> representation.
		/// Returns <c>null</c> if it cannot be loaded.
		/// </summary>
		AudioClip LoadClip(AssetReference reference);

		/// <summary>
		/// Creates a controllable, non-started voice for <paramref name="clip"/>. The caller owns the handle and
		/// must <see cref="IAudioHandle.Dispose"/> it. Returns <c>null</c> if this backend can't play the clip.
		/// </summary>
		IAudioHandle CreateHandle(AudioClip clip, bool looped);

		/// <summary>
		/// Fire-and-forget one-shot with volume/pitch/pan baked in. Returns <c>false</c> when the platform has
		/// no free voice (MonoGame/OpenAL caps at 32 simultaneous sources).
		/// </summary>
		bool PlayOneShot(AudioClip clip, float volume, float pitch, float pan);
	}
}
