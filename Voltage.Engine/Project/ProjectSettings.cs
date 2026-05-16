using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Voltage.Project
{
	[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
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

		/// <summary>
		/// The .vscene file name (without extension) that the game loads on startup.
		/// When empty, the game creates a default empty scene (called MainScene).
		/// </summary>
		public string InitialScene;
		public DisplaySettings Display;
		public AudioSettings Audio;
		public DesignResolutionSettings DesignResolution;
		public PhysicsSettings Physics;
		public RenderingSettings Rendering;
		public EntitySettings Entities;
		public string ContentDirectory;

		[DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(ProjectSettings))]
		[DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(DisplaySettings))]
		[DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(AudioSettings))]
		[DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(DesignResolutionSettings))]
		[DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(PhysicsSettings))]
		[DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(RenderingSettings))]
		[DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(EntitySettings))]
		[DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(string))]
		public ProjectSettings()
		{
			InitialScene = "";
			Display = new DisplaySettings();
			Audio = new AudioSettings();
			DesignResolution = new DesignResolutionSettings();
			Physics = new PhysicsSettings();
			Rendering = new RenderingSettings();
			Entities = new EntitySettings();
			ContentDirectory = "Content";
		}

		[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
		public class DisplaySettings
		{
			public int ScreenWidth = 1280;
			public int ScreenHeight = 720;
			public bool IsFullscreen = false;
			public bool EnableVSync = true;
		}

		[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
		public class AudioSettings
		{
			public float MasterVolume = 1.0f;
			public float MusicVolume = 0.8f;
			public float SFXVolume = 1.0f;
		}

		[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
		public class DesignResolutionSettings
		{
			public int Width = 1280;
			public int Height = 720;
			public Scene.SceneResolutionPolicy ResolutionPolicy = Scene.SceneResolutionPolicy.BestFit;
			public int HorizontalBleed = 0;
			public int VerticalBleed = 0;
		}

		[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
		public class PhysicsSettings
		{
			[DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(Dictionary<string, int>))]
			public Dictionary<string, int> PhysicsLayers = new()
			{
				{ "Default", 0 },
				{ "Ground", 1 }
			};
		}

		[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
		public class RenderingSettings
		{
			[DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(Dictionary<string, int>))]
			public Dictionary<string, int> RenderingLayers = new()
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

		[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
		public class EntitySettings
		{
			[DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(Dictionary<string, int>))]
			public Dictionary<string, int> EntityTags = new()
			{
				{ "Default", 0 },
				{ "Player", 1 }
			};
		}

		#region Physics Layer API

		/// <summary>
		/// Returns the raw index stored for a physics layer name (e.g. "Default" → 0, "Ground" → 1).
		/// Returns -1 if not found.
		/// </summary>
		public int GetPhysicsLayer(string layerName)
		{
			if (Physics.PhysicsLayers.TryGetValue(layerName, out var layerValue))
				return layerValue;

			return -1;
		}

		/// <summary>
		/// Returns the bitmask bit for a physics layer by name (e.g. "Ground" → 1 &lt;&lt; 1).
		/// Use this when setting <c>Collider.PhysicsLayer</c> or building a <c>CollidesWithLayers</c> mask.
		/// Returns 0 if not found.
		/// </summary>
		public int GetPhysicsLayerBit(string layerName)
		{
			if (Physics.PhysicsLayers.TryGetValue(layerName, out var index))
				return 1 << index;

			return 0;
		}

		/// <summary>
		/// Returns the bitmask bit for a physics layer by its index (e.g. index 1 → 1 &lt;&lt; 1 = 2).
		/// Use this when setting <c>Collider.PhysicsLayer</c> or building a <c>CollidesWithLayers</c> mask.
		/// </summary>
		public int GetPhysicsLayerBit(int layerIndex) => 1 << layerIndex;

		/// <summary>
		/// Returns a combined bitmask for multiple physics layer names.
		/// Useful for setting <c>Collider.CollidesWithLayers</c> in scripts.
		/// <example>
		/// <code>
		/// collider.CollidesWithLayers = ProjectSettings.Instance.GetPhysicsLayerMask("Default", "Ground");
		/// </code>
		/// </example>
		/// </summary>
		public int GetPhysicsLayerMask(params string[] layerNames)
		{
			var mask = 0;
			foreach (var name in layerNames)
				mask |= GetPhysicsLayerBit(name);

			return mask;
		}

		/// <summary>
		/// Tries to get the physics layer index by its name.
		/// </summary>
		public bool TryGetPhysicsLayer(string layerName, out int layerValue)
		{
			return Physics.PhysicsLayers.TryGetValue(layerName, out layerValue);
		}

		#endregion

		#region Render Layer API

		/// <summary>
		/// Returns the render layer value by its name (e.g. "Background" → 0, "Entities" → 1).
		/// Returns 0 if not found.
		/// </summary>
		public int GetRenderLayer(string layerName)
		{
			if (Rendering.RenderingLayers.TryGetValue(layerName, out var layerValue))
				return layerValue;

			return 0;
		}

		/// <summary>
		/// Returns the name of a render layer by its int value. Useful for display and debugging.
		/// Returns null if no layer with that value exists.
		/// </summary>
		public string GetRenderLayerName(int layerValue)
		{
			foreach (var kvp in Rendering.RenderingLayers)
			{
				if (kvp.Value == layerValue)
					return kvp.Key;
			}

			return null;
		}

		/// <summary>
		/// Tries to get the render layer value by its name.
		/// </summary>
		public bool TryGetRenderLayer(string layerName, out int layerValue)
		{
			return Rendering.RenderingLayers.TryGetValue(layerName, out layerValue);
		}

		#endregion

		#region Entity Tag API

		/// <summary>
		/// Gets the entity tag value by its name.
		/// Returns 0 if not found.
		/// </summary>
		public int GetEntityTag(string tagName)
		{
			if (Entities.EntityTags.TryGetValue(tagName, out var tagValue))
				return tagValue;

			return 0;
		}

		/// <summary>
		/// Tries to get the entity tag value by its name.
		/// </summary>
		public bool TryGetEntityTag(string tagName, out int tagValue)
		{
			return Entities.EntityTags.TryGetValue(tagName, out tagValue);
		}

		#endregion
	}
}
