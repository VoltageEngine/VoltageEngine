using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using ImGuiNET;
using Voltage;
using Voltage.Utils;
using Voltage.Utils.Coroutines;
using Voltage.Editor.Core;
using Voltage.Editor.Inspectors.ObjectInspectors;
using Voltage.Editor.UndoActions;
using Voltage.Editor.Utils;
using Voltage.Persistence;
using Num = System.Numerics;

namespace Voltage.Editor.Inspectors;

public class MainEntityInspector
{
	public Entity Entity { get; private set; }
	public float Width { get; set; } = 500f; 
	public bool IsOpen { get; set; } = true; 

	private TransformInspector _transformInspector;
	private List<IComponentInspector> _componentInspectors = new();

	private bool _isEditingUpdateInterval = false;
	private uint _updateIntervalEditStartValue;
	private bool _isEditingName = false;
	private string _nameEditStartValue;

	// Prefab creation / confirmation popup fields
	private string _prefabName = "";
	private bool _showApplyToPrefabCopiesConfirmation = false;
	private List<Entity> _prefabCopiesToModify = new();
	private bool _showApplyToOriginalPrefabConfirmation = false;
	private ImGuiManager _imGuiManager;
	private Entity _lockedEntity;

	// Add Component fields
	private bool _showAddComponentPopup = false;
	private string _componentFilterText = "";
	private List<Type> _availableComponentTypes;
	private static List<Type> _cachedComponentTypes; // Cache to avoid repeated reflection

	public MainEntityInspector(ImGuiManager manager, Entity entity = null)
	{
		_imGuiManager = manager;

		Entity = entity;
		_componentInspectors.Clear();

		if (_imGuiManager.SceneGraphWindow.EntityPane.SelectedEntities.Count == 1 && Entity != null)
		{
			_transformInspector = new TransformInspector(Entity.Transform);
			for (var i = 0; i < Entity.Components.Count; i++)
				_componentInspectors.Add(ComponentInspectorFactory.CreateInspector(Entity.Components[i]));
		}
		else if (_imGuiManager.SceneGraphWindow.EntityPane.SelectedEntities.Count > 1)
		{
			var commonComponents = GetCommonComponents(_imGuiManager.SceneGraphWindow.EntityPane.SelectedEntities);
			_componentInspectors.Clear();
			foreach (var compType in commonComponents)
			{
				var multiInspector = new MultiComponentInspector(compType, _imGuiManager.SceneGraphWindow.EntityPane.SelectedEntities);
				_componentInspectors.Add(multiInspector);
			}
		}

		// Initialize available component types cache if not already done
		if (_cachedComponentTypes == null)
		{
			CacheAvailableComponentTypes();
		}
	}

	/// <summary>
	/// Caches all available Component types with parameterless constructors using reflection.
	/// </summary>
	private static void CacheAvailableComponentTypes()
	{
		_cachedComponentTypes = new List<Type>();

		// Get all loaded assemblies
		var assemblies = AppDomain.CurrentDomain.GetAssemblies();

		foreach (var assembly in assemblies)
		{
			try
			{
				// Get all types that inherit from Component
				var componentTypes = assembly.GetTypes()
					.Where(t => typeof(Component).IsAssignableFrom(t) 
					            && !t.IsAbstract 
					            && !t.IsInterface
					            && t.GetConstructor(Type.EmptyTypes) != null) // Has parameterless constructor
					.OrderBy(t => t.Name);

				_cachedComponentTypes.AddRange(componentTypes);
			}
			catch (ReflectionTypeLoadException)
			{
				// Some assemblies might not be fully loaded, skip them
				continue;
			}
		}
	}

	/// <summary>
	/// Filters available component types based on search text.
	/// </summary>
	private List<Type> GetFilteredComponentTypes()
	{
		if (string.IsNullOrWhiteSpace(_componentFilterText))
			return _cachedComponentTypes;

		return _cachedComponentTypes
			.Where(t => t.Name.Contains(_componentFilterText, StringComparison.OrdinalIgnoreCase) ||
			            t.FullName.Contains(_componentFilterText, StringComparison.OrdinalIgnoreCase))
			.ToList();
	}

	/// <summary>
	/// Adds a component of the specified type to the entity.
	/// </summary>
	private void AddComponentToEntity(Type componentType)
	{
		if (Entity == null || componentType == null)
			return;

		try
		{
			var component = (Component)Activator.CreateInstance(componentType);
			Entity.AddComponent(component, true);
			DelayedSetEntity(Entity);

			EditorChangeTracker.PushUndo(
				new ComponentAddedUndoAction(Entity, component),
				Entity,
				$"Add {componentType.Name} to {Entity.Name}"
			);

			NotificationSystem.ShowTimedNotification($"Added {componentType.Name} to {Entity.Name}");
		}
		catch (Exception ex)
		{
			NotificationSystem.ShowTimedNotification($"Failed to add {componentType.Name}: {ex.Message}");
			Debug.Error($"Failed to add component {componentType.Name}: {ex.Message}");
		}
	}

