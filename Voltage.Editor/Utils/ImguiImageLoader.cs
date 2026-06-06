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

		public static void LoadImages(ImGuiRenderer renderer)
		{
			// Bind textures to ImGui
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
		}
	}
}
