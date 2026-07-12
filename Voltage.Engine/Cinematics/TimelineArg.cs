using Voltage.Serialization;

namespace Voltage.Cinematics
{
	/// <summary>
	/// A serializable argument for a timeline method call, restricted to AOT-safe primitives plus an
	/// entity reference. The source-generated dispatch reads these positionally into a
	/// <c>[TimelineEvent]</c> method's parameters.
	/// </summary>
	public class TimelineArg
	{
		public TimelineArgType Type;

		/// <summary>Backing value for <see cref="TimelineArgType.Float"/> and <see cref="TimelineArgType.Int"/>.</summary>
		public float Number;
		public bool Bool;
		public string Text;

		/// <summary>GUID-based entity reference (used for <see cref="TimelineArgType.Entity"/>).</summary>
		public EntityReference Entity;

		/// <summary>Reference to a specific component on an entity (<see cref="TimelineArgType.Component"/>).</summary>
		public ComponentReference Component;

		/// <summary>Reference to a project asset — texture, sound, data file (<see cref="TimelineArgType.Asset"/>).</summary>
		public AssetReference Asset;

		/// <summary>Reference to a prefab (<see cref="TimelineArgType.Prefab"/>).</summary>
		public PrefabReference Prefab;

		public float AsFloat() => Number;
		public int AsInt() => (int)Number;
		public bool AsBool() => Bool;
		public string AsString() => Text;
		public EntityReference AsEntity() => Entity;
		public ComponentReference AsComponentReference() => Component;
		public AssetReference AsAssetReference() => Asset;
		public PrefabReference AsPrefabReference() => Prefab;
	}
}
