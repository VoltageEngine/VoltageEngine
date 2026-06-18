using Microsoft.Xna.Framework.Audio;

namespace Voltage.Utils.Audio
{
	/// <summary>
	/// Contract for any <see cref="Voltage.Component"/> (or other engine object) that
	/// produces audio output.
	///
	/// Adoption pattern:
	/// 1. Implement this interface on your component.
	/// 2. In <c>OnAddedToEntity</c> (or the constructor), call
	///    <see cref="AudioComponentRegistry.Register(IAudioComponent)"/>.
	/// 3. In <c>OnRemovedFromEntity</c> (or Dispose), call
	///    <see cref="AudioComponentRegistry.Unregister(IAudioComponent)"/>.
	/// 4. Implement <see cref="OnAudioStateChanged"/> to stop or resume your
	///    active <see cref="SoundEffectInstance"/>s.
	///
	/// The registry automatically calls <see cref="OnAudioStateChanged"/> whenever
	/// <see cref="Core.IsAudioOn"/> changes via <see cref="Core.OnSwitchAudio"/>.
	/// Your component should also guard its own <c>Play</c> calls with a
	/// <c>Core.IsAudioOn</c> check so newly-started sounds respect the current state.
	/// </summary>
	public interface IAudioComponent
	{
		/// <summary>
		/// Called by the audio system whenever the global mute state changes.
		/// </summary>
		/// <param name="isAudioOn">
		/// <c>true</c>  — audio has been re-enabled; resume/un-pause active instances.
		/// <c>false</c> — audio has been muted; stop or pause all active instances.
		/// </param>
		void OnAudioStateChanged(bool isAudioOn);
	}
}