	/// <summary>
	/// Draws the Add Component popup with search and selection.
	/// </summary>
	private void DrawAddComponentPopup()
	{
		if (_showAddComponentPopup)
		{
			ImGui.OpenPopup("add-component-popup");
			_showAddComponentPopup = false;
		}

		var center = new Num.Vector2(Screen.Width * 0.5f, Screen.Height * 0.4f);
		ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new Num.Vector2(0.5f, 0.5f));
		ImGui.SetNextWindowSize(new Num.Vector2(400, 500), ImGuiCond.Appearing);

		bool open = true;
		if (ImGui.BeginPopupModal("add-component-popup", ref open, ImGuiWindowFlags.NoResize))
		{
			ImGui.Text("Add Component");
			ImGui.Separator();

			// Search/filter box
			ImGui.Text("Search:");
			ImGui.InputText("##ComponentFilter", ref _componentFilterText, 50);

			VoltageEditorUtils.SmallVerticalSpace();

			// Component list
			var filteredTypes = GetFilteredComponentTypes();
			
			ImGui.Text($"Available Components ({filteredTypes.Count}):");
			ImGui.Separator();

			if (ImGui.BeginChild("ComponentList", new Num.Vector2(0, 350), true))
			{
				foreach (var componentType in filteredTypes)
				{
					// Show namespace and type name for clarity
					var displayName = $"{componentType.Name}";
					var namespace_text = $"({componentType.Namespace})";

					if (ImGui.Selectable(displayName))
					{
						AddComponentToEntity(componentType);
						ImGui.CloseCurrentPopup();
					}

					if (ImGui.IsItemHovered())
					{
						ImGui.SetTooltip($"{componentType.FullName}");
					}

					// Show namespace in smaller text
					ImGui.SameLine();
					ImGui.PushStyleColor(ImGuiCol.Text, new Num.Vector4(0.6f, 0.6f, 0.6f, 1.0f));
					ImGui.Text(namespace_text);
					ImGui.PopStyleColor();
				}
			}
			ImGui.EndChild();

			VoltageEditorUtils.SmallVerticalSpace();

			// Close button
			var buttonWidth = 80f;
			var windowWidth = ImGui.GetWindowSize().X;
			var centerStart = (windowWidth - buttonWidth) * 0.5f;
			
			ImGui.SetCursorPosX(centerStart);
			
			if (ImGui.Button("Cancel", new Num.Vector2(buttonWidth, 0)))
			{
				_componentFilterText = "";
				ImGui.CloseCurrentPopup();
			}

			ImGui.EndPopup();
		}
	}

	/// <summary>
	/// Finds the set of component types that are common to all selected entities.
	/// </summary>
	private List<Type> GetCommonComponents(List<Entity> entities)
	{
		if (entities == null || entities.Count == 0)
			return new List<Type>();

		var firstEntity = entities[0];
		var commonTypes = firstEntity.Components.Select(c => c.GetType().FullName).ToHashSet();

		foreach (var entity in entities.Skip(1))
		{
			var types = entity.Components.Select(c => c.GetType().FullName).ToHashSet();
			commonTypes.IntersectWith(types);
		}

		return firstEntity.Components
			.Where(c => commonTypes.Contains(c.GetType().FullName))
			.Select(c => c.GetType())
			.Distinct()
			.ToList();
	}

	/// <summary>
	/// Refreshes all component inspectors. Call this after components are added, removed, or replaced.
	/// </summary>
	public void RefreshComponentInspectors()
	{
		if (Entity == null)
			return;
			
		_componentInspectors.Clear();
		
		for (var i = 0; i < Entity.Components.Count; i++)
		{
			_componentInspectors.Add(ComponentInspectorFactory.CreateInspector(Entity.Components[i]));
		}
	}

	public void SetEntity(Entity entity)
	{
		if (_imGuiManager == null)
		{
			_imGuiManager = Voltage.Core.GetGlobalManager<ImGuiManager>();
			return;
		}

		_imGuiManager.SelectedInspectorTab = ImGuiManager.InspectorTab.EntityInspector;

		if (_imGuiManager.IsInspectorTabLocked)
			return;

		Entity = entity;
		_componentInspectors.Clear();
		_transformInspector = null;

		if (_imGuiManager.SceneGraphWindow.EntityPane.SelectedEntities.Count == 1 && Entity != null)
		{
			_transformInspector = new TransformInspector(Entity.Transform);
			RefreshComponentInspectors();
		}
		else if (_imGuiManager.SceneGraphWindow.EntityPane.SelectedEntities.Count > 1)
		{
			var commonComponents = GetCommonComponents(_imGuiManager.SceneGraphWindow.EntityPane.SelectedEntities);
			foreach (var compType in commonComponents)
			{
				var multiInspector = new MultiComponentInspector(compType, _imGuiManager.SceneGraphWindow.EntityPane.SelectedEntities);
				_componentInspectors.Add(multiInspector);
			}
		}
	}

	public void Draw(
		ImGuiWindowFlags windowFlags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoCollapse |
		                               ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize)
	{
		if (!IsOpen)
			return;

		if (_imGuiManager == null)
			_imGuiManager = Voltage.Core.GetGlobalManager<ImGuiManager>();

		var windowPosX = Screen.Width - _imGuiManager.InspectorTabWidth + _imGuiManager.InspectorWidthOffset;
		var windowPosY = _imGuiManager.MainWindowPositionY + 32f;
		var windowWidth = _imGuiManager.InspectorTabWidth - _imGuiManager.InspectorWidthOffset;
		var windowHeight = Screen.Height - windowPosY;

		ImGui.SetNextWindowPos(new Num.Vector2(windowPosX, windowPosY), ImGuiCond.Always);
		ImGui.SetNextWindowSize(new Num.Vector2(windowWidth, windowHeight), ImGuiCond.Always);

		var open = IsOpen;

		if (ImGui.Begin("##MainEntityInspector", ref open, windowFlags))
		{
			// If more than one entity is selected
			if (_imGuiManager.SceneGraphWindow.EntityPane.SelectedEntities.Count > 1 && !_imGuiManager.IsInspectorTabLocked)
			{
				ImGui.SetWindowFontScale(1.5f);
				ImGui.Text("Multiple Entities Selected");
				ImGui.SetWindowFontScale(1.0f);

				ImGui.PushFont(ImGui.GetIO().FontDefault); // Use default font (smallest)
				ImGui.PushStyleColor(ImGuiCol.Text, new Num.Vector4(0.8f, 0.8f, 0.8f, 1.0f));
				for (int i = 0; i < _imGuiManager.SceneGraphWindow.EntityPane.SelectedEntities.Count; i++)
				{
					ImGui.Text($"{i + 1}. {_imGuiManager.SceneGraphWindow.EntityPane.SelectedEntities[i].Name}");
				}

				ImGui.PopStyleColor();
				ImGui.PopFont();

				VoltageEditorUtils.BigVerticalSpace();

				// Show common Components
				foreach (var inspector in _componentInspectors)
				{
					inspector.Draw();
					VoltageEditorUtils.MediumVerticalSpace();
				}
			}
			else if ((_imGuiManager.SceneGraphWindow.EntityPane.SelectedEntities.Count == 1 && Entity != null) 
			         || _lockedEntity != null)
			{
				var entityName = Entity.Name;
				ImGui.SetWindowFontScale(1.5f);
				ImGui.Text(entityName);
				ImGui.SetWindowFontScale(1.0f);

				float spacing = 12f * _imGuiManager.FontSizeMultiplier;
				float iconSize = 20f * _imGuiManager.FontSizeMultiplier;
				ImGui.SameLine(0, spacing);

				// Lock Mode Button
				System.Numerics.Vector4 lockedButtonColor;
				if (_imGuiManager.IsInspectorTabLocked)
				{
					lockedButtonColor = new System.Numerics.Vector4(0.2f, 0.5f, 1f, 1f); // blue
					ImGui.PushStyleColor(ImGuiCol.Button, lockedButtonColor);
					if(ImGui.ImageButton("Lock Off", _imGuiManager.ImageLoader.LockedInspectorIconId, new Num.Vector2(iconSize, iconSize)))
					{
						_imGuiManager.IsInspectorTabLocked = false;
						_lockedEntity = null;
					}

					if (ImGui.IsItemHovered())
						ImGui.SetTooltip("Unlock the current inspector");
				}
				else
				{
					lockedButtonColor = ImGui.GetStyle().Colors[(int)ImGuiCol.Button];
					ImGui.PushStyleColor(ImGuiCol.Button, lockedButtonColor);
					if (ImGui.ImageButton("Lock On", _imGuiManager.ImageLoader.UnlockedInspectorIconId, new Num.Vector2(iconSize, iconSize)))
					{
						_imGuiManager.IsInspectorTabLocked = true;
						_lockedEntity = Entity;
					}

					if (ImGui.IsItemHovered())
						ImGui.SetTooltip("Lock the current inspector");
				}

				ImGui.PopStyleColor();
				VoltageEditorUtils.BigVerticalSpace();

				if (Entity == null)
				{
					ImGui.TextColored(new Num.Vector4(1, 1, 0, 1), "No entity selected.");
				}
				else
				{
					var type = Entity.Type.ToString();
					ImGui.InputText("InstanceType", ref type, 30);

					// Show OriginalPrefabName for Prefab entities (readonly)
					if (Entity.Type == Entity.InstanceType.Prefab && !string.IsNullOrEmpty(Entity.OriginalPrefabName))
					{
						var originalPrefabName = Entity.OriginalPrefabName;
						ImGui.InputText("Original Prefab Name", ref originalPrefabName, 50,
							ImGuiInputTextFlags.ReadOnly);
					}

					#region Entity Basic Info
					// Enabled
					{
						bool oldEnabled = Entity.Enabled;
						bool enabled = oldEnabled;
						if (ImGui.Checkbox("Enabled", ref enabled) && enabled != oldEnabled)
						{
							EditorChangeTracker.PushUndo(
								new GenericValueChangeAction(
									Entity,
									typeof(Entity).GetProperty(nameof(Entity.Enabled)),
									oldEnabled,
									enabled,
									$"{Entity.Name}.Enabled"
								),
								Entity,
								$"{Entity.Name}.Enabled"
							);
							Entity.SetEnabled(enabled);
						}
					}

					// Name (edit session)
					{
						string name = Entity.Name;
						bool changed = ImGui.InputText("Name", ref name, 25);

						if (ImGui.IsItemActive() && !_isEditingName)
						{
							_isEditingName = true;
							_nameEditStartValue = Entity.Name;
						}

						if (_isEditingName && ImGui.IsItemDeactivatedAfterEdit())
						{
							_isEditingName = false;
							Entity.Name = name;

							if (Entity.Name != _nameEditStartValue)
							{
								EditorChangeTracker.PushUndo(
									new GenericValueChangeAction(
										Entity,
										typeof(Entity).GetProperty(nameof(Entity.Name)),
										_nameEditStartValue,
										Entity.Name,
										$"{_nameEditStartValue}.Name"
									),
									Entity,
									$"{_nameEditStartValue}.Name"
								);
							}
						}
					}


					// UpdateOrder
					{
						int oldUpdateOrder = Entity.UpdateOrder;
						int updateOrder = oldUpdateOrder;
						if (ImGui.InputInt("Update Order", ref updateOrder) && updateOrder != oldUpdateOrder)
						{
							EditorChangeTracker.PushUndo(
								new GenericValueChangeAction(
									Entity,
									typeof(Entity).GetProperty(nameof(Entity.UpdateOrder)),
									oldUpdateOrder,
									updateOrder,
									$"{Entity.Name}.UpdateOrder"
								),
								Entity,
								$"{Entity.Name}.UpdateOrder"
							);
							Entity.SetUpdateOrder(updateOrder);
						}
					}

					// UpdateInterval
					{
						int updateInterval = (int)Entity.UpdateInterval;

						bool changed = ImGui.SliderInt("Update Interval", ref updateInterval, 1, 100);

						// Start of edit session: store the initial value
						if (ImGui.IsItemActive() && !_isEditingUpdateInterval)
						{
							_isEditingUpdateInterval = true;
							_updateIntervalEditStartValue = Entity.UpdateInterval;
						}

						// Apply the value while dragging
						if (changed)
							Entity.UpdateInterval = (uint)updateInterval;

						// End of edit session: push undo if value changed
						if (_isEditingUpdateInterval && ImGui.IsItemDeactivatedAfterEdit())
						{
							_isEditingUpdateInterval = false;
							if (Entity.UpdateInterval != _updateIntervalEditStartValue)
							{
								EditorChangeTracker.PushUndo(
									new GenericValueChangeAction(
										Entity,
										(obj, val) => ((Entity)obj).UpdateInterval = (uint)val,
										_updateIntervalEditStartValue,
										Entity.UpdateInterval,
										$"{Entity.Name}.UpdateInterval"
									),
									Entity,
									$"{Entity.Name}.UpdateInterval"
								);
							}
						}
					}

					// Tag
					{
						int oldTag = Entity.Tag;
						int tag = oldTag;
						if (ImGui.InputInt("Tag", ref tag) && tag != oldTag)
						{
							EditorChangeTracker.PushUndo(
								new GenericValueChangeAction(
									Entity,
									typeof(Entity).GetProperty(nameof(Entity.Tag)),
									oldTag,
									tag,
									$"{Entity.Name}.Tag"
								),
								Entity,
								$"{Entity.Name}.Tag"
							);
							Entity.Tag = tag;
						}
					}

					VoltageEditorUtils.MediumVerticalSpace();
					
					// IsSelectableInEditor
					{
						bool oldSelectable = Entity.IsSelectableInEditor;
						bool isSelectable = oldSelectable;
						if (ImGui.Checkbox("Can Be Selected", ref isSelectable) && isSelectable != oldSelectable)
						{
							EditorChangeTracker.PushUndo(
								new GenericValueChangeAction(
									Entity,
									typeof(Entity).GetProperty(nameof(Entity.IsSelectableInEditor)),
									oldSelectable,
									isSelectable,
									$"{Entity.Name}.IsSelectableInEditor"
								),
								Entity,
								$"{Entity.Name}.IsSelectableInEditor"
							);
							Entity.IsSelectableInEditor = isSelectable;
						}

						if (ImGui.IsItemHovered())
						{
							ImGui.SetTooltip("If FALSE, you won't be able select this \n Entity with your cursor in the Editor.");
						}
					}

					// DebugRenderEnabled
					{
						bool oldDebugEnabled = Entity.DebugRenderEnabled;
						bool debugEnabled = oldDebugEnabled;
						if (ImGui.Checkbox("Debug Render Enabled", ref debugEnabled) && debugEnabled != oldDebugEnabled)
						{
							EditorChangeTracker.PushUndo(
								new GenericValueChangeAction(
									Entity,
									typeof(Entity).GetProperty(nameof(Entity.DebugRenderEnabled)),
									oldDebugEnabled,
									debugEnabled,
									$"{Entity.Name}.DebugRenderEnabled"
								),
								Entity,
								$"{Entity.Name}.DebugRenderEnabled"
							);
							Entity.DebugRenderEnabled = debugEnabled;
						}
					}
					#endregion

					VoltageEditorUtils.MediumVerticalSpace();

					if (_transformInspector != null)
					{
						_transformInspector.Draw();
					}

					VoltageEditorUtils.MediumVerticalSpace();

					for (var i = _componentInspectors.Count - 1; i >= 0; i--)
					{
						if (_componentInspectors[i].Entity == null)
						{
							_componentInspectors.RemoveAt(i);
							continue;
						}

						_componentInspectors[i].Draw();
						VoltageEditorUtils.MediumVerticalSpace();
					}

					// Add Component button
					if (VoltageEditorUtils.CenteredButton("Add Component", 0.6f))
					{
						_showAddComponentPopup = true;
						_componentFilterText = "";
					}

					VoltageEditorUtils.MediumVerticalSpace();

					if (Entity.Type != Entity.InstanceType.HardCoded && VoltageEditorUtils.CenteredButton("Create Prefab", 0.6f))
					{
						_prefabName = Entity.Name + "_Prefab";
						ImGui.OpenPopup("prefab-creator");
					}

					if (Entity.Type == Entity.InstanceType.Prefab && !string.IsNullOrEmpty(Entity.OriginalPrefabName))
					{
						VoltageEditorUtils.MediumVerticalSpace();
						if (VoltageEditorUtils.CenteredButton("Apply to Prefab Copies", 0.8f))
						{
							ShowApplyToPrefabCopiesConfirmation();
						}
					}

					if (Entity.Type == Entity.InstanceType.Prefab && !string.IsNullOrEmpty(Entity.OriginalPrefabName))
					{
						VoltageEditorUtils.MediumVerticalSpace();
						if (VoltageEditorUtils.CenteredButton("Apply to Original Prefab", 0.8f))
						{
							_showApplyToOriginalPrefabConfirmation = true;
						}
					}

					DrawAddComponentPopup();
					DrawPrefabCreatorPopup();
					DrawApplyToPrefabCopiesConfirmationPopup();
					DrawApplyToOriginalPrefabConfirmationPopup();
				}
			}
			else
			{
				ImGui.TextColored(new Num.Vector4(1, 1, 0, 1), "No entity selected.");
			}

		}

		ImGui.End();
		ImGui.PopStyleVar();
		ImGui.PopStyleColor();

		if (!open)
			Voltage.Core.GetGlobalManager<ImGuiManager>().CloseMainEntityInspector();
	}

	/// <summary>
	/// Draws the prefab creation popup with name input and create/cancel buttons.
	/// </summary>
	private void DrawPrefabCreatorPopup()
	{
		// Center the popup when it first appears
		var center = new Num.Vector2(Screen.Width * 0.5f, Screen.Height * 0.4f);
		ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new Num.Vector2(0.5f, 0.5f));

		bool open = true;
		if (ImGui.BeginPopupModal("prefab-creator", ref open, ImGuiWindowFlags.AlwaysAutoResize))
		{
			ImGui.Text("Create Prefab from Entity");
			ImGui.Separator();
			
			ImGui.Text("Prefab Name:");
			ImGui.InputText("##PrefabName", ref _prefabName, 50);

			// Check if prefab name already exists and show warning
			var correctedName = CorrectPrefabName(_prefabName.Trim(), Entity.GetType().Name);
			bool prefabExists = CheckPrefabExists(correctedName);
			
			if (prefabExists)
			{
				ImGui.TextColored(new Num.Vector4(1.0f, 0.2f, 0.2f, 1.0f), $"Warning: Prefab '{correctedName}' already exists!");
			}

			VoltageEditorUtils.MediumVerticalSpace();

			// Center the buttons
			var buttonWidth = 80f;
			var spacing = 10f;
			var totalButtonWidth = (buttonWidth * 2) + spacing;
			var windowWidth = ImGui.GetWindowSize().X;
			var centerStart = (windowWidth - totalButtonWidth) * 0.5f;
			
			ImGui.SetCursorPosX(centerStart);
			
			// Disable create button if prefab exists
			if (prefabExists)
				ImGui.BeginDisabled();
			
			if (ImGui.Button("Create", new Num.Vector2(buttonWidth, 0)))
			{
				if (!string.IsNullOrWhiteSpace(_prefabName))
				{
					CreatePrefabFromEntity(correctedName);
					ImGui.CloseCurrentPopup();
				}
			}
			
			if (prefabExists)
				ImGui.EndDisabled();
			
			ImGui.SameLine();
			
			if (ImGui.Button("Cancel", new Num.Vector2(buttonWidth, 0)))
			{
				ImGui.CloseCurrentPopup();
			}

			ImGui.EndPopup();
		}
	}

	/// <summary>
	/// Checks if a prefab with the given name already exists.
	/// </summary>
	/// <param name="prefabName">Name of the prefab to check</param>
	/// <returns>True if prefab exists, false otherwise</returns>
	private bool CheckPrefabExists(string prefabName)
	{
		var prefabFilePath = $"Content/Data/Prefabs/{prefabName}.json";
		return File.Exists(prefabFilePath);
	}

	/// <summary>
	/// Creates a prefab from the current entity using the DuplicateEntity method from EntityPane.
	/// Handles async saving and notifications.
	/// </summary>
	private async void CreatePrefabFromEntity(string prefabName, bool canOverride = false)
	{
		if (Entity == null || _imGuiManager?.SceneGraphWindow?.EntityPane == null)
			return;

		var newPrefab = _imGuiManager.SceneGraphWindow.EntityPane.DuplicateEntity(Entity, prefabName);

		if (newPrefab != null)
		{
			newPrefab.Type = Entity.InstanceType.Prefab;
			newPrefab.Name = prefabName;
			newPrefab.OriginalPrefabName = prefabName;

			bool saveSuccessful = await _imGuiManager.InvokePrefabCreated(newPrefab, canOverride);

			if (saveSuccessful)
			{
				_imGuiManager.SceneGraphWindow.AddPrefabToCache(newPrefab.Name);
				NotificationSystem.ShowTimedNotification($"Successfully created and saved prefab: {newPrefab.Name}");
			}
			else if(!canOverride)
			{
				NotificationSystem.ShowTimedNotification($"Failed to save prefab: {newPrefab.Name} - Prefab with this name already exists!");
			}
		}
		else
		{
			NotificationSystem.ShowTimedNotification($"Failed to create prefab: {prefabName}");
		}
	}

	/// <summary>
	/// Ensures the prefab name is unique within its EntityType directory.
	/// Only adds EntityType prefix if the name would conflict with existing prefabs.
	/// </summary>
	/// <param name="inputName">The user-provided prefab name</param>
	/// <param name="entityTypeName">The entity's type name</param>
	/// <returns>Unique prefab name</returns>
	private string CorrectPrefabName(string inputName, string entityTypeName)
	{
		if (string.IsNullOrWhiteSpace(inputName))
			return $"{entityTypeName}_Prefab";

		var cleanedName = inputName.Trim();
		
		// Check if prefab with this name already exists in the EntityType directory
		var prefabsDirectory = $"Content/Data/Prefabs/{entityTypeName}";
		
		if (!Directory.Exists(prefabsDirectory))
		{
			return cleanedName;
		}

		var prefabFilePath = Path.Combine(prefabsDirectory, $"{cleanedName}.json");
		
		if (!File.Exists(prefabFilePath))
		{
			return cleanedName;
		}

		// Name conflict detected - try to find a unique variant
		int suffix = 1;
		string uniqueName;
		
		do
		{
			uniqueName = $"{cleanedName}_{suffix}";
			prefabFilePath = Path.Combine(prefabsDirectory, $"{uniqueName}.json");
			suffix++;
		}
		while (File.Exists(prefabFilePath));

		return uniqueName;
	}

	/// <summary>
	/// Applies the current prefab entity's component data to all other entities in the scene 
	/// that have the same OriginalPrefabName.
	/// </summary>
	private void ApplyToPrefabCopies()
	{
		if (Entity == null || Entity.Type != Entity.InstanceType.Prefab || string.IsNullOrEmpty(Entity.OriginalPrefabName))
			return;

		// Use the pre-found list of prefab copies
		var prefabCopies = _prefabCopiesToModify;
		
		if (prefabCopies.Count == 0)
		{
			NotificationSystem.ShowTimedNotification($"No other copies of prefab '{Entity.OriginalPrefabName}' found in scene.");
			return;
		}

		// Create undo action to track all changes
		var undoActions = new List<EditorChangeTracker.IEditorAction>();

		foreach (var targetEntity in prefabCopies)
		{
			// Store the entity's old component data for undo
			var oldComponentData = new Dictionary<string, ComponentData>();
			foreach (var component in targetEntity.Components)
			{
				if (component.Data != null)
				{
					try
					{
						var jsonSettings = new JsonSettings
						{
							PrettyPrint = false,
							TypeNameHandling = TypeNameHandling.Auto,
							PreserveReferencesHandling = false
						};
						
						// Deep clone the old data for undo
						var json = Json.ToJson(component.Data, jsonSettings);
						var clonedOldData = (ComponentData)Json.FromJson(json, component.Data.GetType());
						oldComponentData[component.Name] = clonedOldData;
					}
					catch (Exception ex)
					{
						Debug.Error($"Failed to backup component data for undo: {component.Name} - {ex.Message}");
					}
				}
			}

			// Apply component data from source entity to target entity
			bool hasChanges = false;
			foreach (var sourceComponent in Entity.Components)
			{
				if (sourceComponent.Data == null)
					continue;

				// Find matching component in target entity
				var targetComponent = targetEntity.Components.FirstOrDefault(c => 
					c.GetType() == sourceComponent.GetType() && c.Name == sourceComponent.Name);

				if (targetComponent != null)
				{
					try
					{
						var jsonSettings = new JsonSettings
						{
							PrettyPrint = false,
							TypeNameHandling = TypeNameHandling.Auto,
							PreserveReferencesHandling = false
						};
						
						// Deep clone the source component data
						var json = Json.ToJson(sourceComponent.Data, jsonSettings);
						var clonedData = (ComponentData)Json.FromJson(json, sourceComponent.Data.GetType());
						
						// Apply the cloned data to the target component
						targetComponent.Data = clonedData;
						hasChanges = true;
					}
					catch (Exception ex)
					{
						Debug.Error($"Failed to copy component data: {sourceComponent.Name} - {ex.Message}");
					}
				}
			}

			// Only create undo action if there were actual changes
			if (hasChanges)
			{
				undoActions.Add(new PrefabCopyUndoAction(targetEntity, oldComponentData, Entity.OriginalPrefabName));
			}
		}

		// Create a composite undo action that can undo all changes at once
		if (undoActions.Count > 0)
		{
			var compositeUndo = new CompositePrefabApplyUndoAction(undoActions, Entity.OriginalPrefabName);
			EditorChangeTracker.PushUndo(
				compositeUndo,
				Entity,
				$"Apply '{Entity.OriginalPrefabName}' to {undoActions.Count} copies"
			);

			NotificationSystem.ShowTimedNotification($"Applied prefab '{Entity.OriginalPrefabName}' to {undoActions.Count} copies.");
		}
		else
		{
			NotificationSystem.ShowTimedNotification($"No changes were applied - all copies are already up to date.");
		}

		_prefabCopiesToModify.Clear();
	}

	/// <summary>
	/// Used when we're dealing with a newly loaded entity that might not be ready to be set immediately.
	/// </summary>
	/// <param name="entity"></param>
	/// <param name="time"></param>
	public void DelayedSetEntity(Entity entity, float time = 0.05f)
	{
		Voltage.Core.StartCoroutine(ShowInspector(entity, time));
	}

	private IEnumerator ShowInspector(Entity entity, float time)
	{
		yield return Coroutine.WaitForSeconds(time);
		SetEntity(entity);
	}

	/// <summary>
	/// Shows the confirmation popup for applying prefab changes to copies.
	/// </summary>
	private void ShowApplyToPrefabCopiesConfirmation()
	{
		if (Entity == null || Entity.Type != Entity.InstanceType.Prefab || string.IsNullOrEmpty(Entity.OriginalPrefabName))
			return;

		_prefabCopiesToModify = Voltage.Core.Scene.Entities
			.Where(e => e != Entity &&
						e.Type == Entity.InstanceType.Prefab && 
						e.OriginalPrefabName == Entity.OriginalPrefabName)
			.ToList();

		if (_prefabCopiesToModify.Count == 0)
		{
			NotificationSystem.ShowTimedNotification($"No other copies of prefab '{Entity.OriginalPrefabName}' found in scene.");
			return;
		}

		_showApplyToPrefabCopiesConfirmation = true;
	}

	/// <summary>
	/// Draws the apply to prefab copies confirmation popup.
	/// </summary>
	private void DrawApplyToPrefabCopiesConfirmationPopup()
	{
		if (_showApplyToPrefabCopiesConfirmation)
		{
			ImGui.OpenPopup("apply-to-prefab-copies-confirmation");
			_showApplyToPrefabCopiesConfirmation = false; // Only open once
		}

		// Center the popup when it first appears
		var center = new Num.Vector2(Screen.Width * 0.45f, Screen.Height * 0.45f);
		ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new Num.Vector2(0.5f, 0.5f));
		ImGui.SetNextWindowSize(new Num.Vector2(450, 0), ImGuiCond.Appearing);

		bool open = true;
		if (ImGui.BeginPopupModal("apply-to-prefab-copies-confirmation", ref open, ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoTitleBar))
		{
			ImGui.Text("Apply Changes to Prefab Copies");
			ImGui.Separator();
			
			ImGui.TextWrapped($"You are going to change prefab values for these entities:");
			
			VoltageEditorUtils.SmallVerticalSpace();
			
			// Show the list of entities that will be affected
			ImGui.TextColored(new Num.Vector4(1.0f, 0.8f, 0.2f, 1.0f), $"Prefab: {Entity.OriginalPrefabName}");
			ImGui.TextColored(new Num.Vector4(0.8f, 0.8f, 0.8f, 1.0f), $"Entities to be modified ({_prefabCopiesToModify.Count}):");

			// Create a scrollable region for the entity list
			if (ImGui.BeginChild("EntityList", new Num.Vector2(0, Math.Min(200, _prefabCopiesToModify.Count * 80 + 20)), true))
			{
				foreach (var prefabCopy in _prefabCopiesToModify)
				{
					ImGui.BulletText($"{prefabCopy.Name}");
					ImGui.Dummy(new Num.Vector2(0, 2)); // spacing between items
				}
			}
			ImGui.EndChild();

			VoltageEditorUtils.MediumVerticalSpace();
			
			ImGui.TextColored(new Num.Vector4(1.0f, 0.6f, 0.2f, 1.0f), "This action can be undone with Ctrl+Z");

			VoltageEditorUtils.MediumVerticalSpace();

			// Center the buttons
			var buttonWidth = 80f;
			var spacing = 10f;
			var totalButtonWidth = (buttonWidth * 2) + spacing;
			var windowWidth = ImGui.GetWindowSize().X;
			var centerStart = (windowWidth - totalButtonWidth) * 0.5f;
			
			ImGui.SetCursorPosX(centerStart);
			
			if (ImGui.Button("OK", new Num.Vector2(buttonWidth, 0)))
			{
				// Proceed with applying changes
				ApplyToPrefabCopies();
				ImGui.CloseCurrentPopup();
			}
			
			ImGui.SameLine();
			
			if (ImGui.Button("Cancel", new Num.Vector2(buttonWidth, 0)))
			{
				// Clear the list and close popup
				_prefabCopiesToModify.Clear();
				ImGui.CloseCurrentPopup();
			}

			ImGui.EndPopup();
		}
	}

	/// <summary>
	/// Draws the confirmation popup for applying changes to the original prefab.
	/// </summary>
	private void DrawApplyToOriginalPrefabConfirmationPopup()
	{
		if (_showApplyToOriginalPrefabConfirmation)
		{
			ImGui.OpenPopup("apply-to-original-prefab-confirmation");
			_showApplyToOriginalPrefabConfirmation = false; // Only open once
		}

		// Center the popup when it first appears
		var center = new Num.Vector2(Screen.Width * 0.5f, Screen.Height * 0.5f);
		ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new Num.Vector2(0.5f, 0.5f));
		ImGui.SetNextWindowSize(new Num.Vector2(400, 0), ImGuiCond.Appearing);

		bool open = true;
		if (ImGui.BeginPopupModal("apply-to-original-prefab-confirmation", ref open, ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoTitleBar))
		{
			ImGui.TextColored(new Num.Vector4(1.0f, 0.6f, 0.2f, 1.0f), $"Are you sure you want to override the data of '{Entity.OriginalPrefabName}'?");
			ImGui.Separator();
			ImGui.TextWrapped("This action will overwrite the original prefab file and cannot be undone outside of this session.");

			VoltageEditorUtils.MediumVerticalSpace();

			// Center the buttons
			var buttonWidth = 80f;
			var spacing = 10f;
			var totalButtonWidth = (buttonWidth * 2) + spacing;
			var windowWidth = ImGui.GetWindowSize().X;
			var centerStart = (windowWidth - totalButtonWidth) * 0.5f;
			
			ImGui.SetCursorPosX(centerStart);

			if (ImGui.Button("Yes", new Num.Vector2(buttonWidth, 0)))
			{
				ApplyToOriginalPrefabWithUndo();
				ImGui.CloseCurrentPopup();
			}

			ImGui.SameLine();

			if (ImGui.Button("No", new Num.Vector2(buttonWidth, 0)))
			{
				ImGui.CloseCurrentPopup();
			}

			ImGui.EndPopup();
		}
	}

	private async void ApplyToOriginalPrefabWithUndo()
	{
		// Save the current entity's data to its original prefab file
		if (Entity != null && Entity.Type == Entity.InstanceType.Prefab && !string.IsNullOrEmpty(Entity.OriginalPrefabName))
		{
			// Save the prefab using the async event system
			bool saveSuccessful = await Voltage.Core.GetGlobalManager<ImGuiManager>().InvokePrefabCreated(Entity, true);

			if (saveSuccessful)
			{
				NotificationSystem.ShowTimedNotification($"Applied changes to original prefab: {Entity.OriginalPrefabName}");
			}
			else
			{
				NotificationSystem.ShowTimedNotification($"Failed to apply changes to prefab: {Entity.OriginalPrefabName}");
			}
		}
	}
}