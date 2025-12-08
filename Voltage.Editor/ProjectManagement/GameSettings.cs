using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Voltage.Editor.ProjectManagement
{
	public class GameSettings
	{

		public static GameSettings Instance
		{
			get
			{
				if (_instance == null)
					_instance = new GameSettings();
				return _instance;
			}
			set
			{
				_instance = value;
			}
		}
		private static GameSettings _instance;
		public DisplaySettings Display { get; set; } = new();
		public AudioSettings Audio { get; set; } = new();
		public PhysicsSettings Physics { get; set; } = new();
		public RenderingSettings Rendering { get; set; } = new();
		public EntitySettings Entities { get; set; } = new();
		public string ContentDirectory { get; set; } = "Content";
	}

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
		public float MusicVolume { get; set; } = 1.0f;
		public float SFXVolume { get; set; } = 1.0f;
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
}
