using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;

namespace Voltage.Serialization;

/// <summary>
/// A serializable, inspector-assignable reference to a project asset (texture, audio, data file, etc...)
/// — the general-asset analogue of <see cref="PrefabReference"/>. Declare a public field of this type
/// on a component to get a drag/drop asset slot in the inspector, then load it from code with
/// <see cref="Voltage.Scene.LoadAsset{T}(AssetReference)"/>.
///
/// <example>
/// <code>
/// public partial class Player : Component
/// {
///     public AssetReference Portrait;   // assign in the inspector (drag from the Asset Browser)
///
///     public override void OnStart()
///     {
///         var tex = Core.Scene.LoadAsset&lt;Texture2D&gt;(Portrait);
///     }
/// }
/// </code>
/// </example>
///
/// Resolution is <b>GUID-first</b>: the asset is found by its stable <see cref="AssetGuid"/> (via the
/// editor's AssetDatabase, or the baked <see cref="Voltage.Assets.AssetManifest"/> in a published
/// build), so renaming or moving the file does not break the reference. The stored
/// <see cref="AssetPath"/> is only a fallback.
/// </summary>
[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public struct AssetReference
{
	public Guid AssetGuid;
	/// Project-relative path. Fallback only - prefer the GUID.</summary>
	public string AssetPath;
	public string AssetName;

	public readonly bool IsValid =>
		AssetGuid != Guid.Empty || !string.IsNullOrEmpty(AssetPath);

	/// <summary>
	/// Resolves the asset's current absolute file path, GUID-first (so it survives a rename/move),
	/// falling back to the stored path. Returns <c>null</c> when it cannot be resolved.
	/// </summary>
	public readonly string ResolvePath()
	{
		// 1. GUID via the editor's resolver (AssetDatabase) — wired only in the editor.
		if (AssetGuid != Guid.Empty && Scene.AssetPathResolver != null)
		{
			var p = Scene.AssetPathResolver(AssetGuid);
			if (!string.IsNullOrEmpty(p))
				return p;
		}

		// 2. GUID via the baked asset manifest — published builds.
		if (AssetGuid != Guid.Empty &&
		    Voltage.Assets.AssetManifest.TryGetAbsolutePath(AssetGuid, out var byGuid))
			return byGuid;

		// 3. Stored path fallback (project-relative → game base directory; absolute as-is).
		if (string.IsNullOrEmpty(AssetPath))
			return null;
		return Path.IsPathRooted(AssetPath)
			? AssetPath
			: Path.Combine(AppContext.BaseDirectory, AssetPath);
	}

	public override readonly string ToString() =>
		IsValid ? $"{AssetName ?? AssetPath}({AssetGuid})" : "(None)";
}
