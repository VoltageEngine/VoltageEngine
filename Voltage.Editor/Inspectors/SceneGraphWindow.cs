using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ImGuiNET;
using Voltage;
using Voltage.ECS;
using Voltage.Utils;
using Voltage.Editor.Core;
using Voltage.Editor.FilePickers;
using Voltage.Editor.Inspectors.SceneGraphPanes;
using Voltage.Editor.UndoActions;
using Voltage.Editor.Utils;
using Num = System.Numerics;

namespace Voltage.Editor.Inspectors;

// Helper classes for organizing entities and prefabs with nested namespaces
public class EntityCategory
{
	public string Name { get; set; }
	public List<string> EntityTypes { get; set; } = new List<string>();
	public Dictionary<string, EntityCategory> SubCategories { get; set; } = new Dictionary<string, EntityCategory>();
}

public class PrefabCategory
{
	public string Name { get; set; }
	public Dictionary<string, List<string>> PrefabsByEntityType { get; set; } = new Dictionary<string, List<string>>();
	public Dictionary<string, PrefabCategory> SubCategories { get; set; } = new Dictionary<string, PrefabCategory>();
}

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
	private ImGuiManager _imGuiManager;
	public float SceneGraphWidth => _sceneGraphWidth;
	public float SceneGraphPosY { get; set; }
	public bool IsOpen { get; private set; }

	private string _entityFilterName;

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

	// Prefab caching
	private List<string> _cachedPrefabNames = new();
	private bool _prefabCacheInitialized = false;

	// Entity and Prefab organization
	private Dictionary<string, EntityCategory> _entityCategories = new Dictionary<string, EntityCategory>();
	private List<string> _uncategorizedEntities = new List<string>(); // For entities directly in Dynamic namespace
	private Dictionary<string, PrefabCategory> _prefabCategories = new Dictionary<string, PrefabCategory>();
	private Dictionary<string, List<string>> _uncategorizedPrefabs = new Dictionary<string, List<string>>(); // For prefabs directly in Dynamic namespace

	// Prefab deletion
	private bool _showDeletePrefabConfirmation = false;
	private string _prefabToDelete = "";

	// File Pickers
	public TmxFilePicker TmxFilePicker;
	public AsepriteFilePicker AsepriteFilePicker;
	
	// Events
	public static event Action<TmxFilePicker.TmxSelection> OnTmxFileSelected;
	public static event Action<AsepriteFilePicker.AsepriteSelection> OnAsepriteImageSelected;

	public void OnSceneChanged()
	{
		_postProcessorsPane.OnSceneChanged();
		_renderersPane.OnSceneChanged();
		
		TmxFilePicker = new TmxFilePicker(
			this,
			"tmx-file-picker",
			System.IO.Path.Combine(Environment.CurrentDirectory, "Content")
		);
		
		AsepriteFilePicker = new AsepriteFilePicker(
			this,
			"aseprite-image-loader",
			System.IO.Path.Combine(Environment.CurrentDirectory, "Content"), 
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
		
		var prefabsDirectory = "Content/Data/Prefabs";
		if (Directory.Exists(prefabsDirectory))
		{
			// Search through all EntityType subdirectories
			var entityTypeDirectories = Directory.GetDirectories(prefabsDirectory);
			
			foreach (var entityTypeDir in entityTypeDirectories)
			{
				var prefabFiles = Directory.GetFiles(entityTypeDir, "*.json");
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

	public void Show(ref bool isOpen)
	{
		IsOpen = isOpen;

		if (Voltage.Core.Scene == null || !isOpen)
			return;

		if (_imGuiManager == null)
			_imGuiManager = Voltage.Core.GetGlobalManager<ImGuiManager>();

		var windowHeight = Screen.Height - SceneGraphPosY;
		SceneGraphPosY = _imGuiManager.MainWindowPositionY;

		ImGui.PushStyleVar(ImGuiStyleVar.GrabMinSize, 0.0f);
		ImGui.PushStyleColor(ImGuiCol.ResizeGrip, new Num.Vector4(0, 0, 0, 0));

		ImGui.SetNextWindowPos(new Num.Vector2(0, SceneGraphPosY), ImGuiCond.Always);
		ImGui.SetNextWindowSize(new Num.Vector2(_sceneGraphWidth, windowHeight), ImGuiCond.FirstUseEver);

		var windowFlags = ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoCollapse;

		if (ImGui.Begin("Scene Graph", ref isOpen, windowFlags))
		{
			// Update width after user resizes
			var currentWidth = ImGui.GetWindowSize().X;
			if (Math.Abs(currentWidth - _sceneGraphWidth) > 0.01f)
				_sceneGraphWidth = Math.Clamp(currentWidth, _minSceneGraphWidth, _maxSceneGraphWidth);

			VoltageEditorUtils.SmallVerticalSpace();
			if (Voltage.Core.IsEditMode)
			{
				ImGui.TextWrapped("Press F1/F2 to switch to Play mode.");
				VoltageEditorUtils.SmallVerticalSpace();

				if (VoltageEditorUtils.CenteredButton("Edit Mode", 0.8f))
					Voltage.Core.InvokeSwitchEditMode(false); 

				VoltageEditorUtils.SmallVerticalSpace();
				if (VoltageEditorUtils.CenteredButton("Reset Scene", 0.8f))
					Voltage.Core.InvokeResetScene();
			}
			else
			{
				ImGui.TextWrapped("Press F1/F2 to switch to Edit mode.");
				VoltageEditorUtils.SmallVerticalSpace();

				if (VoltageEditorUtils.CenteredButton("Play Mode", 0.8f))
					Voltage.Core.InvokeSwitchEditMode(true);
			}

			VoltageEditorUtils.MediumVerticalSpace();
			if (ImGui.CollapsingHeader("Post Processors"))
				_postProcessorsPane.Draw();

			if (ImGui.CollapsingHeader("Renderers"))
				_renderersPane.Draw();

			if (ImGui.CollapsingHeader("Entities (double-click label to inspect)", ImGuiTreeNodeFlags.DefaultOpen))
				_entityPane.Draw();

			VoltageEditorUtils.MediumVerticalSpace();
			if (VoltageEditorUtils.CenteredButton("Save Scene", 0.7f))
				_imGuiManager.InvokeSaveSceneChanges();

			VoltageEditorUtils.MediumVerticalSpace();

			// IMPORTANT: This assumes that Entities are registered under the "Dynamic" namespace or its sub-namespaces \
			// (e.g. Dynamic.Interactables.Platforms). Adjust the logic in OrganizeEntitiesByNamespace if your project uses a different structure.
			if (VoltageEditorUtils.CenteredButton("Add Entity", 0.6f))
			{
				_entityFilterName = "";
				ImGui.OpenPopup("entity-selector");
				
				OrganizeEntitiesByNamespace();
				OrganizePrefabsByNamespaceAndType();
			}

			// Open file pickers when needed
			if (TmxFilePicker.IsOpen)
			{
				ImGui.OpenPopup(TmxFilePicker.PopupId);
			}

			if (AsepriteFilePicker.IsOpen)
			{
				ImGui.OpenPopup(AsepriteFilePicker.PopupId);
			}

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
		// Always call this so the popup can continue rendering after being opened
		DrawDeletePrefabConfirmationPopup();

		HandleEntitySelectionNavigation();

		if (ImGui.IsMouseClicked(ImGuiMouseButton.Left)
		    && !ImGui.IsAnyItemHovered()
		    && ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows))
		{
			EntityPane.DeselectAllEntities();
		}
	}

	/// <summary>
	/// Organizes dynamic entities into a hierarchical structure based on their namespace.
	/// Entities directly in "Dynamic" namespace are added to uncategorized list.
	/// Entities in nested namespaces (e.g., Dynamic.Interactables.Platforms) are organized hierarchically.
	/// </summary>
	private void OrganizeEntitiesByNamespace()
	{
		_entityCategories.Clear();
		_uncategorizedEntities.Clear();

		foreach (var entityTypeName in EntityFactoryRegistry.GetRegisteredTypes())
		{
			var type = EntityFactoryRegistry.GetEntityType(entityTypeName);
			if (type == null)
			{
				Debug.Warn($"Could not resolve type for: {entityTypeName}");
				continue;
			}

			var fullNamespace = type.Namespace;
			if (string.IsNullOrEmpty(fullNamespace))
			{
				Debug.Warn($"Type {type.Name} has no namespace");
				continue;
			}

			var namespaceParts = fullNamespace.Split('.');
			
			// Find the index of "Dynamic" in the namespace
			int dynamicIndex = -1;
			for (int i = 0; i < namespaceParts.Length; i++)
			{
				if (namespaceParts[i] == "Dynamic")
				{
					dynamicIndex = i;
					break;
				}
			}

			if (dynamicIndex == -1)
			{
				Debug.Warn($"Type {type.Name} is not in a 'Dynamic' namespace");
				continue;
			}

			var categoriesAfterDynamic = namespaceParts.Skip(dynamicIndex + 1).ToArray();

			// Case 1: Entity is directly in "Dynamic" namespace (no sub-namespaces)
			if (categoriesAfterDynamic.Length == 0)
			{
				_uncategorizedEntities.Add(type.Name);
			}
			// Case 2: Entity is in nested namespace (e.g., Dynamic.Interactables.Platforms)
			else
			{
				// Build nested category structure
				var currentCategory = _entityCategories;
				EntityCategory leafCategory = null;

				for (int i = 0; i < categoriesAfterDynamic.Length; i++)
				{
					var categoryName = categoriesAfterDynamic[i];

					if (!currentCategory.ContainsKey(categoryName))
					{
						currentCategory[categoryName] = new EntityCategory { Name = categoryName };
					}

					leafCategory = currentCategory[categoryName];
					currentCategory = leafCategory.SubCategories;
				}

				// Add the entity type to the leaf category
				if (leafCategory != null)
				{
					leafCategory.EntityTypes.Add(type.Name);
				}
			}
		}
	}

	/// <summary>
	/// Organizes prefabs into a hierarchical structure based on namespace and entity type.
	/// Prefabs of entities directly in "Dynamic" namespace are kept separate.
	/// Prefabs in nested namespaces are organized hierarchically.
	/// </summary>
	private void OrganizePrefabsByNamespaceAndType()
	{
		_prefabCategories.Clear();
		_uncategorizedPrefabs.Clear();

		var prefabsDirectory = "Content/Data/Prefabs";
		if (!Directory.Exists(prefabsDirectory))
		{
			Debug.Warn($"Prefabs directory does not exist: {prefabsDirectory}");
			return;
		}

		var entityTypeDirectories = Directory.GetDirectories(prefabsDirectory);

		foreach (var entityTypeDir in entityTypeDirectories)
		{
			var prefabFiles = Directory.GetFiles(entityTypeDir, "*.json");
			
			foreach (var prefabFile in prefabFiles)
			{
				try
				{
					var prefabName = Path.GetFileNameWithoutExtension(prefabFile);
					var prefabData = _imGuiManager.InvokePrefabLoadRequested(prefabName);
				
					if (string.IsNullOrEmpty(prefabData.EntityType))
					{
						Debug.Warn($"Prefab '{prefabName}' has no EntityType specified");
						continue;
					}

					// Get the entity type directly from registry
					var entityType = EntityFactoryRegistry.GetEntityType(prefabData.EntityType);
					if (entityType == null)
					{
						Debug.Warn($"Could not resolve entity type '{prefabData.EntityType}' for prefab '{prefabName}'");
						continue;
					}

					// Extract namespace
					var fullNamespace = entityType.Namespace;
					if (string.IsNullOrEmpty(fullNamespace))
					{
						Debug.Warn($"Entity type '{entityType.Name}' has no namespace");
						continue;
					}

					var namespaceParts = fullNamespace.Split('.');
				
					// Find the index of "Dynamic" in the namespace
					int dynamicIndex = -1;
					for (int i = 0; i < namespaceParts.Length; i++)
					{
						if (namespaceParts[i] == "Dynamic")
						{
							dynamicIndex = i;
							break;
						}
					}

					if (dynamicIndex == -1)
					{
						Debug.Warn($"Entity type '{entityType.Name}' is not in a 'Dynamic' namespace");
						continue;
					}

					var categoriesAfterDynamic = namespaceParts.Skip(dynamicIndex + 1).ToArray();

					// Case 1 = Prefab's entity is directly in "Dynamic" namespace
					if (categoriesAfterDynamic.Length == 0)
					{
						var entityTypeName = entityType.Name;
						if (!_uncategorizedPrefabs.ContainsKey(entityTypeName))
						{
							_uncategorizedPrefabs[entityTypeName] = new List<string>();
						}
						_uncategorizedPrefabs[entityTypeName].Add(prefabName);
					}
					// Case 2 = Prefab's entity is in nested namespace
					{
						// Build nested category structure
						var currentCategory = _prefabCategories;
						PrefabCategory leafCategory = null;

						for (int i = 0; i < categoriesAfterDynamic.Length; i++)
						{
							var categoryName = categoriesAfterDynamic[i];

							if (!currentCategory.ContainsKey(categoryName))
							{
								currentCategory[categoryName] = new PrefabCategory { Name = categoryName };
							}

							leafCategory = currentCategory[categoryName];
							currentCategory = leafCategory.SubCategories;
						}

						// Add prefab under its entity type in the leaf category
						if (leafCategory != null)
						{
							var entityTypeName = entityType.Name;
							if (!leafCategory.PrefabsByEntityType.ContainsKey(entityTypeName))
							{
								leafCategory.PrefabsByEntityType[entityTypeName] = new List<string>();
							}
							leafCategory.PrefabsByEntityType[entityTypeName].Add(prefabName);
						}
					}
				}
				catch (Exception ex)
				{
					Debug.Error($"Error organizing prefab '{Path.GetFileName(prefabFile)}': {ex.Message}");
				}
			}
		}
	
	}

	/// <summary>
	/// Renders a collapsible header for entity categories using CollapsingHeader (recursive for nested categories)
	/// </summary>
	private void RenderEntityCategory(EntityCategory category, int indentLevel = 0)
	{
		var indent = new string(' ', indentLevel * 2);
		
		if (ImGui.CollapsingHeader($"{indent}[{category.Name}]##entity-{category.Name}-{indentLevel}"))
		{
			ImGui.Indent();
			
			foreach (var entityType in category.EntityTypes.OrderBy(e => e))
			{
				if (ImGui.Selectable($"{indent}  {entityType}"))
				{
					CreateEntityFromFactory(entityType);
					ImGui.CloseCurrentPopup();
				}
			}
			
			// Recursively render all subcategories
			foreach (var subCategory in category.SubCategories.Values.OrderBy(c => c.Name))
			{
				RenderEntityCategory(subCategory, indentLevel + 1);
			}
			
			ImGui.Unindent();
		}
	}

	/// <summary>
	/// Renders a collapsible header for prefab categories using CollapsingHeader 
	/// </summary>
	private void RenderPrefabCategory(PrefabCategory category, int indentLevel = 0)
	{
		var indent = new string(' ', indentLevel * 2);
		
		if (ImGui.CollapsingHeader($"{indent}[{category.Name}]##prefab-{category.Name}-{indentLevel}"))
		{
			ImGui.Indent();
			
			foreach (var entityTypeKvp in category.PrefabsByEntityType.OrderBy(kvp => kvp.Key))
			{
				var entityTypeName = entityTypeKvp.Key;
				var prefabs = entityTypeKvp.Value;

				if (ImGui.CollapsingHeader($"{indent}  [{entityTypeName}]##prefab-type-{entityTypeName}-{indentLevel}"))
				{
					ImGui.Indent();
					
					foreach (var prefabName in prefabs.OrderBy(p => p))
					{
						if (ImGui.Selectable($"{indent}    {prefabName}"))
						{
							CreateEntityFromPrefab(prefabName);
							ImGui.CloseCurrentPopup();
						}
						
						if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
						{
							ImGui.OpenPopup($"prefab-context-{prefabName}");
						}
						
						if (ImGui.BeginPopup($"prefab-context-{prefabName}"))
						{
							if (ImGui.Selectable("Create Prefab Instance"))
							{
								CreateEntityFromPrefab(prefabName);
								ImGui.CloseCurrentPopup();
							}
							
							ImGui.Separator();
							
							if (ImGui.Selectable("Delete Prefab"))
							{
								_prefabToDelete = prefabName;
								_showDeletePrefabConfirmation = true;
								ImGui.CloseCurrentPopup();
							}
							
							ImGui.EndPopup();
						}
					}
					
					ImGui.Unindent();
				}
			}
			
			foreach (var subCategory in category.SubCategories.Values.OrderBy(c => c.Name))
			{
				RenderPrefabCategory(subCategory, indentLevel + 1);
			}
			
			ImGui.Unindent();
		}
	}

	private void DrawEntitySelectorPopup()
	{
		if (ImGui.BeginPopup("entity-selector"))
		{
			ImGui.InputText("###EntityFilter", ref _entityFilterName, 25);
			ImGui.Separator();

			RefreshPrefabCache();

			// Draw categorized Dynamic Entities
			ImGui.TextColored(new Num.Vector4(0.8f, 0.8f, 1.0f, 1.0f), "Dynamic Entities:");
			ImGui.Separator();
			
			if (string.IsNullOrEmpty(_entityFilterName))
			{
				// First, render uncategorized entities (directly in Dynamic namespace)
				foreach (var entityType in _uncategorizedEntities.OrderBy(e => e))
				{
					if (ImGui.Selectable(entityType))
					{
						CreateEntityFromFactory(entityType);
						ImGui.CloseCurrentPopup();
					}
				}
				
				// Then, render organized categories (nested namespaces)
				foreach (var category in _entityCategories.Values.OrderBy(c => c.Name))
				{
					RenderEntityCategory(category);
				}
			}
			else
			{
				// Show flat filtered list
				foreach (var typeName in EntityFactoryRegistry.GetRegisteredTypes())
				{
					if (typeName.ToLower().Contains(_entityFilterName.ToLower()))
					{
						if (ImGui.Selectable(typeName))
						{
							CreateEntityFromFactory(typeName);
							ImGui.CloseCurrentPopup();
						}
					}
				}
			}

			if (_cachedPrefabNames.Count > 0)
			{
				ImGui.Separator();
				ImGui.TextColored(new Num.Vector4(1.0f, 0.6f, 0.2f, 1.0f), "Prefabs:");
				ImGui.Separator();
				
				if (string.IsNullOrEmpty(_entityFilterName))
				{
					// First, render uncategorized prefabs (for entities directly in Dynamic namespace)
					foreach (var entityTypeKvp in _uncategorizedPrefabs.OrderBy(kvp => kvp.Key))
					{
						var entityTypeName = entityTypeKvp.Key;
						var prefabs = entityTypeKvp.Value;
						
						if (ImGui.CollapsingHeader($"[{entityTypeName}]##uncategorized-prefab-{entityTypeName}"))
						{
							ImGui.Indent();
							
							foreach (var prefabName in prefabs.OrderBy(p => p))
							{
								if (ImGui.Selectable($"  {prefabName}"))
								{
									CreateEntityFromPrefab(prefabName);
									ImGui.CloseCurrentPopup();
								}
								
								if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
								{
									ImGui.OpenPopup($"prefab-context-{prefabName}");
								}
								
								if (ImGui.BeginPopup($"prefab-context-{prefabName}"))
								{
									if (ImGui.Selectable("Create Prefab Instance"))
									{
										CreateEntityFromPrefab(prefabName);
										ImGui.CloseCurrentPopup();
									}
							
									ImGui.Separator();
							
									if (ImGui.Selectable("Delete Prefab"))
									{
										_prefabToDelete = prefabName;
										_showDeletePrefabConfirmation = true;
										ImGui.CloseCurrentPopup();
									}
							
									ImGui.EndPopup();
								}
							}
							
							ImGui.Unindent();
						}
					}
					
					// Then, render organized categories (nested namespaces)
					foreach (var category in _prefabCategories.Values.OrderBy(c => c.Name))
					{
						RenderPrefabCategory(category);
					}
				}
				else
				{
					// Show flat filtered list
					foreach (var prefabName in _cachedPrefabNames)
					{
						if (prefabName.ToLower().Contains(_entityFilterName.ToLower()))
						{
							if (ImGui.Selectable($"{prefabName} [Prefab]"))
							{
								CreateEntityFromPrefab(prefabName);
								ImGui.CloseCurrentPopup();
							}
							
							if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
							{
								ImGui.OpenPopup($"prefab-context-{prefabName}");
							}
							
							if (ImGui.BeginPopup($"prefab-context-{prefabName}"))
							{
								if (ImGui.Selectable("Create Prefab Instance"))
								{
									CreateEntityFromPrefab(prefabName);
									ImGui.CloseCurrentPopup();
								}
							
								ImGui.Separator();
							
								if (ImGui.Selectable("Delete Prefab"))
								{
									_prefabToDelete = prefabName;
									_showDeletePrefabConfirmation = true;
									ImGui.CloseCurrentPopup();
								}
							
								ImGui.EndPopup();
							}
						}
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
			ImGui.Text("Delete Prefab");
			ImGui.Separator();
			
			ImGui.TextWrapped($"Are you sure you want to delete the '{_prefabToDelete}' prefab completely?");
			ImGui.TextColored(new Num.Vector4(1.0f, 0.6f, 0.2f, 1.0f), "This action cannot be undone!");

			VoltageEditorUtils.MediumVerticalSpace();

			var buttonWidth = 80f;
			var spacing = 10f;
			var totalButtonWidth = (buttonWidth * 2) + spacing;
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
			var prefabsDirectory = "Content/Data/Prefabs";
			bool fileDeleted = false;
			string deletedFilePath = null;

			if (Directory.Exists(prefabsDirectory))
			{
				var entityTypeDirectories = Directory.GetDirectories(prefabsDirectory);
				
				foreach (var entityTypeDir in entityTypeDirectories)
				{
					var prefabFilePath = Path.Combine(entityTypeDir, $"{prefabName}.json");
					
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
				OrganizePrefabsByNamespaceAndType();
				
				NotificationSystem.ShowTimedNotification($"Successfully deleted prefab: {prefabName}");
				
				ImGui.CloseCurrentPopup();
			}
			else
			{
				var errorMsg = $"Prefab file not found: {prefabName}";
				NotificationSystem.ShowTimedNotification(errorMsg);
				Debug.Error(errorMsg);
			}
		}
		catch (Exception ex)
		{
			var errorMsg = $"Failed to delete prefab {prefabName}: {ex.Message}";
			NotificationSystem.ShowTimedNotification(errorMsg);
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
	/// Creates a new entity from a prefab.
	/// </summary>
	private void CreateEntityFromPrefab(string prefabName)
	{
		try
		{
			var prefabData = _imGuiManager.InvokePrefabLoadRequested(prefabName);

			if (prefabData.EntityData == null)
			{
				NotificationSystem.ShowTimedNotification($"Null Prefab EntityData: {prefabName}");
				return;
			}

			if (prefabData.EntityType == null)
			{
				NotificationSystem.ShowTimedNotification($"Invalid prefab data format for: {prefabName} - EntityType property not found");
				return;
			}

			if (EntityFactoryRegistry.TryCreate(prefabData.EntityType, out var entity))
			{
				EntityFactoryRegistry.InvokeEntityCreated(entity);
				entity.Type = Entity.InstanceType.Prefab;
				entity.Transform.Position = Voltage.Core.Scene.Camera.Transform.Position;

				_imGuiManager.InvokeLoadEntityData(entity, prefabData);
				entity.Name = Voltage.Core.Scene.GetUniqueEntityName(prefabData.Name, entity);
				entity.OriginalPrefabName = prefabName;

				EditorChangeTracker.PushUndo(
					new EntityCreateDeleteUndoAction(Voltage.Core.Scene, entity, wasCreated: true,
						$"Create Entity from Prefab {entity.Name}"),
					entity,
					$"Create Entity from Prefab {entity.Name}"
				);

				_imGuiManager.SceneGraphWindow.EntityPane.SetSelectedEntity(entity, false);
				_imGuiManager.MainEntityInspector.DelayedSetEntity(entity);

				NotificationSystem.ShowTimedNotification($"Created entity from prefab: {prefabName}");
			}
			else
			{
				NotificationSystem.ShowTimedNotification(
					$"Failed to create entity from prefab: {prefabName}. Entity type '{prefabData.EntityType}' not registered.");
			}
		}
		catch (Exception ex)
		{
			NotificationSystem.ShowTimedNotification($"Error creating entity from prefab {prefabName}: {ex.Message}");
		}
	}

	/// <summary>
	/// Creates a new entity from the EntityFactoryRegistry.
	/// </summary>
	private void CreateEntityFromFactory(string typeName)
	{
		if (EntityFactoryRegistry.TryCreate(typeName, out var entity))
		{
			EntityFactoryRegistry.InvokeEntityCreated(entity);
			entity.Type = Entity.InstanceType.Dynamic;
			entity.Name = Voltage.Core.Scene.GetUniqueEntityName(typeName, entity); 
			entity.Transform.Position = Voltage.Core.Scene.Camera.Transform.Position;

			EditorChangeTracker.PushUndo(
				new EntityCreateDeleteUndoAction(Voltage.Core.Scene, entity, wasCreated: true,
					$"Create Entity {entity.Name}"),
				entity,
				$"Create Entity {entity.Name}"
			);

			var imGuiManager = Voltage.Core.GetGlobalManager<ImGuiManager>();
			if (imGuiManager != null)
			{
				imGuiManager.SceneGraphWindow.EntityPane.SetSelectedEntity(entity, false);
				_imGuiManager.MainEntityInspector.DelayedSetEntity(entity);
			}
		}
	}

	#region Entity Selection Navigation
	private void HandleEntitySelectionNavigation()
	{
		var selectedEntity = _imGuiManager.SceneGraphWindow.EntityPane.SelectedEntities.FirstOrDefault();

		if (!Voltage.Core.IsEditMode || selectedEntity == null || !ImGui.IsWindowFocused(ImGuiFocusedFlags.AnyWindow))
			return; 

		var hierarchyList = BuildHierarchyList();
		var currentEntity = _imGuiManager?.MainEntityInspector?.Entity;
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
		var entities = Voltage.Core.Scene?.Entities;
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