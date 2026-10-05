using System;

namespace Voltage.Serialization
{
	/// <summary>Legacy name-based type rename; <c>[ComponentId]</c> / <c>[AssetTypeId]</c> make renames automatic, so this is no longer needed.</summary>
	[Obsolete("Class and namespace renames are handled by the stamped [ComponentId] / [AssetTypeId]; this attribute only serves scenes saved before those ids existed.")]
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
