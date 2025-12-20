namespace Voltage.Editor.Interfaces
{
	/// <summary>
	/// Interface to mark components that should be saved during PlayMode.
	/// Components implementing this interface will have their data saved even when "bool ignoreEntityTransform" in DataLoader.SaveSceneDataAsync() true.
	/// </summary>
	public interface IPlayModeSaveableComponent
    {
    }
}