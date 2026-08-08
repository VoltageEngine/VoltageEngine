using Voltage.Serialization;

namespace Voltage.Cinematics
{
	/// <summary>
	/// Maps one of a timeline asset's abstract <see cref="TimelineRole"/> slots to a concrete scene entity.
	/// Lives on the <see cref="TimelineDirector"/> (per-scene data), not on the reusable asset — which is
	/// what lets the same timeline drive different actors in different levels.
	/// </summary>
	/// <remarks>
	/// <see cref="ISerializableData"/> is required, not decorative: without it the generator does not
	/// recognise <c>List&lt;RoleBinding&gt;</c> as serializable and silently drops the director's bindings
	/// from the scene, so every role resolves to null on load.
	/// </remarks>
	public class RoleBinding : ISerializableData
	{
		/// <summary>The role name this binding satisfies (matches a <see cref="TimelineRole.Name"/>).</summary>
		public string Role;

		/// <summary>The scene entity bound to that role (GUID-based, survives renames).</summary>
		public EntityReference Entity;
	}
}
