using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using Voltage.Serialization;

namespace Voltage.Audio
{
	/// <summary>
	/// Starts a music track when a collider enters this entity's trigger collider — the building block
	/// for location-based music ("this cave has its own theme"). Requires a <see cref="Collider"/> set
	/// as a trigger on the same entity; trigger callbacks fire when the overlapping entity moves via the
	/// engine's <c>Mover</c> (see <see cref="ITriggerListener"/>).
	///
	/// <para>Music is played through <see cref="AudioManager.PlayMusic"/>, which crossfades from whatever
	/// was playing, so moving between zones blends smoothly.</para>
	/// </summary>
	// IUpdatableInPauseMode: keep pushing live Volume edits to the current music even while paused, so it
	// can be tuned in real time in the editor (which uses Pause to inspect).
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

		// Music volume actually applied: forced to 0 when muted or non-positive, so Volume <= 0 (or Muted)
		// is truly silent.
		private float EffectiveVolume => (Muted || Volume <= 0f) ? 0f : MathHelper.Clamp(Volume, 0f, 1f);

		public override void OnStart()
		{
			if(GetComponent<Collider>() == null)
				Debug.Error($"MusicZoneComponent requires a Collider component on the same Entity (Name = {Entity.Name})!");
		}

		public void OnTriggerEnter(Collider other, Collider local)
		{
			// Never start music in the editor's Edit mode.
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
			// While the triggering entity is inside this zone, keep the live music volume in sync with the
			// inspector Volume so edits are heard immediately — no re-trigger needed. Mirrors the live-sync
			// on AudioSourceComponent. (The Music-bus volume is separately live via the mixer.)
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

		#region Serialization

		public class MusicZoneComponentData : ComponentData
		{
			// AssetReference decomposed to primitives — see AudioSourceComponentData for why.
			public string TrackGuid;
			public string TrackPath;
			public string TrackName;
			public float Volume = 1f;
			public bool Muted;
			public float FadeSeconds = 2f;
			public bool StopOnExit = false;
		}

		private MusicZoneComponentData _data = new MusicZoneComponentData();

		public override ComponentData Data
		{
			get
			{
				_data ??= new MusicZoneComponentData();
				_data.TrackGuid = Track.AssetGuid == Guid.Empty ? null : Track.AssetGuid.ToString();
				_data.TrackPath = Track.AssetPath;
				_data.TrackName = Track.AssetName;
				_data.Volume = Volume;
				_data.Muted = Muted;
				_data.FadeSeconds = FadeSeconds;
				_data.StopOnExit = StopOnExit;
				_data.Enabled = Enabled;
				return _data;
			}
			set
			{
				if (value is MusicZoneComponentData d)
				{
					_data = d;
					Track = new AssetReference
					{
						AssetGuid = Guid.TryParse(d.TrackGuid, out var g) ? g : Guid.Empty,
						AssetPath = d.TrackPath,
						AssetName = d.TrackName,
					};
					Volume = d.Volume;
					Muted = d.Muted;
					FadeSeconds = d.FadeSeconds;
					StopOnExit = d.StopOnExit;
					Enabled = d.Enabled;
				}
			}
		}

		#endregion
	}
}
