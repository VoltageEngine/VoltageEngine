using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Nez;

namespace Voltage.Editor.UndoActions;

public class MultiEntityTransformUndoAction : EditorChangeTracker.IEditorAction
{
	private readonly List<Entity> _entities;
	private readonly Dictionary<Entity, Vector2> _oldPositions;
	private readonly Dictionary<Entity, Vector2> _newPositions;
	private readonly string _description;

	public string Description => _description;

	public MultiEntityTransformUndoAction(List<Entity> entities, Dictionary<Entity, Vector2> oldPositions, Dictionary<Entity, Vector2> newPositions, string description)
	{
		_entities = new List<Entity>(entities);
		_oldPositions = new Dictionary<Entity, Vector2>(oldPositions);
		_newPositions = new Dictionary<Entity, Vector2>(newPositions);
		_description = description;
	}

	public void Undo()
	{
		foreach (var entity in _entities)
		{
			if (_oldPositions.TryGetValue(entity, out var pos))
				entity.Transform.SetPosition(pos);
		}
	}

	public void Redo()
	{
		foreach (var entity in _entities)
		{
			if (_newPositions.TryGetValue(entity, out var pos))
				entity.Transform.SetPosition(pos);
		}
	}
}