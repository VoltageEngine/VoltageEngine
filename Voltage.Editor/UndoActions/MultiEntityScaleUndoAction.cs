using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Nez;

namespace Voltage.Editor.UndoActions
{
	/// <summary>
	/// Undo action for scaling multiple entities via gizmo.
	/// </summary>
	public class MultiEntityScaleUndoAction : EditorChangeTracker.IEditorAction
	{
		private readonly List<Entity> _entities;
		private readonly Dictionary<Entity, Vector2> _startScales;
		private readonly Dictionary<Entity, Vector2> _endScales;
		public string Description { get; }

		public MultiEntityScaleUndoAction(
			List<Entity> entities,
			Dictionary<Entity, Vector2> startScales,
			Dictionary<Entity, Vector2> endScales,
			string description)
		{
			_entities = entities;
			_startScales = new Dictionary<Entity, Vector2>(startScales);
			_endScales = new Dictionary<Entity, Vector2>(endScales);
			Description = description;
		}

		public void Undo()
		{
			foreach (var entity in _entities)
			{
				if (_startScales.TryGetValue(entity, out var scale))
					entity.Transform.Scale = scale;
			}
		}

		public void Redo()
		{
			foreach (var entity in _entities)
			{
				if (_endScales.TryGetValue(entity, out var scale))
					entity.Transform.Scale = scale;
			}
		}
	}
}