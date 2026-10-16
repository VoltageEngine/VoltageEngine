using Microsoft.Xna.Framework;
using NUnit.Framework;
using Voltage.Spatial;

namespace Voltage.Effects.Tests;

[TestFixture, NonParallelizable]
public class SpatialHashTests
{
    [TestCase(false, false, false)]
    [TestCase(false, false, true)]
    [TestCase(false, true, false)]
    [TestCase(false, true, true)]
    [TestCase(true, false, false)]
    [TestCase(true, false, true)]
    [TestCase(true, true, false)]
    [TestCase(true, true, true)]
    public void Shallow_ray_in_one_row_or_column_finishes_and_still_hits(bool transpose, bool reverse, bool mirror)
    {
        var start = new Vector2(870, -292);
        var end = new Vector2(900, -300);
        if (transpose) { start = new Vector2(start.Y, start.X); end = new Vector2(end.Y, end.X); }
        if (mirror) { start = -start; end = -end; }
        if (reverse) (start, end) = (end, start);
        var hash = new SpatialHash();
        var hits = new RaycastHit[1];
        Assert.That(hash.Linecast(start, end, hits, Physics.AllLayers), Is.Zero);
        var collider = new Entity("Target", Entity.InstanceType.NonSerialized) { Position = end }
            .AddComponent(new BoxCollider(2, 2));
        hash.Register(collider);
        Assert.That(hash.Linecast(start, end, hits, Physics.AllLayers), Is.EqualTo(1));
        Assert.That(hits[0].Collider, Is.SameAs(collider));
    }

    [Test]
    public void Multi_cell_ray_keeps_hit_order_and_layer_filtering()
    {
        var hash = new SpatialHash();
        var first = new Entity("Near", Entity.InstanceType.NonSerialized) { Position = new Vector2(120, 30) }
            .AddComponent(new BoxCollider(4, 24));
        var second = new Entity("Far", Entity.InstanceType.NonSerialized) { Position = new Vector2(220, 30) }
            .AddComponent(new BoxCollider(4, 24));
        first.Layer = 1;
        second.Layer = 2;
        hash.Register(second);
        hash.Register(first);
        var hits = new RaycastHit[2];
        Assert.That(hash.Linecast(new Vector2(20, 20), new Vector2(280, 40), hits, Physics.AllLayers), Is.EqualTo(2));
        Assert.That(hits[0].Collider, Is.SameAs(first));
        Assert.That(hits[1].Collider, Is.SameAs(second));
        Assert.That(hash.Linecast(new Vector2(20, 20), new Vector2(280, 40), hits, 2), Is.EqualTo(1));
        Assert.That(hits[0].Collider, Is.SameAs(second));
    }
}
