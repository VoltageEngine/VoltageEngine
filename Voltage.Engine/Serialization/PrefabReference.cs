using System;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Xna.Framework;

namespace Voltage.Serialization;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public struct PrefabReference
{
	public Guid PrefabGuid;
	public string PrefabPath;
	public string PrefabName;

	public readonly bool IsValid =>
		!string.IsNullOrEmpty(PrefabPath) || PrefabGuid != Guid.Empty;

	public readonly Entity Instantiate(Scene scene, Vector2 position = default)
	{
		if (scene == null || string.IsNullOrEmpty(PrefabPath))
			return null;
		return scene.LoadPrefab(PrefabPath, position);
	}

	public override readonly string ToString() =>
		IsValid ? $"{PrefabName ?? PrefabPath}({PrefabGuid})" : "(None)";
}
