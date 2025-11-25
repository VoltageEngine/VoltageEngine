using System.Collections.Generic;
using Nez;

namespace Voltage.Editor.UndoActions
{
    /// <summary>
    /// Undo action for rotating multiple entities via gizmo.
    /// </summary>
    public class MultiEntityRotationUndoAction : EditorChangeTracker.IEditorAction
    {
        private readonly List<Entity> _entities;
        private readonly Dictionary<Entity, float> _startRotations;
        private readonly Dictionary<Entity, float> _endRotations;
        public string Description { get; }

        public MultiEntityRotationUndoAction(
            List<Entity> entities,
            Dictionary<Entity, float> startRotations,
            Dictionary<Entity, float> endRotations,
            string description)
        {
            _entities = entities;
            _startRotations = new Dictionary<Entity, float>(startRotations);
            _endRotations = new Dictionary<Entity, float>(endRotations);
            Description = description;
        }

        public void Undo()
        {
            foreach (var entity in _entities)
            {
                if (_startRotations.TryGetValue(entity, out var rot))
                    entity.Transform.Rotation = rot;
            }
        }

        public void Redo()
        {
            foreach (var entity in _entities)
            {
                if (_endRotations.TryGetValue(entity, out var rot))
                    entity.Transform.Rotation = rot;
            }
        }
    }
}