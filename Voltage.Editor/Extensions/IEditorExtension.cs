namespace Voltage.Editor.Extensions;

/// <summary>
/// A plugin that's used to extend the Editor's functions, for example a
/// "Dialogue Creator" plugin, which could open new windows inside the Editor. 
/// </summary>
public abstract class IEditorExtension 
{
	public bool IsOpen { get; private set; }
	
	/// <summary>
	/// Register this Editor plugin so that it's in the list of the plugins to be rendered by the Editor
	/// </summary>
	public virtual void Register()
	{
		ExtensionManager.RegisterEditorPlugin(this);
	}
	
	public virtual void UnRegister()
	{
		ExtensionManager.UnregisterEditorPlugin(this);
	}

	public virtual void OpenWindow()
	{
		IsOpen =  true;
	}

	public virtual void DrawWindow()
	{
	}

	public virtual void CloseWindow()
	{
		IsOpen = false;
	}
}