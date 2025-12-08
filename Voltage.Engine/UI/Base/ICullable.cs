using Microsoft.Xna.Framework;


namespace Voltage.UI
{
	public interface ICullable
	{
		void SetCullingArea(Rectangle cullingArea);
	}
}