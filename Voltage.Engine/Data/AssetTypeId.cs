using System;

namespace Voltage
{
	/// <summary>
	/// Stable, rename-proof identity for a <see cref="Voltage.Data.DataAsset"/> type.
	/// </summary>
	[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
	public sealed class AssetTypeIdAttribute : Attribute
	{
		public string Id { get; }

		public AssetTypeIdAttribute(string id) => Id = id;
	}
}
