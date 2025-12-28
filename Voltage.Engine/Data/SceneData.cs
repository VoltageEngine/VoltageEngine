using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Voltage.Persistence;

namespace Voltage.Data;

/// <summary>
/// Complete scene data structure that can be serialized to/from JSON.
/// Contains all information needed to reconstruct a scene.
/// </summary>
public class SceneData
{
	#region Scene Metadata

	public string Name { get; set; } = "Untitled Scene";
	public string FilePath { get; set; } = string.Empty;
	public DateTime CreatedAt { get; set; } = DateTime.Now;
	public DateTime ModifiedAt { get; set; } = DateTime.Now;

	#endregion

	#region Scene Settings

	/// <summary>
	/// Background clear color for the scene
	/// </summary>
	public Color ClearColor { get; set; } = new Color(100, 149, 237, 255); // CornflowerBlue

	/// <summary>
	/// Letterbox color used when rendering
	/// </summary>
	public Color LetterboxColor { get; set; } = new Color(0, 0, 0, 255); // Black

	/// <summary>
	/// Resolution policy for the scene
	/// </summary>
	public string ResolutionPolicy { get; set; } = "BestFit";

	/// <summary>
	/// Design resolution width (the width and height the scene is designed for)
	/// </summary>
	public int DesignResolutionWidth { get; set; } = 1920;

	/// <summary>
	/// Design resolution height
	/// </summary>
	public int DesignResolutionHeight { get; set; } = 1080;

	/// <summary>
	/// Horizontal bleed for BestFit (if selected) resolution policy
	/// </summary>
	public int HorizontalBleed { get; set; } = 0;

	/// <summary>
	/// Vertical bleed for BestFit (if selected) resolution policy
	/// </summary>
	public int VerticalBleed { get; set; } = 0;

	/// <summary>
	/// Whether post-processing is enabled
	/// </summary>
	public bool EnablePostProcessing { get; set; } = true;

	#endregion

	#region Scene Content

	/// <summary>
	/// Path to associated Tiled map file (if any)
	/// </summary>
	public string TiledMapFileName { get; set; } = string.Empty;

	/// <summary>
	/// List of all serializable entities in the scene
	/// </summary>
	public List<SceneEntityData> Entities { get; set; } = new();

	#endregion

	#region Editor Data

	/// <summary>
	/// Editor-specific data (camera position, selected entities, etc.)
	/// Not used at runtime
	/// </summary>
	public Dictionary<string, string> EditorData { get; set; } = new();

	#endregion

	#region Entity Data

	/// <summary>
	/// Data for a single entity in the scene
	/// </summary>
	public class SceneEntityData
	{
		public Guid Id { get; set; }
		public Guid? ParentId { get; set; }

		public Entity.InstanceType InstanceType { get; set; }
		public string Name { get; set; }

		// Transform
		public Vector2 Position { get; set; }
		public float Rotation { get; set; }
		public Vector2 Scale { get; set; } = new Vector2(1, 1);

		// Hierarchy
		public string ParentEntityName { get; set; }

		// Properties
		public bool Enabled { get; set; } = true;
		public int UpdateOrder { get; set; }
		public int Tag { get; set; }
		public bool IsSelectableInEditor { get; set; } = true;
		public bool DebugRenderEnabled { get; set; }

		// Prefab reference
		public string OriginalPrefabName { get; set; }

		// Entity-specific data
		public EntityData EntityData { get; set; }
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