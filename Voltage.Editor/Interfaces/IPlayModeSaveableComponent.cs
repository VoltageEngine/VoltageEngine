namespace Voltage.Editor.Interfaces
{
    /// <summary>
    /// Interface to mark components that should be saved during PlayMode.
    /// Components implementing this interface will have their data saved even when ignoreEntityTransform is true.
    /// </summary>
    public interface IPlayModeSaveableComponent
    {
    }
}