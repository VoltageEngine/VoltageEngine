using System;
using Microsoft.Xna.Framework;
using Voltage.Serialization;
using Voltage.Utils;

namespace Voltage.Audio
{
	/// <summary>
	/// Plays a sound through the engine <see cref="AudioManager"/> — one-shots, looping ambience, and 2D
	/// positional playback (distance attenuation + pan relative to the active
	/// <see cref="AudioListenerComponent"/>). Routed through a named mixer <see cref="AudioBus"/>, so bus
	/// volume/mute changes apply live to looping/positional voices.
	/// </summary>
	// IUpdatableInPauseMode: keep syncing live inspector edits to the playing voice while the editor is paused.
	[ComponentId("AudioSourceComponent")]
	public partial class AudioSourceComponent : Component, IUpdatable, IUpdatableInPauseMode
	{
		/// <summary>Audio clip to play (drag from the Asset Browser).</summary>
		public AssetReference Clip;

		/// <summary>Target mixer bus name (Master/Music/SFX/UI/Ambience/Voice). Defaults to SFX.</summary>
		public string Bus = "SFX";

		/// <summary>Base volume 0..1 (before bus gain and positional attenuation). 0 = silent.</summary>
		[Range(0f, 1f)]
		public float Volume = 1f;

		/// <summary>Mutes this source while keeping it playing (un-mute restores the volume).</summary>
		public bool Muted;

		/// <summary>Pitch shift -1..1 (XNA units; 0 = unchanged).</summary>
		[Range(-1f, 1f)]
		public float Pitch = 0f;

		/// <summary>Stereo pan -1..1 (ignored for 3D sources, which pan by position).</summary>
		[Range(-1f, 1f)]
		public float Pan = 0f;

		/// <summary>Loop the sound (looping sources create a persistent, controllable voice).</summary>
		public bool Loop = false;

		/// <summary>Play automatically when the component starts.</summary>
		public bool PlayOnStart = true;

		/// <summary>Use 2D positional playback: attenuate/pan by distance to the active listener.</summary>
		public bool Is3D = false;

		/// <summary>Distance (world units) within which the sound is at full volume.</summary>
		public float MinDistance = 100f;

		/// <summary>Distance (world units) beyond which the sound is silent.</summary>
		public float MaxDistance = 800f;

		/// <summary>If &gt; 0, each one-shot randomizes pitch by ±this amount (footstep/hit variation).</summary>
		public float RandomPitchRange = 0f;

		/// <summary>Voice-management priority: higher survives when the voice pool is full. Default 128 (mid).</summary>
		public int Priority = 128;

		/// <summary>Max simultaneous voices of this clip across the whole mix (0 = unlimited).</summary>
		public int MaxInstances = 0;

		/// <summary>Low-pass cutoff in Hz for occlusion/muffling (0 = open/no filter). Software backend only.</summary>
		public float LowPassCutoffHz = 0f;

		/// <summary>How much this source feeds the global reverb, 0..1. Software backend only.</summary>
		[Range(0f, 1f)]
		public float ReverbSend = 0f;

		private AudioClip _clip;
		private AudioVoice _voice;
		private bool _clipLoadAttempted;

		// Muted or non-positive Volume forces silence rather than quiet playback.
		private float EffectiveVolume => (Muted || Volume <= 0f) ? 0f : MathHelper.Clamp(Volume, 0f, 1f);

		public override void OnAddedToEntity()
		{
			// The editor doesn't reload the scene on Edit→Play, so OnStart won't re-run then — start auto-play
			// from this transition instead.
			Core.OnSwitchEditMode += OnEditModeChanged;
		}

		public override void OnStart()
		{
			EnsureClipLoaded();
			if (PlayOnStart && Enabled && !Core.IsEditMode)
				Play();
		}

		private void OnEditModeChanged(bool isEditMode)
		{
			// This static event outlives scene reloads; a stale component left subscribed would spawn an
			// orphan, untracked voice on the next Play. Ignore components no longer in the live scene and
			// self-heal by unsubscribing.
			if (Entity == null || Entity.Scene != Core.Scene)
			{
				Core.OnSwitchEditMode -= OnEditModeChanged;
				return;
			}

			// Entering Edit stops all audio centrally in AudioManager, so only entering Play needs handling.
			if (!isEditMode && PlayOnStart && Enabled)
				Play();
		}

		public override void OnDisabled()
		{
			Stop();
		}

		/// <summary>Editor gizmo: draws the 3D reach — inner circle = full volume (MinDistance), outer = silence (MaxDistance).</summary>
		public override void DebugRender(Batcher batcher)
		{
			if (!Is3D)
				return;

			var pos = Transform.Position;
			float thickness = Debug.Size.LineSizeMultiplier;
			batcher.DrawCircle(pos, MinDistance, Color.LimeGreen * 0.7f, thickness, 48);
			batcher.DrawCircle(pos, Math.Max(MaxDistance, MinDistance + 1f), Color.OrangeRed * 0.5f, thickness, 48);
		}

