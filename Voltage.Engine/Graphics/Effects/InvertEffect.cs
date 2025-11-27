using Microsoft.Xna.Framework.Graphics;


namespace Voltage
{
	public class InvertEffect : Effect
	{
		public InvertEffect() : base(Core.GraphicsDevice, EffectResource.InvertBytes)
		{
		}
	}
}