using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ImGuiNET;
using Microsoft.Xna.Framework;
using Voltage.Editor.DebugUtils;
using Voltage.Utils;
using Voltage.Editor.ImGuiCore;
using Voltage.Editor.FilePickers;
using Voltage.Editor.Inspectors.SceneGraphPanes;
using Voltage.Editor.Undo;
using Voltage.Editor.Utils;
using Num = System.Numerics;
using Voltage.Editor.Interfaces;
using Voltage.Editor.ProjectFile;
using Voltage.Editor.Serialization;
using Voltage.Editor.Undo.Core;
using Voltage.Editor.Undo.EntityActions;

namespace Voltage.Editor.Windows;

public class SceneGraphWindow
{
	/// <summary>
	/// A copy of a component that can be pasted to another entity
	/// </summary>
	public Component CopiedComponent { get; set; }
	public EntityPane EntityPane => _entityPane;
	private PostProcessorsPane _postProcessorsPane = new();
	private RenderersPane _renderersPane = new();
	private EntityPane _entityPane = new();
	private SceneComponentsPane _sceneComponentsPane = new();
	private ImGuiManager _imGuiManager;
	public float SceneGraphWidth => _sceneGraphWidth;
	public float SceneGraphPosY { get; set; }
	public bool IsOpen { get; private set; }

	private string _prefabFilterName;

	private float _sceneGraphWidth = 420f;
	private readonly float _minSceneGraphWidth = 1f;
	private readonly float _maxSceneGraphWidth = Screen.MonitorWidth;

	// Key Hold duration params
	private float _upKeyHoldTime = 0f;
	private float _downKeyHoldTime = 0f;
	private double _lastRepeatTime = 0f;
	private const float RepeatDelay = 0.3f;
	private const float RepeatRate = 0.08f;
	public HashSet<Entity> ExpandedEntities = new();

	// SerializedPrefab caching
	private List<string> _cachedPrefabNames = new();
	private bool _prefabCacheInitialized = false;

	// SerializedPrefab deletion
	private bool _showDeletePrefabConfirmation = false;
	private string _prefabToDelete = "";

	// File Pickers
	public TmxFilePicker TmxFilePicker;
	public AsepriteFilePicker AsepriteFilePicker;
	
	// Events
	public static event Action<TmxFilePicker.TmxSelection> OnTmxFileSelected;
	public static event Action<AsepriteFilePicker.AsepriteSelection> OnAsepriteImageSelected;

	/// <summary>
	/// Called by <see cref="ImGuiManager.OpenSceneComponentInspector"/> to bring the
	/// Scene Graph window to the foreground so the user can see the selected component.
	/// </summary>
	public void FocusSceneComponentInspector()
	{
		// The inspector is drawn inline; just set next-frame focus on the scene graph window.
		ImGui.SetWindowFocus("Scene Graph ###SceneGraphWindow");
	}

	public void OnSceneChanged()
	{
		_postProcessorsPane.OnSceneChanged();
		_renderersPane.OnSceneChanged();
		_sceneComponentsPane.OnSceneChanged();
		
		TmxFilePicker = new TmxFilePicker(
			this,
			"tmx-file-picker",
			Path.Combine(Environment.CurrentDirectory, "Content")
		);
		
		AsepriteFilePicker = new AsepriteFilePicker(
			this,
			"aseprite-image-loader",
			Path.Combine(Environment.CurrentDirectory, "Content"), 
			false
		);
	}

	/// <summary>
	/// Refreshes the prefab cache by scanning the prefabs directory and its subdirectories.
	/// Called on first use and when new prefabs are created.
	/// </summary>
	public void RefreshPrefabCache()
	{
		if(_prefabCacheInitialized)
			return;

		_cachedPrefabNames.Clear();
		
		var prefabsDirectory = ProjectManager.Instance.CurrentProject.PrefabsFolder;
		if (Directory.Exists(prefabsDirectory))
		{
			// Search through all EntityType subdirectories
			var entityTypeDirectories = Directory.GetDirectories(prefabsDirectory);
			
			foreach (var entityTypeDir in entityTypeDirectories)
			{
				var prefabFiles = Directory.GetFiles(entityTypeDir, "*.prefab");
				foreach (var file in prefabFiles)
				{
					var fileName = Path.GetFileNameWithoutExtension(file);
					_cachedPrefabNames.Add(fileName);
				}
			}
		}
		
		_prefabCacheInitialized = true;
	}

