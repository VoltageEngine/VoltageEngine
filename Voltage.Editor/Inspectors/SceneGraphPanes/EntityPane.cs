using System;
using System.Collections.Generic;
using System.Linq;
using ImGuiNET;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Voltage.Editor.DebugUtils;
using Voltage.Editor.ImGuiCore;
using Voltage.Editor.Undo.Core;
using Voltage.Editor.Undo.ComponentActions;
using Voltage.Editor.Undo.EntityActions;
using Voltage.Editor.Utils;
using Voltage.Persistence;
using Voltage.Serialization;

namespace Voltage.Editor.Inspectors.SceneGraphPanes;

public class EntityPane
{
	#region Fields and Properties

    private const int MIN_ENTITIES_FOR_CLIPPER = 100;
    private Entity _previousEntity;
	public List<Entity> SelectedEntities => _selectedEntities;
	private ImGuiManager _imGuiManager;
	private List<Entity> _copiedEntities = new();
	private List<Entity> _selectedEntities = new();
	private Entity _lastRangeSelectEntity;

	// Drag-drop state
	private const string DragDropPayloadType = "ENTITY_DRAG";
	private bool _isDragging;
	private Entity _dragDropTarget;
	private bool _dragDropTargetIsRoot; // dropping onto root = unparent

	// When clicking an already-selected entity with no modifier, we
	// wait for mouse release without drag before collapsing the selection.
	private Entity _pendingSelectEntity;

	public void SetSelectedEntity(Entity entity, bool ctrlDown, bool shiftDown = false)
	{
		if (entity == null && !ctrlDown && !shiftDown)
			return;

		var hierarchyList = _imGuiManager.SceneGraphWindow.BuildHierarchyList();

		if (shiftDown && _lastRangeSelectEntity != null)
		{
			int startIdx = hierarchyList.IndexOf(_lastRangeSelectEntity);
			int endIdx = hierarchyList.IndexOf(entity);
			if (startIdx != -1 && endIdx != -1)
			{
				int minIdx = Math.Min(startIdx, endIdx);
				int maxIdx = Math.Max(startIdx, endIdx);
				if (ctrlDown)
				{
					// Add range to current selection
					for (int i = minIdx; i <= maxIdx; i++)
					{
						if (!_selectedEntities.Contains(hierarchyList[i]))
							_selectedEntities.Add(hierarchyList[i]);
					}
				}
				else
				{
					// Replace selection with range
					_selectedEntities.Clear();
					for (int i = minIdx; i <= maxIdx; i++)
						_selectedEntities.Add(hierarchyList[i]);
				}
			}
		}
		else if (ctrlDown)
		{
			if (_selectedEntities.Contains(entity))
				_selectedEntities.Remove(entity);
			else
				_selectedEntities.Add(entity);
			_lastRangeSelectEntity = entity;
		}
		else
		{
			_selectedEntities.Clear();
			_selectedEntities.Add(entity);
			_lastRangeSelectEntity = entity;
		}
	}
	#endregion

	#region Main Draw Entry Point

	/// <summary>
	/// Main entry point for drawing the entity pane UI and gizmos.
	/// </summary>
	public unsafe void Draw()
	{
		if (_imGuiManager == null)
			_imGuiManager = Core.GetGlobalManager<ImGuiManager>();

		// Draw entity tree (with clipper for large lists)
		if (Core.Scene.Entities.Count > MIN_ENTITIES_FOR_CLIPPER)
		{
			var clipperPtr = ImGuiNative.ImGuiListClipper_ImGuiListClipper();
			var clipper = new ImGuiListClipperPtr(clipperPtr);

			clipper.Begin(Core.Scene.Entities.Count, -1);

			while (clipper.Step())
				for (var i = clipper.DisplayStart; i < clipper.DisplayEnd; i++)
					DrawEntity(Core.Scene.Entities[i]);

			ImGuiNative.ImGuiListClipper_destroy(clipperPtr);
		}
		else
		{
			for (var i = 0; i < Core.Scene.Entities.Count; i++)
				DrawEntity(Core.Scene.Entities[i]);
		}


		// Unparent
		ImGui.InvisibleButton("##drop_root", new System.Numerics.Vector2(-1, 1));
		if (ImGui.IsItemHovered())
			ImGui.SetTooltip("Drop here to unparent");

		if (ImGui.BeginDragDropTarget())
		{
			unsafe
			{
				var payload = ImGui.AcceptDragDropPayload(DragDropPayloadType);
				if (payload.NativePtr != null)
					ReparentSelectedEntities(null, -1);
			}
			ImGui.EndDragDropTarget();
		}

		VoltageEditorUtils.MediumVerticalSpace();
		EntityDuplicationAndDeletion();
	}

