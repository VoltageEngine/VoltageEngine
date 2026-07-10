using Microsoft.Xna.Framework.Audio;

namespace Voltage.Audio
{
	/// <summary>
	/// Swappable audio backend. The engine ships one implementation,
	/// <see cref="MonoGameAudioBackend"/> (built on MonoGame's <c>SoundEffect</c> API, which maps to
	/// OpenAL on desktop and each console's native audio). This interface is the seam that lets an
	/// optional, separately-licensed backend (e.g. an FMOD plugin) replace it later without touching
	/// any component or mixer code — the <see cref="AudioManager"/> only ever talks to this contract.
	///
	/// <para>Clips are MonoGame <see cref="SoundEffect"/>s loaded through the normal content pipeline
	/// (<c>Scene.LoadAsset&lt;SoundEffect&gt;</c>). A backend that uses a different clip representation
	/// would extend this contract with its own play path; the handle lifecycle below is what stays
	/// common.</para>
	/// </summary>
	public interface IAudioBackend
	{
		/// <summary>Called once when the backend becomes active.</summary>
		void Init();

		/// <summary>Called when the backend is torn down or replaced. Must release all resources.</summary>
		void Shutdown();

		/// <summary>
		/// Creates a controllable, non-started voice for <paramref name="clip"/>. Used for looping
		/// ambience, music, and positional sounds that the <see cref="AudioManager"/> updates each frame.
		/// The caller owns the returned handle and must <see cref="IAudioHandle.Dispose"/> it.
		/// </summary>
		IAudioHandle CreateHandle(SoundEffect clip, bool looped);

		/// <summary>
		/// Fire-and-forget one-shot with volume/pitch/pan baked in at play time (bus changes after this
		/// call do not affect an already-playing one-shot). Returns <c>false</c> when the platform has
		/// no free voice (MonoGame/OpenAL caps at 32 simultaneous sources).
		/// </summary>
		bool PlayOneShot(SoundEffect clip, float volume, float pitch, float pan);
	}
}
