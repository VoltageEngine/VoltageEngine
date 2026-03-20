using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

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

		public class DisplaySettings
		{
			public int ScreenWidth = 1280;
			public int ScreenHeight = 720;
			public bool IsFullscreen = false;
			public bool EnableVSync = true;
		}

		public class AudioSettings
		{
			public float MasterVolume = 1.0f;
			public float MusicVolume = 0.8f;
			public float SFXVolume = 1.0f;
		}

		public class DesignResolutionSettings
		{
			public int Width = 1280;
			public int Height = 720;
			public Scene.SceneResolutionPolicy ResolutionPolicy = Scene.SceneResolutionPolicy.BestFit;
			public int HorizontalBleed = 0;
			public int VerticalBleed = 0;
		}

		public class PhysicsSettings
		{
			[DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(Dictionary<string, int>))]
			public Dictionary<string, int> PhysicsLayers = new()
			{
				{ "Default", 0 },
				{ "Ground", 1 }
			};
		}


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

		public class EntitySettings
		{
			[DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(Dictionary<string, int>))]
			public Dictionary<string, int> EntityTags = new()
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