	/// <summary>
	/// Reparents all currently selected entities to <paramref name="newParent"/> at <paramref name="insertIndex"/>.
	/// Passing null for newParent unparents them (makes them scene roots).
	/// Passing -1 for insertIndex appends at the end.
	/// Skips entities that would create a cycle (i.e., dropping onto a descendant).
	/// </summary>
	private void ReparentSelectedEntities(Transform newParent, int insertIndex = -1)
	{
		var entries = new List<(Entity entity, Transform oldParent, int oldIndex, Transform newParent, int newIndex)>();

		foreach (var entity in _selectedEntities)
		{
			// Skip if newParent is the entity itself or a descendant of it (cycle guard)
			if (newParent != null && IsDescendantOf(newParent, entity.Transform))
				continue;

			// Skip SceneRequired entities — they must stay at root level as designed
			if (entity.Type == Entity.InstanceType.SceneRequired)
				continue;

			// Capture old position
			int oldIndex = entity.Transform.Parent != null
				? entity.Transform.Parent.Children.IndexOf(entity.Transform)
				: Core.Scene.Entities.EntityFastList.IndexOf(entity);

			int actualInsert = insertIndex;
			if (actualInsert >= 0 && newParent == entity.Transform.Parent)
			{
				// Moving within the same parent: if old slot is before insert point the
				// removal shifts everything down by one.
				if (oldIndex >= 0 && oldIndex < actualInsert)
					actualInsert--;
			}

			entries.Add((entity, entity.Transform.Parent, oldIndex, newParent, actualInsert));
		}

		if (entries.Count == 0)
			return;

		foreach (var entry in entries)
		{
			// Capture world transform before the parent changes so we can reapply it as local
			var worldPos = entry.entity.Transform.Position;
			var worldRot = entry.entity.Transform.Rotation;
			var worldScale = entry.entity.Transform.Scale;

			entry.entity.Transform.SetParentAt(entry.newParent, entry.newIndex);
			if (entry.newParent == null)
				Core.Scene.Entities.MoveEntityToIndex(entry.entity, entry.newIndex);

			// Force-recalculate locals from world values, bypassing equality guards
			entry.entity.Transform.RecomputeLocalsFromWorld(worldPos, worldRot, worldScale);
		}

		EditorChangeTracker.PushUndo(
			new EntityReparentUndoAction(entries, $"Reparent {string.Join(", ", entries.Select(e => e.entity.Name))}"),
			entries[0].entity,
			$"Reparent {string.Join(", ", entries.Select(e => e.entity.Name))}"
		);
	}

	/// <summary>
	/// Returns true if <paramref name="potentialDescendant"/> is equal to or a descendant of <paramref name="ancestor"/>.
	/// </summary>
	private static bool IsDescendantOf(Transform potentialDescendant, Transform ancestor)
	{
		var t = potentialDescendant;
		while (t != null)
		{
			if (t == ancestor)
				return true;
			t = t.Parent;
		}
		return false;
	}
	#endregion

    #region Entity Tree Rendering and Interaction

