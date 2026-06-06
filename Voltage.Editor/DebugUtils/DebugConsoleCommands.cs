using Voltage.Console;
using Voltage.Editor.ImGuiCore;

namespace Voltage.Editor.DebugUtils
{

	public class DebugConsoleCommands
	{

		[Command("editor", "Closes the Editor and maximizes the game window.")]
		public static void EditorOn(bool on)
		{
			var service = Core.GetGlobalManager<ImGuiManager>();
			if (service == null)
			{
				service = new ImGuiManager();
				Core.RegisterGlobalManager(service);
			}
			else
			{
				service.SetEnabled(on);
			}
		}
	}
}
