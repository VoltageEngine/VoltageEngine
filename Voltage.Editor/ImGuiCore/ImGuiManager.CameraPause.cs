namespace Voltage.Editor.ImGuiCore;

public partial class ImGuiManager
{
	private bool _editorOwnsCameraPause;

	/// <summary>Pauses from editor controls while preserving the current gameplay camera position.</summary>
	internal void SetEditorPause(bool paused)
	{
		_editorOwnsCameraPause = paused;
		_cameraTargetPosition = Core.Scene.Camera.Position;
		Core.InvokeSwitchPauseMode(paused);
	}
}