		public virtual void Update()
		{
			if (_voice == null)
				return;

			if (!_voice.IsAlive)
			{
				// Finished/stopped — drop the reference. (A merely virtual voice — culled by distance or
				// capacity — is still Alive and kept, so it keeps syncing for revival.)
				_voice = null;
				return;
			}

			// Live-sync inspector-editable params each frame. (Loop is structural — set at voice creation —
			// so changing it needs a replay.)
			_voice.BaseVolume = EffectiveVolume;
			_voice.BasePitch = MathHelper.Clamp(Pitch, -1f, 1f);
			_voice.BasePan = MathHelper.Clamp(Pan, -1f, 1f);
			_voice.Bus = Core.Audio.Bus(Bus);
			_voice.LowPassCutoffHz = LowPassCutoffHz;
			_voice.ReverbSend = ReverbSend;
			_voice.Is3D = Is3D;
			_voice.MinDistance = MinDistance;
			_voice.MaxDistance = Math.Max(MaxDistance, MinDistance + 1f);

			if (Is3D)
				_voice.Position = Transform.Position;
		}

		/// <summary>Plays the clip. Looping/positional sounds are tracked as a controllable voice.</summary>
		public void Play() => Play(0f);

		/// <summary>
		/// Plays the clip, optionally fading in over <paramref name="fadeInSeconds"/> (0 = instant).
		/// Looping/positional sounds are tracked as a controllable voice.
		/// </summary>
		public void Play(float fadeInSeconds)
		{
			EnsureClipLoaded();
			if (_clip == null)
			{
				Debug.Warn($"[AudioSource] '{Entity?.Name}' has no playable clip (Clip valid={Clip.IsValid}); nothing to play.");
				return;
			}
			if (Core.Audio == null)
				return;

			// One component owns one controllable (live-tunable) voice at a time, replacing any existing one
			// (Unity's AudioSource.Play semantic). For overlapping fire-and-forget SFX, call Core.Audio.PlaySfx.
			_voice?.Stop();

			var settings = BuildSettings();
			settings.FadeInSeconds = fadeInSeconds;
			_voice = Core.Audio.PlayControlled(_clip, settings);
		}

		// Builds play settings from the current inspector values (applying per-shot pitch randomization).
		private AudioPlaySettings BuildSettings()
		{
			float pitch = Pitch;
			if (RandomPitchRange > 0f)
				pitch = MathHelper.Clamp(Pitch + Random.Range(-RandomPitchRange, RandomPitchRange), -1f, 1f);

			var settings = AudioPlaySettings.Default;
			settings.Bus = Bus;
			settings.Volume = EffectiveVolume;
			settings.Pitch = pitch;
			settings.Pan = Pan;
			settings.Loop = Loop;
			settings.Priority = Priority;
			settings.MaxInstances = MaxInstances;
			settings.LowPassCutoffHz = LowPassCutoffHz;
			settings.ReverbSend = ReverbSend;
			settings.Is3D = Is3D;
			settings.Position = Transform.Position;
			settings.MinDistance = MinDistance;
			settings.MaxDistance = MaxDistance;
			return settings;
		}

		/// <summary>Starts the source fading in over <paramref name="seconds"/> (or fades a playing voice back to full).</summary>
		public void FadeIn(float seconds)
		{
			if (_voice != null && _voice.IsPlaying)
			{
				_voice.FadeTo(1f, seconds);
				return;
			}
			Play(seconds);
		}

		/// <summary>Fades the current voice to silence over <paramref name="seconds"/>, then stops it.</summary>
		public void FadeOut(float seconds)
		{
			_voice?.FadeOutAndStop(seconds);
			_voice = null;
		}

		/// <summary>Fades the current voice toward <paramref name="level"/> (0..1 fraction of this source's Volume) over <paramref name="seconds"/>.</summary>
		public void FadeTo(float level, float seconds) => _voice?.FadeTo(level, seconds);

		/// <summary>
		/// Smoothly swaps to <paramref name="newClip"/>: the current voice fades out and stops while the new
		/// clip fades in over <paramref name="seconds"/> — e.g. blending one looping ambience into another.
		/// </summary>
		public void CrossfadeTo(AssetReference newClip, float seconds)
		{
			_voice?.FadeOutAndStop(seconds);
			_voice = null;

			Clip = newClip;
			_clip = null;
			_clipLoadAttempted = false;
			EnsureClipLoaded();
			if (_clip == null || Core.Audio == null)
				return;

			var settings = BuildSettings();
			settings.FadeInSeconds = seconds;
			_voice = Core.Audio.PlayControlled(_clip, settings);
		}

		/// <summary>Stops the looping/positional voice (no effect on already-fired one-shots).</summary>
		public void Stop()
		{
			_voice?.Stop();
			_voice = null;
		}

		public override void OnRemovedFromEntity()
		{
			Core.OnSwitchEditMode -= OnEditModeChanged;
			Stop();
			_clip = null;
			_clipLoadAttempted = false;
		}

		private void EnsureClipLoaded()
		{
			if (_clip != null || _clipLoadAttempted)
				return;

			_clipLoadAttempted = true;
			if (Clip.IsValid)
				_clip = Core.Audio?.LoadClip(Clip);
		}

		// Voltage.SourceGenerators emits serialization for this partial class: public fields round-trip;
		// private runtime state (_clip, _voice, _clipLoadAttempted) is not serialized.
	}
}
