using System.Collections.Generic;
using System.Linq;
using ImGuiNET;
using Nez.Utils;
using Voltage.Editor.Inspectors.ObjectInspectors;
using Voltage.Editor.Utils;

namespace Voltage.Editor.Inspectors.SceneGraphPanes
{
	/// <summary>
	/// manages displaying the current Renderers in the Scene
	/// </summary>
	public class RenderersPane
	{
		List<RendererInspector> _renderers = new List<RendererInspector>();
		bool _isRendererListInitialized;

		void UpdateRenderersPaneList()
		{
			// first, we check our list of inspectors and sync it up with the current list of PostProcessors in the Scene.
			// we limit the check to once every 60 fames
			if (!_isRendererListInitialized || Time.FrameCount % 60 == 0)
			{
				_isRendererListInitialized = true;

				for (var i = 0; i < Nez.Core.Scene._renderers.Length; i++)
				{
					var renderer = Nez.Core.Scene._renderers.Buffer[i];
					if (!_renderers.Any(inspector => inspector.Renderer == renderer))
					{
						_renderers.Add(new RendererInspector(renderer));
					}
				}
			}
		}

		public void OnSceneChanged()
		{
			_renderers.Clear();
			_isRendererListInitialized = false;
			UpdateRenderersPaneList();
		}

		public void Draw()
		{
			UpdateRenderersPaneList();

			ImGui.Indent();
			for (var i = 0; i < _renderers.Count; i++)
			{
				// watch out for removed Renderers
				if (_renderers[i].Renderer.Scene == null)
				{
					_renderers.RemoveAt(i);
					continue;
				}

				_renderers[i].Draw();
				VoltageEditorUtils.SmallVerticalSpace();
			}

			if (_renderers.Count == 0)
				VoltageEditorUtils.SmallVerticalSpace();

			ImGui.Unindent();
		}
	}
}