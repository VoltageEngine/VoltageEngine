using System;

namespace Voltage.Serialization
{
	/// <summary>
	/// Declares that this component class (or generated data type) was previously known under
	/// one or more different fully-qualified names. When serialized scenes or prefabs reference
	/// an old name that no longer resolves directly, the engine checks <see cref="TypeRenameRegistry"/>
	/// (which is populated from this attribute at module initialization time) and maps the old
	/// name to the current type so the component loads without data loss.
	///
	/// Apply to the component class; the source generator propagates rename registrations to
	/// the generated data type automatically.
	///
	/// Chained renames are supported: if a class was renamed twice, stack two attributes
	/// (or pass multiple names to the same attribute) to cover all historical names.
	///
	/// <example>
	/// <code>
	/// // Class was "Jolt.Scripts.Enemies.DroneController", then "Jolt.Scripts.Enemies.DroneComponent".
	/// // Now it lives here:
	/// [FormerlyKnownAs("Jolt.Scripts.Enemies.DroneController",
	///                   "Jolt.Scripts.Enemies.DroneComponent")]
	/// public partial class DroneAI : Component { ... }
	/// </code>
	/// </example>
	///
	/// Mirrors the spirit of <see cref="global::Voltage.Persistence.DecodeAliasAttribute"/>,
	/// which handles renamed FIELDS within a component's data JSON. Use <c>[FormerlyKnownAs]</c>
	/// for TYPE-level renames (class/namespace moves); use <c>[DecodeAlias]</c> for FIELD-level
	/// renames inside the data class.
	/// </summary>
	[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
	public sealed class FormerlyKnownAsAttribute : Attribute
	{
		/// <summary>
		/// The old fully-qualified type names that map to the annotated class.
		/// Each string must be the exact value previously stored in <c>ComponentTypeName</c>
		/// or <c>DataTypeName</c> in a scene/prefab JSON file.
		/// </summary>
		public string[] OldNames { get; }

		/// <param name="oldFullyQualifiedNames">
		/// One or more old fully-qualified type names (e.g.
		/// <c>"Jolt.Scripts.Enemies.DroneComponent"</c>).
		/// </param>
		public FormerlyKnownAsAttribute(params string[] oldFullyQualifiedNames)
		{
			if (oldFullyQualifiedNames == null || oldFullyQualifiedNames.Length == 0)
				throw new ArgumentException(
					"[FormerlyKnownAs] requires at least one old name.", nameof(oldFullyQualifiedNames));

			OldNames = oldFullyQualifiedNames;
		}
	}
}