    /// <summary>
    /// Draws a single entity node in the tree, handles selection, context menu, and inspector opening.
    /// </summary>
    private void DrawEntity(Entity entity, bool onlyDrawRoots = true)
    {
        if (onlyDrawRoots && entity.Transform.Parent != null)
            return;

        bool isSelected = _selectedEntities.Contains(entity);
        ImGui.PushID((int)entity.Id);
        bool treeNodeOpened = false;
        var flags = isSelected ? ImGuiTreeNodeFlags.Selected : 0;
        bool isExpanded = _imGuiManager.SceneGraphWindow.ExpandedEntities.Contains(entity);
        if (entity.Transform.ChildCount > 0)
            ImGui.SetNextItemOpen(isExpanded, ImGuiCond.Always);

		// Set special color for entities based on type
		bool isPrefab = entity.Type == Entity.InstanceType.SerializedPrefab;
		bool isNonSerialized = entity.Type == Entity.InstanceType.NonSerialized;
		bool isSceneRequired = entity.Type == Entity.InstanceType.SceneRequired;

		if (isSceneRequired)
		{
			// Light orange
			ImGui.PushStyleColor(ImGuiCol.Text, new System.Numerics.Vector4(1.0f, 0.8f, 0.4f, 1.0f));
		}
		else if (isPrefab)
		{
			// Orange 
			ImGui.PushStyleColor(ImGuiCol.Text, new System.Numerics.Vector4(1.0f, 0.6f, 0.2f, 1.0f));
		}
		else if (isNonSerialized)
		{
			// Green
			ImGui.PushStyleColor(ImGuiCol.Text, new System.Numerics.Vector4(0.2f, 1.0f, 0.2f, 1.0f));
		}

		// Draw tree node
		if (entity.Transform.ChildCount > 0)
			treeNodeOpened = ImGui.TreeNodeEx($"{entity.Name} ({entity.Transform.ChildCount})###{entity.Id}",
				ImGuiTreeNodeFlags.OpenOnArrow | flags);
		else
			treeNodeOpened = ImGui.TreeNodeEx($"{entity.Name} ({entity.Transform.ChildCount})###{entity.Id}",
				ImGuiTreeNodeFlags.Leaf | ImGuiTreeNodeFlags.OpenOnArrow | flags);

		if (isSceneRequired || isPrefab || isNonSerialized)
		{
			ImGui.PopStyleColor();
		}

		if (entity.Transform.ChildCount > 0)
		{
			if (ImGui.IsItemClicked(ImGuiMouseButton.Left) &&
			    ImGui.GetMousePos().X - ImGui.GetItemRectMin().X <= ImGui.GetTreeNodeToLabelSpacing())
			{
				if (isExpanded)
					_imGuiManager.SceneGraphWindow.ExpandedEntities.Remove(entity);
				else
					_imGuiManager.SceneGraphWindow.ExpandedEntities.Add(entity);
			}
		}
		VoltageEditorUtils.ShowContextMenuTooltip();

		ImGui.OpenPopupOnItemClick("entityContextMenu", ImGuiPopupFlags.MouseButtonRight);
		DrawEntityContextMenuPopup(entity);

		if (ImGui.IsItemClicked(ImGuiMouseButton.Left) &&
			ImGui.GetMousePos().X - ImGui.GetItemRectMin().X > ImGui.GetTreeNodeToLabelSpacing())
		{
			bool ctrlDown = Input.IsKeyDown(Keys.LeftControl) || Input.IsKeyDown(Keys.RightControl) || ImGui.GetIO().KeyCtrl || ImGui.GetIO().KeySuper;
			bool shiftDown = Input.IsKeyDown(Keys.LeftShift) || Input.IsKeyDown(Keys.RightShift) || ImGui.GetIO().KeyShift;

			if (!_selectedEntities.Contains(entity) || ctrlDown || shiftDown)
			{
				// Normal selection change — entity not yet selected, or modifier held
				SetSelectedEntity(entity, ctrlDown, shiftDown);
				_imGuiManager.OpenMainEntityInspector(entity);
				ImGui.SetWindowFocus();
				_pendingSelectEntity = null;
			}
			else
			{
				// Entity already selected, no modifier — might be starting a drag.
				// Defer collapsing the selection until mouse release (only if no drag occurred).
				_pendingSelectEntity = entity;
				_imGuiManager.OpenMainEntityInspector(entity);
				ImGui.SetWindowFocus();
			}
		}

		// Resolve deferred single-select on mouse release if no drag happened
		if (_pendingSelectEntity == entity &&
			ImGui.IsMouseReleased(ImGuiMouseButton.Left) &&
			!ImGui.IsMouseDragging(ImGuiMouseButton.Left))
		{
			SetSelectedEntity(entity, false, false);
			_pendingSelectEntity = null;
		}

		if (ImGui.IsMouseClicked(0) && ImGui.IsItemClicked() &&
			ImGui.GetMousePos().X - ImGui.GetItemRectMin().X > ImGui.GetTreeNodeToLabelSpacing())
			if (Core.Scene.Entities.Count > 0 && Core.IsEditMode)
			{
				if (_previousEntity == null || !_previousEntity.Equals(entity))
				{
					_previousEntity = entity;
				}

				_imGuiManager.CursorSelectionManager.SetCameraTargetPosition(entity.Transform.Position);
			}

		// Drag source
		if (ImGui.BeginDragDropSource(ImGuiDragDropFlags.SourceNoDisableHover | ImGuiDragDropFlags.SourceNoHoldToOpenOthers))
		{
			// Clear any pending deferred single-select so the full multi-selection is intact when dropped
			_pendingSelectEntity = null;

			if (!_selectedEntities.Contains(entity))
			{
				_selectedEntities.Clear();
				_selectedEntities.Add(entity);
			}
			unsafe
			{
				uint id = entity.Id;
				ImGui.SetDragDropPayload(DragDropPayloadType, (nint)(&id), sizeof(uint));
			}
			var names = _selectedEntities.Count == 1
				? $"Move: {_selectedEntities[0].Name}"
				: $"Move {_selectedEntities.Count} entities";
			ImGuiSafe.TextSafe(names);
			ImGui.EndDragDropSource();
		}

		// Drop target selection
		if (ImGui.BeginDragDropTarget())
		{
			var itemMin = ImGui.GetItemRectMin();
			var itemMax = ImGui.GetItemRectMax();
			float mouseY = ImGui.GetMousePos().Y;
			float relY = (itemMax.Y > itemMin.Y) ? (mouseY - itemMin.Y) / (itemMax.Y - itemMin.Y) : 0.5f; 
			bool insertAbove = relY <= 0.425f; // Top 42.5% of the row -> insert as sibling above
			bool insertBelow = relY >= 0.545f; // Bottom 54.5% of the row -> insert as sibling below
			bool insertAsSibling = insertAbove || insertBelow;  // Middle 15% -> make child of this entity

			int siblingIndex = -1;
			if (insertAsSibling)
			{
				Transform siblingParent = entity.Transform.Parent;
				if (siblingParent != null)
				{
					int myIdx = siblingParent.Children.IndexOf(entity.Transform);
					siblingIndex = insertAbove ? myIdx : myIdx + 1;
				}
				else
				{
					int myIdx = Core.Scene.Entities.EntityFastList.IndexOf(entity);
					siblingIndex = insertAbove ? myIdx : myIdx + 1;
				}
			}

			// Draw indicator line for sibling inserts
			var drawList = ImGui.GetWindowDrawList();
			if (insertAsSibling)
			{
				float lineY = insertAbove ? itemMin.Y : itemMax.Y;
				uint lineColor = ImGui.GetColorU32(ImGuiCol.DragDropTarget);
				drawList.AddLine(
					new System.Numerics.Vector2(itemMin.X, lineY),
					new System.Numerics.Vector2(itemMax.X, lineY),
					lineColor, 2f);
			}

			unsafe
			{
				var payload = ImGui.AcceptDragDropPayload(DragDropPayloadType);
				if (payload.NativePtr != null)
				{
					if (insertAsSibling)
						ReparentSelectedEntities(entity.Transform.Parent, siblingIndex);
					else
						ReparentSelectedEntities(entity.Transform);
				}
			}
			ImGui.EndDragDropTarget();
		}

		// Recursively draw children
		if (treeNodeOpened)
		{
			for (var i = 0; i < entity.Transform.ChildCount; i++)
				DrawEntity(entity.Transform.GetChild(i).Entity, false);

			ImGui.TreePop();
		}

		ImGui.PopID();
	}

