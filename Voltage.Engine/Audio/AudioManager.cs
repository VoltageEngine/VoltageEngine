using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Voltage.Serialization;
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

		/// <summary>If &gt; 0, the voice starts silent and fades up to full over this many seconds (no click).</summary>
		public float FadeInSeconds;

		/// <summary>Higher wins when the voice pool is full (0..255-ish; ties broken by loudness). Default 128.</summary>
		public int Priority;

		/// <summary>Max simultaneous voices of this same clip (0 = unlimited). Excess is stolen or dropped.</summary>
		public int MaxInstances;

		/// <summary>Per-voice low-pass cutoff in Hz for occlusion/muffling (0 = open). Software backend only.</summary>
		public float LowPassCutoffHz;

		/// <summary>Per-voice reverb send amount 0..1. Software backend only.</summary>
		public float ReverbSend;

		// Positional (2D spatial): distance attenuation + horizontal pan relative to the listener.
		public bool Is3D;
		public Vector2 Position;
		public float MinDistance;
		public float MaxDistance;

		/// <summary>Sensible defaults: SFX bus, full volume, non-looping, non-positional, mid priority.</summary>
		public static AudioPlaySettings Default => new AudioPlaySettings
		{
			Bus = "SFX",
			Volume = 1f,
			Pitch = 0f,
			Pan = 0f,
			Loop = false,
			Priority = 128,
			MaxInstances = 0,
			Is3D = false,
			MinDistance = 100f,
			MaxDistance = 800f,
		};
	}

	/// <summary>
	/// The engine's audio hub, accessed via <see cref="Core.Audio"/>. A <see cref="GlobalManager"/>, so it
	/// ticks independently of the scene — music and UI sounds keep playing across scene changes and while
	/// paused. Owns the active <see cref="IAudioBackend"/>, the <see cref="AudioMixer"/> bus tree, the
	/// <see cref="MusicChannel"/>, listener state, and the controlled <see cref="AudioVoice"/>s.
	/// </summary>
	public sealed class AudioManager : GlobalManager
	{
		public IAudioBackend Backend { get; private set; }
		public AudioMixer Mixer { get; }

		/// <summary>
		/// Maximum simultaneous real (audible) controlled voices before priority-based stealing kicks in.
		/// Music is not counted. Kept below the ~32-source platform ceiling with headroom for one-shots and music.
		/// </summary>
		public int MaxVoices = 24;

		private readonly MusicChannel _music;
		private readonly List<AudioVoice> _voices = new();

		// Active 2D listener (set by AudioListenerComponent). Only one is used at a time.
		private Vector2 _listenerPosition;
		private bool _hasListener;

		// When true, SFX/Ambience/Voice buses pause in PauseMode; Music/UI keep playing.
		private bool _pauseGameplayBusesOnPause = true;
		private bool _gameplayPaused;

		/// <summary>
		/// When true, the default backend prefers the software mixer where supported, falling back to MonoGame.
		/// Must be set <b>before</b> the AudioManager is constructed.
		/// </summary>
		public static bool PreferSoftwareBackend = false;

		private static IAudioBackend CreateDefaultBackend()
		{
			if (PreferSoftwareBackend)
			{
				try
				{
					if (SoftwareMixingAudioBackend.IsSupported)
					{
						Debug.Log("[Audio] Using SoftwareMixingAudioBackend (software mixing + DSP-ready).");
						return new SoftwareMixingAudioBackend();
					}
					Debug.Warn("[Audio] Software backend unsupported on this platform; using MonoGameAudioBackend.");
				}
				catch (Exception ex)
				{
					Debug.Warn($"[Audio] Software backend probe failed ({ex.Message}); using MonoGameAudioBackend.");
				}
			}
			return new MonoGameAudioBackend();
		}

		public AudioManager(IAudioBackend backend = null)
		{
			Backend = backend ?? CreateDefaultBackend();
			Backend.Init();
			Mixer = new AudioMixer();
			_music = new MusicChannel(Backend, Mixer);

			// Dialogue ducks Music + Ambience by default.
			_dialogueDuckBuses.Add(Mixer.Music);
			_dialogueDuckBuses.Add(Mixer.Ambience);

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

		/// <summary>Fire-and-forget, non-positional one-shot with bus gain baked in at play time.</summary>
		public void PlaySfx(AudioClip clip, string bus = "SFX", float volume = 1f, float pitch = 0f, float pan = 0f)
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
		/// Starts and returns a controlled voice with a live handle. Volume/pan are recomputed each frame from
		/// bus gain and, when <see cref="AudioPlaySettings.Is3D"/> is set, distance to the listener.
		/// </summary>
		public AudioVoice PlayControlled(AudioClip clip, AudioPlaySettings settings)
		{
			if (clip == null)
				return null;

			var voice = new AudioVoice
			{
				Clip = clip,
				Bus = Mixer.GetBus(settings.Bus),
				BaseVolume = MathHelper.Clamp(settings.Volume, 0f, 1f),
				BasePitch = MathHelper.Clamp(settings.Pitch, -1f, 1f),
				BasePan = MathHelper.Clamp(settings.Pan, -1f, 1f),
				Priority = settings.Priority,
				Is3D = settings.Is3D,
				Position = settings.Position,
				MinDistance = settings.MinDistance,
				MaxDistance = Math.Max(settings.MaxDistance, settings.MinDistance + 1f),
				Loop = settings.Loop,
				PendingFadeInSeconds = settings.FadeInSeconds,
				LowPassCutoffHz = settings.LowPassCutoffHz,
				ReverbSend = settings.ReverbSend,
			};

			// Per-clip instance cap: steal the weakest same-clip voice if the newcomer outranks it; otherwise
			// drop a one-shot, or let a loop wait virtual.
			if (settings.MaxInstances > 0 && InstanceCount(clip) >= settings.MaxInstances)
			{
				var weakest = WeakestInstance(clip);
				if (weakest != null && Importance(voice) > Importance(weakest))
					RetireOrVirtualize(weakest);
				else if (!settings.Loop)
					return null;
			}

			// Distance culling + global cap: an audible newcomer takes (or steals) a real slot; else a loop
			// waits virtual and a one-shot is dropped.
			if (IsAudible(voice) && TryEnsureRealSlot(voice))
				MakeReal(voice);
			else if (settings.Loop)
				voice.IsVirtual = true;
			else
				return null;

			_voices.Add(voice);
			return voice;
		}

		public void PlayMusic(AudioClip clip, float volume = 1f, float fadeSeconds = 1.5f)
			=> _music.Play(clip, MathHelper.Clamp(volume, 0f, 1f), fadeSeconds);

		/// <summary>Loads a clip in the active backend's representation. Returns null if unresolved.</summary>
		public AudioClip LoadClip(AssetReference reference) => Backend.LoadClip(reference);

		public void StopMusic(float fadeSeconds = 1.5f) => _music.Stop(fadeSeconds);

		/// <summary>Sets the currently-playing track's base volume live (does not affect the Music bus).</summary>
		public void SetMusicVolume(float volume) => _music.SetActiveVolume(volume);

		public bool IsMusicPlaying => _music.IsPlaying;

		#endregion

		#region Ducking

		/// <summary>
		/// Momentarily attenuates <paramref name="bus"/> to <paramref name="targetMultiplier"/> (0..1),
		/// recovering to full over <paramref name="recoverySeconds"/>.
		/// </summary>
		public void Duck(string bus, float targetMultiplier, float recoverySeconds)
		{
			var b = Mixer.GetBus(bus);
			b.TransientDuck = MathHelper.Clamp(targetMultiplier, 0f, 1f);
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
				bus.TransientDuck = Math.Min(1f, bus.TransientDuck + kvp.Value * dt);
				if (bus.TransientDuck >= 1f)
					_duckRecoveryDone.Add(bus);
			}
			foreach (var bus in _duckRecoveryDone)
				_duckRecovery.Remove(bus);
		}

		private readonly List<AudioBus> _duckRecoveryDone = new();

		#endregion

		#region Auto-duck on dialogue

		/// <summary>Enable automatic ducking of Music/Ambience while dialogue plays on the Voice bus.</summary>
		public bool AutoDuckDialogueEnabled = false;

		/// <summary>Multiplier the ducked buses are held at while dialogue plays (0..1). Default 0.35.</summary>
		public float DialogueDuckLevel = 0.35f;

		/// <summary>How quickly the duck engages when dialogue starts (seconds). Default 0.15.</summary>
		public float DialogueAttackSeconds = 0.15f;

		/// <summary>How quickly the duck releases after dialogue ends (seconds). Default 0.5.</summary>
		public float DialogueReleaseSeconds = 0.5f;

		private readonly List<AudioBus> _dialogueDuckBuses = new();

		// Ref-counted manual dialogue holds, for lines with no tracked Voice-bus voice (e.g. text-only dialogue).
		private int _dialogueHolds;

		/// <summary>Chooses which buses dialogue ducks (by name). Defaults to Music + Ambience.</summary>
		public void SetDialogueDuckBuses(params string[] busNames)
		{
			_dialogueDuckBuses.Clear();
			if (busNames == null)
				return;
			foreach (var name in busNames)
				_dialogueDuckBuses.Add(Mixer.GetBus(name));
		}

		/// <summary>Manually holds the dialogue duck (ref-counted). Pair with <see cref="EndDialogueDuck"/>.</summary>
		public void BeginDialogueDuck() => _dialogueHolds++;

		/// <summary>Releases one manual dialogue-duck hold.</summary>
		public void EndDialogueDuck()
		{
			if (_dialogueHolds > 0)
				_dialogueHolds--;
		}

		// Active while any manual hold is set, or any real Voice-bus voice is playing.
		private bool DialogueActive()
		{
			if (_dialogueHolds > 0)
				return true;

			for (int i = 0; i < _voices.Count; i++)
			{
				var v = _voices[i];
				if (!v.IsVirtual && v.Bus == Mixer.Voice && v.IsPlaying)
					return true;
			}
			return false;
		}

		private void UpdateAutoDuck(float dt)
		{
			bool active = AutoDuckDialogueEnabled && DialogueActive();
			float target = active ? MathHelper.Clamp(DialogueDuckLevel, 0f, 1f) : 1f;
			float seconds = active ? DialogueAttackSeconds : DialogueReleaseSeconds;
			float speed = seconds > 0f ? 1f / seconds : 1000f;

			for (int i = 0; i < _dialogueDuckBuses.Count; i++)
			{
				var bus = _dialogueDuckBuses[i];
				bus.SustainedDuck = MoveToward(bus.SustainedDuck, target, speed * dt);
			}
		}

		private static float MoveToward(float from, float to, float maxDelta)
		{
			if (Math.Abs(to - from) <= maxDelta)
				return to;
			return from + Math.Sign(to - from) * maxDelta;
		}

		#endregion

		#region Profiling

		/// <summary>When true, logs a software-mixer stats line every ~2s. No-op unless the software backend is active.</summary>
		public bool LogAudioStats = false;
		private float _statsLogTimer;

		/// <summary>
		/// Fills <paramref name="stats"/> with software-mixer profiling counters. Returns false when the software
		/// mixer isn't the active backend.
		/// </summary>
		public bool TryGetSoftwareAudioStats(out AudioProfilerStats stats)
		{
			if (Backend is SoftwareMixingAudioBackend sw)
			{
				stats = sw.GetStats();
				return true;
			}
			stats = default;
			return false;
		}

		private void UpdateStatsLog(float dt)
		{
			if (Backend is not SoftwareMixingAudioBackend sw)
				return;

			_statsLogTimer += dt;
			if (_statsLogTimer < 2f)
				return;
			_statsLogTimer = 0f;

			var s = sw.GetStats();
			Debug.Log($"[Audio] mix {s.MixAvgMs:0.00}ms / {s.BudgetMs:0.0}ms ({s.LoadPercent:0}% budget) · " +
				$"peak {s.MixPeakMs:0.00}ms · voices {s.ActiveVoices} · underruns {s.Underruns}");
		}

		#endregion

		#region Reverb (software backend only)

		/// <summary>Whether the active backend can apply DSP reverb (only the software mixing backend can).</summary>
		public bool ReverbSupported => Backend is SoftwareMixingAudioBackend;

		/// <summary>Sets and enables the global reverb (room size / damping / wet, all 0..1). No-op on backends without DSP.</summary>
		public void SetReverb(float roomSize, float damping, float wet)
		{
			if (Backend is SoftwareMixingAudioBackend sw)
				sw.Mixer.SetReverb(roomSize, damping, wet, enabled: true);
		}

		/// <summary>Enables/disables the global reverb (no-op on backends without DSP).</summary>
		public void SetReverbEnabled(bool enabled)
		{
			if (Backend is SoftwareMixingAudioBackend sw)
				sw.Mixer.SetReverbEnabled(enabled);
		}

		#endregion

		#region Snapshots

		private readonly Dictionary<string, AudioSnapshot> _snapshots = new(StringComparer.OrdinalIgnoreCase);

		private struct BusTransition { public AudioBus Bus; public float Start; public float Target; }
		private BusTransition[] _transitionSteps = new BusTransition[8];
		private int _transitionStepCount;
		private float _transitionElapsed;
		private float _transitionDuration;
		private bool _transitioning;

		/// <summary>True while a snapshot transition is in progress.</summary>
		public bool IsTransitioning => _transitioning;

		/// <summary>
		/// Captures the current mixer bus volumes as a snapshot. Master is excluded by default so a later
		/// transition never overwrites the player's global volume; pass <paramref name="includeMaster"/> to include it.
		/// </summary>
		public AudioSnapshot CaptureSnapshot(bool includeMaster = false)
		{
			var volumes = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
			foreach (var bus in Mixer.Buses)
			{
				if (!includeMaster && bus == Mixer.Master)
					continue;
				volumes[bus.Name] = bus.Volume;
			}
			return new AudioSnapshot(volumes);
		}

		/// <summary>Captures the current mixer volumes and stores them under <paramref name="name"/> for later transition.</summary>
		public void SaveSnapshot(string name, bool includeMaster = false)
		{
			if (string.IsNullOrEmpty(name))
				return;
			_snapshots[name] = CaptureSnapshot(includeMaster);
		}

		/// <summary>Whether a snapshot has been stored under <paramref name="name"/>.</summary>
		public bool HasSnapshot(string name) => !string.IsNullOrEmpty(name) && _snapshots.ContainsKey(name);

		/// <summary>Forgets a stored snapshot.</summary>
		public bool RemoveSnapshot(string name) => _snapshots.Remove(name);

		/// <summary>
		/// Transitions the mixer to a stored snapshot over <paramref name="seconds"/> (0 = instant). Returns
		/// false if no snapshot is stored under <paramref name="name"/>.
		/// </summary>
		public bool TransitionTo(string name, float seconds)
		{
			if (string.IsNullOrEmpty(name) || !_snapshots.TryGetValue(name, out var snapshot))
				return false;
			TransitionTo(snapshot, seconds);
			return true;
		}

		/// <summary>
		/// Smoothstep-transitions each captured bus volume toward <paramref name="snapshot"/> over
		/// <paramref name="seconds"/> (0 = instant). Interrupting starts from the current volumes.
		/// </summary>
		public void TransitionTo(AudioSnapshot snapshot, float seconds)
		{
			if (snapshot == null)
				return;

			if (seconds <= 0f)
			{
				foreach (var kvp in snapshot.BusVolumes)
					Mixer.GetBus(kvp.Key).Volume = MathHelper.Clamp(kvp.Value, 0f, 1f);
				_transitioning = false;
				return;
			}

			_transitionStepCount = 0;
			foreach (var kvp in snapshot.BusVolumes)
			{
				if (_transitionStepCount >= _transitionSteps.Length)
					Array.Resize(ref _transitionSteps, _transitionSteps.Length * 2);
				var bus = Mixer.GetBus(kvp.Key);
				_transitionSteps[_transitionStepCount++] = new BusTransition
				{
					Bus = bus,
					Start = bus.Volume,
					Target = MathHelper.Clamp(kvp.Value, 0f, 1f),
				};
			}

			_transitionElapsed = 0f;
			_transitionDuration = seconds;
			_transitioning = true;
		}

		private void UpdateSnapshotTransition(float dt)
		{
			if (!_transitioning)
				return;

			_transitionElapsed += dt;
			float t = _transitionDuration > 0f ? MathHelper.Clamp(_transitionElapsed / _transitionDuration, 0f, 1f) : 1f;
			float eased = t * t * (3f - 2f * t);

			for (int i = 0; i < _transitionStepCount; i++)
			{
				var step = _transitionSteps[i];
				step.Bus.Volume = MathHelper.Lerp(step.Start, step.Target, eased);
			}

			if (t >= 1f)
				_transitioning = false;
		}

		#endregion

		#region Update loop

		public override void Update()
		{
			// Unscaled: fades and ducking must not stretch under slow-mo/pause.
			float dt = Time.UnscaledDeltaTime;

			_music.Update(dt);
			UpdateDucking(dt);
			UpdateAutoDuck(dt);
			UpdateSnapshotTransition(dt);
			UpdateVoices(dt);

			if (LogAudioStats)
				UpdateStatsLog(dt);
		}

		private void UpdateVoices(float dt)
		{
			// 1. Advance fades and retire finished real voices; virtual voices leave only on explicit Stop().
			for (int i = _voices.Count - 1; i >= 0; i--)
			{
				var voice = _voices[i];

				if (voice.IsVirtual)
				{
					// Virtual voices are inaudible — a requested stop removes them immediately, nothing to fade.
					if (voice.StopRequested || voice.StopAfterFade)
						_voices.RemoveAt(i);
					continue;
				}

				// Advance the fade first, so a fade-out-and-stop retires the same frame.
				voice.AdvanceFade(dt);

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

			// 2. Reconcile real/virtual membership by audibility + capacity. Skipped while gameplay audio is
			//    paused so we never spin up a handle during PauseMode.
			if (!_gameplayPaused)
				ReconcileVoices();
		}

		// Demotes real loops that went inaudible, then promotes audible virtual loops, stealing weaker real
		// voices where the newcomer outranks them.
		private void ReconcileVoices()
		{
			for (int i = 0; i < _voices.Count; i++)
			{
				var v = _voices[i];
				if (!v.IsVirtual && v.Loop && !IsAudible(v))
					DemoteToVirtual(v);
			}

			for (int i = 0; i < _voices.Count; i++)
			{
				var v = _voices[i];
				if (!v.IsVirtual || !IsAudible(v))
					continue;
				if (TryEnsureRealSlot(v))
					MakeReal(v);
			}
		}

		// Recomputes and writes a real voice's output volume/pan/pitch.
		private void ApplyVoice(AudioVoice voice)
		{
			if (voice.Handle == null || voice.Handle.IsDisposed)
				return;

			float output = VoiceOutput(voice, out float pan);
			voice.Handle.Volume = output;
			voice.Handle.Pan = pan;
			voice.Handle.Pitch = voice.BasePitch;
			voice.Handle.LowPassCutoffHz = voice.LowPassCutoffHz;
			voice.Handle.ReverbSend = voice.ReverbSend;
		}

		#region Voice management

		// Output volume: base * fade * bus gain * positional. Pure math, valid for a virtual voice too so it
		// can be ranked for revival/stealing without a live handle.
		private float VoiceOutput(AudioVoice voice, out float pan)
		{
			float gain = Mixer.EffectiveGain(voice.Bus);
			float positionalFactor = 1f;
			pan = voice.BasePan;

			if (voice.Is3D && _hasListener)
				ComputeSpatial(voice, out positionalFactor, out pan);

			return voice.BaseVolume * voice.FadeVolume * gain * positionalFactor;
		}

		// Steal ranking: priority dominates, loudness breaks ties. Higher = keep, lower = steal first.
		private float Importance(AudioVoice voice)
		{
			float output = VoiceOutput(voice, out _);
			return voice.Priority * 1000f + output * 100f;
		}

		// Audible = non-positional (or no listener), or 3D within MaxDistance of the listener.
		private bool IsAudible(AudioVoice voice)
		{
			if (!voice.Is3D || !_hasListener)
				return true;
			return Vector2.Distance(voice.Position, _listenerPosition) <= voice.MaxDistance;
		}

		private int RealVoiceCount()
		{
			int n = 0;
			for (int i = 0; i < _voices.Count; i++)
				if (!_voices[i].IsVirtual)
					n++;
			return n;
		}

		private int InstanceCount(AudioClip clip)
		{
			int n = 0;
			for (int i = 0; i < _voices.Count; i++)
				if (_voices[i].Clip == clip)
					n++;
			return n;
		}

		private AudioVoice WeakestReal()
		{
			AudioVoice weakest = null;
			float min = float.MaxValue;
			for (int i = 0; i < _voices.Count; i++)
			{
				var v = _voices[i];
				if (v.IsVirtual)
					continue;
				float imp = Importance(v);
				if (imp < min) { min = imp; weakest = v; }
			}
			return weakest;
		}

		private AudioVoice WeakestInstance(AudioClip clip)
		{
			AudioVoice weakest = null;
			float min = float.MaxValue;
			for (int i = 0; i < _voices.Count; i++)
			{
				var v = _voices[i];
				if (v.Clip != clip)
					continue;
				float imp = Importance(v);
				if (imp < min) { min = imp; weakest = v; }
			}
			return weakest;
		}

		// Ensures a real slot for `incoming`, stealing the weakest real voice if the pool is full and the
		// newcomer outranks it. False when the pool is full of more-important voices.
		private bool TryEnsureRealSlot(AudioVoice incoming)
		{
			if (RealVoiceCount() < MaxVoices)
				return true;

			var weakest = WeakestReal();
			if (weakest != null && Importance(incoming) > Importance(weakest))
			{
				RetireOrVirtualize(weakest);
				return true;
			}
			return false;
		}

		// Frees a real slot: a loop demotes to virtual (kept for revival); a one-shot is stopped and removed.
		private void RetireOrVirtualize(AudioVoice voice)
		{
			if (voice.Loop)
			{
				DemoteToVirtual(voice);
			}
			else
			{
				if (voice.Handle != null && !voice.Handle.IsDisposed)
				{
					voice.Handle.Stop();
					voice.Handle.Dispose();
				}
				voice.Handle = null;
				_voices.Remove(voice);
			}
		}

		// Creates the backend handle and begins playback. A first start honors the requested fade-in; a revive
		// from virtual uses a short fade so the loop doesn't click back in.
		private void MakeReal(AudioVoice voice)
		{
			voice.Handle = Backend.CreateHandle(voice.Clip, voice.Loop);
			if (voice.Handle == null)
			{
				// Backend can't play this clip representation — retire rather than NRE.
				voice.StopRequested = true;
				return;
			}
			voice.IsVirtual = false;

			float fadeIn = voice.PendingFadeInSeconds;
			if (fadeIn <= 0f && voice.HasBeenReal)
				fadeIn = 0.06f;
			voice.PendingFadeInSeconds = 0f;
			voice.HasBeenReal = true;

			if (fadeIn > 0f)
			{
				voice.FadeVolume = 0f;
				voice.FadeTo(1f, fadeIn);
			}

			ApplyVoice(voice);
			voice.Handle.Play();
		}

		// Drops the real handle but keeps the voice tracked as virtual (revived by ReconcileVoices).
		private void DemoteToVirtual(AudioVoice voice)
		{
			if (voice.Handle != null && !voice.Handle.IsDisposed)
			{
				voice.Handle.Stop();
				voice.Handle.Dispose();
			}
			voice.Handle = null;
			voice.IsVirtual = true;
		}

		#endregion

		// 2D spatialization: linear distance attenuation between Min/Max plus a horizontal pan from the
		// emitter's offset. Hand-computed (not XNA Apply3D) for predictable 2D behavior and full Min/Max control.
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

			// Pan by horizontal offset (normalized to MaxDistance), added to base pan.
			float spatialPan = MathHelper.Clamp(delta.X / voice.MaxDistance, -1f, 1f);
			pan = MathHelper.Clamp(voice.BasePan + spatialPan, -1f, 1f);
		}

		private void OnEditModeChanged(bool isEditMode)
		{
			// Audio is Play-mode only; silence everything when returning to Edit mode.
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
			// In the editor, Pause (F2) freezes the game to inspect/tune — keep audio playing. The game build
			// still suspends gameplay audio below.
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
				// Pause only non-Music/UI voices.
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
