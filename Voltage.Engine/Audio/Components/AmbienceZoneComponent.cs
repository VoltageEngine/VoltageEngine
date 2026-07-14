using System;
using Microsoft.Xna.Framework;
using Voltage.Serialization;

namespace Voltage.Audio
{
	/// <summary>
	/// A looping ambience "bed" that fades in when a collider enters this entity's trigger and fades out on
	/// exit. Overlapping zones each keep their own voice, so their beds blend. Routed through the Ambience bus.
	/// Set <see cref="PlayOnStart"/> for a scene-wide bed; enable <see cref="Is3D"/> for a localized one.
	/// </summary>
	// IUpdatableInPauseMode: keep syncing live Volume/bus edits to the playing bed while the editor is paused.
	[ComponentId("AmbienceZoneComponent")]
	public partial class AmbienceZoneComponent : Component, ITriggerListener, IUpdatable, IUpdatableInPauseMode
	{
		/// <summary>Looping ambience clip to play (drag from the Asset Browser).</summary>
		public AssetReference Ambience;

		/// <summary>Target mixer bus name. Defaults to Ambience.</summary>
		public string Bus = "Ambience";

		/// <summary>Base volume 0..1 (before bus gain and positional attenuation). 0 = silent.</summary>
		[Range(0f, 1f)]
		public float Volume = 1f;

		/// <summary>Mutes this bed while keeping it playing (un-mute restores the volume).</summary>
		public bool Muted;

		/// <summary>Crossfade duration in seconds for fade-in on enter and fade-out on exit.</summary>
		public float FadeSeconds = 2f;

		/// <summary>Start immediately (a scene-wide bed) rather than waiting for a trigger enter.</summary>
		public bool PlayOnStart = false;

		/// <summary>Use 2D positional playback: attenuate/pan by distance to the active listener (a localized bed).</summary>
		public bool Is3D = false;

		/// <summary>Distance (world units) within which the bed is at full volume (3D only).</summary>
		public float MinDistance = 100f;

		/// <summary>Distance (world units) beyond which the bed is silent (3D only).</summary>
		public float MaxDistance = 1200f;

		/// <summary>Voice-management priority. Beds sit below gameplay SFX/dialogue so they yield first. Default 96.</summary>
		public int Priority = 96;

		private AudioClip _clip;
		private bool _clipLoadAttempted;
		private AudioVoice _voice;

		// Muted or non-positive Volume forces silence.
		private float EffectiveVolume => (Muted || Volume <= 0f) ? 0f : MathHelper.Clamp(Volume, 0f, 1f);

		public override void OnAddedToEntity()
		{
			// The editor doesn't reload the scene on Edit→Play, so OnStart won't re-run then — start auto-play
			// from this transition instead.
			Core.OnSwitchEditMode += OnEditModeChanged;
		}

		public override void OnStart()
		{
			if (!PlayOnStart && GetComponent<Collider>() == null)
				Debug.Warn($"AmbienceZoneComponent on '{Entity?.Name}' has no trigger Collider and PlayOnStart is off — it will never start.");

			if (PlayOnStart && Enabled && !Core.IsEditMode)
				FadeInBed();
		}

		private void OnEditModeChanged(bool isEditMode)
		{
			// Ignore components no longer in the live scene and self-heal by unsubscribing (mirrors AudioSource).
			if (Entity == null || Entity.Scene != Core.Scene)
			{
				Core.OnSwitchEditMode -= OnEditModeChanged;
				return;
			}

			// Entering Edit stops all audio centrally in AudioManager, so only entering Play needs handling.
			if (!isEditMode && PlayOnStart && Enabled)
				FadeInBed();
		}

		public void OnTriggerEnter(Collider other, Collider local)
		{
			if (Core.IsEditMode || !Enabled)
				return;
			FadeInBed();
		}

		public void OnTriggerExit(Collider other, Collider local) => FadeOutBed();

		public override void OnDisabled()
		{
			FadeOutBed();
		}

		/// <summary>Editor gizmo: 3D reach — inner circle = full volume (MinDistance), outer = silence (MaxDistance).</summary>
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
				// Keep syncing while merely virtual (culled by distance/capacity) so the bed can revive.
				_voice = null;
				return;
			}

			// Live-sync inspector-editable params each frame. Fades use a separate envelope, so writing
			// BaseVolume here never disturbs an in-progress fade.
			_voice.BaseVolume = EffectiveVolume;
			_voice.Bus = Core.Audio.Bus(Bus);
			_voice.Is3D = Is3D;
			_voice.MinDistance = MinDistance;
			_voice.MaxDistance = Math.Max(MaxDistance, MinDistance + 1f);
			if (Is3D)
				_voice.Position = Transform.Position;
		}

		/// <summary>Starts the bed fading in (or fades a playing bed back up to full).</summary>
		public void FadeInBed()
		{
			EnsureClipLoaded();
			if (_clip == null)
			{
				Debug.Warn($"[Ambience] '{Entity?.Name}' has no playable clip (Ambience valid={Ambience.IsValid}); nothing to play.");
				return;
			}
			if (Core.Audio == null)
				return;

			if (_voice != null && _voice.IsPlaying)
			{
				_voice.FadeTo(1f, FadeSeconds);
				return;
			}

			var settings = AudioPlaySettings.Default;
			settings.Bus = Bus;
			settings.Loop = true;
			settings.Volume = EffectiveVolume;
			settings.Pan = 0f;
			settings.Priority = Priority;
			settings.Is3D = Is3D;
			settings.Position = Transform.Position;
			settings.MinDistance = MinDistance;
			settings.MaxDistance = MaxDistance;
			settings.FadeInSeconds = FadeSeconds;

			_voice = Core.Audio.PlayControlled(_clip, settings);
		}

		/// <summary>Fades the bed out and stops it. Overlapping zones are unaffected (each owns its own voice).</summary>
		public void FadeOutBed()
		{
			_voice?.FadeOutAndStop(FadeSeconds);
			_voice = null;
		}

		public override void OnRemovedFromEntity()
		{
			Core.OnSwitchEditMode -= OnEditModeChanged;
			_voice?.Stop();
			_voice = null;
			_clip = null;
			_clipLoadAttempted = false;
		}

		private void EnsureClipLoaded()
		{
			if (_clip != null || _clipLoadAttempted)
				return;

			_clipLoadAttempted = true;
			if (Ambience.IsValid)
				_clip = Core.Audio?.LoadClip(Ambience);
		}

		// Voltage.SourceGenerators emits serialization for this partial class: public fields round-trip;
		// private runtime state (_clip, _voice, _clipLoadAttempted) is not serialized.
	}
}