    #endregion

    #region Entity Context Menu

    /// <summary>
    /// Draws the context menu popup for entity actions (copy, clone, destroy, etc).
    /// </summary>
    private void DrawEntityContextMenuPopup(Entity entity)
    {
        if (_imGuiManager == null)
            _imGuiManager = Core.GetGlobalManager<ImGuiManager>();

        if (ImGui.BeginPopup("entityContextMenu"))
        {
            if (_imGuiManager.SceneGraphWindow.CopiedComponent != null && ImGui.Selectable("Paste Component"))
            {
                var copiedComponent = _imGuiManager.SceneGraphWindow.CopiedComponent;
                var existingComponent = entity.Components.FirstOrDefault(c => c.GetType() == copiedComponent.GetType());
                
                if (existingComponent != null)
                {
                    // Use JSON serialization for reliable deep cloning
                    try
                    {
                        var jsonSettings = new JsonSettings
                        {
                            PrettyPrint = false,
                            TypeNameHandling = TypeNameHandling.Auto,
                            PreserveReferencesHandling = false
                        };
                        
                        var sourceData = copiedComponent.Data;
                        if (sourceData != null)
                        {
                            var oldData = existingComponent.Data; // for Undo
                            var json = Json.ToJson(sourceData, jsonSettings);
                            var clonedData = (ComponentData)Json.FromJson(json, sourceData.GetType());
                            
                            EditorChangeTracker.PushUndo(
                                new ComponentDataChangeAction(
                                    existingComponent,
                                    oldData,
                                    clonedData,
                                    $"Paste {copiedComponent.GetType().Name} to {entity.Name}"
                            ),
                            entity,
                            $"Paste {copiedComponent.GetType().Name} to {entity.Name}"
                        );
                        
                        existingComponent.Data = clonedData;
						}
                    }
                    catch (Exception ex)
                    {
                        var clonedComponent = copiedComponent.Clone();
                        clonedComponent.Name = existingComponent.Name;
                        entity.ReplaceComponent(clonedComponent);
                        Debug.Error($"Failed to paste component data: {ex.Message}. Used fallback paste method for {copiedComponent.GetType().Name}");
                    }
                    
                    _imGuiManager.RefreshMainEntityInspector();
                }
                else
                {
                    var clonedComponent = copiedComponent.Clone();
                    entity.AddComponent(clonedComponent);
                    _imGuiManager.RefreshMainEntityInspector();
                }
            }

            if (ImGui.Selectable($"Open {entity.Name} in separate window"))
                Core.GetGlobalManager<ImGuiManager>().OpenSeparateEntityInspector(entity);

            // Entity Commands
            if (ImGui.Selectable("Move Camera to " + entity.Name))
                if (Core.Scene.Entities.Count > 0 && Core.IsEditMode)
                    _imGuiManager.CursorSelectionManager.SetCameraTargetPosition(entity.Transform.Position);

            
            string reason = null;
            if (entity.Type == Entity.InstanceType.NonSerialized)
            {
                reason = "Can't duplicate NonSerialized entities!";
            }
            else if (entity.Type == Entity.InstanceType.SceneRequired)
            {
                reason = "Can't duplicate SceneRequired entities!";
            }

            if (reason == null)
            {
                if (ImGui.Selectable("Duplicate Entity " + entity.Name))
                    DuplicateEntity(entity);
            }
            else
            {
                ImGui.BeginDisabled(true);
                ImGui.Selectable(reason);
                ImGui.EndDisabled();
            }

            if (entity.Type == Entity.InstanceType.SceneRequired)
            {
                ImGui.BeginDisabled(true);
                ImGui.Selectable("Can't delete SceneRequired entities!");
                ImGui.EndDisabled();
            }
            else
            {
                if (ImGui.Selectable("Destroy Entity"))
                {
                    // Push undo BEFORE destroying, so the entity is still valid
                    EditorChangeTracker.PushUndo(
                        new EntityCreateDeleteUndoAction(entity.Scene, entity, wasCreated: false, $"Delete Entity {entity.Name}"),
                        entity,
                        $"Delete Entity {entity.Name}"
                    );
                    entity.Destroy();
                }
            }

			if (ImGui.Selectable("Create Child Entity", false, ImGuiSelectableFlags.DontClosePopups))
			{
				var child = new Entity("Child Entity");
				child.Transform.SetParent(entity.Transform);
				child.Type = Entity.InstanceType.Serialized;
				entity.Scene.AddEntity(child);

				EditorChangeTracker.PushUndo(
					new EntityCreateDeleteUndoAction(entity.Scene, child, wasCreated: true, $"Create Child Entity under {entity.Name}"),
					child,
					$"Create Child Entity under {entity.Name}"
				);

				_imGuiManager.MainEntityInspectorWindow.DelayedSetEntity(child);
			}

			// Add an empty Parent (multi-selection only)
			if (_selectedEntities.Count > 1 && ImGui.Selectable("Add an empty Parent"))
			{
				var entitiesToGroup = _selectedEntities
					.Where(e => e.Type != Entity.InstanceType.SceneRequired)
					.ToList();

				if (entitiesToGroup.Count > 0)
				{
					// Compute center position of all selected entities
					var center = Vector2.Zero;
					foreach (var e in entitiesToGroup)
						center += e.Transform.Position;
					center /= entitiesToGroup.Count;

					var parentEntity = new Entity("Parent");
					parentEntity.Type = Entity.InstanceType.Serialized;
					parentEntity.Transform.Position = center;
					entity.Scene.AddEntity(parentEntity);

					var reparentEntries = new List<(Entity entity, Transform oldParent, int oldIndex, Transform newParent, int newIndex)>();
					for (int i = 0; i < entitiesToGroup.Count; i++)
					{
						var e = entitiesToGroup[i];
						int oldIdx = e.Transform.Parent != null
							? e.Transform.Parent.Children.IndexOf(e.Transform)
							: Core.Scene.Entities.EntityFastList.IndexOf(e);
						reparentEntries.Add((e, e.Transform.Parent, oldIdx, parentEntity.Transform, i));
					}

					foreach (var e in entitiesToGroup)
					{
						var worldPos = e.Transform.Position;
						var worldRot = e.Transform.Rotation;
						var worldScale = e.Transform.Scale;

						e.Transform.SetParent(parentEntity.Transform);
						e.Transform.RecomputeLocalsFromWorld(worldPos, worldRot, worldScale);
					}

					// Push a composite undo: parent creation + reparenting
					EditorChangeTracker.PushUndo(
						new EntityCreateDeleteUndoAction(entity.Scene, parentEntity, wasCreated: true, $"Add empty Parent for selection"),
						parentEntity,
						$"Add empty Parent"
					);
					EditorChangeTracker.PushUndo(
						new EntityReparentUndoAction(reparentEntries, $"Group under {parentEntity.Name}"),
						parentEntity,
						$"Group under {parentEntity.Name}"
					);

					_imGuiManager.MainEntityInspectorWindow.DelayedSetEntity(parentEntity);
					_selectedEntities.Clear();
					_selectedEntities.Add(parentEntity);
				}
			}

			ImGui.EndPopup();
        }
    }
	#endregion

