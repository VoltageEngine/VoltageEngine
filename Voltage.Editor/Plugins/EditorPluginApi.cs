using System;
using Voltage.Editor.ImGuiCore;
using Voltage.Editor.ProjectFile;

namespace Voltage.Editor.Plugins
{
	/// <summary>Editor-plugin API version. A manifest declaring a different one is refused rather than risking an ABI crash, since editor plugins bind directly against Voltage.Editor.dll.</summary>
	public static class EditorPluginApi
	{
		/// <summary>v2 added EditorMenu placement to IEditorPluginContext.AddMenuItem.</summary>
		public const int Version = 2;
	}

	/// <summary>Menus a plugin may contribute to. A plugin can add to a menu, never restructure one.</summary>
	public enum EditorMenu
	{
		/// <summary>Under Plugins, grouped per plugin. The default, and the right answer for most tools.</summary>
		Plugins,

		File,
		Project,
		View,
		Scripting,
		Effects,
		Build,
		Help,
	}

	/// <summary>Entry point of an editor plugin, instantiated at project open from an assembly listed under the manifest's Editor.Assemblies. The API is unstable by contract: declare EditorPluginApiVersion in plugin.json.</summary>
	public interface IEditorPlugin
	{
		/// <summary>Called once after load. Throwing disables this plugin without crashing the editor.</summary>
		void Initialize(IEditorPluginContext context);

		/// <summary>Called when the project closes or the editor shuts down. Release resources here.</summary>
		void Shutdown();
	}

	/// <summary>The editor services handed to an <see cref="IEditorPlugin"/> at initialization.</summary>
	public interface IEditorPluginContext
	{
		/// <summary>Registers a window drawn every frame while IsOpen; it owns its own Begin/End.</summary>
		void RegisterWindow(EditorPluginWindow window);

		/// <summary>Adds an entry under the Plugins menu. '/' nests submenus.</summary>
		void AddMenuItem(string path, Action onClick);

		/// <summary>Adds an entry to a specific menu, for a tool that belongs beside the host's own commands.</summary>
		void AddMenuItem(EditorMenu menu, string path, Action onClick);

		/// <summary>The editor's ImGui manager (texture binding, layout services, ...).</summary>
		ImGuiManager ImGuiManager { get; }

		/// <summary>The currently open project.</summary>
		IGameProject CurrentProject { get; }

		/// <summary>Fired when the current project is about to close (before plugin shutdown).</summary>
		event Action ProjectClosing;
	}

	/// <summary>Base class for plugin windows. Pass ref IsOpen to Begin so the close button works.</summary>
	public abstract class EditorPluginWindow
	{
		/// <summary>Window title (also used as the ImGui id - keep it unique within your plugin).</summary>
		public string Title = "Plugin Window";

		public bool IsOpen;

		public abstract void Draw();
	}
}
