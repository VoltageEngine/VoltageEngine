using System;
using Microsoft.Xna.Framework;

namespace Voltage.Audio
{
	/// <summary>
	/// A controlled, manager-tracked sound: looping ambience, a positional effect, or a one-shot the caller
	/// wants a handle on. <see cref="AudioManager"/> recomputes output volume/pan each frame from base values,
	/// bus gain, and (when <see cref="Is3D"/>) listener distance, so slider changes and movement stay live.
	///
	/// <para>Non-looping voices are cleaned up once finished; looping voices live until <see cref="Stop"/>.</para>
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

		// DSP (software backend only): per-voice low-pass cutoff (0 = open) and reverb send amount (0..1).
		internal float LowPassCutoffHz;
		internal float ReverbSend;

		internal bool Loop;
		internal bool StopRequested;

		// Higher Priority survives when the voice pool is full (ties broken by loudness). Clip is retained so
		// a virtualized loop can be re-created when it becomes audible / a slot frees.
		internal int Priority = 128;
		internal AudioClip Clip;

		// Tracked but holds no live handle (culled by distance or stolen for capacity). Only loops are
		// virtualized; one-shots are dropped. Loops resume from their loop start when revived (the backend
		// exposes no sample position to preserve).
		internal bool IsVirtual;

		// Fade-in applied the first time the voice becomes real (avoids a click on start / revive).
		internal float PendingFadeInSeconds;

		// True once the voice has had a real handle — lets a revive add a click-guard fade while a brand-new
		// one-shot still starts crisp unless a fade was explicitly requested.
		internal bool HasBeenReal;

		// Fade envelope: a 0..1 multiplier over BaseVolume, advanced by the AudioManager. Kept separate from
		// BaseVolume so a component that live-syncs BaseVolume each frame never fights an in-progress fade.
		// FadeSpeed 0 = idle.
		internal float FadeVolume = 1f;
		internal float FadeTarget = 1f;
		internal float FadeSpeed;
		internal bool StopAfterFade;

		/// <summary>True while the voice has a live, playing handle.</summary>
		public bool IsPlaying =>
			Handle != null && !Handle.IsDisposed && Handle.State == AudioPlayState.Playing;

		/// <summary>
		/// True while the voice still belongs to its owner — either audibly playing or held virtual (culled
		/// for capacity/distance but waiting to revive). Owners should test this, not <see cref="IsPlaying"/>,
		/// before dropping their reference, so a temporarily-virtual loop isn't treated as finished.
		/// </summary>
		public bool IsAlive => IsVirtual || IsPlaying;

		/// <summary>
		/// Fades the voice's 0..1 envelope toward <paramref name="target"/> over <paramref name="seconds"/>
		/// (0 = instant), without disturbing base volume. Set <paramref name="stopWhenSilent"/> to stop and
		/// dispose the voice once it reaches 0.
		/// </summary>
		public void FadeTo(float target, float seconds, bool stopWhenSilent = false)
		{
			FadeTarget = MathHelper.Clamp(target, 0f, 1f);
			FadeSpeed = seconds > 0f ? 1f / seconds : 1000f;
			StopAfterFade = stopWhenSilent && FadeTarget <= 0f;
		}

		/// <summary>Fades to silence over <paramref name="seconds"/>, then stops and disposes the voice.</summary>
		public void FadeOutAndStop(float seconds) => FadeTo(0f, seconds, stopWhenSilent: true);

		// Advances the fade envelope toward its target; requests a stop once a fade-out-and-stop reaches 0.
		internal void AdvanceFade(float dt)
		{
			if (FadeSpeed <= 0f)
				return;

			float maxDelta = FadeSpeed * dt;
			if (Math.Abs(FadeTarget - FadeVolume) <= maxDelta)
			{
				FadeVolume = FadeTarget;
				FadeSpeed = 0f; // reached — go idle
				if (StopAfterFade && FadeVolume <= 0.0001f)
					StopRequested = true;
			}
			else
			{
				FadeVolume += Math.Sign(FadeTarget - FadeVolume) * maxDelta;
			}
		}

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
