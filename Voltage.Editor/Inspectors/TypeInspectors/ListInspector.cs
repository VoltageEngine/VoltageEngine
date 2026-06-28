using System;
using System.Collections;
using System.Collections.Generic;
using ImGuiNET;
using Microsoft.Xna.Framework;
using Voltage;
using Voltage.Editor.Utils;
using Voltage.Utils;
using Num = System.Numerics;


namespace Voltage.Editor.Inspectors.TypeInspectors
{
	public class ListInspector : AbstractTypeInspector
	{
		public static Type[] KSupportedTypes =
			{typeof(int), typeof(uint), typeof(long), typeof(ulong), typeof(float), typeof(string), typeof(Vector2)};

		enum ElementKind { Primitive, Entity, Transform, Component }

		IList _list;
		Type _elementType;
		bool _isArray;
		ElementKind _elementKind;

		// Picker state: which list index is waiting for a picker popup this frame
		int _pendingPickerIndex = -1;
		// Which list index the currently-open picker is editing
		int _activePickerIndex = -1;
		// Search text for the entity hierarchy picker (shared popup, one per ListInspector)
		string _pickerSearch = string.Empty;

		public override void Initialize()
		{
			base.Initialize();

			_isArray = _valueType.IsArray;
			_elementType = _isArray ? _valueType.GetElementType() : _valueType.GetGenericArguments()[0];

			if (_elementType == typeof(Entity))
				_elementKind = ElementKind.Entity;
			else if (_elementType == typeof(Transform))
				_elementKind = ElementKind.Transform;
			else if (typeof(Component).IsAssignableFrom(_elementType))
				_elementKind = ElementKind.Component;
			else
				_elementKind = ElementKind.Primitive;

			RefreshList();
		}

		public override void DrawMutable()
		{
			// Re-fetch in case the backing field was replaced (e.g. array resize)
			RefreshList();

			ImGui.Indent();
			if (ImGui.CollapsingHeader($"{_name} [{_list.Count}]###{_name}", ImGuiTreeNodeFlags.FramePadding))
			{
				ImGui.Indent();

				if (_isArray)
				{
					// Array size control — arrays must be replaced to resize
					int size = _list.Count;
					ImGui.SetNextItemWidth(100);
					if (ImGui.InputInt($"Size##arrsize_{_scopeId}", ref size, 1) && size >= 0 && size != _list.Count)
						ResizeArray(size);
				}
				else
				{
					// List: Add / Clear
					if (ImGui.Button("Add Element"))
					{
						if (_elementKind != ElementKind.Primitive)
							_list.Add(null);
						else if (_elementType == typeof(string))
							_list.Add("");
						else
							_list.Add(Activator.CreateInstance(_elementType));
					}

					ImGui.SameLine(ImGui.GetWindowWidth() - ImGui.GetItemRectSize().X -
					               ImGui.GetStyle().ItemInnerSpacing.X);

					if (ImGui.Button("Clear"))
						ImGui.OpenPopup("Clear Data");

					if (VoltageEditorUtils.SimpleDialog("Clear Data", "Are you sure you want to clear the data?"))
					{
						_list.Clear();
						Debug.Log($"list count: {_list.Count}");
					}
				}

				ImGui.PushItemWidth(-ImGui.GetStyle().IndentSpacing);

				int removeAt = -1;
				for (var i = 0; i < _list.Count; i++)
				{
					if (_elementKind == ElementKind.Primitive)
						DrawPrimitiveSlot(i);
					else
						DrawReferenceSlot(i, ref removeAt);
				}

				if (removeAt >= 0 && !_isArray)
					_list.RemoveAt(removeAt);

				ImGui.PopItemWidth();

				// Draw the single shared picker popup — one per ListInspector per frame
				if (_elementKind == ElementKind.Entity || _elementKind == ElementKind.Transform)
					DrawEntityPickerPopup();
				else if (_elementKind == ElementKind.Component)
					DrawComponentPickerPopup();

				ImGui.Unindent();
			}

			ImGui.Unindent();
		}


		void DrawPrimitiveSlot(int i)
		{
			if (_elementType == typeof(int))
				DrawWidget((int) Convert.ChangeType(_list[i], _elementType), i);
			else if (_elementType == typeof(float))
				DrawWidget((float) Convert.ChangeType(_list[i], _elementType), i);
			else if (_elementType == typeof(string))
				DrawWidget((string) Convert.ChangeType(_list[i], _elementType), i);
			else if (_elementType == typeof(Vector2))
				DrawWidget((Vector2) Convert.ChangeType(_list[i], _elementType), i);
		}

