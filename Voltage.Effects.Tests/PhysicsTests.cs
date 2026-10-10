using Microsoft.Xna.Framework;
using NUnit.Framework;
using Voltage.Utils;

namespace Voltage.Effects.Tests;

[TestFixture, NonParallelizable]
public class PhysicsTests
{
    private float deltaTime;
    private Vector2 gravity;

    [SetUp]
    public void SetUp()
    {
        deltaTime = Time.DeltaTime;
        gravity = Physics.Gravity;
        Physics.Reset();
    }

    [TearDown]
    public void TearDown()
    {
        Physics.Clear();
        Time.DeltaTime = deltaTime;
        Physics.Gravity = gravity;
    }

    [TestCase(1f / 30f)]
    [TestCase(1f / 60f)]
    [TestCase(1f / 144f)]
    [TestCase(0.25f)]
    [TestCase(0f)]
    public void Instant_impulse_keeps_legacy_60_hz_strength_at_every_frame_duration(float duration)
    {
        Time.DeltaTime = duration;
        var body = new ArcadeRigidbody { Mass = 2, Velocity = new Vector2(3, 7) };
        var impulse = new Vector2(2, -4);
        body.AddImpulse(impulse);
        var expected = new Vector2(3, 7) + impulse * (100000f / (60f * 60f) / 2f);
        Assert.That(body.Velocity.X, Is.EqualTo(expected.X).Within(0.0001f));
        Assert.That(body.Velocity.Y, Is.EqualTo(expected.Y).Within(0.0001f));
    }

    [Test]
    public void Instant_impulse_obeys_mass_and_does_not_move_an_immovable_body()
    {
        var light = new ArcadeRigidbody { Mass = 1 };
        var heavy = new ArcadeRigidbody { Mass = 4 };
        var fixedBody = new ArcadeRigidbody { Mass = 0, Velocity = new Vector2(3, 7) };
        var impulse = new Vector2(2, -4);
        light.AddImpulse(impulse);
        heavy.AddImpulse(impulse);
        fixedBody.AddImpulse(impulse);
        Assert.That(heavy.Velocity, Is.EqualTo(light.Velocity / 4));
        Assert.That(fixedBody.Velocity, Is.EqualTo(new Vector2(3, 7)));
    }

    [Test]
    public void Normal_frame_movement_and_gravity_keep_existing_integration()
    {
        var body = CreateBody(new Vector2(10, 20));
        body.ShouldUseGravity = true;
        body.Velocity = new Vector2(6, -4);
        Physics.Gravity = new Vector2(0, 30);
        Time.DeltaTime = 1f / 60f;
        var expectedVelocity = body.Velocity + Physics.Gravity * Time.DeltaTime;
        var expectedPosition = body.Entity.Position + expectedVelocity * Time.DeltaTime;
        body.Update();
        Assert.That(body.Velocity, Is.EqualTo(expectedVelocity));
        Assert.That(body.Entity.Position, Is.EqualTo(expectedPosition));
    }

    [TestCase(1200f, 0.1f)]
    [TestCase(600f, 0.25f)]
    public void Fast_or_long_frame_fall_stops_on_a_one_pixel_floor(float speed, float duration)
    {
        var floor = CreateCollider(new RectangleF(-100, 40, 200, 1));
        var body = CreateBody(Vector2.Zero);
        body.Velocity = new Vector2(0, speed);
        Time.DeltaTime = duration;
        body.Update();
        Assert.That(body.Entity.GetComponent<Collider>().Bounds.Bottom, Is.EqualTo(floor.Bounds.Top).Within(0.0001f));
        Assert.That(body.Velocity.Y, Is.Zero.Within(0.0001f));
    }

