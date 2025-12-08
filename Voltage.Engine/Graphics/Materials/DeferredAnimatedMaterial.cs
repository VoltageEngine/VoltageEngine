using Voltage.Textures;
using Voltage.Sprites;

namespace Voltage.Materials
{
	public class DeferredAnimatedMaterial : DeferredSpriteMaterial
	{
		private SpriteAnimator _animator;
		private SpriteAnimation _normalMapAnimation;

		public DeferredAnimatedMaterial(SpriteAnimator animator, SpriteAnimation normalMapAnimation)
			: base(normalMapAnimation.Sprites[0].Texture2D)
		{
			_animator = animator;
			_normalMapAnimation = normalMapAnimation;
		}

		public override void OnPreRender(Camera camera)
		{
			// Sync normal map frame with main animation frame
			int frame = _animator.CurrentFrame;
			if (frame >= 0 && frame < _normalMapAnimation.Sprites.Length)
				NormalMap = _normalMapAnimation.Sprites[frame].Texture2D;
			else
				NormalMap = _normalMapAnimation.Sprites[0].Texture2D;

			base.OnPreRender(camera);
		}
	}
}
