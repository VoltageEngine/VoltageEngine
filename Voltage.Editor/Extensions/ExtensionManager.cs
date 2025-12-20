namespace Voltage.Editor.Extensions;

//TODO: Implement the Extensions into the Editor's ImGui pipelines
public class ExtensionManager
{
	static IEditorExtension[] plugins;
	private static int _lastIndex = 0;
	
	/// <summary>
	/// Call when we need to open a plugin
	/// </summary>
	/// <param name="extension"></param>
	public static void RegisterEditorPlugin(IEditorExtension extension)
	{
		plugins[_lastIndex] =  extension;
		_lastIndex++;
	}

	/// <summary>
	/// Call when we need to close a plugin
	/// </summary>
	/// <param name="extension"></param>
	public static void UnregisterEditorPlugin(IEditorExtension extension)
	{
		plugins[_lastIndex] = null;
		
		if(_lastIndex > 0)
			_lastIndex--;
	}

	public static void DrawEditorPlugins()
	{
		for (int i = 0; i < plugins.Length; i++)
		{
			if(plugins[i].IsOpen)
				plugins[i].DrawWindow();
		}
	}
}