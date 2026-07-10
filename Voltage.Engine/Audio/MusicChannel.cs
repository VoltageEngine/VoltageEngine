using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework.Audio;

namespace Voltage.Audio
{
	/// <summary>
	/// Streaming-music layer routed on the Music bus. Plays a single looping track at a time and
	/// crossfades to a new one: starting a track fades any existing tracks out while the new one fades
	/// in. Because it runs on the same <see cref="IAudioHandle"/> path as everything else, music obeys
	/// the mixer (Music-bus volume, master mute) uniformly — unlike MonoGame's separate
	/// <c>MediaPlayer</c>/<c>Song</c> API, which cannot be bus-mixed or crossfaded.
	///
	/// <para>Each track keeps its <see cref="MusicTrack.Base"/> volume (the caller's requested level)
	/// separate from its <see cref="MusicTrack.FadeCurrent"/> crossfade envelope, so the base volume can
	/// be changed live (<see cref="SetActiveVolume"/>) without disturbing an in-progress fade — enabling
	/// real-time volume editing of a <see cref="MusicZoneComponent"/>.</para>
	/// </summary>
	internal sealed class MusicChannel
	{
		private sealed class MusicTrack
		{
			public IAudioHandle Handle;
			public float Base;        // caller-requested volume 0..1 (live-editable)
			public float FadeCurrent; // current crossfade envelope 0..1
			public float FadeTarget;  // target crossfade envelope 0..1 (1 = in, 0 = out)
			public float FadeSpeed;   // envelope units per second
		}

		private readonly IAudioBackend _backend;
		private readonly AudioMixer _mixer;
		private readonly List<MusicTrack> _tracks = new();

		public MusicChannel(IAudioBackend backend, AudioMixer mixer)
		{
			_backend = backend;
			_mixer = mixer;
		}

		public bool IsPlaying => _tracks.Count > 0;

		/// <summary>Crossfades to <paramref name="clip"/> over <paramref name="fadeSeconds"/> (0 = instant).</summary>
		public void Play(SoundEffect clip, float volume, float fadeSeconds)
		{
			if (clip == null)
				return;

			float speed = FadeSpeedFor(fadeSeconds);

			// Existing tracks fade out.
			foreach (var t in _tracks)
			{
				t.FadeTarget = 0f;
				t.FadeSpeed = speed;
			}

			var handle = _backend.CreateHandle(clip, looped: true);
			var track = new MusicTrack
			{
				Handle = handle,
				Base = Clamp01(volume),
				FadeCurrent = fadeSeconds > 0f ? 0f : 1f,
				FadeTarget = 1f,
				FadeSpeed = speed,
			};

			handle.Volume = track.FadeCurrent * track.Base * _mixer.EffectiveGain(_mixer.Music);
			handle.Play();
			_tracks.Add(track);
		}

		/// <summary>Fades all music out over <paramref name="fadeSeconds"/> (0 = instant).</summary>
		public void Stop(float fadeSeconds)
		{
			float speed = FadeSpeedFor(fadeSeconds);
			foreach (var t in _tracks)
			{
				t.FadeTarget = 0f;
				t.FadeSpeed = speed;
			}
		}

		/// <summary>
		/// Sets the base volume of the currently-active (fading-in / playing) track, live. Used for
		/// real-time volume editing; does not disturb the crossfade envelope. No-op if nothing is playing.
		/// </summary>
		public void SetActiveVolume(float volume)
		{
			for (int i = _tracks.Count - 1; i >= 0; i--)
			{
				if (_tracks[i].FadeTarget > 0f)
				{
					_tracks[i].Base = Clamp01(volume);
					return;
				}
			}
		}

		public void Update(float dt)
		{
			if (_tracks.Count == 0)
				return;

			float gain = _mixer.EffectiveGain(_mixer.Music);

			for (int i = _tracks.Count - 1; i >= 0; i--)
			{
				var t = _tracks[i];
				t.FadeCurrent = MoveToward(t.FadeCurrent, t.FadeTarget, t.FadeSpeed * dt);

				if (!t.Handle.IsDisposed)
					t.Handle.Volume = t.FadeCurrent * t.Base * gain;

				// Retire a track once it has fully faded out.
				if (t.FadeTarget <= 0f && t.FadeCurrent <= 0.0001f)
				{
					t.Handle.Stop();
					t.Handle.Dispose();
					_tracks.RemoveAt(i);
				}
			}
		}

		public void Shutdown()
		{
			foreach (var t in _tracks)
			{
				t.Handle.Stop();
				t.Handle.Dispose();
			}
			_tracks.Clear();
		}

		// A zero/negative fade means "snap" — a very large per-second speed reaches the target next frame.
		private static float FadeSpeedFor(float seconds) => seconds > 0f ? 1f / seconds : 1000f;

		private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);

		private static float MoveToward(float from, float to, float maxDelta)
		{
			if (Math.Abs(to - from) <= maxDelta)
				return to;
			return from + Math.Sign(to - from) * maxDelta;
		}
	}
}