    [Test]
    public void Direct_attack_movement_stops_on_thin_floor_without_extra_gravity()
    {
        var floor = CreateCollider(new RectangleF(-100, 40, 200, 1));
        var body = CreateBody(Vector2.Zero);
        body.ShouldUseGravity = true;
        Time.DeltaTime = 0.25f;
        body.MoveWithCollisions(new Vector2(0, 120));
        Assert.That(body.Entity.GetComponent<Collider>().Bounds.Bottom, Is.EqualTo(floor.Bounds.Top).Within(0.0001f));
        Assert.That(body.Velocity, Is.EqualTo(Vector2.Zero));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Fast_movement_respects_one_way_solid_face_and_keeps_unobstructed_endpoint(bool verticalWall)
    {
        var axis = verticalWall ? Vector2.UnitX : Vector2.UnitY;
        var rectangle = verticalWall ? new RectangleF(40, -100, 1, 200) : new RectangleF(-100, 40, 200, 1);
        var obstacle = CreateCollider(rectangle);
        obstacle.OneWayNormal = -axis;
        var body = CreateBody(axis * 80);
        Time.DeltaTime = 0.1f;
        body.Velocity = -axis * 600;
        body.Update();
        Assert.That(Vector2.Distance(body.Entity.Position, axis * 20), Is.LessThan(0.0001f));
        Assert.That(body.Velocity, Is.EqualTo(-axis * 600));
        body.Velocity = axis * 600;
        body.Update();
        var bounds = body.Entity.GetComponent<Collider>().Bounds;
        Assert.That(verticalWall ? bounds.Right : bounds.Bottom,
            Is.EqualTo(verticalWall ? obstacle.Bounds.Left : obstacle.Bounds.Top).Within(0.0001f));
    }

    [Test]
    public void One_way_floor_still_resolves_resting_overlap_and_allows_lateral_motion()
    {
        var floor = CreateCollider(new RectangleF(-100, 40, 200, 1));
        floor.OneWayNormal = -Vector2.UnitY;
        var body = CreateBody(new Vector2(0, 37));
        Time.DeltaTime = 0;
        body.Update();
        Assert.That(body.Entity.GetComponent<Collider>().Bounds.Bottom, Is.EqualTo(floor.Bounds.Top));
        Time.DeltaTime = 0.1f;
        body.Velocity = new Vector2(600, 0);
        body.Update();
        Assert.That(Vector2.Distance(body.Entity.Position, new Vector2(60, 36)), Is.LessThan(0.0001f));
    }

    [Test]
    public void Substeps_poll_trigger_lifecycle_once_per_frame_and_direct_moves_do_not_poll()
    {
        var trigger = CreateCollider(new RectangleF(-250, -250, 500, 500));
        trigger.IsTrigger = true;
        var listener = trigger.Entity.AddComponent(new TriggerCounter());
        var body = CreateBody(Vector2.Zero);
        body.Velocity = new Vector2(0, 600);
        Time.DeltaTime = 0.1f;
        body.Update();
        Assert.That(listener.Enters, Is.EqualTo(1));
        Assert.That(listener.Stays, Is.Zero);
        body.MoveWithCollisions(new Vector2(0, 20));
        Assert.That(listener.Enters, Is.EqualTo(1));
        Assert.That(listener.Stays, Is.Zero);
        Time.DeltaTime = 0;
        body.Update();
        Assert.That(listener.Stays, Is.EqualTo(1));
        body.Entity.Position = new Vector2(1000, 1000);
        body.Update();
        Assert.That(listener.Exits, Is.EqualTo(1));
    }

    private static Collider CreateCollider(RectangleF rectangle)
    {
        var entity = new Entity("Obstacle", Entity.InstanceType.NonSerialized);
        var collider = entity.AddComponent(new BoxCollider(rectangle));
        collider.OnStart();
        return collider;
    }

    private static ArcadeRigidbody CreateBody(Vector2 position)
    {
        var entity = new Entity("Body", Entity.InstanceType.NonSerialized) { Position = position };
        var collider = entity.AddComponent(new BoxCollider(8, 8));
        collider.OnStart();
        var body = entity.AddComponent(new ArcadeRigidbody { Mass = 1, Elasticity = 0, Friction = 0, ShouldUseGravity = false });
        body.OnStart();
        return body;
    }

    private sealed class TriggerCounter : Component, ITriggerListener
    {
        public int Enters, Stays, Exits;
        public void OnTriggerEnter(Collider other, Collider local) => Enters++;
        public void OnTriggerStay(Collider other, Collider local) => Stays++;
        public void OnTriggerExit(Collider other, Collider local) => Exits++;
    }
}
