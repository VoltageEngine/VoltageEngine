using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using Voltage.Utils;

namespace Voltage.Audio
{
	/// <summary>
	/// Parameters for starting a controlled voice via <see cref="AudioManager.PlayControlled"/>.
	/// </summary>
	public struct AudioPlaySettings
	{
		public string Bus;
		public float Volume;
		public float Pitch;
		public float Pan;
		public bool Loop;

		// Positional (2D spatial): distance attenuation + horizontal pan relative to the listener.
		public bool Is3D;
		public Vector2 Position;
		public float MinDistance;
		public float MaxDistance;

		/// <summary>Sensible defaults: SFX bus, full volume, non-looping, non-positional.</summary>
		public static AudioPlaySettings Default => new AudioPlaySettings
		{
			Bus = "SFX",
			Volume = 1f,
			Pitch = 0f,
			Pan = 0f,
			Loop = false,
			Is3D = false,
			MinDistance = 100f,
			MaxDistance = 800f,
		};
	}

	/// <summary>
	/// The engine's audio hub, accessed via <see cref="Core.Audio"/>. Registered as a
	/// <see cref="GlobalManager"/>, so it ticks every frame independently of the scene — music and UI
	/// sounds keep playing across scene changes and while paused (per-entity sources freeze their own
	/// updates during pause, which is the desired split).
	///
	/// <para>Owns the active <see cref="IAudioBackend"/>, the <see cref="AudioMixer"/> bus tree, the
	/// <see cref="MusicChannel"/>, listener state, and the controlled <see cref="AudioVoice"/>s whose
	/// volume/pan it recomputes each frame.</para>
	/// </summary>
	public sealed class AudioManager : GlobalManager
	{
		public IAudioBackend Backend { get; private set; }
		public AudioMixer Mixer { get; }

		private readonly MusicChannel _music;
		private readonly List<AudioVoice> _voices = new();

		// Active 2D listener (set by AudioListenerComponent). Only one is used at a time.
		private Vector2 _listenerPosition;
		private bool _hasListener;

		// When true, the SFX/Ambience/Voice buses pause while the game is in PauseMode; Music/UI keep
		// playing. Toggle via PauseGameplayAudioOnPause.
		private bool _pauseGameplayBusesOnPause = true;
		private bool _gameplayPaused;

		public AudioManager(IAudioBackend backend = null)
		{
			Backend = backend ?? new MonoGameAudioBackend();
			Backend.Init();
			Mixer = new AudioMixer();
			_music = new MusicChannel(Backend, Mixer);

			Core.OnSwitchPauseMode += OnPauseModeChanged;
			Core.OnSwitchEditMode += OnEditModeChanged;
		}

		/// <summary>Whether gameplay buses (SFX/Ambience/Voice) pause while the game is in PauseMode.</summary>
		public bool PauseGameplayAudioOnPause
		{
			get => _pauseGameplayBusesOnPause;
			set => _pauseGameplayBusesOnPause = value;
		}

		#region Mixer convenience

		public AudioBus Bus(string name) => Mixer.GetBus(name);
		public void SetBusVolume(string name, float volume) => Mixer.GetBus(name).Volume = MathHelper.Clamp(volume, 0f, 1f);
		public float GetBusVolume(string name) => Mixer.GetBus(name).Volume;
		public void SetBusMute(string name, bool mute) => Mixer.GetBus(name).Mute = mute;

		#endregion

		#region Listener

		/// <summary>Sets the world position of the audio listener used for positional attenuation/pan.</summary>
		public void SetListener(Vector2 position)
		{
			_listenerPosition = position;
			_hasListener = true;
		}

		/// <summary>Clears the active listener (positional sounds fall back to their base volume/pan).</summary>
		public void ClearListener() => _hasListener = false;

		#endregion

		#region Playback

		/// <summary>
		/// Fire-and-forget, non-positional one-shot with bus gain baked in at play time. Ideal for UI
		/// clicks and brief SFX that need no further control.
		/// </summary>
		public void PlaySfx(SoundEffect clip, string bus = "SFX", float volume = 1f, float pitch = 0f, float pan = 0f)
		{
			if (clip == null)
				return;

			var targetBus = Mixer.GetBus(bus);
			float gain = Mixer.EffectiveGain(targetBus);
			if (gain <= 0f)
			{
				Debug.Log($"[Audio] PlaySfx on '{targetBus.Name}' produced no sound: effective gain 0 " +
					$"(IsAudioOn={Core.IsAudioOn}, busVol={targetBus.Volume}, masterVol={Mixer.Master.Volume}).");
				return;
			}

			Backend.PlayOneShot(clip, volume * gain, pitch, pan);
		}

