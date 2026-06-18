using System.Collections.Generic;

namespace Voltage.Utils.Audio
{
	/// <summary>
	/// Lightweight registry that connects <see cref="IAudioComponent"/> instances to the
	/// global <see cref="Core.OnSwitchAudio"/> event.
	///
	/// Components call <see cref="Register"/> on creation and <see cref="Unregister"/> on
	/// destruction; the registry broadcasts <see cref="IAudioComponent.OnAudioStateChanged"/>
	/// to every live component whenever the mute state flips.
	///
	/// Thread safety: all calls must come from the game/update thread (standard for ECS components).
	/// </summary>
	public static class AudioComponentRegistry
	{
		// Weak references would be safer in a long-running editor session, but the
		// explicit Register/Unregister contract is simpler and consistent with how
		// other engine registries (e.g. GlobalManager) work.
		private static readonly List<IAudioComponent> _components = new();
		private static bool _subscribed = false;

		/// <summary>
		/// Register a component so it receives future audio-state change notifications.
		/// Safe to call more than once for the same instance (idempotent).
		/// </summary>
		public static void Register(IAudioComponent component)
		{
			EnsureSubscribed();

			if (!_components.Contains(component))
				_components.Add(component);
		}

		/// <summary>
		/// Unregister a component.  Must be called when the component is destroyed or
		/// removed from its entity to avoid stale references.
		/// </summary>
		public static void Unregister(IAudioComponent component)
		{
			_components.Remove(component);
		}

		/// <summary>
		/// Notifies all registered components of the current audio state.
		/// Called automatically via <see cref="Core.OnSwitchAudio"/>; also available for
		/// manual invocation (e.g. after late registration).
		/// </summary>
		public static void BroadcastAudioState(bool isAudioOn)
		{
			// Iterate a copy so that OnAudioStateChanged implementations can safely
			// call Unregister without invalidating the enumeration.
			var snapshot = _components.ToArray();
			foreach (var comp in snapshot)
				comp.OnAudioStateChanged(isAudioOn);
		}

		private static void EnsureSubscribed()
		{
			if (_subscribed)
				return;

			_subscribed = true;
			Core.OnSwitchAudio += BroadcastAudioState;
		}
	}
}
