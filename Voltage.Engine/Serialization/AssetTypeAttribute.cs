using System;
using System.Diagnostics.CodeAnalysis;

namespace Voltage.Serialization
{
	/// <summary>
	/// Constrains an <see cref="AssetReference"/> slot to one kind of asset: the editor filters the picker, rejects a mismatched drop, and labels the empty slot with the wanted type.
	/// </summary>
	/// <example>
	/// <code>
	/// [AssetType(typeof(DifficultyProfile))] public AssetReference Difficulty;
	/// [AssetType(".png", ".aseprite")]       public AssetReference Portrait;
	/// </code>
	/// </example>
	[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
	public sealed class AssetTypeAttribute : Attribute
	{
		/// <summary>
		/// For a <c>DataAsset</c> subclass the editor matches the file's <c>@assetType</c> header; for other types, the extension registered for that type.
		/// </summary>
		[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
		public Type AssetType { get; }

		/// <summary>Including the leading dot. Takes precedence over <see cref="AssetType"/>.</summary>
		public string[] Extensions { get; }

		/// <param name="assetType">A <c>DataAsset</c> subclass, or a type with a registered asset format.</param>
		public AssetTypeAttribute(
			[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] Type assetType)
		{
			AssetType = assetType;
		}

		/// <param name="extensions">Including the leading dot, e.g. ".png".</param>
		public AssetTypeAttribute(params string[] extensions)
		{
			Extensions = extensions;
		}
	}
}
