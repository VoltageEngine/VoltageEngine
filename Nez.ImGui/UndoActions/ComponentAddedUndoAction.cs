using Nez;
using Nez.Editor;
using System;
using System.Linq;

namespace Nez.ImGuiTools.UndoActions
{
	/// <summary>
	/// Undo action for adding a component to an entity.
	/// </summary>
	public class ComponentAddedUndoAction : EditorChangeTracker.IEditorAction
	{
		private Entity _entity;
		private Component _component;
		private Type _componentType;
		private string _componentName;
		private bool _wasActuallyAdded;

		public string Description => $"Add {_componentType.Name} to {_entity?.Name ?? "Unknown Entity"}";

		public ComponentAddedUndoAction(Entity entity, Component component)
		{
			_entity = entity;
			_component = component;
			_componentType = component.GetType();
			_componentName = component.Name;
			
			// Verify the component was actually added to the entity
			_wasActuallyAdded = entity.Components.Contains(component);
			
			if (!_wasActuallyAdded)
			{
				Debug.Warn($"ComponentAddedUndoAction created for a component that wasn't actually added: {_componentType.Name}");
			}
		}

		public void Undo()
		{
			if (!_wasActuallyAdded)
			{
				Debug.Warn("Cannot undo component addition - component was never actually added");
				return;
			}

			if (_entity == null)
			{
				Debug.Warn("Cannot undo component addition - entity is null");
				return;
			}

			// Find and remove the component
			var componentToRemove = _entity.Components
				.FirstOrDefault(c => c.GetType() == _componentType && c.Name == _componentName);

			if (componentToRemove != null)
			{
				_entity.RemoveComponent(componentToRemove);
				_component = componentToRemove; // Store reference for redo
			}
			else
			{
				Debug.Warn($"Cannot undo component addition - component {_componentName} not found on entity {_entity.Name}");
			}
		}

		public void Redo()
		{
			if (!_wasActuallyAdded)
			{
				Debug.Warn("Cannot redo component addition - component was never actually added");
				return;
			}

			if (_entity == null)
			{
				Debug.Warn("Cannot redo component addition - entity is null");
				return;
			}

			// Check if component already exists before re-adding
			var existingComponent = _entity.Components
				.FirstOrDefault(c => c.GetType() == _componentType && c.Name == _componentName);

			if (existingComponent != null)
			{
				Debug.Warn($"Cannot redo component addition - component {_componentName} already exists on entity {_entity.Name}");
				return;
			}

			if (_component != null)
			{
				// Re-add the component
				_entity.AddComponent(_component);
			}
			else
			{
				// If component reference is lost, create a new instance
				try
				{
					var newComponent = (Component)Activator.CreateInstance(_componentType);
					newComponent.Name = _componentName;
					_entity.AddComponent(newComponent);
					_component = newComponent;
				}
				catch (Exception ex)
				{
					Debug.Error($"Failed to recreate component {_componentName}: {ex.Message}");
				}
			}
		}
	}
}