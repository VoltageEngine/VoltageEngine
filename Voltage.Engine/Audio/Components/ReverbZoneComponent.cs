namespace Voltage.Audio
{
	/// <summary>
	/// Turns on the global reverb with a chosen character while a collider is inside this entity's trigger.
	/// Sources feed the reverb via their own <c>ReverbSend</c>; this zone just sets room size / damping / wet
	/// and enables it, disabling on exit. Reverb is a software-mixing-backend feature; on the MonoGame backend
	/// this is a harmless no-op. Requires a trigger <see cref="Collider"/> on the same entity.
	/// </summary>
	// IUpdatableInPauseMode: re-apply while inside so inspector edits are heard live even when paused.
	[ComponentId("ReverbZoneComponent")]
	public partial class ReverbZoneComponent : Component, ITriggerListener, IUpdatable, IUpdatableInPauseMode
	{
		/// <summary>Reverb size / decay length (0 = small room, 1 = large hall).</summary>
		[Range(0f, 1f)] public float RoomSize = 0.5f;

		/// <summary>High-frequency damping of the tail (0 = bright, 1 = dark/muffled).</summary>
		[Range(0f, 1f)] public float Damping = 0.5f;

		/// <summary>Wet level mixed into the output (0..1).</summary>
		[Range(0f, 1f)] public float Wet = 0.3f;

		/// <summary>Turn the reverb off when leaving the zone (otherwise it persists until another zone changes it).</summary>
		public bool DisableOnExit = true;

		private bool _inside;
		private float _lastRoom = -1f, _lastDamp = -1f, _lastWet = -1f;

		public override void OnStart()
		{
			if (GetComponent<Collider>() == null)
				Debug.Error($"ReverbZoneComponent requires a Collider component on the same Entity (Name = {Entity.Name})!");
		}

		public void OnTriggerEnter(Collider other, Collider local)
		{
			if (Core.IsEditMode || !Enabled || Core.Audio == null)
				return;

			Core.Audio.SetReverb(RoomSize, Damping, Wet);
			_inside = true;
		}

		public void OnTriggerExit(Collider other, Collider local)
		{
			_inside = false;
			if (Core.IsEditMode || Core.Audio == null)
				return;

			if (DisableOnExit)
				Core.Audio.SetReverbEnabled(false);
		}

		public virtual void Update()
		{
			if (!_inside || Core.Audio == null)
				return;
			if (RoomSize == _lastRoom && Damping == _lastDamp && Wet == _lastWet)
				return;

			_lastRoom = RoomSize; _lastDamp = Damping; _lastWet = Wet;
			Core.Audio.SetReverb(RoomSize, Damping, Wet);
		}

		public override void OnDisabled()
		{
			if (_inside && DisableOnExit)
				Core.Audio?.SetReverbEnabled(false);
			_inside = false;
		}

		// Voltage.SourceGenerators emits serialization for this partial class: public fields round-trip;
		// private runtime state (_inside) is not serialized.
	}
}