		/// <summary>
		/// Starts and returns a controlled voice (looping ambience, positional sound, or any sound the
		/// caller wants a live handle on). Volume/pan are recomputed each frame from bus gain and, when
		/// <see cref="AudioPlaySettings.Is3D"/> is set, distance to the listener.
		/// </summary>
		public AudioVoice PlayControlled(SoundEffect clip, AudioPlaySettings settings)
		{
			if (clip == null)
				return null;

			var handle = Backend.CreateHandle(clip, settings.Loop);
			var voice = new AudioVoice
			{
				Handle = handle,
				Bus = Mixer.GetBus(settings.Bus),
				BaseVolume = MathHelper.Clamp(settings.Volume, 0f, 1f),
				BasePitch = MathHelper.Clamp(settings.Pitch, -1f, 1f),
				BasePan = MathHelper.Clamp(settings.Pan, -1f, 1f),
				Is3D = settings.Is3D,
				Position = settings.Position,
				MinDistance = settings.MinDistance,
				MaxDistance = Math.Max(settings.MaxDistance, settings.MinDistance + 1f),
				Loop = settings.Loop,
			};

			ApplyVoice(voice);
			handle.Play();
			_voices.Add(voice);

			if (voice.Is3D && _hasListener)
			{
				float dist = Vector2.Distance(voice.Position, _listenerPosition);
				Debug.LogIf(dist > voice.MaxDistance,
					$"[Audio] 3D source started {dist:0} units from the listener, beyond MaxDistance " +
					$"{voice.MaxDistance:0} — it will be silent until closer. Increase MaxDistance or disable Is3D.");
			}

			return voice;
		}

		public void PlayMusic(SoundEffect clip, float volume = 1f, float fadeSeconds = 1.5f)
			=> _music.Play(clip, MathHelper.Clamp(volume, 0f, 1f), fadeSeconds);

		public void StopMusic(float fadeSeconds = 1.5f) => _music.Stop(fadeSeconds);

		/// <summary>Sets the currently-playing track's base volume live (does not affect the Music bus).</summary>
		public void SetMusicVolume(float volume) => _music.SetActiveVolume(volume);

		public bool IsMusicPlaying => _music.IsPlaying;

		#endregion

		#region Ducking

		/// <summary>
		/// Momentarily attenuates <paramref name="bus"/> to <paramref name="targetMultiplier"/> (0..1),
		/// recovering to full over <paramref name="recoverySeconds"/>. Typical use: duck Music/SFX while
		/// dialogue plays on the Voice bus.
		/// </summary>
		public void Duck(string bus, float targetMultiplier, float recoverySeconds)
		{
			var b = Mixer.GetBus(bus);
			b.DuckMultiplier = MathHelper.Clamp(targetMultiplier, 0f, 1f);
			_duckRecovery[b] = recoverySeconds > 0f ? 1f / recoverySeconds : 1000f;
		}

		private readonly Dictionary<AudioBus, float> _duckRecovery = new();

		private void UpdateDucking(float dt)
		{
			if (_duckRecovery.Count == 0)
				return;

			_duckRecoveryDone.Clear();
			foreach (var kvp in _duckRecovery)
			{
				var bus = kvp.Key;
				bus.DuckMultiplier = Math.Min(1f, bus.DuckMultiplier + kvp.Value * dt);
				if (bus.DuckMultiplier >= 1f)
					_duckRecoveryDone.Add(bus);
			}
			foreach (var bus in _duckRecoveryDone)
				_duckRecovery.Remove(bus);
		}

		private readonly List<AudioBus> _duckRecoveryDone = new();

		#endregion

		#region Update loop

		public override void Update()
		{
			// Unscaled time: fades and ducking must not stretch under slow-mo/pause.
			float dt = Time.UnscaledDeltaTime;

			_music.Update(dt);
			UpdateDucking(dt);
			UpdateVoices();
		}

