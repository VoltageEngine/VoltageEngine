using System.Collections.Generic;
using Nez;

namespace Voltage.Editor.UndoActions;

public class MultiEntityDeleteUndoAction : EditorChangeTracker.IEditorAction
{
	private readonly Scene _scene;
	private readonly List<Entity> _deletedEntities;
	private readonly string _description;

	public string Description => _description;

	public MultiEntityDeleteUndoAction(Scene scene, List<Entity> deletedEntities, string description)
	{
		_scene = scene;
		_deletedEntities = new List<Entity>(deletedEntities);
		_description = description;
	}

	public void Undo()
	{
		foreach (var entity in _deletedEntities)
		{
			if (entity != null && entity.Scene != _scene)
				entity.AttachToScene(_scene);
		}
	}

	public void Redo()
	{
		foreach (var entity in _deletedEntities)
		{
			if (entity != null && entity.Scene == _scene)
				entity.Destroy();
		}
	}
}