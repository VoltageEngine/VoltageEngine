namespace Voltage.Cinematics
{
	/// <summary>
	/// The evaluation context a <see cref="TimelineDirector"/> hands to tracks each frame. Decouples track
	/// evaluation from the concrete director: a track only needs to resolve its role to a live entity
	/// (whether pre-bound or spawned) and reach the scene.
	/// </summary>
	public interface ITimelineContext
	{
		/// <summary>The scene the timeline is playing in.</summary>
		Scene Scene { get; }

		/// <summary>Resolves a role name to its live entity (bound actor or active spawnable), or null.</summary>
		Entity ResolveRole(string role);
	}
}
