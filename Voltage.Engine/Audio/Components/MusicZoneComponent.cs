using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using Voltage.Serialization;

namespace Voltage.Audio
{
	/// <summary>
	/// Starts a music track when a collider enters this entity's trigger collider — the building block for
	/// location-based music. Requires a trigger <see cref="Collider"/> on the same entity. Music plays via
	/// <see cref="AudioManager.PlayMusic"/>, which crossfades from whatever was playing.
	/// </summary>
	// IUpdatableInPauseMode: keep pushing live Volume edits to the current music while paused, so it can be
	// tuned in real time in the editor (which pauses to inspect).
	[ComponentId("MusicZoneComponent")]
	public partial class MusicZoneComponent : Component, ITriggerListener, IUpdatable, IUpdatableInPauseMode
	{
		/// <summary>Music track to play on enter (drag from the Asset Browser).</summary>
		public AssetReference Track;

		/// <summary>Music volume 0..1 on the Music bus. 0 = silent.</summary>
		[Range(0f, 1f)]
		public float Volume = 1f;

		/// <summary>Mutes this zone's music while keeping it playing (un-mute restores the volume).</summary>
		public bool Muted;

		/// <summary>Crossfade duration in seconds when entering the zone.</summary>
		public float FadeSeconds = 2f;

		/// <summary>Stop the music when leaving the zone (otherwise it keeps playing until another zone takes over).</summary>
		public bool StopOnExit = false;

		private SoundEffect _track;
		private bool _trackLoadAttempted;
		private bool _inside;

		// Music volume applied: forced to 0 when muted or non-positive, so Volume <= 0 (or Muted) is truly silent.
		private float EffectiveVolume => (Muted || Volume <= 0f) ? 0f : MathHelper.Clamp(Volume, 0f, 1f);

		public override void OnStart()
		{
			if(GetComponent<Collider>() == null)
				Debug.Error($"MusicZoneComponent requires a Collider component on the same Entity (Name = {Entity.Name})!");
		}

		public void OnTriggerEnter(Collider other, Collider local)
		{
			// Never start music in Edit mode.
			if (Core.IsEditMode)
				return;

			EnsureTrackLoaded();
			if (_track != null)
			{
				Core.Audio?.PlayMusic(_track, EffectiveVolume, FadeSeconds);
				_inside = true;
			}
		}

		public void OnTriggerExit(Collider other, Collider local)
		{
			_inside = false;
			if (StopOnExit)
				Core.Audio?.StopMusic(FadeSeconds);
		}

		public virtual void Update()
		{
			// While inside the zone, keep the live music volume synced to the inspector Volume so edits are
			// heard immediately — no re-trigger. (The Music-bus volume is separately live via the mixer.)
			if (_inside)
				Core.Audio?.SetMusicVolume(EffectiveVolume);
		}

		private void EnsureTrackLoaded()
		{
			if (_track != null || _trackLoadAttempted)
				return;

			_trackLoadAttempted = true;
			if (Track.IsValid && Core.Scene != null)
				_track = Core.Scene.LoadAsset<SoundEffect>(Track);
		}

		public override void OnRemovedFromEntity()
		{
			_track = null;
			_trackLoadAttempted = false;
		}

		// Serialization is emitted by Voltage.SourceGenerators for this partial class: the public
		// AssetReference Track and tunable fields round-trip automatically. Runtime state (_track,
		// _trackLoadAttempted, _inside) stays private and is not serialized.
	}
}
