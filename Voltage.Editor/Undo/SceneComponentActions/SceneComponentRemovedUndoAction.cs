using System;
using Voltage.Editor.Undo.Core;

namespace Voltage.Editor.Undo.SceneComponentActions;

/// <summary>
/// Undo action for removing a <see cref="SceneComponent"/> from the scene.
/// </summary>
public class SceneComponentRemovedUndoAction : EditorChangeTracker.IEditorAction
{
	private readonly Scene _scene;
	private SceneComponent _component;
	private readonly Type _componentType;
	private readonly string _componentName;
	// Snapshot of the component's data at the time of deletion so the redo path
	// can restore field values if the component is re-instantiated.
	private ComponentData _snapshotData;

	public string Description { get; }

	public SceneComponentRemovedUndoAction(Scene scene, SceneComponent component, string description)
	{
		_scene         = scene;
		_component     = component;
		_componentType = component.GetType();
		_componentName = component.Name;
		Description    = description;

		// Snapshot data while the component is still alive
		try { _snapshotData = component.Data; }
		catch { /* ignore snapshot failure */ }
	}

	public void Undo()
	{
		if (_scene == null || _component == null)
			return;

		_component.SetSerialized(true);
		_scene.AddSceneComponent(_component);
	}

	public void Redo()
	{
		if (_scene == null)
			return;

		// Find the live instance in case the component was re-added by Undo
		SceneComponent live = null;
		for (var i = 0; i < _scene._sceneComponents.Length; i++)
		{
			var sc = _scene._sceneComponents.Buffer[i];
			if (sc.GetType() == _componentType && sc.Name == _componentName)
			{
				live = sc;
				break;
			}
		}

		if (live != null)
			_scene.RemoveSceneComponent(live);
	}
}
