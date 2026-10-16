using System.Collections.Generic;
using System.Linq;
using ImGuiNET;
using Voltage.Utils;
using Voltage.Editor.Inspectors.ObjectInspectors;
using Voltage.Editor.Utils;

namespace Voltage.Editor.Inspectors.SceneGraphPanes
{
	/// <summary>
	/// manages displaying the current Renderers in the Scene
	/// </summary>
	public class RenderersPane
	{
		readonly List<RendererInspector> _renderers = new();
		Scene _scene;

		void UpdateRenderersPaneList()
		{
			var scene = Core.Scene;
			if (_scene != scene)
			{
				_scene = scene;
				_renderers.Clear();
			}
			if (scene == null)
				return;

			_renderers.RemoveAll(inspector =>
				!scene._renderers.Contains(inspector.Renderer) &&
				!scene._afterPostProcessorRenderers.Contains(inspector.Renderer));

			for (var i = 0; i < scene._renderers.Length; i++)
				AddRendererInspector(scene._renderers.Buffer[i]);
			for (var i = 0; i < scene._afterPostProcessorRenderers.Length; i++)
				AddRendererInspector(scene._afterPostProcessorRenderers.Buffer[i]);
		}

		void AddRendererInspector(Renderer renderer)
		{
			if (!_renderers.Any(inspector => inspector.Renderer == renderer))
				_renderers.Add(new RendererInspector(renderer));
		}

		public void OnSceneChanged()
		{
			_renderers.Clear();
			_scene = null;
			UpdateRenderersPaneList();
		}

		public void Draw()
		{
			UpdateRenderersPaneList();

			ImGui.Indent();
			foreach (var inspector in _renderers)
			{
				inspector.Draw();
				VoltageEditorUtils.SmallVerticalSpace();
			}

			if (_renderers.Count == 0)
				VoltageEditorUtils.SmallVerticalSpace();

			ImGui.Unindent();
		}
	}
}
