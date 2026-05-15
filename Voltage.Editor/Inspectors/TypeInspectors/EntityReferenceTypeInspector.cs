using System;
using ImGuiNET;
using Voltage;
using Voltage.Editor.Utils;
using Voltage.Utils;
using Num = System.Numerics;

namespace Voltage.Editor.Inspectors.TypeInspectors;

/// <summary>
/// Inspector for public Entity-typed and Transform-typed fields on a Component.
/// Renders a drag-and-drop slot that accepts entities dragged from the scene graph,
/// or opens an inline picker popup. Mirrors ComponentReferenceTypeInspector.
/// </summary>
public class EntityReferenceTypeInspector : AbstractTypeInspector
{
	public const string DragDropPayloadId = "VOLTAGE_ENTITY_REF";

	/// <summary>Shared slot: scene graph deposits the dragged entity here.</summary>
	public static Entity DraggedEntity;

	private bool _showPicker;

	/// <summary>True when this inspector is bound to a Transform-typed field.</summary>
	private bool _isTransformField;

	public override void Initialize()
	{
		base.Initialize();
		_isTransformField = _valueType == typeof(Transform);
	}

	public override void DrawMutable()
	{
		// Resolve the current value to an Entity for display regardless of field type
		Entity current = null;
		if (_isTransformField)
		{
			var t = _getter(_target) as Transform;
			current = t?.Entity;
		}
		else
		{
			current = _getter(_target) as Entity;
		}

		ImGui.Text(_name);
		ImGui.SameLine();

		var label = current != null
			? current.Name
			: $"None ({_valueType.Name})";  // was hardcoded "None (Entity)"

		var buttonColor = current != null
			? new Num.Vector4(0.2f, 0.45f, 0.35f, 0.9f)
			: new Num.Vector4(0.22f, 0.22f, 0.22f, 0.9f);

		ImGui.PushStyleColor(ImGuiCol.Button, buttonColor);
		ImGui.PushStyleColor(ImGuiCol.ButtonHovered, buttonColor with { W = 1f });

		if (ImGui.Button($"{label}##entref_{_scopeId}", new Num.Vector2(-1, 0)))
			_showPicker = !_showPicker;

		ImGui.PopStyleColor(2);
		HandleTooltip();

		if (ImGui.BeginDragDropTarget())
		{
			var payload = ImGui.AcceptDragDropPayload(DragDropPayloadId);

			bool accepted;
			unsafe { accepted = payload.NativePtr != null; }

			if (accepted && DraggedEntity != null)
			{
				AssignEntity(DraggedEntity);
				DraggedEntity = null;
			}

			ImGui.EndDragDropTarget();
		}

		// Right-click: clear
		if (current != null && ImGui.BeginPopupContextItem($"entref_ctx_{_scopeId}"))
		{
			if (ImGui.Selectable("Clear"))
				SetValueWithUndo(null, $"Clear {_name}");

			ImGui.EndPopup();
		}

		if (_showPicker)
		{
			ImGui.OpenPopup($"entref_picker_{_scopeId}");
			_showPicker = false;
		}

		DrawPickerPopup(current);
	}

	private void AssignEntity(Entity entity)
	{
		if (_isTransformField)
			SetValueWithUndo(entity?.Transform, $"Assign {_name}");
		else
			SetValueWithUndo(entity, $"Assign {_name}");
	}

	private void DrawPickerPopup(Entity current)
	{
		var center = new Num.Vector2(Screen.Width * 0.5f, Screen.Height * 0.5f);
		ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new Num.Vector2(0.5f, 0.5f));
		ImGui.SetNextWindowSize(new Num.Vector2(400, 450), ImGuiCond.Appearing);

		bool open = true;
		if (!ImGui.BeginPopupModal($"entref_picker_{_scopeId}", ref open, ImGuiWindowFlags.NoResize))
			return;

		// Show field name + type in the popup title
		ImGui.TextColored(new Num.Vector4(0.4f, 1f, 0.6f, 1f), $"{_name}  ({_valueType.Name})");
		ImGui.Separator();

		if (ImGui.Selectable($"  None ({_valueType.Name})", current == null))
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
				bool isSelected = entity == current;

				if (ImGui.Selectable($"  {entity.Name}", isSelected))
				{
					AssignEntity(entity);
					ImGui.CloseCurrentPopup();
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