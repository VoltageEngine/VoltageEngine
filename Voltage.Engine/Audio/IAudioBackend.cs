using Microsoft.Xna.Framework.Audio;

namespace Voltage.Audio
{
	/// <summary>
	/// Swappable audio backend. The seam that lets an optional, separately-licensed backend (e.g. an
	/// FMOD plugin) replace the default <see cref="MonoGameAudioBackend"/> later without touching any
	/// component or mixer code — <see cref="AudioManager"/> only ever talks to this contract.
	///
	/// <para>Clips are MonoGame <see cref="SoundEffect"/>s loaded through the normal content pipeline.
	/// A backend with a different clip representation would extend this with its own play path; the
	/// handle lifecycle below stays common.</para>
	/// </summary>
	public interface IAudioBackend
	{
		/// <summary>Called once when the backend becomes active.</summary>
		void Init();

		/// <summary>Called when the backend is torn down or replaced. Must release all resources.</summary>
		void Shutdown();

		/// <summary>
		/// Creates a controllable, non-started voice for <paramref name="clip"/> (looping ambience, music,
		/// positional sounds). The caller owns the handle and must <see cref="IAudioHandle.Dispose"/> it.
		/// </summary>
		IAudioHandle CreateHandle(SoundEffect clip, bool looped);

		/// <summary>
		/// Fire-and-forget one-shot with volume/pitch/pan baked in at play time. Returns <c>false</c> when
		/// the platform has no free voice (MonoGame/OpenAL caps at 32 simultaneous sources).
		/// </summary>
		bool PlayOneShot(SoundEffect clip, float volume, float pitch, float pan);
	}
}