		void DrawWidget(int value, int index)
		{
			if (ImGui.DragInt($"{index}", ref value))
			{
				_list[index] = value;
				SpecialCasesHandling();
			}
		}

		void DrawWidget(float value, int index)
		{
			if (ImGui.DragFloat($"{index}", ref value))
			{
				_list[index] = value;
				SpecialCasesHandling();
			}
		}

		void DrawWidget(string value, int index)
		{
			if (ImGui.InputText($"{index}", ref value, 200))
			{
				_list[index] = value;
				SpecialCasesHandling();
			}
		}

		void DrawWidget(Vector2 value, int index)
		{
			var vec = value.ToNumerics();
			if (ImGui.DragFloat2($"{index}", ref vec))
			{
				_list[index] = vec.ToXNA();
				SpecialCasesHandling();
			}
		}

		// Reference element drawing (Entity / Transform / Component)

		void DrawReferenceSlot(int index, ref int removeAt)
		{
			ImGui.PushID(index);

			// Build label and colour based on what's currently assigned
			string label;
			Num.Vector4 buttonColor;

			if (_elementKind == ElementKind.Component)
			{
				var comp = _list[index] as Component;
				label = comp != null ? comp.ToString() : $"None ({_elementType.Name})";
				buttonColor = comp != null
					? new Num.Vector4(0.2f, 0.35f, 0.55f, 0.9f)
					: new Num.Vector4(0.22f, 0.22f, 0.22f, 0.9f);
			}
			else
			{
				var entity = ResolveEntityAt(index);
				label = entity != null ? entity.Name : $"None ({_elementType.Name})";
				buttonColor = entity != null
					? new Num.Vector4(0.2f, 0.45f, 0.35f, 0.9f)
					: new Num.Vector4(0.22f, 0.22f, 0.22f, 0.9f);
			}

			// Row: [index]  [reference button ───────────]  [X (lists only)]
			ImGui.AlignTextToFramePadding();
			ImGuiSafe.TextSafe($"{index}");
			ImGui.SameLine();

			float removeButtonWidth = !_isArray ? 26f : 0f;
			float spacing = !_isArray ? ImGui.GetStyle().ItemSpacing.X : 0f;
			float availableWidth = ImGui.GetContentRegionAvail().X - removeButtonWidth - spacing;

			ImGui.PushStyleColor(ImGuiCol.Button, buttonColor);
			ImGui.PushStyleColor(ImGuiCol.ButtonHovered, buttonColor with { W = 1f });

			if (ImGui.Button($"{label}##ref_{index}", new Num.Vector2(availableWidth, 0)))
				_pendingPickerIndex = index;

			ImGui.PopStyleColor(2);

			// Drag-drop target on the button
			if (ImGui.BeginDragDropTarget())
			{
				bool accepted = false;

				if (_elementKind == ElementKind.Component)
				{
					var payload = ImGui.AcceptDragDropPayload(ComponentReferenceTypeInspector.DragDropPayloadId);
					unsafe { accepted = payload.NativePtr != null; }
					if (accepted && ComponentReferenceTypeInspector.DraggedComponent != null)
					{
						var dragged = ComponentReferenceTypeInspector.DraggedComponent;
						if (_elementType.IsAssignableFrom(dragged.GetType()))
							_list[index] = dragged;
						else
							Debug.Warn($"[ListInspector] Cannot assign {dragged.GetType().Name} to element of type {_elementType.Name}.");
						ComponentReferenceTypeInspector.DraggedComponent = null;
					}
				}
				else
				{
					var payload = ImGui.AcceptDragDropPayload(EntityReferenceTypeInspector.DragDropPayloadId);
					unsafe { accepted = payload.NativePtr != null; }
					if (accepted && EntityReferenceTypeInspector.DraggedEntity != null)
					{
						AssignEntityAt(index, EntityReferenceTypeInspector.DraggedEntity);
						EntityReferenceTypeInspector.DraggedEntity = null;
					}
				}

				ImGui.EndDragDropTarget();
			}

			// Right-click: clear slot
			if (ImGui.BeginPopupContextItem($"reflist_ctx_{index}"))
			{
				if (ImGui.Selectable("Clear"))
					_list[index] = null;
				ImGui.EndPopup();
			}

			// Remove button (lists only)
			if (!_isArray)
			{
				ImGui.SameLine();
				if (ImGui.Button($"X##rem_{index}", new Num.Vector2(removeButtonWidth, 0)))
					removeAt = index;
			}

			ImGui.PopID();
		}

