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

	/// <summary>
	/// Name of the scene
	/// </summary>
	public string Name { get; set; } = "Untitled Scene";

	/// <summary>
	/// When the scene was created
	/// </summary>
	public DateTime CreatedAt { get; set; } = DateTime.Now;

	/// <summary>
	/// When the scene was last modified
	/// </summary>
	public DateTime ModifiedAt { get; set; } = DateTime.Now;

	/// <summary>
	/// Optional description of the scene
	/// </summary>
	public string Description { get; set; } = string.Empty;

	#endregion

	#region Scene Settings

	/// <summary>
	/// Background clear color for the scene
	/// </summary>
	public SerializableColor ClearColor { get; set; } = new SerializableColor(100, 149, 237, 255); // CornflowerBlue

	/// <summary>
	/// Letterbox color used when rendering
	/// </summary>
	public SerializableColor LetterboxColor { get; set; } = new SerializableColor(0, 0, 0, 255); // Black

	/// <summary>
	/// Resolution policy for the scene
	/// </summary>
	public string ResolutionPolicy { get; set; } = "BestFit";

	/// <summary>
	/// Design resolution width
	/// </summary>
	public int DesignResolutionWidth { get; set; } = 1920;

	/// <summary>
	/// Design resolution height
	/// </summary>
	public int DesignResolutionHeight { get; set; } = 1080;

	/// <summary>
	/// Horizontal bleed for BestFit resolution policy
	/// </summary>
	public int HorizontalBleed { get; set; } = 0;

	/// <summary>
	/// Vertical bleed for BestFit resolution policy
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
		public Entity.InstanceType InstanceType { get; set; }
		public string Name { get; set; }
		public string EntityType { get; set; } = "Entity";

		// Transform
		public SerializableVector2 Position { get; set; }
		public float Rotation { get; set; }
		public SerializableVector2 Scale { get; set; } = new SerializableVector2(1, 1);

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

	#region Helper Structures

	/// <summary>
	/// Serializable Vector2 wrapper
	/// </summary>
	public class SerializableVector2
	{
		public float X { get; set; }
		public float Y { get; set; }

		public SerializableVector2() { }

		public SerializableVector2(float x, float y)
		{
			X = x;
			Y = y;
		}

		public static implicit operator Vector2(SerializableVector2 v) => new Vector2(v.X, v.Y);
		public static implicit operator SerializableVector2(Vector2 v) => new SerializableVector2(v.X, v.Y);
	}

	/// <summary>
	/// Serializable Color wrapper
	/// </summary>
	public class SerializableColor
	{
		public byte R { get; set; }
		public byte G { get; set; }
		public byte B { get; set; }
		public byte A { get; set; }

		public SerializableColor() { }

		public SerializableColor(byte r, byte g, byte b, byte a)
		{
			R = r;
			G = g;
			B = b;
			A = a;
		}

		public static implicit operator Color(SerializableColor c) => new Color(c.R, c.G, c.B, c.A);
		public static implicit operator SerializableColor(Color c) => new SerializableColor(c.R, c.G, c.B, c.A);
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