using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using Microsoft.Xna.Framework;

namespace Voltage.Serialization;

/// <summary>
/// A serializable, inspector-assignable reference to a prefab asset. Declare a public field of this
/// type on a component to get a drag/drop prefab slot in the inspector, then spawn it from code with
/// <see cref="Instantiate(Vector2)"/>.
///
/// <example>
/// <code>
/// public partial class Spawner : Component
/// {
///     public PrefabReference EnemyPrefab;   // assign in the inspector
///
///     public void Spawn(Vector2 at) => EnemyPrefab.Instantiate(at);
/// }
/// </code>
/// </example>
///
/// Resolution is <b>GUID-first</b>: the prefab is found by its stable <see cref="PrefabGuid"/> (via the
/// editor's AssetDatabase, or the baked <see cref="Voltage.Assets.AssetManifest"/> in a published
/// build), so renaming or moving the <c>.vprefab</c> does not break the reference. The stored
/// <see cref="PrefabPath"/> is only a fallback.
/// </summary>
[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public struct PrefabReference
{
	public Guid PrefabGuid;
	/// <summary>Project-relative path captured at assignment time. Fallback only — prefer the GUID.</summary>
	public string PrefabPath;
	public string PrefabName;

	public readonly bool IsValid =>
		PrefabGuid != Guid.Empty || !string.IsNullOrEmpty(PrefabPath);

	/// <summary>
	/// Resolves the prefab's current absolute file path, GUID-first (so it survives a rename/move),
	/// falling back to the stored path. Returns <c>null</c> when it cannot be resolved.
	/// </summary>
	public readonly string ResolvePath()
	{
		// 1. GUID via the editor's resolver (AssetDatabase) — wired only in the editor.
		if (PrefabGuid != Guid.Empty && Scene.PrefabPathResolver != null)
		{
			var p = Scene.PrefabPathResolver(PrefabGuid, PrefabName);
			if (!string.IsNullOrEmpty(p))
				return p;
		}

		// 2. GUID via the baked asset manifest — published builds.
		if (PrefabGuid != Guid.Empty &&
		    Voltage.Assets.AssetManifest.TryGetAbsolutePath(PrefabGuid, out var byGuid))
			return byGuid;

		// 3. Stored path fallback. Project-relative paths are resolved against the game base
		// directory (where Content/Data ship); absolute paths are used as-is.
		if (string.IsNullOrEmpty(PrefabPath))
			return null;
		return Path.IsPathRooted(PrefabPath)
			? PrefabPath
			: Path.Combine(AppContext.BaseDirectory, PrefabPath);
	}

	/// <summary>Instantiates this prefab into the active scene (<see cref="Core.Scene"/>).</summary>
	public readonly Entity Instantiate(Vector2 position = default) => Instantiate(Core.Scene, position);

	/// <summary>Instantiates this prefab into <paramref name="scene"/> at <paramref name="position"/>.</summary>
	public readonly Entity Instantiate(Scene scene, Vector2 position = default)
	{
		if (scene == null || !IsValid)
			return null;

		var path = ResolvePath();
		if (string.IsNullOrEmpty(path))
		{
			Debug.Warn($"[PrefabReference] Could not resolve prefab '{PrefabName ?? PrefabPath}' " +
			           $"(guid {PrefabGuid}). Open the project in the editor to (re)generate the asset manifest.");
			return null;
		}

		return scene.LoadPrefab(path, position);
	}

	public override readonly string ToString() =>
		IsValid ? $"{PrefabName ?? PrefabPath}({PrefabGuid})" : "(None)";
}
