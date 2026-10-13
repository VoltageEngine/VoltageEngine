using Voltage.Editor.Undo.Core;

namespace Voltage.Editor.Undo.EntityActions;

public class EntityCreateDeleteUndoAction : EditorChangeTracker.IEditorAction
{
	private readonly Scene _scene;
	private readonly Entity _entity;
	private readonly bool _wasCreated; // true = creation, false = deletion
	private readonly string _description;
	private readonly Transform _parent;
	private readonly int _siblingIndex;

	public string Description => _description;

	public EntityCreateDeleteUndoAction(Scene scene, Entity entity, bool wasCreated, string description)
	{
		_scene = scene;
		_entity = entity;
		_wasCreated = wasCreated;
		_description = description;
		_parent = entity.Transform.Parent;
		_siblingIndex = _parent?.Children.IndexOf(entity.Transform) ?? -1;
	}

	public void Undo()
	{
		if (_wasCreated)
			Detach();
		else
			Attach();
	}

	public void Redo()
	{
		if (_wasCreated)
			Attach();
		else
			Detach();
	}

	private void Detach()
	{
		_entity.DetachFromScene();
		_entity.Transform.SetParent(null);
	}

	private void Attach()
	{
		_entity.Transform.SetParentAt(_parent, _siblingIndex);
		_entity.AttachToScene(_scene);
	}
}
