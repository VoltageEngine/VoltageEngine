using System.Reflection;
using Microsoft.Xna.Framework;
using NUnit.Framework;
using Voltage.Cinematics;
using Voltage.Serialization;

namespace Voltage.Effects.Tests;

[TestFixture]
public class TimelineReloadTests
{
    private string _id;

    [SetUp]
    public void RegisterFirstGeneration()
    {
        _id = nameof(TimelineReloadTests) + Guid.NewGuid();
        ComponentIdRegistry.Register(_id, typeof(OldActor));
        TimelinePropertyRegistry.RegisterFloat(_id, "Fade", c => ((OldActor)c).Fade, (c, v) => ((OldActor)c).Fade = v, typeof(OldActor));
        TimelinePropertyRegistry.RegisterVector2(_id, "Position", c => ((OldActor)c).Position, (c, v) => ((OldActor)c).Position = v, typeof(OldActor));
        TimelinePropertyRegistry.RegisterColor(_id, "Tint", c => ((OldActor)c).Tint, (c, v) => ((OldActor)c).Tint = v, typeof(OldActor));
        TimelineDispatch.Register(_id, "Cue", (c, _) => ((OldActor)c).Calls++, typeof(OldActor));
    }

    private void Reload()
    {
        ComponentIdRegistry.Register(_id, typeof(NewActor));
        TimelinePropertyRegistry.RegisterFloat(_id, "Fade", c => ((NewActor)c).Fade, (c, v) => ((NewActor)c).Fade = v, typeof(NewActor));
        TimelinePropertyRegistry.RegisterVector2(_id, "Position", c => ((NewActor)c).Position, (c, v) => ((NewActor)c).Position = v, typeof(NewActor));
        TimelinePropertyRegistry.RegisterColor(_id, "Tint", c => ((NewActor)c).Tint, (c, v) => ((NewActor)c).Tint = v, typeof(NewActor));
        TimelineDispatch.Register(_id, "Cue", (c, _) => ((NewActor)c).Calls++, typeof(NewActor));
    }

