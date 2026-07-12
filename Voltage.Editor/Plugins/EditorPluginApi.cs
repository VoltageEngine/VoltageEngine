using System;
using Voltage.Editor.ImGuiCore;
using Voltage.Editor.ProjectFile;

namespace Voltage.Editor.Plugins
{
	/// <summary>
	/// Version of the editor-plugin API surface. Bumped when <see cref="IEditorPlugin"/>,
	/// <see cref="IEditorPluginContext"/>, or <see cref="EditorPluginWindow"/> change incompatibly.
	/// A plugin whose manifest declares a different EditorPluginApiVersion is refused with a clear
	/// message rather than risking an ABI crash (editor plugins bind directly against
	/// Voltage.Editor.dll and ImGui.NET).
	/// </summary>
	public static class EditorPluginApi
	{
		public const int Version = 1;
	}

	/// <summary>
	/// Entry point of an editor plugin. Implement this in an assembly listed under the manifest's
	/// <c>Editor.Assemblies</c>; the editor instantiates every concrete implementation with a
	/// parameterless constructor at project open.
	///
	/// Editor plugins reference <c>Voltage.Editor.dll</c> directly (Unity-style: full access to
	/// windows, inspectors, undo, ImGui). The API is unstable by contract — pin the editor version
	/// your plugin targets and declare <c>EditorPluginApiVersion</c> in plugin.json.
	/// </summary>
	public interface IEditorPlugin
	{
		/// <summary>
		/// Called once after the plugin loads for an opened project. Register windows and menu items
		/// through the context here. Throwing disables this plugin (surfaced in the Plugin Manager)
		/// without crashing the editor.
		/// </summary>
		void Initialize(IEditorPluginContext context);

		/// <summary>Called when the project closes or the editor shuts down. Release resources here.</summary>
		void Shutdown();
	}

	/// <summary>The editor services handed to an <see cref="IEditorPlugin"/> at initialization.</summary>
	public interface IEditorPluginContext
	{
		/// <summary>
		/// Registers a window drawn every editor frame while its IsOpen is true. Windows own their
		/// full ImGui.Begin/End lifecycle inside <see cref="EditorPluginWindow.Draw"/>.
		/// </summary>
		void RegisterWindow(EditorPluginWindow window);

		/// <summary>
		/// Adds an entry under the editor's Plugins menu. Use '/' to nest submenus, e.g.
		/// "FMOD/Event Browser". The action runs when the item is clicked.
		/// </summary>
		void AddMenuItem(string path, Action onClick);

		/// <summary>The editor's ImGui manager (texture binding, layout services, …).</summary>
		ImGuiManager ImGuiManager { get; }

		/// <summary>The currently open project.</summary>
		IGameProject CurrentProject { get; }

		/// <summary>Fired when the current project is about to close (before plugin shutdown).</summary>
		event Action ProjectClosing;
	}

	/// <summary>
	/// Base class for plugin-provided editor windows. The host calls <see cref="Draw"/> every frame
	/// while <see cref="IsOpen"/> — implementations do their own ImGui.Begin/End and should pass
	/// <c>ref IsOpen</c> to Begin so the window's close button works.
	/// </summary>
	public abstract class EditorPluginWindow
	{
		/// <summary>Window title (also used as the ImGui id — keep it unique within your plugin).</summary>
		public string Title = "Plugin Window";

		public bool IsOpen;

		public abstract void Draw();
	}
}
