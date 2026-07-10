using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using Voltage.Serialization;
using Voltage.Utils;

namespace Voltage.Audio
{
	/// <summary>
	/// Plays a sound through the engine <see cref="AudioManager"/>. Handles one-shots, looping ambience,
	/// and 2D positional playback (distance attenuation + horizontal pan relative to the active
	/// <see cref="AudioListenerComponent"/>). Routed through a mixer <see cref="AudioBus"/> by name, so
	/// runtime bus-volume/mute changes apply live to looping/positional voices.
	///
	/// <para>Assign <see cref="Clip"/> by dragging an audio asset onto the inspector slot; it is loaded
	/// via <c>Scene.LoadAsset&lt;SoundEffect&gt;</c> in <see cref="OnStart"/>.</para>
	/// </summary>
	// IUpdatableInPauseMode: keep syncing live inspector edits to the playing voice even while the game
	// is paused, so audio can be tuned in real time in the editor (which uses Pause to inspect).
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

		private SoundEffect _clip;
		private AudioVoice _voice;
		private bool _clipLoadAttempted;

		// Volume actually sent to the voice: forced to 0 when muted or non-positive, so Volume <= 0 (or
		// Muted) is truly silent rather than "playing quietly".
		private float EffectiveVolume => (Muted || Volume <= 0f) ? 0f : MathHelper.Clamp(Volume, 0f, 1f);

		public override void OnAddedToEntity()
		{
			// Audio only belongs to Play mode. The editor does not reload the scene on Edit→Play, so
			// OnStart does not re-run when entering Play — we start auto-play from this transition instead
			// (and never play while in Edit mode).
			Core.OnSwitchEditMode += OnEditModeChanged;
		}

		public override void OnStart()
		{
			EnsureClipLoaded();
			// Never auto-play in the editor's Edit mode. In a running game IsEditMode is always false.
			if (PlayOnStart && !Core.IsEditMode)
				Play();
		}

		private void OnEditModeChanged(bool isEditMode)
		{
			// Core.OnSwitchEditMode is a static event. A scene reload swaps in a new Scene without tearing
			// down the old components, so a stale component can stay subscribed and would otherwise spawn an
			// orphan, untracked voice on the next Play (heard as audio that won't fully mute). Ignore any
			// component that is no longer part of the live scene, and self-heal by unsubscribing.
			if (Entity == null || Entity.Scene != Core.Scene)
			{
				Core.OnSwitchEditMode -= OnEditModeChanged;
				return;
			}

			// Entering Play mode: (re)start auto-play. Entering Edit mode: the AudioManager stops all audio
			// centrally (and the scene typically reloads), so there is nothing to do here.
			if (!isEditMode && PlayOnStart)
				Play();
		}

		public virtual void Update()
		{
			if (_voice == null)
				return;

			if (!_voice.IsPlaying)
			{
				// Finished/stopped — drop the reference so we don't keep syncing a dead voice.
				_voice = null;
				return;
			}

			// Live-sync inspector-editable parameters into the playing voice every frame, so edits made
			// in the editor are heard immediately — no save/reload. This mirrors how colliders recompute
			// their shape from live properties each frame for real-time visual feedback; here it is audio
			// feedback. (Loop is structural — set when the voice is created — so changing it needs a replay.)
			_voice.BaseVolume = EffectiveVolume;
			_voice.BasePitch = MathHelper.Clamp(Pitch, -1f, 1f);
			_voice.BasePan = MathHelper.Clamp(Pan, -1f, 1f);
			_voice.Bus = Core.Audio.Bus(Bus);
			_voice.Is3D = Is3D;
			_voice.MinDistance = MinDistance;
			_voice.MaxDistance = Math.Max(MaxDistance, MinDistance + 1f);

			if (Is3D)
				_voice.Position = Transform.Position;
		}

