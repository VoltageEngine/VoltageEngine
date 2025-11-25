using ImGuiNET;
using Nez;
using Voltage.Editor.Core;
using Voltage.Editor.Inspectors.TypeInspectors;
using Voltage.Editor.Utils;

namespace Voltage.Editor.Inspectors.ObjectInspectors;

public class RendererInspector
{
	public Renderer Renderer => _renderer;

	private int _scopeId = VoltageEditorUtils.GetScopeId();
	private string _name;
	private Renderer _renderer;
	private MaterialInspector _materialInspector;

	public RendererInspector(Renderer renderer)
	{
		_renderer = renderer;
		_name = _renderer.GetType().Name;
		_materialInspector = new MaterialInspector
		{
			AllowsMaterialRemoval = false
		};
		_materialInspector.SetTarget(renderer, renderer.GetType().GetField("Material"));
		_materialInspector.Initialize(); 
	}

	public void Draw()
	{
		ImGui.PushID(_scopeId);
		var isOpen = ImGui.CollapsingHeader(_name);

		VoltageEditorUtils.ShowContextMenuTooltip();

		if (ImGui.BeginPopupContextItem())
		{
			if (ImGui.Selectable("Remove Renderer"))
			{
				isOpen = false;
				Nez.Core.Scene.RemoveRenderer(_renderer);
				ImGui.CloseCurrentPopup();
			}

			ImGui.EndPopup();
		}

		if (isOpen)
		{
			ImGui.Indent();

			_materialInspector.Draw();

			ImGui.Checkbox("shouldDebugRender", ref Renderer.ShouldDebugRender);

			var value = Renderer.RenderTargetClearColor.ToNumerics();
			if (ImGui.ColorEdit4("renderTargetClearColor", ref value))
				Renderer.RenderTargetClearColor = value.ToXNAColor();

			if (Renderer.Camera != null)
				if (VoltageEditorUtils.LabelButton("Camera", Renderer.Camera.Entity.Name))
					Nez.Core.GetGlobalManager<ImGuiManager>().OpenSeparateEntityInspector(Renderer.Camera.Entity);

			ImGui.PushStyleVar(ImGuiStyleVar.Alpha, ImGui.GetStyle().Alpha * 0.5f);
			VoltageEditorUtils.DisableNextWidget();
			var tempBool = Renderer.WantsToRenderToSceneRenderTarget;
			ImGui.Checkbox("wantsToRenderToSceneRenderTarget", ref tempBool);

			VoltageEditorUtils.DisableNextWidget();
			tempBool = Renderer.WantsToRenderAfterPostProcessors;
			ImGui.Checkbox("wantsToRenderAfterPostProcessors", ref tempBool);
			ImGui.PopStyleVar();

			ImGui.Unindent();
		}

		ImGui.PopID();
	}
}