using Microsoft.Xna.Framework;

namespace Voltage.Audio
{
	/// <summary>
	/// A controlled, manager-tracked sound: looping ambience, a positional effect, or any one-shot the
	/// caller wants a handle on. The <see cref="AudioManager"/> recomputes its output volume/pan every
	/// frame from its base values, its bus gain, and (when <see cref="Is3D"/>) its distance to the
	/// active listener — so runtime bus-slider changes and movement are reflected live.
	///
	/// <para>Non-looping voices are cleaned up automatically once the underlying sound finishes.
	/// Looping voices live until <see cref="Stop"/> is called (typically from the owning component's
	/// <c>OnRemovedFromEntity</c>).</para>
	/// </summary>
	public sealed class AudioVoice
	{
		internal IAudioHandle Handle;
		internal AudioBus Bus;

		internal float BaseVolume = 1f;
		internal float BasePitch;
		internal float BasePan;

		internal bool Is3D;
		internal Vector2 Position;
		internal float MinDistance = 100f;
		internal float MaxDistance = 800f;

		internal bool Loop;
		internal bool StopRequested;

		/// <summary>True while the voice has a live, playing handle.</summary>
		public bool IsPlaying =>
			Handle != null && !Handle.IsDisposed && Handle.State == AudioPlayState.Playing;

		/// <summary>Updates the world position used for distance attenuation/pan (3D voices only).</summary>
		public void SetPosition(Vector2 position) => Position = position;

		/// <summary>Sets the pre-bus base volume 0..1; combined with bus gain and positional factor.</summary>
		public void SetBaseVolume(float volume) => BaseVolume = MathHelper.Clamp(volume, 0f, 1f);

		public void SetBasePitch(float pitch) => BasePitch = MathHelper.Clamp(pitch, -1f, 1f);

		public void Pause() => Handle?.Pause();
		public void Resume() => Handle?.Resume();

		/// <summary>Requests the voice stop; the manager fades it out of its tracking and disposes it.</summary>
		public void Stop() => StopRequested = true;
	}
}