	#region Copy and Paste Logic
	/// <summary>
	/// Handles copy/paste/duplicate shortcuts for entities.
	/// </summary>
	///
	private void EntityDuplicationAndDeletion()
    {
	    if (ImGui.IsAnyItemActive() || ImGui.IsAnyItemFocused())
		    return;

		// Handle Copy/Paste/Duplicate Shortcuts
		bool gameCtrlDown = Input.IsKeyDown(Keys.LeftControl) || Input.IsKeyDown(Keys.RightControl);
	    bool imguiCtrlDown = ImGui.GetIO().KeyCtrl;

	    bool ShouldBlockDuplication(Entity entity)
	    {
	        if (entity != null && (entity.Type == Entity.InstanceType.NonSerialized
	                               || entity.Type == Entity.InstanceType.SceneRequired))
	        {
	            Debug.Error($"Cannot duplicate {entity.Type} entities.");
	            return true; 
	        }
	        return false;
	    }

	    bool ShouldBlockDeletion(Entity entity)
	    {
	        return entity != null && entity.Type == Entity.InstanceType.SceneRequired;
	    }

	    // Ctrl+D: Duplicate selected
	    if (Core.IsEditMode && imguiCtrlDown && ImGui.IsKeyPressed(ImGuiKey.D, false) && _selectedEntities.Count > 0)
	    {
	        var entitiesToDuplicate = _selectedEntities.Where(e => !ShouldBlockDuplication(e)).ToList();
	        if (entitiesToDuplicate.Count > 1)
	        {
	            DuplicateEntities(entitiesToDuplicate);
	        }
	        else
	        {
	            foreach (var entity in entitiesToDuplicate)
	                DuplicateEntity(entity);
	        }
	    }

	    // Ctrl+C: Copy all selected entities
	    if (Core.IsEditMode && gameCtrlDown && Input.IsKeyPressed(Keys.C) && _selectedEntities.Count > 0)
	    {
	        _copiedEntities = _selectedEntities.ToList();
	    }
	    else if (imguiCtrlDown && ImGui.IsKeyPressed(ImGuiKey.C) && _selectedEntities.Count > 0)
	    {
	        _copiedEntities = _selectedEntities.ToList();
	    }

	    // Ctrl+V: Paste (duplicate all copied entities)
	    if (Core.IsEditMode && gameCtrlDown && Input.IsKeyPressed(Keys.V) && _copiedEntities.Count > 0)
	    {
	        var entitiesToDuplicate = _copiedEntities.Where(e => !ShouldBlockDuplication(e)).ToList();
	        if (entitiesToDuplicate.Count > 1)
	        {
	            DuplicateEntities(entitiesToDuplicate);
	        }
	        else
	        {
	            foreach (var entity in entitiesToDuplicate)
	                DuplicateEntity(entity);
	        }
	    }
	    else if (imguiCtrlDown && ImGui.IsKeyPressed(ImGuiKey.V) && _copiedEntities.Count > 0)
	    {
	        var entitiesToDuplicate = _copiedEntities.Where(e => !ShouldBlockDuplication(e)).ToList();
	        if (entitiesToDuplicate.Count > 1)
	        {
	            DuplicateEntities(entitiesToDuplicate);
	        }
	        else
	        {
	            foreach (var entity in entitiesToDuplicate)
	                DuplicateEntity(entity);
	        }
	    }

	    // Delete: Remove all selected entities with Undo/Redo support 
	    if (Core.IsEditMode && _selectedEntities.Count > 0 &&
	        (Input.IsKeyPressed(Keys.Delete) || ImGui.IsKeyPressed(ImGuiKey.Delete)))
	    {
	        var entitiesToDelete = _selectedEntities.Where(e => !ShouldBlockDeletion(e)).ToList();

	        if (entitiesToDelete.Count > 0)
	        {
	            // Push a single undo for all entities
	            EditorChangeTracker.PushUndo(
	                new MultiEntityDeleteUndoAction(Core.Scene, entitiesToDelete, 
	                    $"Deleted: {string.Join(", ", entitiesToDelete.Select(e => e.Name))}"),
	                entitiesToDelete.FirstOrDefault(),
	                $"Deleted: {string.Join(", ", entitiesToDelete.Select(e => e.Name))}"
	            );

	            foreach (var entity in entitiesToDelete)
	                entity.Destroy();
	        }

	        DeselectAllEntities();
	    }
	}

