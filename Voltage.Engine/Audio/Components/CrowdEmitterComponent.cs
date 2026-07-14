using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Voltage.Serialization;
using Voltage.Utils;

namespace Voltage.Audio
{
	/// <summary>
	/// Scatters randomized one-shot "walla" (murmurs, coughs, footsteps, laughs) around this entity to bring a
	/// crowd to life. <see cref="Density"/> drives how often bits fire; each is thrown at a random offset within
	/// <see cref="Radius"/> with pitch/volume jitter, optionally spatialized via <see cref="Is3D"/>, and capped
	/// by <see cref="MaxConcurrent"/> so a busy scene never exhausts the platform voice pool.
	/// </summary>
	[ComponentId("CrowdEmitterComponent")]
	public partial class CrowdEmitterComponent : Component, IUpdatable
	{
		/// <summary>Pool of one-shot clips to pick from (drag several from the Asset Browser for variety).</summary>
		public List<AssetReference> Clips = new();

		/// <summary>Target mixer bus name. Defaults to Ambience.</summary>
		public string Bus = "Ambience";

		/// <summary>Crowd busyness 0..1: 0 = silent, 1 = busiest (shortest interval between bits).</summary>
		[Range(0f, 1f)]
		public float Density = 0.5f;

		/// <summary>Seconds between bits at <see cref="Density"/> 1 (busiest).</summary>
		public float MinInterval = 0.35f;

		/// <summary>Seconds between bits approaching <see cref="Density"/> 0 (sparsest).</summary>
		public float MaxInterval = 3f;

		/// <summary>Radius (world units) around this entity within which bits are scattered.</summary>
		public float Radius = 400f;

		/// <summary>Base volume 0..1 for each bit (before jitter and bus gain).</summary>
		[Range(0f, 1f)]
		public float Volume = 0.8f;

		/// <summary>Each bit randomizes volume by ±this amount.</summary>
		public float VolumeJitter = 0.2f;

		/// <summary>Each bit randomizes pitch by ±this amount (XNA units).</summary>
		public float PitchJitter = 0.15f;

		/// <summary>Spatialize each bit (attenuate/pan by distance to the listener) for a spread crowd.</summary>
		public bool Is3D = true;

		/// <summary>Distance (world units) within which a bit is at full volume (3D only).</summary>
		public float MinDistance = 100f;

		/// <summary>Distance (world units) beyond which a bit is silent (3D only).</summary>
		public float MaxDistance = 900f;

		/// <summary>Maximum simultaneous crowd bits — a soft voice-management cap so the pool never starves.</summary>
		public int MaxConcurrent = 8;

		/// <summary>Voice-management priority for each bit. Low by default so walla yields to gameplay/dialogue.</summary>
		public int Priority = 64;

		/// <summary>Begin scattering as soon as the game starts.</summary>
		public bool PlayOnStart = true;

		private readonly List<AudioClip> _loaded = new();
		private readonly List<AudioVoice> _active = new();
		private bool _loadAttempted;
		private bool _running;
		private float _timer;
		private float _nextInterval;

		public override void OnStart()
		{
			if (PlayOnStart && Enabled && !Core.IsEditMode)
				_running = true;
			_nextInterval = NextInterval();
		}

		public override void OnDisabled()
		{
			_running = false;
			foreach (var voice in _active)
				voice?.Stop();
			_active.Clear();
		}

		/// <summary>Editor gizmo: cyan = scatter radius (where bits originate); when 3D also draws Min/Max reach.</summary>
		public override void DebugRender(Batcher batcher)
		{
			var pos = Transform.Position;
			float thickness = Debug.Size.LineSizeMultiplier;
			batcher.DrawCircle(pos, Radius, Color.DeepSkyBlue * 0.7f, thickness, 48);

			if (Is3D)
			{
				batcher.DrawCircle(pos, MinDistance, Color.LimeGreen * 0.6f, thickness, 48);
				batcher.DrawCircle(pos, Math.Max(MaxDistance, MinDistance + 1f), Color.OrangeRed * 0.4f, thickness, 48);
			}
		}

		/// <summary>Starts or stops scattering crowd bits (already-playing bits are left to finish).</summary>
		public void SetActive(bool active) => _running = active;

		public virtual void Update()
		{
			PruneActive();

			if (!_running || Core.IsEditMode || Density <= 0f || Core.Audio == null)
				return;

			EnsureClipsLoaded();
			if (_loaded.Count == 0)
				return;

			_timer += Time.DeltaTime;
			if (_timer < _nextInterval)
				return;

			_timer = 0f;
			_nextInterval = NextInterval();

			// Respect the concurrency cap — drop this bit rather than steal a voice (crowd bits are cheap).
			if (_active.Count >= MaxConcurrent)
				return;

			SpawnOne();
		}

		private void SpawnOne()
		{
			var clip = _loaded[Random.NextInt(_loaded.Count)];
			if (clip == null)
				return;

			// Uniform point within the disc: sqrt(rand) radius avoids clustering at the center.
			float angle = Random.NextAngle();
			float r = Radius * (float)Math.Sqrt(Random.NextFloat());
			var offset = new Vector2((float)Math.Cos(angle) * r, (float)Math.Sin(angle) * r);

			var settings = AudioPlaySettings.Default;
			settings.Bus = Bus;
			settings.Loop = false;
			settings.Volume = MathHelper.Clamp(Volume + Random.Range(-VolumeJitter, VolumeJitter), 0f, 1f);
			settings.Pitch = MathHelper.Clamp(Random.Range(-PitchJitter, PitchJitter), -1f, 1f);
			settings.Priority = Priority;
			settings.Is3D = Is3D;
			settings.Position = Transform.Position + offset;
			settings.MinDistance = MinDistance;
			settings.MaxDistance = MaxDistance;

			var voice = Core.Audio.PlayControlled(clip, settings);
			if (voice != null)
				_active.Add(voice);
		}

		// Interval between bits: Density 1 -> MinInterval, Density 0 -> MaxInterval, with ±25% jitter so bits
		// don't fall on a metronome.
		private float NextInterval()
		{
			float t = MathHelper.Clamp(Density, 0f, 1f);
			float baseInterval = MathHelper.Lerp(MaxInterval, MinInterval, t);
			return Math.Max(0.05f, baseInterval * Random.Range(0.75f, 1.25f));
		}

		private void PruneActive()
		{
			for (int i = _active.Count - 1; i >= 0; i--)
				if (_active[i] == null || !_active[i].IsPlaying)
					_active.RemoveAt(i);
		}

		private void EnsureClipsLoaded()
		{
			if (_loadAttempted)
				return;

			_loadAttempted = true;
			if (Core.Audio == null || Clips == null)
				return;

			foreach (var reference in Clips)
			{
				if (!reference.IsValid)
					continue;
				var clip = Core.Audio.LoadClip(reference);
				if (clip != null)
					_loaded.Add(clip);
			}
		}

		public override void OnRemovedFromEntity()
		{
			foreach (var voice in _active)
				voice?.Stop();
			_active.Clear();
			_loaded.Clear();
			_loadAttempted = false;
			_running = false;
		}

		// Voltage.SourceGenerators emits serialization for this partial class: public fields round-trip;
		// private runtime state is not serialized.
	}
}
