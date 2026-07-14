using System;
using System.Collections.Generic;

namespace Voltage.Audio
{
	/// <summary>
	/// Smoothly shifts the whole mixer to a "mood" when a collider enters this entity's trigger. Each per-bus
	/// slider is that mood's target volume; on enter the mixer crossfades every bus toward these values over
	/// <see cref="FadeSeconds"/>. Master is left alone (the player's global volume). Requires a trigger
	/// <see cref="Collider"/> on the same entity.
	/// </summary>
	// IUpdatableInPauseMode: re-apply the mood while inside so slider edits are heard live even when paused.
	[ComponentId("MixerSnapshotZoneComponent")]
	public partial class MixerSnapshotZoneComponent : Component, ITriggerListener, IUpdatable, IUpdatableInPauseMode
	{
		/// <summary>Target Music-bus volume for this mood.</summary>
		[Range(0f, 1f)] public float Music = 1f;

		/// <summary>Target SFX-bus volume for this mood.</summary>
		[Range(0f, 1f)] public float Sfx = 1f;

		/// <summary>Target UI-bus volume for this mood.</summary>
		[Range(0f, 1f)] public float Ui = 1f;

		/// <summary>Target Ambience-bus volume for this mood.</summary>
		[Range(0f, 1f)] public float Ambience = 1f;

		/// <summary>Target Voice-bus volume for this mood.</summary>
		[Range(0f, 1f)] public float Voice = 1f;

		/// <summary>Crossfade duration in seconds when entering the mood.</summary>
		public float FadeSeconds = 2f;

		/// <summary>Restore the mixer to how it was before entering, on exit (for a transient mood zone).</summary>
		public bool RestoreOnExit = false;

		private AudioSnapshot _previous;
		private bool _inside;
		private float _lastMusic = -1f, _lastSfx = -1f, _lastUi = -1f, _lastAmbience = -1f, _lastVoice = -1f;

		public override void OnStart()
		{
			if (GetComponent<Collider>() == null)
				Debug.Error($"MixerSnapshotZoneComponent requires a Collider component on the same Entity (Name = {Entity.Name})!");
		}

		public void OnTriggerEnter(Collider other, Collider local)
		{
			if (Core.IsEditMode || !Enabled || Core.Audio == null)
				return;

			if (RestoreOnExit)
				_previous = Core.Audio.CaptureSnapshot();

			Core.Audio.TransitionTo(BuildSnapshot(), FadeSeconds);
			_inside = true;
		}

		public void OnTriggerExit(Collider other, Collider local)
		{
			_inside = false;
			if (Core.IsEditMode || Core.Audio == null)
				return;

			if (RestoreOnExit && _previous != null)
				Core.Audio.TransitionTo(_previous, FadeSeconds);
		}

		public virtual void Update()
		{
			// Re-apply only when a slider actually changed (after the enter crossfade), avoiding a per-frame alloc.
			if (!_inside || Core.Audio == null || Core.Audio.IsTransitioning)
				return;
			if (Music == _lastMusic && Sfx == _lastSfx && Ui == _lastUi && Ambience == _lastAmbience && Voice == _lastVoice)
				return;

			_lastMusic = Music; _lastSfx = Sfx; _lastUi = Ui; _lastAmbience = Ambience; _lastVoice = Voice;
			Core.Audio.TransitionTo(BuildSnapshot(), 0f);
		}

		private AudioSnapshot BuildSnapshot()
		{
			var volumes = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase)
			{
				["Music"] = Music,
				["SFX"] = Sfx,
				["UI"] = Ui,
				["Ambience"] = Ambience,
				["Voice"] = Voice,
			};
			return new AudioSnapshot(volumes);
		}

		// Voltage.SourceGenerators emits serialization for this partial class: public fields round-trip;
		// private runtime state (_previous) is not serialized.
	}
}
