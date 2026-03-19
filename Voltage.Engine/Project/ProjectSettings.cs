using System.Collections.Generic;
using System.Text.Json;

namespace Voltage.Project
{
	public class ProjectSettings
	{

		public static ProjectSettings Instance
		{
			get
			{
				if (_instance == null)
					_instance = new ProjectSettings();
				return _instance;
			}
			set
			{
				_instance = value;
			}
		}
		private static ProjectSettings _instance;
		public DisplaySettings Display { get; set; } = new();
		public AudioSettings Audio { get; set; } = new();
		public DesignResolutionSettings DesignResolution { get; set; } = new();
		public PhysicsSettings Physics { get; set; } = new();
		public RenderingSettings Rendering { get; set; } = new();
		public EntitySettings Entities { get; set; } = new();

		public string ContentDirectory { get; set; } = "Content";

		/// <summary>
		/// The .vscene file name (without extension) that the game loads on startup.
		/// When empty, the game creates a default empty scene.
		/// </summary>
		public string InitialScene { get; set; } = "";

		#region AOT-safe JSON serialization

		/// <summary>
		/// Deserializes ProjectSettings from a JSON string using the source-generated
		/// serializer. Safe for NativeAOT and trimmed deployments.
		/// </summary>
		public static ProjectSettings LoadFromJson(string json)
		{
			return JsonSerializer.Deserialize(json, ProjectSettingsJsonContext.Default.ProjectSettings)
			       ?? new ProjectSettings();
		}

		/// <summary>
		/// Serializes this ProjectSettings instance to a pretty-printed JSON string
		/// using the source-generated serializer. Safe for NativeAOT and trimmed deployments.
		/// </summary>
		public string SaveToJson()
		{
			return JsonSerializer.Serialize(this, ProjectSettingsJsonContext.Default.ProjectSettings);
		}

		#endregion

		public class DisplaySettings
		{
			public int ScreenWidth { get; set; } = 1280;
			public int ScreenHeight { get; set; } = 720;
			public bool IsFullscreen { get; set; } = false;
			public bool EnableVSync { get; set; } = true;
		}

		public class AudioSettings
		{
			public float MasterVolume { get; set; } = 1.0f;
			public float MusicVolume { get; set; } = 0.8f;
			public float SFXVolume { get; set; } = 1.0f;
		}

		public class DesignResolutionSettings
		{
			public int Width { get; set; } = 1280;
			public int Height { get; set; } = 720;
			public Scene.SceneResolutionPolicy ResolutionPolicy { get; set; } = Scene.SceneResolutionPolicy.BestFit;
			public int HorizontalBleed { get; set; } = 0;
			public int VerticalBleed { get; set; } = 0;
		}

		public class PhysicsSettings
		{
			public Dictionary<string, int> PhysicsLayers { get; set; } = new()
			{
				{ "Default", 0 },
				{ "Ground", 1 }
			};
		}


		public class RenderingSettings
		{
			public Dictionary<string, int> RenderingLayers { get; set; } = new()
			{
				{ "Lighting", 100 },
				{ "BehindAll", 99 },
				{ "HideObject", 30 },
				{ "Background", 0 },
				{ "Entities", 1 },
				{ "Foreground", -2 },
				{ "InFrontOfAll", -30 },
				{ "UIElement", -99 }
			};
		}

		public class EntitySettings
		{
			public Dictionary<string, int> EntityTags { get; set; } = new()
			{
				{ "Default", 0 },
				{ "Player", 1 }
			};
		}

		#region Helper Methods
		/// <summary>
		/// Gets the physics layer value by its name.
		/// </summary>
		/// <param name="layerName">The name of the physics layer.</param>
		/// <returns>The physics layer value, or -1 if not found.</returns>
		public int GetPhysicsLayer(string layerName)
		{
			if (Physics.PhysicsLayers.TryGetValue(layerName, out var layerValue))
				return layerValue;

			return -1;
		}

		/// <summary>
		/// Gets the render layer value by its name.
		/// </summary>
		/// <param name="layerName">The name of the render layer.</param>
		/// <returns>The render layer value, or 0 if not found.</returns>
		public int GetRenderLayer(string layerName)
		{
			if (Rendering.RenderingLayers.TryGetValue(layerName, out var layerValue))
				return layerValue;

			return 0;
		}

		/// <summary>
		/// Gets the entity tag value by its name.
		/// </summary>
		/// <param name="tagName">The name of the entity tag.</param>
		/// <returns>The entity tag value, or 0 if not found.</returns>
		public int GetEntityTag(string tagName)
		{
			if (Entities.EntityTags.TryGetValue(tagName, out var tagValue))
				return tagValue;

			return 0;
		}

		/// <summary>
		/// Tries to get the physics layer value by its name.
		/// </summary>
		/// <param name="layerName">The name of the physics layer.</param>
		/// <param name="layerValue">The physics layer value if found.</param>
		/// <returns>True if the layer was found, false otherwise.</returns>
		public bool TryGetPhysicsLayer(string layerName, out int layerValue)
		{
			return Physics.PhysicsLayers.TryGetValue(layerName, out layerValue);
		}

		/// <summary>
		/// Tries to get the render layer value by its name.
		/// </summary>
		/// <param name="layerName">The name of the render layer.</param>
		/// <param name="layerValue">The render layer value if found.</param>
		/// <returns>True if the layer was found, false otherwise.</returns>
		public bool TryGetRenderLayer(string layerName, out int layerValue)
		{
			return Rendering.RenderingLayers.TryGetValue(layerName, out layerValue);
		}

		/// <summary>
		/// Tries to get the entity tag value by its name.
		/// </summary>
		/// <param name="tagName">The name of the entity tag.</param>
		/// <param name="tagValue">The entity tag value if found.</param>
		/// <returns>True if the tag was found, false otherwise.</returns>
		public bool TryGetEntityTag(string tagName, out int tagValue)
		{
			return Entities.EntityTags.TryGetValue(tagName, out tagValue);
		}
		#endregion
	}
}
