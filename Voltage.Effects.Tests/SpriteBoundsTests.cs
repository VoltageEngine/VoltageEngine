using System.Runtime.CompilerServices;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using NUnit.Framework;
using Voltage.Sprites;
using Voltage.Textures;

namespace Voltage.Effects.Tests;

[TestFixture]
public class SpriteBoundsTests
{
    [Test]
    public void Sprite_set_after_an_empty_bounds_read_gets_real_bounds()
    {
        var texture = (Texture2D)RuntimeHelpers.GetUninitializedObject(typeof(Texture2D));
        var entity = new Entity("actor") { Position = new Vector2(825, 265) };
        // A saved custom origin that matches the sprite's own, as on a disabled cutscene actor.
        var animator = entity.AddComponent(new SpriteAnimator { Origin = new Vector2(4, 4) });
        // Editor picking reads bounds of disabled actors whose animator never started.
        Assert.That(animator.Bounds.Width, Is.Zero);
        animator.AddAnimation("Clip", new[] { new Sprite(texture, new Rectangle(0, 0, 8, 8)) }, 10);
        Assert.That(animator.Bounds, Is.EqualTo(new RectangleF(821, 261, 8, 8)));
    }
}
