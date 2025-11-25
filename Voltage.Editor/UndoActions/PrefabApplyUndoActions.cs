using Voltage.Persistence;
using System;
using System.Collections.Generic;
using System.Linq;
using Nez;

namespace Voltage.Editor.UndoActions
{
	/// <summary>
	/// Undo action for applying prefab changes to a single entity copy.
	/// </summary>
	public class PrefabCopyUndoAction : EditorChangeTracker.IEditorAction
	{
		private Entity _entity;
		private Dictionary<string, ComponentData> _oldComponentData;
		private Dictionary<string, ComponentData> _newComponentData;
		private string _prefabName;

		public string Description => $"Apply prefab '{_prefabName}' to {_entity?.Name ?? "Unknown Entity"}";

		public PrefabCopyUndoAction(Entity entity, Dictionary<string, ComponentData> oldComponentData, string prefabName)
		{
			_entity = entity;
			_oldComponentData = oldComponentData;
			_prefabName = prefabName;
			
			// Capture the new component data (after changes)
			_newComponentData = new Dictionary<string, ComponentData>();
			foreach (var component in entity.Components)
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
						
						var json = Json.ToJson(component.Data, jsonSettings);
						var clonedData = (ComponentData)Json.FromJson(json, component.Data.GetType());
						_newComponentData[component.Name] = clonedData;
					}
					catch (Exception ex)
					{
						System.Console.WriteLine($"Failed to capture new component data for undo: {component.Name} - {ex.Message}");
					}
				}
			}
		}

		public void Undo()
		{
			if (_entity == null || _entity.IsDestroyed)
				return;

			// Restore old component data
			foreach (var kvp in _oldComponentData)
			{
				var component = _entity.Components.FirstOrDefault(c => c.Name == kvp.Key);
				if (component != null)
				{
					component.Data = kvp.Value;
				}
			}
		}

		public void Redo()
		{
			if (_entity == null || _entity.IsDestroyed)
				return;

			// Reapply new component data
			foreach (var kvp in _newComponentData)
			{
				var component = _entity.Components.FirstOrDefault(c => c.Name == kvp.Key);
				if (component != null)
				{
					component.Data = kvp.Value;
				}
			}
		}
	}

	/// <summary>
	/// Composite undo action that handles multiple prefab copy operations as a single undo/redo operation.
	/// </summary>
	public class CompositePrefabApplyUndoAction : EditorChangeTracker.IEditorAction
	{
		private List<EditorChangeTracker.IEditorAction> _undoActions;
		private string _prefabName;

		public string Description => $"Apply prefab '{_prefabName}' to {_undoActions.Count} copies";

		public CompositePrefabApplyUndoAction(List<EditorChangeTracker.IEditorAction> undoActions, string prefabName)
		{
			_undoActions = undoActions ?? new List<EditorChangeTracker.IEditorAction>();
			_prefabName = prefabName;
		}

		public void Undo()
		{
			// Undo all actions in reverse order
			for (int i = _undoActions.Count - 1; i >= 0; i--)
			{
				_undoActions[i].Undo();
			}
		}

		public void Redo()
		{
			// Redo all actions in forward order
			foreach (var action in _undoActions)
			{
				action.Redo();
			}
		}
	}
}