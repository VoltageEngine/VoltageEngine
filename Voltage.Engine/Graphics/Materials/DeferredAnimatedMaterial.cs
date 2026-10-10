using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Voltage.DeferredLighting;
using Voltage.Sprites;

namespace Voltage.Materials;

public class DeferredAnimatedMaterial : DeferredSpriteMaterial
{
    private readonly SpriteAnimator _animator;
    private readonly SpriteAnimation _normalMapAnimation;
    private readonly IReadOnlyDictionary<string, SpriteAnimation> _normalAnimations;

    public DeferredAnimatedMaterial(SpriteAnimator animator, SpriteAnimation normalMapAnimation)
        : base(normalMapAnimation.Sprites[0].Texture2D)
    {
        _animator = animator;
        _normalMapAnimation = normalMapAnimation;
    }

    public DeferredAnimatedMaterial(SpriteAnimator animator, IReadOnlyDictionary<string, SpriteAnimation> normalAnimations)
        : base(DeferredLightingRenderer.NullNormalMapTexture)
    {
        _animator = animator;
        _normalAnimations = normalAnimations;
    }

    public override void OnPreRender(Camera camera)
    {
        var animation = _normalMapAnimation;
        if (_normalAnimations != null && _animator.CurrentAnimationName != null)
            _normalAnimations.TryGetValue(_animator.CurrentAnimationName, out animation);
        var diffuse = _animator.Sprite;
        if (animation?.Sprites is not { Length: > 0 } || diffuse == null)
        {
            Effect.SetNormalMap(DeferredLightingRenderer.NullNormalMapTexture);
            Effect.SetNormalUvTransform(new Vector4(1, 1, 0, 0));
            Effect.SetNormalTransform(new Vector4(1, 0, 0, 1));
        }
        else
        {
            var normal = animation.Sprites[Math.Clamp(_animator.CurrentFrame, 0, animation.Sprites.Length - 1)];
            var d = diffuse.SourceRect;
            var n = normal.SourceRect;
            float scaleX = (float)n.Width * diffuse.Texture2D.Width / (normal.Texture2D.Width * d.Width);
            float scaleY = (float)n.Height * diffuse.Texture2D.Height / (normal.Texture2D.Height * d.Height);
            Effect.SetNormalMap(normal.Texture2D);
            Effect.SetNormalUvTransform(new Vector4(scaleX, scaleY,
                (float)n.X / normal.Texture2D.Width - (float)d.X / diffuse.Texture2D.Width * scaleX,
                (float)n.Y / normal.Texture2D.Height - (float)d.Y / diffuse.Texture2D.Height * scaleY));
            float flipX = _animator.FlipX ? -1 : 1;
            float flipY = _animator.FlipY ? -1 : 1;
            var scale = _animator.Entity?.Scale ?? Vector2.One;
            if (scale.X < 0) flipX = -flipX;
            if (scale.Y < 0) flipY = -flipY;
            float cosine = MathF.Cos(_animator.RenderRotation), sine = MathF.Sin(_animator.RenderRotation);
            Effect.SetNormalTransform(new Vector4(cosine * flipX, sine * flipY, -sine * flipX, cosine * flipY));
        }
        base.OnPreRender(camera);
    }
}