	/// <summary>
	/// Adds a new prefab name to the cache without rescanning the directory.
	/// Called when a new prefab is successfully created.
	/// </summary>
	public void AddPrefabToCache(string prefabName)
	{
		if (!_cachedPrefabNames.Contains(prefabName))
		{
			_cachedPrefabNames.Add(prefabName);
		}
	}

	public bool Show(bool isOpen)
	{
		IsOpen = isOpen;

		if (Core.Scene == null || !isOpen)
			return false;

		if (_imGuiManager == null)
			_imGuiManager = Core.GetGlobalManager<ImGuiManager>();

		ImGui.PushStyleVar(ImGuiStyleVar.GrabMinSize, 0.0f);
		ImGui.PushStyleColor(ImGuiCol.ResizeGrip, new Num.Vector4(0, 0, 0, 0));
		
		var windowFlags = ImGuiWindowFlags.None; 

		if (ImGui.Begin("Scene Graph ###SceneGraphWindow", ref isOpen, windowFlags))
		{
			// Update width after user resizes
			var currentWidth = ImGui.GetWindowSize().X;
			if (Math.Abs(currentWidth - _sceneGraphWidth) > 0.01f)
				_sceneGraphWidth = Math.Clamp(currentWidth, _minSceneGraphWidth, _maxSceneGraphWidth);

			VoltageEditorUtils.SmallVerticalSpace();
			if (Core.IsEditMode)
			{
				ImGui.TextWrapped("Press F1 for Play/Edit mode, or F2 for Pause Mode");
				VoltageEditorUtils.SmallVerticalSpace();

				DrawPlayPauseButtons();

				VoltageEditorUtils.SmallVerticalSpace();
				if (VoltageEditorUtils.CenteredButton("Reset Scene", 0.8f))
					Core.InvokeResetScene();
			}
			else
			{
				ImGui.TextWrapped("\"Press F1 for Play/Edit mode, or F2 for Pause Mode\"");
				VoltageEditorUtils.SmallVerticalSpace();
				DrawPlayPauseButtons();
			}

			VoltageEditorUtils.MediumVerticalSpace();
			if (ImGui.CollapsingHeader("Post Processors"))
				_postProcessorsPane.Draw();

			if (ImGui.CollapsingHeader("Renderers"))
				_renderersPane.Draw();

			if (ImGui.CollapsingHeader("Entities (double-click label to inspect)", ImGuiTreeNodeFlags.DefaultOpen))
				_entityPane.Draw();

			VoltageEditorUtils.SmallVerticalSpace();
			if (ImGui.CollapsingHeader("Scene Components"))
				_sceneComponentsPane.Draw();

			VoltageEditorUtils.MediumVerticalSpace();
			if (VoltageEditorUtils.CenteredButton("Save Scene", 0.7f))
				SerializationManager.Instance.InvokeSaveSceneChanges();

			VoltageEditorUtils.MediumVerticalSpace();

			if (VoltageEditorUtils.CenteredButton("Add Entity", 0.6f))
			{
				_prefabFilterName = "";
				ImGui.OpenPopup("entity-selector");
			}

			#region FilePickers

			if (TmxFilePicker.IsOpen)
			{
				ImGui.OpenPopup(TmxFilePicker.PopupId);
			}

			if (AsepriteFilePicker.IsOpen)
			{
				ImGui.OpenPopup(AsepriteFilePicker.PopupId);
			}

			#endregion

			VoltageEditorUtils.MediumVerticalSpace();
			if (CopiedComponent != null)
			{
				VoltageEditorUtils.VeryBigVerticalSpace();
				ImGui.TextWrapped($"Component Copied: {CopiedComponent.GetType().Name}");

				VoltageEditorUtils.SmallVerticalSpace();
				if (VoltageEditorUtils.CenteredButton("Clear Copied Component", 0.8f))
					CopiedComponent = null;
			}

			DrawEntitySelectorPopup();

			if (TmxFilePicker.IsOpen)
			{
				TmxFilePicker.TmxSelection tmxSelection = TmxFilePicker.Draw();
				if (tmxSelection != null)
				{
					OnTmxFileSelected?.Invoke(tmxSelection);
				}
			}

			if (AsepriteFilePicker.IsOpen)
			{
				AsepriteFilePicker.AsepriteSelection asepriteSelection = AsepriteFilePicker.Draw();
				if (asepriteSelection != null)
				{
					OnAsepriteImageSelected?.Invoke(asepriteSelection);
				}
			}

			ImGui.End();
			ImGui.PopStyleVar();
			ImGui.PopStyleColor();
		}

		// Draw delete confirmation popup outside of the main window
		DrawDeletePrefabConfirmationPopup();

		HandleEntitySelectionNavigation();

		if (ImGui.IsMouseClicked(ImGuiMouseButton.Left)
		    && !ImGui.IsAnyItemHovered()
		    && ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows))
		{
			EntityPane.DeselectAllEntities();
		}