		void DrawEntityPickerPopup()
		{
			if (_pendingPickerIndex >= 0)
			{
				ImGui.OpenPopup($"reflist_epicker_{_scopeId}");
				_activePickerIndex = _pendingPickerIndex;
				_pendingPickerIndex = -1;
				_pickerSearch = string.Empty;
			}

			if (_activePickerIndex < 0 || _activePickerIndex >= _list.Count)
				return;

			var center = new Num.Vector2(Screen.Width * 0.5f, Screen.Height * 0.5f);
			ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new Num.Vector2(0.5f, 0.5f));
			ImGui.SetNextWindowSize(new Num.Vector2(400, 450), ImGuiCond.Appearing);

			bool open = true;
			if (!ImGui.BeginPopupModal($"reflist_epicker_{_scopeId}", ref open, ImGuiWindowFlags.NoResize))
				return;

			var currentEntity = ResolveEntityAt(_activePickerIndex);

			// Shared hierarchy picker — identical search + scene-tree UI as single Entity/Transform fields.
			if (EntityHierarchyPicker.DrawBody($"{_name}[{_activePickerIndex}]", _elementType, currentEntity,
				    ref _pickerSearch, out bool cleared, out var picked))
			{
				if (cleared)
					_list[_activePickerIndex] = null;
				else
					AssignEntityAt(_activePickerIndex, picked);

				_activePickerIndex = -1;
				ImGui.CloseCurrentPopup();
			}

			ImGui.Separator();
			VoltageEditorUtils.SmallVerticalSpace();
			if (VoltageEditorUtils.CenteredButton("Cancel", 0.5f))
			{
				_activePickerIndex = -1;
				ImGui.CloseCurrentPopup();
			}

			ImGui.EndPopup();
		}

		void DrawComponentPickerPopup()
		{
			if (_pendingPickerIndex >= 0)
			{
				ImGui.OpenPopup($"reflist_cpicker_{_scopeId}");
				_activePickerIndex = _pendingPickerIndex;
				_pendingPickerIndex = -1;
			}

			if (_activePickerIndex < 0 || _activePickerIndex >= _list.Count)
				return;

			var center = new Num.Vector2(Screen.Width * 0.5f, Screen.Height * 0.5f);
			ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new Num.Vector2(0.5f, 0.5f));
			ImGui.SetNextWindowSize(new Num.Vector2(400, 450), ImGuiCond.Appearing);

			bool open = true;
			if (!ImGui.BeginPopupModal($"reflist_cpicker_{_scopeId}", ref open, ImGuiWindowFlags.NoResize))
				return;

			ImGuiSafe.TextColoredSafe(new Num.Vector4(0.3f, 0.8f, 1f, 1f),
				$"{_name}[{_activePickerIndex}]  ({_elementType.Name})");
			ImGui.Separator();

			var currentComp = _list[_activePickerIndex] as Component;

			if (ImGui.Selectable($"  None ({_elementType.Name})", currentComp == null))
			{
				_list[_activePickerIndex] = null;
				_activePickerIndex = -1;
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
						if (!_elementType.IsAssignableFrom(comp.GetType()))
							continue;

						bool isSelected = comp == currentComp;
						if (ImGui.Selectable($"  {entity.Name}  /  {comp}", isSelected))
						{
							_list[_activePickerIndex] = comp;
							_activePickerIndex = -1;
							ImGui.CloseCurrentPopup();
						}
					}
				}
			}

			ImGui.Separator();
			VoltageEditorUtils.SmallVerticalSpace();
			if (VoltageEditorUtils.CenteredButton("Cancel", 0.5f))
			{
				_activePickerIndex = -1;
				ImGui.CloseCurrentPopup();
			}

			ImGui.EndPopup();
		}


		Entity ResolveEntityAt(int index)
		{
			if (_elementKind == ElementKind.Transform)
				return (_list[index] as Transform)?.Entity;
			return _list[index] as Entity;
		}

		void AssignEntityAt(int index, Entity entity)
		{
			_list[index] = _elementKind == ElementKind.Transform
				? (object) entity?.Transform
				: entity;
		}

		void ResizeArray(int newSize)
		{
			var newArray = Array.CreateInstance(_elementType, newSize);
			Array.Copy((Array) _list, newArray, Math.Min(_list.Count, newSize));
			_setter(newArray);
			_list = newArray;
		}

		void RefreshList()
		{
			var current = _getter(_target) as IList;
			if (current != null)
			{
				_list = current;
				return;
			}

			// Field is null — create a default empty collection
			if (_isArray)
			{
				_list = Array.CreateInstance(_elementType, 0);
			}
			else
			{
				var listType = typeof(List<>).MakeGenericType(_elementType);
				_list = (IList) Activator.CreateInstance(listType);
			}
		}

		private void SpecialCasesHandling()
		{
			if (_target is PolygonCollider polyCollider)
				polyCollider.UpdateShapeFromPoints();
		}
	}
}
