namespace Voltage.Audio
{
	/// <summary>
	/// Marks its entity as the audio listener: the reference point positional
	/// <see cref="AudioSourceComponent"/>s attenuate and pan against. Typically added to the player or
	/// the camera. If several exist, the last one to update each frame wins.
	/// </summary>
	public partial class AudioListenerComponent : Component, IUpdatable
	{
		public virtual void Update()
		{
			Core.Audio?.SetListener(Transform.Position);
		}

		public override void OnRemovedFromEntity()
		{
			// Only clear if no other listener has since taken over (best-effort; last-writer-wins otherwise).
			Core.Audio?.ClearListener();
		}

		#region Serialization

		private ComponentData _data = new ListenerComponentData();

		// A distinct, non-abstract ComponentData subtype so the component still round-trips (it has no
		// tunable fields of its own beyond the base Enabled flag).
		public class ListenerComponentData : ComponentData
		{
		}

		public override ComponentData Data
		{
			get
			{
				_data ??= new ListenerComponentData();
				_data.Enabled = Enabled;
				return _data;
			}
			set
			{
				if (value is ListenerComponentData d)
				{
					_data = d;
					Enabled = d.Enabled;
				}
			}
		}

		#endregion
	}
}