	/// <summary>
	/// Duplicates the given entity and its subtree, remaps intra-subtree references so
	/// EntityReference/ComponentReference fields that pointed at siblings or children now
	/// point at the corresponding clones, then resolves all references. Returns the root clone.
	/// </summary>
	public Entity DuplicateEntity(Entity entity, string customName = null)
	{
		if (entity == null || entity.Scene == null)
			return null;

		var idMap = new Dictionary<Guid, Guid>();
		var clone = DuplicateEntityInternal(entity, customName, idMap);
		if (clone == null)
			return null;

		ComponentReferenceResolver.RemapEntitySubtree(clone, idMap);
		ComponentReferenceResolver.ResolveEntitySubtree(clone, Core.Scene);

		EditorChangeTracker.PushUndo(
			new EntityCreateDeleteUndoAction(entity.Scene, clone, wasCreated: true, $"Created: Entity {clone.Name}"),
			clone,
			$"Created: {clone.Name}"
		);

		_imGuiManager.MainEntityInspectorWindow.DelayedSetEntity(clone);
		return clone;
	}

	/// <summary>
	/// Internal recursive clone builder. Populates <paramref name="idMap"/> with
	/// sourceId → cloneId pairs for every entity in the subtree, but does NOT remap or resolve.
	/// Pass <paramref name="pendingClones"/> when cloning multiple roots simultaneously so
	/// name uniqueness accounts for the other in-flight clones.
	/// </summary>
	private Entity DuplicateEntityInternal(Entity entity, string customName, Dictionary<Guid, Guid> idMap,
		List<Entity> pendingClones = null)
	{
		var clone = new Entity("Entity");
		string baseName = customName ?? entity.Name;
		clone.Name = Core.Scene.GetUniqueEntityName(baseName, clone, pendingClones);

		clone.Transform.Position = entity.Transform.Position;
		clone.Transform.Rotation = entity.Rotation;
		clone.Transform.Scale = entity.Scale;
		clone.SetTag(entity.Tag);
		clone.Enabled = entity.Enabled;
		clone.DebugRenderEnabled = entity.DebugRenderEnabled;
		clone.UpdateInterval = entity.UpdateInterval;
		clone.UpdateOrder = entity.UpdateOrder;

		if (entity.Type == Entity.InstanceType.NonSerialized || entity.Type == Entity.InstanceType.Serialized
		    || entity.Type == Entity.InstanceType.SceneRequired)
			clone.Type = Entity.InstanceType.Serialized;
		else
		{
			clone.Type = Entity.InstanceType.SerializedPrefab;
			clone.OriginalPrefabName = entity.OriginalPrefabName;
		}

		idMap[entity.PersistentId] = clone.PersistentId;

		foreach (var sourceComponent in entity.Components)
		{
			if (!sourceComponent.IsSerialized)
				continue;

			var componentType = sourceComponent.GetType();
			Component clonedComponent;
			try
			{
				clonedComponent = (Component)Activator.CreateInstance(componentType);
			}
			catch (Exception ex)
			{
				Debug.Error($"Failed to create component {componentType.Name}: {ex.Message}");
				continue;
			}

			clonedComponent.Name = sourceComponent.Name;
			clonedComponent.Enabled = sourceComponent.Enabled;
			clone.AddComponent(clonedComponent);
			clonedComponent.SetSerialized(true);

			if (sourceComponent.Data != null)
			{
				try
				{
					var componentJsonSettings = new JsonSettings
					{
						PrettyPrint = false,
						TypeNameHandling = TypeNameHandling.Auto,
						PreserveReferencesHandling = false
					};

					var json = Json.ToJson(sourceComponent.Data, componentJsonSettings);
					var clonedData = (ComponentData)Json.FromJson(json, sourceComponent.Data.GetType());
					clonedComponent._pendingLoadedData = clonedData;
					clonedComponent.Data = clonedData;
				}
				catch (Exception ex)
				{
					Debug.Warn($"Failed to copy data for component {sourceComponent.GetType().Name}: {ex.Message}");
					try
					{
						var fallbackClone = sourceComponent.Clone();
						if (fallbackClone?.Data != null)
						{
							clonedComponent._pendingLoadedData = fallbackClone.Data;
							clonedComponent.Data = fallbackClone.Data;
						}
					}
					catch (Exception cloneEx)
					{
						Debug.Error($"Clone() fallback also failed for {sourceComponent.GetType().Name}: {cloneEx.Message}");
					}
				}
			}
		}

		Core.Scene.AddEntity(clone);

		for (var i = 0; i < entity.Transform.ChildCount; i++)
		{
			var childEntity = entity.Transform.GetChild(i).Entity;
			if (childEntity.Type == Entity.InstanceType.NonSerialized)
				continue;

			var clonedChild = DuplicateEntityInternal(childEntity, null, idMap);
			if (clonedChild != null)
			{
				var cWorldPos = childEntity.Transform.Position;
				var cWorldRot = childEntity.Transform.Rotation;
				var cWorldScale = childEntity.Transform.Scale;
				clonedChild.Transform.SetParent(clone.Transform);
				clonedChild.Transform.RecomputeLocalsFromWorld(cWorldPos, cWorldRot, cWorldScale);
			}
		}

		return clone;
	}

