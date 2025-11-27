using System.Collections.Generic;

namespace Voltage.Tiled
{
	public interface ITmxLayer : ITmxElement
	{
		float OffsetX { get; }
		float OffsetY { get; }
		float Opacity { get; }
		bool Visible { get; }
		float ParallaxFactorX { get; }
		float ParallaxFactorY { get; }
		Dictionary<string, string> Properties { get; }
	}
}