using Microsoft.Xna.Framework.Graphics;
using Nez.DeferredLighting;

namespace Nez.Materials
{
	public class DeferredSpriteMaterial : Material<DeferredSpriteEffect>
	{
		protected Texture2D NormalMap;

		/// <summary>
		/// DeferredSpriteEffects require a normal map. If you want to forego the normal map and have just diffuse light use the
		/// DeferredLightingRenderer.nullNormalMapTexture.
		/// </summary>
		/// <param name="normalMap">Normal map.</param>
		public DeferredSpriteMaterial(Texture2D normalMap)
		{
			NormalMap = normalMap;
			BlendState = BlendState.Opaque;
			Effect = new DeferredSpriteEffect().SetNormalMap(normalMap);
		}
	}
}