    private ITimelineContext Context(Component actor, bool initialized = false)
    {
        var entity = new Entity("actor");
        entity.AddComponent(actor);
        if (initialized) entity.Update();
        var director = new TimelineDirector();
        var roles = (Dictionary<string, Entity>)typeof(TimelineDirector).GetField("_resolvedRoles", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(director);
        roles["actor"] = entity;
        return director;
    }

    private TimelinePropertyTrack Track(string property) => new()
    {
        TargetRole = "actor", TargetComponentId = _id, Property = property,
        FloatKeys = new() { new() { Time = 0, Value = 0 } },
        Vector2Keys = new() { new() { Time = 0, Value = new(30, 40) } },
        ColorKeys = new() { new() { Time = 0, Value = Color.Red } }
    };

    [TestCase(false)]
    [TestCase(true)]
    public void Stable_identity_resolves_existing_actor_after_latest_type_changes(bool initialized)
    {
        var old = new OldActor();
        var context = Context(old, initialized);
        Reload();
        Assert.That(context.ResolveComponent("actor", _id), Is.SameAs(old));
        Assert.That(context.ResolveComponent("missing", _id), Is.Null);
        Assert.That(context.ResolveComponent("actor", "unknown"), Is.Null);
        Assert.That(Context(new NewActor()).ResolveComponent("actor", _id), Is.TypeOf<NewActor>());
    }

    [Test]
    public void Fade_camera_and_color_continue_on_both_generations_and_restore_their_own_snapshots()
    {
        var old = new OldActor { Fade = 1, Position = Vector2.One, Tint = Color.Blue };
        var oldContext = Context(old);
        var snapshot = new StateSnapshot();
        var tracks = new[] { Track("Fade"), Track("Position"), Track("Tint") };
        foreach (var track in tracks) track.CaptureRestoreState(snapshot, oldContext);
        Reload();
        var newer = new NewActor();
        var newContext = Context(newer);
        foreach (var context in new[] { oldContext, newContext })
            foreach (var track in tracks)
            {
                var problems = new List<string>();
                track.Validate(context, problems);
                Assert.That(problems, Is.Empty);
                track.Evaluate(0, context);
            }
        Assert.Multiple(() =>
        {
            Assert.That(old.Fade, Is.Zero); Assert.That(newer.Fade, Is.Zero);
            Assert.That(old.Position, Is.EqualTo(new Vector2(30, 40))); Assert.That(newer.Position, Is.EqualTo(old.Position));
            Assert.That(old.Tint, Is.EqualTo(Color.Red)); Assert.That(newer.Tint, Is.EqualTo(Color.Red));
        });
        snapshot.Restore();
        Assert.That(old.Fade, Is.EqualTo(1)); Assert.That(old.Position, Is.EqualTo(Vector2.One)); Assert.That(old.Tint, Is.EqualTo(Color.Blue));
        Assert.That(newer.Fade, Is.Zero);
        Assert.That(TimelinePropertyRegistry.Registered().Count(p => p.ComponentId == _id), Is.EqualTo(3));
    }

    [Test]
    public void Removed_or_changed_properties_do_not_use_another_generations_cast()
    {
        var old = new OldActor();
        var oldContext = Context(old);
        Reload();
        TimelinePropertyRegistry.RegisterVector2(_id, "Fade", c => ((NewActor)c).Position, (c, v) => ((NewActor)c).Position = v, typeof(NewActor));
        TimelinePropertyRegistry.RegisterFloat(_id, "Added", c => ((NewActor)c).Fade, (c, v) => ((NewActor)c).Fade = v, typeof(NewActor));
        old.Fade = 1;
        Track("Fade").Evaluate(0, oldContext);
        Assert.That(old.Fade, Is.Zero);
        Assert.DoesNotThrow(() => Track("Added").Evaluate(0, oldContext));
        Assert.That(TimelinePropertyRegistry.TryGetKind(_id, "Added", typeof(OldActor), out _), Is.False);
    }

    [Test]
    public void Events_dispatch_to_each_generation_and_reject_mismatched_targets_and_ids()
    {
        var old = new OldActor(); var newer = new NewActor();
        Reload();
        Assert.That(TimelineDispatch.TryInvoke(_id, "Cue", old, null), Is.True);
        Assert.That(TimelineDispatch.TryInvoke(_id, "Cue", newer, null), Is.True);
        Assert.That(old.Calls, Is.EqualTo(1)); Assert.That(newer.Calls, Is.EqualTo(1));
        Assert.That(TimelineDispatch.TryInvoke("unknown", "Cue", old, null), Is.False);
        Assert.That(TimelineDispatch.TryInvoke(_id, "Missing", old, null), Is.False);
        Assert.That(TimelineDispatch.TryInvoke(_id, "Cue", new UnrelatedActor(), null), Is.False);
        Assert.That(TimelineDispatch.TryInvoke(_id, "Cue", null, null), Is.False);
        Assert.That(TimelineDispatch.RegisteredMethods().Count(p => p.ComponentId == _id), Is.EqualTo(1));
    }

    [Test]
    public void Derived_targets_keep_inherited_accessors_and_event_dispatch()
    {
        var derived = new DerivedActor { Fade = 1 };
        Reload();
        Assert.That(TimelinePropertyRegistry.TryGetFloat(_id, "Fade", derived.GetType(), out _, out var set), Is.True);
        set(derived, 0);
        Assert.That(derived.Fade, Is.Zero);
        Assert.That(TimelineDispatch.TryInvoke(_id, "Cue", derived, null), Is.True);
        Assert.That(derived.Calls, Is.EqualTo(1));
    }

    [Test]
    public void Existing_registration_overloads_remain_compatible()
    {
        TimelinePropertyRegistry.RegisterFloat(_id, "Legacy", c => ((OldActor)c).Fade, (c, v) => ((OldActor)c).Fade = v);
        TimelineDispatch.Register(_id, "Legacy", (c, _) => ((OldActor)c).Calls++);
        var actor = new OldActor();
        Assert.That(TimelinePropertyRegistry.TryGetFloat(_id, "Legacy", typeof(OldActor), out _, out var set), Is.True);
        set(actor, 7);
        Assert.That(actor.Fade, Is.EqualTo(7));
        Assert.That(TimelineDispatch.TryInvoke(_id, "Legacy", actor, null), Is.True);
    }

    private partial class OldActor : Component { public float Fade; public Vector2 Position; public Color Tint; public int Calls; }
    private partial class NewActor : Component { public float Fade; public Vector2 Position; public Color Tint; public int Calls; }
    private partial class DerivedActor : OldActor { }
    private partial class UnrelatedActor : Component { }
}
