using System;
using ImGuiNET;
using Voltage.Editor.Utils;
using Voltage.Utils;
using Num = System.Numerics;

namespace Voltage.Editor.Inspectors.TypeInspectors;

/// <summary>
/// Inspector for public Component-typed fields on a Component.
/// Allows for drag-and-drop assignment and inline picker selection of components.
/// </summary>
public class ComponentReferenceTypeInspector : AbstractTypeInspector
{
	// ImGui drag-drop identifier used by the scene graph when dragging a component row
	public const string DragDropPayloadId = "VOLTAGE_COMPONENT_REF";
	public static Component DraggedComponent;

	private bool _showPicker;

	public override void DrawMutable()
	{
		var current = _getter(_target) as Component;
		var fieldType = _valueType; // e.g. SpriteAnimator

		ImGui.Text(_name);
		ImGui.SameLine();

		var label = current != null
			? current.ToString()
			: $"None ({fieldType.Name})";

		var buttonColor = current != null
			? new Num.Vector4(0.2f, 0.35f, 0.55f, 0.9f)
			: new Num.Vector4(0.22f, 0.22f, 0.22f, 0.9f);

		ImGui.PushStyleColor(ImGuiCol.Button, buttonColor);
		ImGui.PushStyleColor(ImGuiCol.ButtonHovered, buttonColor with { W = 1f });

		if (ImGui.Button($"{label}##compref_{_scopeId}", new Num.Vector2(-1, 0)))
			_showPicker = !_showPicker;

		ImGui.PopStyleColor(2);
		HandleTooltip();

		if (ImGui.BeginDragDropTarget())
		{
			var payload = ImGui.AcceptDragDropPayload(DragDropPayloadId);

			bool accepted;
			unsafe { accepted = payload.NativePtr != null; } 

			if (accepted && DraggedComponent != null)
			{
				if (fieldType.IsAssignableFrom(DraggedComponent.GetType()))
					SetValueWithUndo(DraggedComponent, $"Assign {_name}");
				else
					Debug.Warn($"[ComponentRef] Cannot assign {DraggedComponent.GetType().Name} to field of type {fieldType.Name}.");

				DraggedComponent = null;
			}

			ImGui.EndDragDropTarget();
		}

		// Clear with right-click
		if (current != null && ImGui.BeginPopupContextItem($"compref_ctx_{_scopeId}"))
		{
			if (ImGui.Selectable("Clear"))
				SetValueWithUndo(null, $"Clear {_name}");

			ImGui.EndPopup();
		}

		if (_showPicker)
		{
			ImGui.OpenPopup($"compref_picker_{_scopeId}");
			_showPicker = false;
		}

		DrawPickerPopup(current, fieldType);
	}

	private void DrawPickerPopup(Component current, Type fieldType)
	{
		var center = new Num.Vector2(Screen.Width * 0.5f, Screen.Height * 0.5f);
		ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new Num.Vector2(0.5f, 0.5f));
		ImGui.SetNextWindowSize(new Num.Vector2(400, 450), ImGuiCond.Appearing);

		bool open = true;
		if (!ImGui.BeginPopupModal($"compref_picker_{_scopeId}", ref open, ImGuiWindowFlags.NoResize))
			return;

		ImGui.TextColored(new Num.Vector4(0.3f, 0.8f, 1f, 1f), $"Select {fieldType.Name}");
		ImGui.Separator();

		// None option
		bool noneSelected = current == null;
		if (ImGui.Selectable($"  None ({fieldType.Name})", noneSelected))
		{
			SetValueWithUndo(null, $"Clear {_name}");
			ImGui.CloseCurrentPopup();
		}

		ImGui.Separator();

		var scene = Core.Scene;
		if (scene != null)
		{
			for (var e = 0; e < scene.Entities.Count; e++)
			{
				var entity = scene.Entities[e];
				for (var c = 0; c < entity.Components.Count; c++)
				{
					var comp = entity.Components[c];
					if (!fieldType.IsAssignableFrom(comp.GetType()))
						continue;

					bool isSelected = comp == current;
					var displayName = $"  {entity.Name}  /  {comp}";

					if (ImGui.Selectable(displayName, isSelected))
					{
						SetValueWithUndo(comp, $"Assign {_name} = {comp}");
						ImGui.CloseCurrentPopup();
					}
				}
			}
		}

		ImGui.Separator();
		VoltageEditorUtils.SmallVerticalSpace();
		if (VoltageEditorUtils.CenteredButton("Cancel", 0.5f))
			ImGui.CloseCurrentPopup();

		ImGui.EndPopup();
	}
}