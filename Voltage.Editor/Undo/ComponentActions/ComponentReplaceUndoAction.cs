using Voltage.Editor.Undo.Core;

namespace Voltage.Editor.Undo.ComponentActions
{
    public class ComponentReplaceUndoAction : EditorChangeTracker.IEditorAction
    {
        private readonly Voltage.Entity _entity;
        private readonly Voltage.Component _oldComponent;
        private readonly Voltage.Component _newComponent;
        private readonly string _description;

        public string Description => _description;

        public ComponentReplaceUndoAction(Voltage.Entity entity, Voltage.Component oldComponent, Voltage.Component newComponent, string description)
        {
            _entity = entity;
            _oldComponent = oldComponent;
            _newComponent = newComponent;
            _description = description;
        }

        public void Undo()
        {
            if (_entity != null && _oldComponent != null)
            {
                _entity.ReplaceComponent(_oldComponent);
            }
        }

        public void Redo()
        {
            if (_entity != null && _newComponent != null)
            {
                _entity.ReplaceComponent(_newComponent);
            }
        }
    }
}