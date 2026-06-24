using System;
using ImGuiNET;
using Voltage;
using Voltage.Editor.ImGuiCore;
using Voltage.Editor.Utils;
using Voltage.Utils;
using Num = System.Numerics;

namespace Voltage.Editor.Inspectors.TypeInspectors;

public class EntityReferenceTypeInspector : AbstractTypeInspector
{
	public const string DragDropPayloadId = "VOLTAGE_ENTITY_REF";

	public static Entity DraggedEntity;

	private bool _showPicker;
	private string _pickerSearch = string.Empty;

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

		ImGuiSafe.TextSafe(_name);
		ImGui.SameLine();

		var label = current != null
			? current.Name
			: $"None ({_valueType.Name})";  // was hardcoded "None (Entity)"

		var buttonColor = current != null
			? new Num.Vector4(0.2f, 0.45f, 0.35f, 0.9f)
			: new Num.Vector4(0.22f, 0.22f, 0.22f, 0.9f);

		ImGui.PushStyleColor(ImGuiCol.Button, buttonColor);
		ImGui.PushStyleColor(ImGuiCol.ButtonHovered, buttonColor with { W = 1f });

		ImGui.Button($"{label}##entref_{_scopeId}", new Num.Vector2(-1, 0));
		if (ImGui.IsItemHovered())
		{
			// Single click highlights the referenced entity in the scene graph; double click
			// opens the picker to change it.
			if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
				_showPicker = true;
			else if (ImGui.IsItemClicked(ImGuiMouseButton.Left) && current != null)
				PingEntityInSceneGraph(current);
		}

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
			_pickerSearch = string.Empty;
			_showPicker = false;
		}

		DrawPickerPopup(current);
	}

	/// <summary>
	/// Selects the referenced entity in the Scene Graph (expanding its parents and moving the
	/// camera to it) so the user can confirm which entity the reference actually points at,
	/// without changing which entity the inspector is editing.
	/// </summary>
	internal static void PingEntityInSceneGraph(Entity entity)
	{
		if (entity == null)
			return;

		var imgr = Core.GetGlobalManager<ImGuiManager>();
		var graph = imgr?.SceneGraphWindow;
		if (graph == null)
			return;

		var p = entity.Transform.Parent;
		while (p != null)
		{
			graph.ExpandedEntities.Add(p.Entity);
			p = p.Parent;
		}

		graph.EntityPane.SetSelectedEntity(entity, false);
		imgr.CursorSelectionManager.SetCameraTargetPosition(entity.Transform.Position);
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

		ImGuiSafe.TextColoredSafe(new Num.Vector4(0.4f, 1f, 0.6f, 1f), $"{_name}  ({_valueType.Name})");
		ImGui.Separator();

		ImGui.SetNextItemWidth(-1);
		ImGui.InputTextWithHint("##refsearch", "Search...", ref _pickerSearch, 128);
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
			bool filtering = !string.IsNullOrEmpty(_pickerSearch);
			if (filtering)
			{
				string lower = _pickerSearch.ToLowerInvariant();
				for (int e = 0; e < scene.Entities.Count; e++)
				{
					var entity = scene.Entities[e];
					if (!entity.Name.ToLowerInvariant().Contains(lower))
						continue;

					ImGui.PushID((int)entity.Id);
					if (ImGui.Selectable($"  {entity.Name}", entity == current))
					{
						AssignEntity(entity);
						ImGui.CloseCurrentPopup();
					}
					ImGui.PopID();
				}
			}
			else
			{
				for (int e = 0; e < scene.Entities.Count; e++)
				{
					var entity = scene.Entities[e];
					if (entity.Transform.Parent == null)
						DrawEntityTreeNode(entity, current);
				}
			}
		}

		ImGui.Separator();
		VoltageEditorUtils.SmallVerticalSpace();
		if (VoltageEditorUtils.CenteredButton("Cancel", 0.5f))
			ImGui.CloseCurrentPopup();

		ImGui.EndPopup();
	}

	private void DrawEntityTreeNode(Entity entity, Entity current)
	{
		bool isSelected = entity == current;
		bool hasChildren = entity.Transform.ChildCount > 0;

		ImGui.PushID((int)entity.Id);

		if (hasChildren)
		{
			bool nodeOpen = ImGui.TreeNodeEx(entity.Name,
				ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.SpanAvailWidth
				| (isSelected ? ImGuiTreeNodeFlags.Selected : 0));

			if (ImGui.IsItemClicked(ImGuiMouseButton.Left) &&
			    ImGui.GetMousePos().X - ImGui.GetItemRectMin().X > ImGui.GetTreeNodeToLabelSpacing())
			{
				AssignEntity(entity);
				ImGui.CloseCurrentPopup();
			}

			if (nodeOpen)
			{
				for (int i = 0; i < entity.Transform.ChildCount; i++)
					DrawEntityTreeNode(entity.Transform.GetChild(i).Entity, current);

				ImGui.TreePop();
			}
		}
		else
		{
			ImGui.TreeNodeEx(entity.Name,
				ImGuiTreeNodeFlags.Leaf | ImGuiTreeNodeFlags.NoTreePushOnOpen
				| ImGuiTreeNodeFlags.SpanAvailWidth
				| (isSelected ? ImGuiTreeNodeFlags.Selected : 0));

			if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
			{
				AssignEntity(entity);
				ImGui.CloseCurrentPopup();
			}
		}

		ImGui.PopID();
	}
}