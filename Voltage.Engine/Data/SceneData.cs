using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Xna.Framework;
using Voltage.Persistence;

namespace Voltage.Data;

/// <summary>
/// Complete scene data structure that can be serialized to/from JSON.
/// Contains all information needed to reconstruct a scene.
/// </summary>
[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public class SceneData
{
	#region Scene Metadata

	public string Name = "Untitled Scene";
	[JsonExclude]
	public string FilePath = string.Empty;
	public DateTime CreatedAt = DateTime.Now;
	public DateTime ModifiedAt = DateTime.Now;

	#endregion

	#region Scene Settings

	/// <summary>
	/// Background clear color for the scene
	/// </summary>
	public Color ClearColor = new Color(100, 149, 237, 255); // CornflowerBlue

	/// <summary>
	/// Letterbox color used when rendering
	/// </summary>
	public Color LetterboxColor = new Color(0, 0, 0, 255); // Black

	/// <summary>
	/// Resolution policy for the scene
	/// </summary>
	public string ResolutionPolicy = "BestFit";

	/// <summary>
	/// Design resolution width (the width and height the scene is designed for)
	/// </summary>
	public int DesignResolutionWidth = 1920;

	/// <summary>
	/// Design resolution height
	/// </summary>
	public int DesignResolutionHeight = 1080;

	/// <summary>
	/// Horizontal bleed for BestFit (if selected) resolution policy
	/// </summary>
	public int HorizontalBleed = 0;

	/// <summary>
	/// Vertical bleed for BestFit (if selected) resolution policy
	/// </summary>
	public int VerticalBleed = 0;

	/// <summary>
	/// Whether post-processing is enabled
	/// </summary>
	public bool EnablePostProcessing = true;

	#endregion

	#region Scene Content

	/// <summary>
	/// Path to associated Tiled map file (if any)
	/// </summary>
	public string TiledMapFileName = string.Empty;

	/// <summary>
	/// List of all serializable entities in the scene
	/// </summary>
	/// [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(SceneData))]
	[DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(List<SceneEntityData>))]
	public List<SceneEntityData> Entities = new();

	/// <summary>
	/// List of scene-scoped components (SceneComponent subclasses) serialized for this scene.
	/// </summary>
	[DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(List<SceneComponentDataEntry>))]
	public List<SceneComponentDataEntry> SceneComponents = new();

	#endregion

	#region Editor Data

	/// <summary>
	/// Editor-specific data (camera position, selected entities, etc.)
	/// Not used at runtime
	/// </summary>
	public Dictionary<string, string> EditorData = new();

	#endregion

	#region Entity Data

	/// <summary>
	/// Data for a single entity in the scene
	/// </summary>
	[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
	public class SceneEntityData
	{
		public Guid Id;
		public Guid? ParentId;

		public Entity.InstanceType InstanceType;
		public string Name;

		public Vector2 Position;
		public float Rotation;
		public Vector2 Scale = new Vector2(1, 1);

		public string ParentEntityName;

		public bool Enabled = true;
		public int UpdateOrder;
		public int Tag;
		public bool IsSelectableInEditor = true;
		public bool DebugRenderEnabled = false;

		public string OriginalPrefabName;

		/// <summary>
		/// Stable GUID of the prefab asset (from its <c>.meta</c> sidecar).
		/// Empty/null for entities from scenes saved before Phase 3.
		/// Falls back to <see cref="OriginalPrefabName"/> for resolution when absent.
		/// </summary>
		public Guid? OriginalPrefabGuid;

		/// <summary>
		/// Component-level override tracking (Phase 4b).
		/// When non-null this entity is stored as a delta against its source prefab rather than
		/// as a full standalone copy.
		/// <para>
		/// Contains the <see cref="ComponentDataEntry.ComponentName"/> of every component whose
		/// data has been changed relative to the source prefab, plus every component that was
		/// added on this instance (not present in the prefab).
		/// Components NOT listed here are inherited from the current prefab unchanged.
		/// </para>
		/// <para><b>Backward compatibility rule:</b> null means "legacy full-data entry" — the
		/// instance owns all its component data and the source prefab is not consulted during
		/// load.  This preserves behaviour for all scenes saved before Phase 4b.</para>
		/// </summary>
		public HashSet<string> PrefabOverrides;

		/// <summary>
		/// Names of components that exist in the source prefab but have been explicitly
		/// removed on this instance (Phase 4b).  Null when <see cref="PrefabOverrides"/> is null.
		/// </summary>
		public List<string> RemovedPrefabComponents;

		public EntityData EntityData;
	}

	#endregion


	/// <summary>
	/// Gets an entity by name from the scene data
	/// </summary>
	public SceneEntityData GetEntity(string name)
	{
		return Entities.Find(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
	}

	/// <summary>
	/// Creates a deep clone of this SceneData
	/// </summary>
	public SceneData Clone()
	{
		var json = Json.ToJson(this, new JsonSettings
		{
			PrettyPrint = false,
			TypeNameHandling = TypeNameHandling.Auto
		});
		return Json.FromJson<SceneData>(json);
	}
}