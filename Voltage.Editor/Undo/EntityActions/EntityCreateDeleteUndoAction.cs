using Voltage.Editor.Undo.Core;

namespace Voltage.Editor.Undo.EntityActions;

public class EntityCreateDeleteUndoAction : EditorChangeTracker.IEditorAction
{
	private readonly Scene _scene;
	private readonly Entity _entity;
	private readonly bool _wasCreated; // true = creation, false = deletion
	private readonly string _description;

	public string Description => _description;

	public EntityCreateDeleteUndoAction(Scene scene, Entity entity, bool wasCreated, string description)
	{
		_scene = scene;
		_entity = entity;
		_wasCreated = wasCreated;
		_description = description;
	}

	public void Undo()
	{
		if (_wasCreated)
			_entity.DetachFromScene();
		else
			_entity.AttachToScene(_scene);
	}

	public void Redo()
	{
		if (_wasCreated)
			_entity.AttachToScene(_scene);
		else
			_entity.DetachFromScene();
	}
}