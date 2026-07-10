using System;
using Voltage.Utils;
using Voltage.Editor.ImGuiCore;

namespace Voltage.Editor.Utils
{
	public static class ImguiImageLoader
	{
		public static IntPtr NormalCursorIconID;
		public static IntPtr ResizeCursorIconID;
		public static IntPtr RotateCursorIconID;
		public static IntPtr ColliderResizeCursorIconID;

		public static IntPtr LockedInspectorIconId;
		public static IntPtr UnlockedInspectorIconId;

		public static IntPtr WarningIconId;
		public static IntPtr ErrorIconId;
		public static IntPtr InfoIconId;
		public static IntPtr SuccessIconId;
		public static IntPtr FailIconId;

		// Asset Browser file-type icons: live in DefaultContent/UI/RemixIcon/FileTypes/.
		// Confirmed present: Aseprite, Prefab, Scene, Script, Unsupported-File.
		// Effect and Tiled have no dedicated icon yet — both fall back to Unsupported.
		public static IntPtr AssetIconTexture;     // .png / .aseprite / .ase
		public static IntPtr AssetIconPrefab;      // .prefab
		public static IntPtr AssetIconScene;       // .vscene
		public static IntPtr AssetIconScript;      // .cs
		public static IntPtr AssetIconUnsupported; // fallback (also used for Effect, Tiled)
		public static IntPtr AssetIconAudio;       // .wav / .ogg / .mp3

		// Editor-mode controls (menu bar cluster): live in DefaultContent/UI/RemixIcon/EditorModes/.
		public static IntPtr EditorModePlay;
		public static IntPtr EditorModeStop;
		public static IntPtr EditorModePause;
		public static IntPtr EditorModeReset;

		// Audio toggle icons (menu bar, right-aligned): live in DefaultContent/UI/RemixIcon/MenuBar/.
		public static IntPtr AudioOn;
		public static IntPtr AudioMute;

		public static void LoadImages(ImGuiRenderer renderer)
		{
			NormalCursorIconID = renderer.BindTexture(Core.Content.LoadTexture("DefaultContent/UI/Custom/CursorSelection-UI-Normal.png"));
			ResizeCursorIconID = renderer.BindTexture(Core.Content.LoadTexture("DefaultContent/UI/Custom/CursorSelection-UI-Resize.png"));
			RotateCursorIconID = renderer.BindTexture(Core.Content.LoadTexture("DefaultContent/UI/Custom/CursorSelection-UI-Rotate.png"));
			ColliderResizeCursorIconID = renderer.BindTexture(Core.Content.LoadTexture("DefaultContent/UI/Custom/CursorSelection-UI-ColliderResize.png"));

			LockedInspectorIconId = renderer.BindTexture(Core.Content.LoadAsepriteFile("DefaultContent/UI/Custom/Inspector-LockMode.aseprite").GetTextureFromLayers(1, true, false, "Locked"));
			UnlockedInspectorIconId = renderer.BindTexture(Core.Content.LoadAsepriteFile("DefaultContent/UI/Custom/Inspector-LockMode.aseprite").GetTextureFromLayers(1, true, false, "Unlocked"));

			WarningIconId = renderer.BindTexture(Core.Content.LoadTexture("DefaultContent/UI/RemixIcon/alert-hexangular.png"));
			ErrorIconId = renderer.BindTexture(Core.Content.LoadTexture("DefaultContent/UI/RemixIcon/alert-triangle.png"));
			InfoIconId = renderer.BindTexture(Core.Content.LoadTexture("DefaultContent/UI/RemixIcon/information.png"));
			SuccessIconId = renderer.BindTexture(Core.Content.LoadTexture("DefaultContent/UI/RemixIcon/checkbox-circle.png"));
			FailIconId = renderer.BindTexture(Core.Content.LoadTexture("DefaultContent/UI/RemixIcon/close-circle.png"));

			AssetIconTexture     = renderer.BindTexture(Core.Content.LoadTexture("DefaultContent/UI/RemixIcon/FileTypes/Voltage-Aseprite.png"));
			AssetIconPrefab      = renderer.BindTexture(Core.Content.LoadTexture("DefaultContent/UI/RemixIcon/FileTypes/Voltage-Prefab.png"));
			AssetIconScene       = renderer.BindTexture(Core.Content.LoadTexture("DefaultContent/UI/RemixIcon/FileTypes/Voltage-Scene.png"));
			AssetIconScript      = renderer.BindTexture(Core.Content.LoadTexture("DefaultContent/UI/RemixIcon/FileTypes/Voltage-Script.png"));
			AssetIconAudio       = renderer.BindTexture(Core.Content.LoadTexture("DefaultContent/UI/RemixIcon/FileTypes/Voltage-Audio.png"));
			AssetIconUnsupported = renderer.BindTexture(Core.Content.LoadTexture("DefaultContent/UI/RemixIcon/FileTypes/Voltage-Unsupported-File.png"));

			EditorModePlay  = renderer.BindTexture(Core.Content.LoadTexture("DefaultContent/UI/RemixIcon/EditorModes/Voltage-Play.png"));
			EditorModeStop  = renderer.BindTexture(Core.Content.LoadTexture("DefaultContent/UI/RemixIcon/EditorModes/Voltage-Stop.png"));
			EditorModePause = renderer.BindTexture(Core.Content.LoadTexture("DefaultContent/UI/RemixIcon/EditorModes/Voltage-Pause.png"));
			EditorModeReset = renderer.BindTexture(Core.Content.LoadTexture("DefaultContent/UI/RemixIcon/EditorModes/Voltage-Reset.png"));

			AudioOn   = renderer.BindTexture(Core.Content.LoadTexture("DefaultContent/UI/RemixIcon/MenuBar/volume-up.png"));
			AudioMute = renderer.BindTexture(Core.Content.LoadTexture("DefaultContent/UI/RemixIcon/MenuBar/volume-mute.png"));
		}
	}
}
