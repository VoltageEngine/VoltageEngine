using System;
using Microsoft.Xna.Framework;
using Voltage.Serialization;

namespace Voltage.Audio
{
	/// <summary>
	/// Starts a music track when a collider enters this entity's trigger collider — the building block for
	/// location-based music. Requires a trigger <see cref="Collider"/> on the same entity. Music plays via
	/// <see cref="AudioManager.PlayMusic"/>, which crossfades from whatever was playing.
	/// </summary>
	// IUpdatableInPauseMode: keep pushing live Volume edits to the current music while the editor is paused.
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

		private AudioClip _track;
		private bool _trackLoadAttempted;
		private bool _inside;

		// Muted or non-positive Volume forces silence.
		private float EffectiveVolume => (Muted || Volume <= 0f) ? 0f : MathHelper.Clamp(Volume, 0f, 1f);

		public override void OnStart()
		{
			if(GetComponent<Collider>() == null)
				Debug.Error($"MusicZoneComponent requires a Collider component on the same Entity (Name = {Entity.Name})!");
		}

		public void OnTriggerEnter(Collider other, Collider local)
		{
			if (Core.IsEditMode || !Enabled)
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
			// Keep the live music volume synced to the inspector while inside the zone.
			if (_inside)
				Core.Audio?.SetMusicVolume(EffectiveVolume);
		}

		private void EnsureTrackLoaded()
		{
			if (_track != null || _trackLoadAttempted)
				return;

			_trackLoadAttempted = true;
			if (Track.IsValid)
				_track = Core.Audio?.LoadClip(Track);
		}

		public override void OnRemovedFromEntity()
		{
			_track = null;
			_trackLoadAttempted = false;
		}

		// Voltage.SourceGenerators emits serialization for this partial class: public fields round-trip;
		// private runtime state (_track, _trackLoadAttempted, _inside) is not serialized.
	}
}
