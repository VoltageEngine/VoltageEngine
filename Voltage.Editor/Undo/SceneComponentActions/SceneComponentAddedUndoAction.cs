using System;
using Voltage.Editor.Undo.Core;

namespace Voltage.Editor.Undo.SceneComponentActions;

/// <summary>
/// Undo action for adding a <see cref="SceneComponent"/> to the scene.
/// </summary>
public class SceneComponentAddedUndoAction : EditorChangeTracker.IEditorAction
{
	private readonly Scene _scene;
	private SceneComponent _component;
	private readonly Type _componentType;
	private readonly string _componentName;

	public string Description => $"Add SceneComponent {_componentType.Name} to scene";

	public SceneComponentAddedUndoAction(Scene scene, SceneComponent component)
	{
		_scene       = scene;
		_component   = component;
		_componentType = component.GetType();
		_componentName = component.Name;
	}

	public void Undo()
	{
		if (_component == null || _scene == null)
			return;

		// Remove the component from the scene without triggering a new undo push
		_scene.RemoveSceneComponent(_component);
	}

	public void Redo()
	{
		if (_scene == null)
			return;

		// Recreate if the reference was lost
		if (_component == null)
		{
			try
			{
				_component = (SceneComponent)Activator.CreateInstance(_componentType);
				_component.Name = _componentName;
			}
			catch (Exception ex)
			{
				Debug.Error($"[SceneComponentAddedUndoAction] Failed to recreate '{_componentName}': {ex.Message}");
				return;
			}
		}

		_component.SetSerialized(true);
		_scene.AddSceneComponent(_component);
	}
}
