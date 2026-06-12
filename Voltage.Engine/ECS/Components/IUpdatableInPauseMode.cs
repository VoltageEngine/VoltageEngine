namespace Voltage
{
	/// <summary>
	/// Marker interface for Components that need to work even in PauseMode. Components implementing this are treated similarly to
	/// "UI" and keep receiving <see cref="IUpdatable.Update"/> calls even while the game is Paused
	/// (<see cref="Core.IsPauseMode"/> == true), whereas regular gameplay components are frozen.
	/// </summary>
	public interface IUpdatableInPauseMode
	{
	}
}
