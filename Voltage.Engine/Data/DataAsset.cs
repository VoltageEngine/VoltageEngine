using System;
using System.Diagnostics.CodeAnalysis;
using Voltage.Persistence;

namespace Voltage.Data
{
	/// <summary>
	/// Base class for a <c>.vasset</c> data container: shared, referenceable data with no entity or update cost.
	/// </summary>
	[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
	public abstract class DataAsset
	{
		/// <summary>GUID of the file this came from; <see cref="Guid.Empty"/> for an in-memory asset.</summary>
		[JsonExclude]
		public Guid SourceGuid { get; internal set; }

		[JsonExclude]
		public string SourcePath { get; internal set; }

		/// <summary>The <c>@version</c> read from the file, for migrations in <see cref="OnLoaded"/>.</summary>
		[JsonExclude]
		public int LoadedVersion { get; internal set; }

		/// <summary>
		/// Called once after every field is populated.
		/// </summary>
		public virtual void OnLoaded() { }
	}

	/// <summary>
	/// Opts a type out of instance sharing: every load returns a fresh copy.
	/// </summary>
	[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
	public sealed class CloneOnLoadAttribute : Attribute
	{
	}

	/// <summary>
	/// Schema version written to the file's <c>@version</c> header; defaults to 1.
	/// </summary>
	[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
	public sealed class AssetVersionAttribute : Attribute
	{
		public int Version { get; }

		public AssetVersionAttribute(int version) => Version = version;
	}
}