		return isOpen;
	}

	private void DrawEntitySelectorPopup()
	{
		if (ImGui.BeginPopup("entity-selector"))
		{
			ImGui.InputText("###PrefabFilter", ref _prefabFilterName, 25);
			ImGui.Separator();

			RefreshPrefabCache();

			// Simple option to create an empty entity
			ImGui.TextColored(new Num.Vector4(0.8f, 1.0f, 0.8f, 1.0f), "Create Empty Entity:");
			ImGui.Separator();
			
			if (ImGui.Selectable("Empty Entity"))
			{
				CreateEmptyEntity();
				ImGui.CloseCurrentPopup();
			}

			// Show prefabs if they exist
			if (_cachedPrefabNames.Count > 0)
			{
				ImGui.Separator();
				ImGui.TextColored(new Num.Vector4(1.0f, 0.6f, 0.2f, 1.0f), "Prefabs:");
				ImGui.Separator();
				
				// Show flat list of prefabs (filtered if search text is entered)
				foreach (var prefabName in _cachedPrefabNames.OrderBy(p => p))
				{
					// Apply filter if search text is entered
					if (!string.IsNullOrEmpty(_prefabFilterName) && 
					    !prefabName.ToLower().Contains(_prefabFilterName.ToLower()))
					{
						continue;
					}

					if (ImGui.Selectable($"{prefabName}"))
					{
						CreateEntityFromPrefab(prefabName);
						ImGui.CloseCurrentPopup();
					}
					
					// Right-click context menu
					if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
					{
						ImGui.OpenPopup($"prefab-context-{prefabName}");
					}
					
					if (ImGui.BeginPopup($"prefab-context-{prefabName}"))
					{
						if (ImGui.Selectable("Create SerializedPrefab Instance"))
						{
							CreateEntityFromPrefab(prefabName);
							ImGui.CloseCurrentPopup();
						}
				
						ImGui.Separator();
				
						if (ImGui.Selectable("Delete SerializedPrefab"))
						{
							_prefabToDelete = prefabName;
							_showDeletePrefabConfirmation = true;
							ImGui.CloseCurrentPopup();
						}
				
						ImGui.EndPopup();
					}
				}
			}

			ImGui.EndPopup();
		}
	}

	/// <summary>
	/// Draws the delete prefab confirmation popup.
	/// </summary>
	private void DrawDeletePrefabConfirmationPopup()
	{
		if (_showDeletePrefabConfirmation)
		{
			ImGui.OpenPopup("delete-prefab-confirmation");
			_showDeletePrefabConfirmation = false;
		}

		var center = new Num.Vector2(Screen.Width * 0.45f, Screen.Height * 0.7f);
		ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new Num.Vector2(0.5f, 0.5f));

		bool open = true;
		if (ImGui.BeginPopupModal("delete-prefab-confirmation", ref open, ImGuiWindowFlags.AlwaysAutoResize))
		{
			ImGui.Text("Delete SerializedPrefab");
			ImGui.Separator();
			
			ImGui.TextWrapped($"Are you sure you want to delete the '{_prefabToDelete}' prefab completely?");
			ImGui.TextColored(new Num.Vector4(1.0f, 0.6f, 0.2f, 1.0f), "This action cannot be undone!");

			VoltageEditorUtils.MediumVerticalSpace();

			var buttonWidth = 80f;
			var spacing = 10f;
			var totalButtonWidth = buttonWidth * 2 + spacing;
			var windowWidth = ImGui.GetWindowSize().X;
			var centerStart = (windowWidth - totalButtonWidth) * 0.5f;
			
			ImGui.SetCursorPosX(centerStart);
			
			if (ImGui.Button("Yes", new Num.Vector2(buttonWidth, 0)))
			{
				DeletePrefab(_prefabToDelete);
			}
			
			ImGui.SameLine(); 
			
			if (ImGui.Button("No", new Num.Vector2(buttonWidth, 0)))
			{
				ImGui.CloseCurrentPopup();
			}

			ImGui.EndPopup();
		}
	}

	/// <summary>
	/// Deletes a prefab file and updates the prefab cache.
	/// Searches in EntityType subdirectories.
	/// </summary>
	private void DeletePrefab(string prefabName)
	{
		try
		{
			var prefabsDirectory = ProjectManager.Instance.CurrentProject.PrefabsFolder;
			bool fileDeleted = false;

			if (Directory.Exists(prefabsDirectory))
			{
				var entityTypeDirectories = Directory.GetDirectories(prefabsDirectory);
				
				foreach (var entityTypeDir in entityTypeDirectories)
				{
					var prefabFilePath = Path.Combine(entityTypeDir, $"{prefabName}.prefab");
					
					if (File.Exists(prefabFilePath))
					{
						File.Delete(prefabFilePath);
						fileDeleted = true;
						break;
					}
				}
			}

			if (fileDeleted)
			{
				_cachedPrefabNames.Remove(prefabName);
				
				// Force prefab cache to reinitialize on next use
				_prefabCacheInitialized = false;
				
				ImGui.CloseCurrentPopup();
			}
			else
			{
				var errorMsg = $"SerializedPrefab file not found: {prefabName}";
				Debug.Error(errorMsg);
			}
		}
		catch (Exception ex)
		{
			var errorMsg = $"Failed to delete prefab {prefabName}: {ex.Message}";
			Debug.Error(errorMsg);
		}
	}

	/// <summary>
	/// Removes a prefab name from the cache without rescanning the directory.
	/// Called when a prefab is successfully deleted.
	/// </summary>
	public void RemovePrefabFromCache(string prefabName)
	{
		_cachedPrefabNames.Remove(prefabName);
	}

	/// <summary>
	/// Creates a new empty entity in the scene.
	/// </summary>
	private void CreateEmptyEntity()
	{
		var entity = new Entity("Entity", Entity.InstanceType.Serialized);
		entity.Type = Entity.InstanceType.Serialized;
		entity.Name = Core.Scene.GetUniqueEntityName("Entity", entity);
		entity.Transform.Position = Core.Scene.Camera.Transform.Position;

		Core.Scene.AddEntity(entity);

		EditorChangeTracker.PushUndo(
			new EntityCreateDeleteUndoAction(Core.Scene, entity, wasCreated: true,
				$"Create Entity {entity.Name}"),
			entity,
			$"Create Entity {entity.Name}"
		);

		_imGuiManager.SceneGraphWindow.EntityPane.SetSelectedEntity(entity, false);
		_imGuiManager.MainEntityInspectorWindow.DelayedSetEntity(entity);
	}

	/// <summary>
	/// Creates a new entity from a prefab.
	/// </summary>
	private void CreateEntityFromPrefab(string prefabName)
	{
		try
		{
			var prefabData = SerializationManager.Instance.InvokePrefabLoadRequested(prefabName);

			if (prefabData.EntityData == null)
			{
				EditorDebug.Log($"Null SerializedPrefab EntityData: {prefabName}");
				return;
			}

			try
			{
				var entity = Core.Scene.SimpleCreateEntity(prefabData.Name, Entity.InstanceType.SerializedPrefab);
				entity.Type = Entity.InstanceType.SerializedPrefab;
				entity.Transform.Position = Core.Scene.Camera.Transform.Position;

				SerializationManager.Instance.InvokeLoadEntityData(entity, prefabData);
				entity.Name = Core.Scene.GetUniqueEntityName(prefabData.Name, entity);
				entity.OriginalPrefabName = prefabName;

				EditorChangeTracker.PushUndo(
					new EntityCreateDeleteUndoAction(Core.Scene, entity, wasCreated: true,
						$"Create Entity from SerializedPrefab {entity.Name}"),
					entity,
					$"Create Entity from SerializedPrefab {entity.Name}"
				);

				_imGuiManager.SceneGraphWindow.EntityPane.SetSelectedEntity(entity, false);
				_imGuiManager.MainEntityInspectorWindow.DelayedSetEntity(entity);

				EditorDebug.Log($"Created entity from prefab: {prefabName}");
			}
			catch
			{
				EditorDebug.Log(
					$"Failed to create entity from prefab: {prefabName}.");
			}
		}
		catch (Exception ex)
		{
			EditorDebug.Log($"Error creating entity from prefab {prefabName}: {ex.Message}");
		}
	}

	/// <summary>
	/// Draws the Play/Stop and Pause/Unpause buttons side by side.
	/// Pause is disabled when in EditMode since there is nothing to pause.
	/// </summary>
	private void DrawPlayPauseButtons()
	{
		var totalPercent = 0.8f;
		var gap = 8f;
		var availWidth = ImGui.GetContentRegionAvail().X * totalPercent;
		var buttonWidth = (availWidth - gap) / 2f;
		var startX = (ImGui.GetContentRegionAvail().X - availWidth) / 2f;
		var buttonHeight = VoltageEditorUtils.GetDefaultWidgetHeight();

		ImGui.SetCursorPosX(startX);

		// Play / Stop
		if (Core.IsEditMode)
		{
			if (ImGui.Button("PLAY", new Num.Vector2(buttonWidth, buttonHeight)))
				Core.InvokeSwitchEditMode(false);
		}
		else
		{
			if (ImGui.Button("STOP", new Num.Vector2(buttonWidth, buttonHeight)))
			{
				Core.IsPauseMode = false;
				Core.InvokeSwitchEditMode(true);
			}
		}

		ImGui.SameLine(0, gap);

		// Pause / Unpause  disabled in EditMode
		if (Core.IsEditMode)
			ImGui.BeginDisabled();

		if (Core.IsPauseMode)
		{
			if (ImGui.Button("Unpause", new Num.Vector2(buttonWidth, buttonHeight)))
				Core.InvokeSwitchPauseMode(false);
		}
		else
		{
			if (ImGui.Button("Pause", new Num.Vector2(buttonWidth, buttonHeight)))
				Core.InvokeSwitchPauseMode(true);
		}

		if (Core.IsEditMode)
			ImGui.EndDisabled();
	}

	#region Entity Selection Navigation
	private void HandleEntitySelectionNavigation()
	{
		var selectedEntity = _imGuiManager.SceneGraphWindow.EntityPane.SelectedEntities.FirstOrDefault();

		if (!Core.IsEditMode || selectedEntity == null || !ImGui.IsWindowFocused(ImGuiFocusedFlags.AnyWindow))
			return; 

		var hierarchyList = BuildHierarchyList();
		var currentEntity = _imGuiManager?.MainEntityInspectorWindow?.Entity;
		if (currentEntity == null || hierarchyList.Count == 0)
			return;

		bool upPressed = ImGui.IsKeyPressed(ImGuiKey.UpArrow);
		bool downPressed = ImGui.IsKeyPressed(ImGuiKey.DownArrow);
		bool upHeld = ImGui.IsKeyDown(ImGuiKey.UpArrow);
		bool downHeld = ImGui.IsKeyDown(ImGuiKey.DownArrow);

		double now = ImGui.GetTime();

		if (upPressed)
		{
			_upKeyHoldTime = (float)now;
			var next = NavigateUp(currentEntity, hierarchyList);
			if (next != null)
			{
				_imGuiManager.OpenMainEntityInspector(next);
				EntityPane.SetSelectedEntity(next, false);
				ExpandParentsAndChildren(next);
				_imGuiManager.CursorSelectionManager.SetCameraTargetPosition(next.Transform.Position);
			}
			_lastRepeatTime = now;
		}
		else if (upHeld)
		{
			if (now - _upKeyHoldTime > RepeatDelay && now - _lastRepeatTime > RepeatRate)
			{
				var next = NavigateUp(currentEntity, hierarchyList);
				if (next != null)
				{
					_imGuiManager.OpenMainEntityInspector(next);
					EntityPane.SetSelectedEntity(next, false);
					ExpandParentsAndChildren(next);
					_imGuiManager.CursorSelectionManager.SetCameraTargetPosition(next.Transform.Position);
				}
				_lastRepeatTime = now;
			}
		}
		else if (!upHeld)
		{
			_upKeyHoldTime = 0f;
		}

		if (downPressed)
		{
			_downKeyHoldTime = (float)now;
			var next = NavigateDown(currentEntity, hierarchyList);
			if (next != null)
			{
				_imGuiManager.OpenMainEntityInspector(next);
				EntityPane.SetSelectedEntity(next, false);
				ExpandParentsAndChildren(next);
				_imGuiManager.CursorSelectionManager.SetCameraTargetPosition(next.Transform.Position); 
			}
			_lastRepeatTime = now;
		}
		else if (downHeld)
		{
			if (now - _downKeyHoldTime > RepeatDelay && now - _lastRepeatTime > RepeatRate)
			{
				var next = NavigateDown(currentEntity, hierarchyList);
				if (next != null)
				{
					_imGuiManager.OpenMainEntityInspector(next);
					EntityPane.SetSelectedEntity(next, false);
					ExpandParentsAndChildren(next);
					_imGuiManager.CursorSelectionManager.SetCameraTargetPosition(next.Transform.Position);
				}
				_lastRepeatTime = now;
			}
		}
		else if (!downHeld)
		{
			_downKeyHoldTime = 0f;
		}
	}

	public List<Entity> BuildHierarchyList()
	{
		var result = new List<Entity>();
		var entities = Core.Scene?.Entities;
		if (entities == null) return result;

		for (int i = 0; i < entities.Count; i++)
		{
			var entity = entities[i];
			if (entity.Transform.Parent == null)
				AddEntityAndChildren(entity, result);
		}
		return result;
	}

	private void AddEntityAndChildren(Entity entity, List<Entity> result)
	{
		result.Add(entity);
		for (int i = 0; i < entity.Transform.ChildCount; i++)
		{
			AddEntityAndChildren(entity.Transform.GetChild(i).Entity, result);
		}
	}

	private Entity GetLastDescendant(Entity entity)
	{
		while (entity.Transform.ChildCount > 0)
			entity = entity.Transform.GetChild(entity.Transform.ChildCount - 1).Entity;
		return entity;
	}

	private Entity NavigateUp(Entity current, List<Entity> hierarchyList)
	{
		int idx = hierarchyList.IndexOf(current);
		if (idx <= 0)
			return null;

		Entity prev = hierarchyList[idx - 1];

		if (current.Transform.Parent != null && prev == current.Transform.Parent.Entity)
			return prev;

		if (prev.Transform.ChildCount > 0)
			return GetLastDescendant(prev);

		return prev;
	}

	private Entity NavigateDown(Entity current, List<Entity> hierarchyList)
	{
		int idx = hierarchyList.IndexOf(current);
		if (idx < 0 || idx >= hierarchyList.Count - 1)
			return null;
		return hierarchyList[idx + 1];
	}

	private void ExpandParentsAndChildren(Entity entity)
	{
		var parent = entity.Transform.Parent;
		while (parent != null)
		{
			ExpandedEntities.Add(parent.Entity);
			parent = parent.Parent;
		}

		var stack = new Stack<Entity>();
		stack.Push(entity);

		while (stack.Count > 0)
		{
			var current = stack.Pop();
			ExpandedEntities.Add(current);

			for (int i = 0; i < current.Transform.ChildCount; i++)
			{
				var child = current.Transform.GetChild(i).Entity;
				if (!ExpandedEntities.Contains(child))
					stack.Push(child);
			}
		}
	}

	#endregion
}