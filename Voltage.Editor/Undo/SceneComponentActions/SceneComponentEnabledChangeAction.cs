using Voltage.Editor.Undo.Core;

namespace Voltage.Editor.Undo.SceneComponentActions;

/// <summary>
/// Undo action for toggling the <see cref="SceneComponent.Enabled"/> flag.
/// </summary>
public class SceneComponentEnabledChangeAction : EditorChangeTracker.IEditorAction
{
	private readonly SceneComponent _component;
	private readonly bool _oldValue;
	private readonly bool _newValue;

	public string Description => $"{_component?.GetType().Name}.Enabled";

	public SceneComponentEnabledChangeAction(SceneComponent component, bool oldValue, bool newValue)
	{
		_component = component;
		_oldValue  = oldValue;
		_newValue  = newValue;
	}

	public void Undo() => _component?.SetEnabled(_oldValue);
	public void Redo() => _component?.SetEnabled(_newValue);
}
