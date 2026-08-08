namespace Voltage
{
	/// <summary>
	/// Marks a plain class as serializable data, so the source generator emits a reflection-free reader for it.
	/// </summary>
	public interface ISerializableData
	{
	}

	/// <summary>
	/// Marks a class as a structured component group, rendered as a
	/// collapsible section in the Editor inspector — analogous to a struct category.
	/// </summary>
	public interface IComponentGroup : ISerializableData
	{
	}
}