		/// <summary>Plays the clip. Looping/positional sounds are tracked as a controllable voice.</summary>
		public void Play()
		{
			EnsureClipLoaded();
			if (_clip == null)
			{
				Debug.Warn($"[AudioSource] '{Entity?.Name}' has no playable clip (Clip valid={Clip.IsValid}); nothing to play.");
				return;
			}
			if (Core.Audio == null)
				return;

			float pitch = Pitch;
			if (RandomPitchRange > 0f)
				pitch = MathHelper.Clamp(Pitch + Random.Range(-RandomPitchRange, RandomPitchRange), -1f, 1f);

			// Always play through a single controllable voice so the sound can be live-tuned from the
			// inspector (volume/pitch/pan/bus/3D). Replace any existing voice — one component owns one
			// voice at a time (Unity's AudioSource.Play() semantic). This also prevents an untracked
			// leftover voice (from a repeated Play) continuing to sound while the tracked one is muted.
			// For overlapping fire-and-forget SFX, call Core.Audio.PlaySfx directly instead.
			_voice?.Stop();

			var settings = AudioPlaySettings.Default;
			settings.Bus = Bus;
			settings.Volume = EffectiveVolume;
			settings.Pitch = pitch;
			settings.Pan = Pan;
			settings.Loop = Loop;
			settings.Is3D = Is3D;
			settings.Position = Transform.Position;
			settings.MinDistance = MinDistance;
			settings.MaxDistance = MaxDistance;

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
			if (Clip.IsValid && Core.Scene != null)
				_clip = Core.Scene.LoadAsset<SoundEffect>(Clip);
		}

		#region Serialization

		public class AudioSourceComponentData : ComponentData
		{
			// AssetReference is decomposed into primitives: the reflection-based JSON deserializer used
			// for engine components (Persistence.Json.FromJson) can't convert a string back into the
			// Guid field of AssetReference. Data get/set rebuilds the AssetReference from these.
			public string ClipGuid;
			public string ClipPath;
			public string ClipName;
			public string Bus = "SFX";
			public float Volume = 1f;
			public bool Muted;
			public float Pitch = 0f;
			public float Pan = 0f;
			public bool Loop = false;
			public bool PlayOnStart = true;
			public bool Is3D = false;
			public float MinDistance = 100f;
			public float MaxDistance = 800f;
			public float RandomPitchRange = 0f;
		}

		private AudioSourceComponentData _data = new AudioSourceComponentData();

		public override ComponentData Data
		{
			get
			{
				_data ??= new AudioSourceComponentData();
				_data.ClipGuid = Clip.AssetGuid == Guid.Empty ? null : Clip.AssetGuid.ToString();
				_data.ClipPath = Clip.AssetPath;
				_data.ClipName = Clip.AssetName;
				_data.Bus = Bus;
				_data.Volume = Volume;
				_data.Muted = Muted;
				_data.Pitch = Pitch;
				_data.Pan = Pan;
				_data.Loop = Loop;
				_data.PlayOnStart = PlayOnStart;
				_data.Is3D = Is3D;
				_data.MinDistance = MinDistance;
				_data.MaxDistance = MaxDistance;
				_data.RandomPitchRange = RandomPitchRange;
				_data.Enabled = Enabled;
				return _data;
			}
			set
			{
				if (value is AudioSourceComponentData d)
				{
					_data = d;
					Clip = new AssetReference
					{
						AssetGuid = Guid.TryParse(d.ClipGuid, out var g) ? g : Guid.Empty,
						AssetPath = d.ClipPath,
						AssetName = d.ClipName,
					};
					Bus = d.Bus;
					Volume = d.Volume;
					Muted = d.Muted;
					Pitch = d.Pitch;
					Pan = d.Pan;
					Loop = d.Loop;
					PlayOnStart = d.PlayOnStart;
					Is3D = d.Is3D;
					MinDistance = d.MinDistance;
					MaxDistance = d.MaxDistance;
					RandomPitchRange = d.RandomPitchRange;
					Enabled = d.Enabled;
				}
			}
		}

		#endregion
	}
}
