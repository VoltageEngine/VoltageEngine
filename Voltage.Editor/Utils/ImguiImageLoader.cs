using System;
using Voltage.Utils;
using Voltage.Editor.ImGuiCore;

namespace Voltage.Editor.Utils
{
	public class ImguiImageLoader
	{
		public IntPtr NormalCursorIconID;
		public IntPtr ResizeCursorIconID;
		public IntPtr RotateCursorIconID;
		public IntPtr ColliderResizeCursorIconID;

		public IntPtr LockedInspectorIconId;
		public IntPtr UnlockedInspectorIconId;

		public void LoadImages(ImGuiRenderer renderer)
		{
			// Bind textures to ImGui
			NormalCursorIconID = renderer.BindTexture(Voltage.Core.Content.LoadTexture("DefaultContent/UI/CursorSelection-UI-Normal.png"));
			ResizeCursorIconID = renderer.BindTexture(Voltage.Core.Content.LoadTexture("DefaultContent/UI/CursorSelection-UI-Resize.png"));
			RotateCursorIconID = renderer.BindTexture(Voltage.Core.Content.LoadTexture("DefaultContent/UI/CursorSelection-UI-Rotate.png"));
			ColliderResizeCursorIconID = renderer.BindTexture(Voltage.Core.Content.LoadTexture("DefaultContent/UI/CursorSelection-UI-ColliderResize.png"));

			LockedInspectorIconId = renderer.BindTexture(Voltage.Core.Content.LoadAsepriteFile("DefaultContent/UI/Inspector-LockMode.aseprite").GetTextureFromLayers("Locked"));
			UnlockedInspectorIconId = renderer.BindTexture(Voltage.Core.Content.LoadAsepriteFile("DefaultContent/UI/Inspector-LockMode.aseprite").GetTextureFromLayers("Unlocked"));
		}
	}
}