    /// <summary>
    /// Duplicates multiple entities, remapping intra-subtree references on each clone
    /// before resolving. Each top-level entity gets its own remap pass over its own subtree.
    /// </summary>
    public List<Entity> DuplicateEntities(IEnumerable<Entity> entitiesToDuplicate)
    {
        var clones = new List<Entity>();

        foreach (var entity in entitiesToDuplicate)
        {
			var idMap = new Dictionary<Guid, Guid>();
			var clone = DuplicateEntityInternal(entity, null, idMap, clones);
			if (clone == null)
				continue;

			ComponentReferenceResolver.RemapEntitySubtree(clone, idMap);
			ComponentReferenceResolver.ResolveEntitySubtree(clone, Core.Scene);

            clones.Add(clone);
        }

        return clones;
	}
	#endregion

    public void DeselectAllEntities()
    {
        _selectedEntities.Clear();
        _lastRangeSelectEntity = null; // Reset anchor
        _imGuiManager.ClearHighlightCache();
	}

	public Vector2 GetSelectedEntitiesCenter()
    {
        if (SelectedEntities.Count == 0)
            return Core.Scene.Camera.Position;        

        Vector2 sum = Vector2.Zero;

        foreach (var e in SelectedEntities)
            sum += e.Transform.Position;

        return sum / SelectedEntities.Count;
    }
}