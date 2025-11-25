using Microsoft.Xna.Framework.Graphics;


namespace Voltage
{
	public class GrayscaleEffect : Effect
	{
		public GrayscaleEffect() : base(Core.GraphicsDevice, EffectResource.GrayscaleBytes)
		{
		}
	}
}