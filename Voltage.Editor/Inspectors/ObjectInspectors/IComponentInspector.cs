using Voltage;

namespace Voltage.Editor.Inspectors.ObjectInspectors
{
	public interface IComponentInspector
	{
		Entity Entity { get; }
		Component Component { get; }

		void Draw();
	}
}