		private void UpdateVoices()
		{
			for (int i = _voices.Count - 1; i >= 0; i--)
			{
				var voice = _voices[i];

				bool finished =
					voice.StopRequested ||
					voice.Handle == null ||
					voice.Handle.IsDisposed ||
					(!voice.Loop && voice.Handle.State == AudioPlayState.Stopped);

				if (finished)
				{
					if (voice.Handle != null && !voice.Handle.IsDisposed)
					{
						voice.Handle.Stop();
						voice.Handle.Dispose();
					}
					_voices.RemoveAt(i);
					continue;
				}

				ApplyVoice(voice);
			}
		}

		// Recomputes and writes a voice's output volume/pan/pitch from base + bus + position.
		private void ApplyVoice(AudioVoice voice)
		{
			float gain = Mixer.EffectiveGain(voice.Bus);
			float positionalFactor = 1f;
			float pan = voice.BasePan;

			if (voice.Is3D && _hasListener)
				ComputeSpatial(voice, out positionalFactor, out pan);

			voice.Handle.Volume = voice.BaseVolume * gain * positionalFactor;
			voice.Handle.Pan = pan;
			voice.Handle.Pitch = voice.BasePitch;
		}

		// 2D spatialization: linear distance attenuation between Min/Max, plus a horizontal pan from the
		// emitter's offset to the listener. Hand-computed (not XNA Apply3D) for predictable 2D behavior
		// and full Min/Max control.
		private void ComputeSpatial(AudioVoice voice, out float attenuation, out float pan)
		{
			Vector2 delta = voice.Position - _listenerPosition;
			float distance = delta.Length();

			if (distance <= voice.MinDistance)
				attenuation = 1f;
			else if (distance >= voice.MaxDistance)
				attenuation = 0f;
			else
				attenuation = 1f - (distance - voice.MinDistance) / (voice.MaxDistance - voice.MinDistance);

			// Pan by horizontal offset (normalized against MaxDistance), added to the voice's base pan so
			// an explicitly panned source still leans that way.
			float spatialPan = MathHelper.Clamp(delta.X / voice.MaxDistance, -1f, 1f);
			pan = MathHelper.Clamp(voice.BasePan + spatialPan, -1f, 1f);
		}

		private void OnEditModeChanged(bool isEditMode)
		{
			// Audio is Play-mode only; returning to Edit mode silences everything centrally so nothing
			// leaks into editing.
			if (isEditMode)
				StopAll();
		}

		/// <summary>Immediately stops and disposes every controlled voice and all music.</summary>
		public void StopAll()
		{
			foreach (var voice in _voices)
			{
				if (voice.Handle != null && !voice.Handle.IsDisposed)
				{
					voice.Handle.Stop();
					voice.Handle.Dispose();
				}
			}
			_voices.Clear();
			_music.Shutdown();
		}

		private void OnPauseModeChanged(bool isPaused)
		{
#if EDITOR
			// In the editor, Pause (F2) freezes the game to inspect/tune, so keep audio playing for
			// real-time adjustment. The game build (no EDITOR) still suspends gameplay audio below.
			return;
#else
			if (!_pauseGameplayBusesOnPause)
				return;

			if (isPaused && !_gameplayPaused)
			{
				_gameplayPaused = true;
				SetGameplayVoicesPaused(true);
			}
			else if (!isPaused && _gameplayPaused)
			{
				_gameplayPaused = false;
				SetGameplayVoicesPaused(false);
			}
#endif
		}

		private void SetGameplayVoicesPaused(bool paused)
		{
			foreach (var voice in _voices)
			{
				// Music runs on its own channel; pause only non-Music/UI voices.
				if (voice.Bus == Mixer.Music || voice.Bus == Mixer.Ui)
					continue;

				if (paused)
					voice.Pause();
				else
					voice.Resume();
			}
		}

		#endregion

		public void Shutdown()
		{
			Core.OnSwitchPauseMode -= OnPauseModeChanged;
			Core.OnSwitchEditMode -= OnEditModeChanged;

			foreach (var voice in _voices)
			{
				if (voice.Handle != null && !voice.Handle.IsDisposed)
				{
					voice.Handle.Stop();
					voice.Handle.Dispose();
				}
			}
			_voices.Clear();
			_music.Shutdown();
			Backend.Shutdown();
		}
	}
}
