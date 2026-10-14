using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using NUnit.Framework;
using Voltage.Cinematics;
using Voltage.Serialization;
using Voltage.Sprites;
using Voltage.Textures;

namespace Voltage.Effects.Tests;

[TestFixture, NonParallelizable]
public class TimelinePreviewTests
{
    [Test]
    public void Sampling_restores_all_visual_state_and_never_runs_gameplay_callbacks()
    {
        if (!OperatingSystem.IsWindows() && Environment.GetEnvironmentVariable("VOLTAGE_TEST_GPU") != "1")
            Assert.Ignore("Requires a native graphics session.");
        using var game = new PreviewGame();
        game.Run();
    }

    public sealed class Participant : Component, ITimelinePreviewParticipant
    {
        public float VisibleValue = 7;
        public float Fade = .8f;
        public TimelineDirector EvaluatedDirector;
        public int LifecycleCalls;
        public override void OnEnabled() => LifecycleCalls++;
        public override void OnDisabled() => LifecycleCalls++;
        public void CaptureTimelinePreview(StateSnapshot snapshot) => snapshot.Capture(VisibleValue, v => VisibleValue = v);
        public void EvaluateTimelinePreview(float time, TimelineAsset asset, TimelineDirector director)
        {
            VisibleValue = time;
            EvaluatedDirector = director;
        }
    }

    private sealed class UnsupportedTrack : TimelineParameterTrack
    {
        public int Calls;
        public override void Evaluate(float time, ITimelineContext context) => Calls++;
    }

    private sealed class PreviewGame : Core
    {
        public PreviewGame() : base(32, 32, windowTitle: "Timeline preview regression") { }

        protected override void Initialize()
        {
            base.Initialize();
            var scene = new Scene();
            Core.Scene = scene;
            var actor = scene.SimpleCreateEntity("actor", Entity.InstanceType.Serialized);
            actor.Position = new Vector2(4, 9);
            var participant = actor.AddComponent(new Participant());
            const string participantId = "preview_test_actor";
            ComponentIdRegistry.Register(participantId, typeof(Participant));
            TimelinePropertyRegistry.RegisterFloat(participantId, "Fade", c => ((Participant)c).Fade,
                (c, value) => ((Participant)c).Fade = value, typeof(Participant));
            using var texture = new Texture2D(GraphicsDevice, 3, 1);
            var animator = actor.AddComponent(new SpriteAnimator());
            animator.AddAnimation("walk", new[] { new Sprite(texture, new Rectangle(0, 0, 1, 1)),
                new Sprite(texture, new Rectangle(1, 0, 1, 1)), new Sprite(texture, new Rectangle(2, 0, 1, 1)) }, 10);
            animator.Play("walk");
            animator.SetFrame(2);
            animator.Pause();
            var cameraEntity = scene.SimpleCreateEntity("shot", Entity.InstanceType.Serialized);
            var camera = cameraEntity.AddComponent(new Camera());
            camera.RawZoom = 2;
            var owner = scene.SimpleCreateEntity("sequence", Entity.InstanceType.Serialized);
            var director = owner.AddComponent(new TimelineDirector());
            scene.Update();
            animator.Origin = new Vector2(7, 11);
            director.Bindings.Add(new() { Role = "actor", Entity = new() { EntityPersistentId = actor.PersistentId.ToString(), EntityName = actor.Name } });
            director.Bindings.Add(new() { Role = "shot", Entity = new() { EntityPersistentId = cameraEntity.PersistentId.ToString(), EntityName = cameraEntity.Name } });
            Assert.That(director.Entity.Scene, Is.SameAs(scene));
            Assert.That(director.Bindings.Count, Is.EqualTo(2));
            Assert.That(scene.FindEntityByPersistentId(actor.PersistentId), Is.SameAs(actor));
            var calls = participant.LifecycleCalls;
            var originalSprite = animator.Sprite;
            var originalFrameLeft = animator.FrameTimeLeft;
            var unsupported = new UnsupportedTrack();
            var asset = new TimelineAsset
            {
                Duration = 2,
                ParameterTracks = new()
                {
                    new TimelineTransformTrack { TargetRole = "actor", Position = new() {
                        new() { Time = 0, Value = Vector2.Zero }, new() { Time = 2, Value = new Vector2(20, 10) } } },
                    new TimelineCameraTrack { TargetRole = "shot", Zoom = new() { new() { Time = 0, Value = 4 } } },
                    new TimelineActivationTrack { TargetRole = "actor", Ranges = new() { new() { Time = 0, Duration = 1 } } },
                    new TimelineSpriteTrack { TargetRole = "actor", Clips = new() { new() { Time = 0, Duration = 1, Animation = "walk" } } },
                    new TimelinePropertyTrack { TargetRole = "actor", TargetComponentId = participantId, Property = "Fade",
                        FloatKeys = new() { new() { Time = 0, Value = .25f } } },
                    unsupported
                },
                Events = new() { new() { Time = 0, BroadcastMessage = "gameplay" } },
                SpawnClips = new() { new() { Time = 0, Duration = 2, SpawnRole = "spawn" } }
            };
            director.SetAsset(new TimelineAsset { Duration = 19 });
            var originalAsset = director.Asset;
            var signals = 0;
            director.OnSignal += (_, _) => signals++;
            using (var preview = new TimelinePreviewSession(director, asset))
            {
                Assert.That(preview.Scene, Is.SameAs(scene));
                Assert.That(director.Bindings[0].Entity.GetPersistentId(), Is.EqualTo(actor.PersistentId));
                preview.Evaluate(.15f);
                Assert.That(preview.ResolveRole("actor"), Is.SameAs(actor), "Persistent actor binding");
                Assert.That(animator.Animations.ContainsKey("walk"), Is.True, "Loaded test animation");
                Assert.That(animator.CurrentFrame, Is.EqualTo(1));
                Assert.That(actor.Position.X, Is.EqualTo(1.5f).Within(.001));
                preview.Evaluate(.15f);
                Assert.That(actor.Position.X, Is.EqualTo(1.5f).Within(.001), "Holding the same sample survives baseline restore and world-cache reads");
                preview.Evaluate(.15f);
                Assert.That(actor.Position.X, Is.EqualTo(1.5f).Within(.001));
                Assert.That(participant.VisibleValue, Is.EqualTo(.15f));
                Assert.That(participant.EvaluatedDirector, Is.SameAs(director), "Separate bound participant receives the owning director");
                Assert.That(participant.Fade, Is.EqualTo(.25f));
                Assert.That(camera.RawZoom, Is.EqualTo(4));
                preview.Evaluate(1.5f);
                Assert.That(actor.Enabled, Is.False);
                Assert.That(animator.CurrentFrame, Is.EqualTo(2), "Leaving a clip restores its original frame");
                preview.Evaluate(.05f);
                Assert.That(actor.Enabled, Is.True);
                Assert.That(animator.CurrentFrame, Is.Zero);
                Assert.That(preview.UnsupportedEffects.Count, Is.EqualTo(3));
                actor.Name = "renamed actor";
                preview.Evaluate(.15f);
                Assert.That(preview.ResolveRole("actor"), Is.SameAs(actor), "Renames retain GUID bindings");
                director.Bindings[0].Entity = new() { EntityPersistentId = Guid.NewGuid().ToString(), EntityName = actor.Name };
                preview.Evaluate(.5f);
                Assert.That(preview.ResolveRole("actor"), Is.Null, "A deleted GUID must not rebind by duplicate name");
                Assert.That(actor.Position, Is.EqualTo(new Vector2(4, 9)), "Unbinding restores previous actor");
                Assert.That(participant.Fade, Is.EqualTo(.8f));
            }
            Assert.That(actor.Position, Is.EqualTo(new Vector2(4, 9)));
            Assert.That(actor.Enabled, Is.True);
            Assert.That(camera.RawZoom, Is.EqualTo(2));
            Assert.That(participant.VisibleValue, Is.EqualTo(7));
            Assert.That(participant.Fade, Is.EqualTo(.8f));
            Assert.That(participant.LifecycleCalls, Is.EqualTo(calls));
            Assert.That(animator.CurrentFrame, Is.EqualTo(2));
            Assert.That(animator.Sprite, Is.SameAs(originalSprite));
            Assert.That(animator.Origin, Is.EqualTo(new Vector2(7, 11)), "Restore preserves custom origin after SetSprite");
            Assert.That(animator.FrameTimeLeft, Is.EqualTo(originalFrameLeft));
            Assert.That(animator.AnimationState, Is.EqualTo(SpriteAnimator.State.Paused));
            Assert.That(signals, Is.Zero);
            Assert.That(unsupported.Calls, Is.Zero);
            Assert.That(director.Asset, Is.SameAs(originalAsset));
            Assert.That(director.State, Is.EqualTo(DirectorState.Stopped));
            var snapshot = new StateSnapshot();
            animator.CapturePreviewState(snapshot);
            Assert.That(animator.SamplePreview("walk", SpriteAnimator.LoopMode.PingPong, .35f), Is.True);
            Assert.That(animator.CurrentFrame, Is.EqualTo(1));
            Assert.That(animator.SamplePreview("walk", SpriteAnimator.LoopMode.ClampForever, 10), Is.True);
            Assert.That(animator.CurrentFrame, Is.EqualTo(2));
            snapshot.Restore();
            Assert.That(animator.CurrentFrame, Is.EqualTo(2));
            Assert.That(animator.Origin, Is.EqualTo(new Vector2(7, 11)));
            Assert.That(animator.FrameTimeLeft, Is.EqualTo(originalFrameLeft));
            CheckChildBeforeParent(scene, texture);
            Exit();
        }

        private static void CheckChildBeforeParent(Scene scene, Texture2D texture)
        {
            var parent = scene.SimpleCreateEntity("preview parent", Entity.InstanceType.Serialized);
            parent.Transform.LocalPosition = new Vector2(100, 50);
            parent.Transform.LocalRotation = .2f;
            parent.Transform.LocalScale = new Vector2(2, 3);
            var child = scene.SimpleCreateEntity("preview child", Entity.InstanceType.Serialized);
            child.Transform.Parent = parent.Transform;
            child.Transform.LocalPosition = new Vector2(7, 11);
            child.Transform.LocalRotation = .3f;
            child.Transform.LocalScale = new Vector2(.5f, .75f);
            var sprite = child.AddComponent(new SpriteAnimator(new Sprite(texture)));
            var director = parent.AddComponent(new TimelineDirector());
            scene.Update();
            director.Bindings.Add(new() { Role = "child", Entity = new() { EntityPersistentId = child.PersistentId.ToString() } });
            director.Bindings.Add(new() { Role = "parent", Entity = new() { EntityPersistentId = parent.PersistentId.ToString() } });
            var originalPosition = child.Position;
            var originalRotation = child.Rotation;
            var originalScale = child.Scale;
            var asset = new TimelineAsset { Duration = 2, ParameterTracks = new()
            {
                new TimelineTintTrack { TargetRole = "child", Tint = new() { new() { Time = 0, Value = Color.Red } } },
                new TimelineTransformTrack { TargetRole = "parent", Position = new() {
                    new() { Time = 0, Value = new Vector2(150, 80) }, new() { Time = 2, Value = new Vector2(200, 90) } },
                    Rotation = new() { new() { Time = 0, Value = .7f } }, Scale = new() { new() { Time = 0, Value = Vector2.One } } }
            } };
            using (var preview = new TimelinePreviewSession(director, asset))
            {
                preview.Evaluate(1.5f);
                preview.Evaluate(.25f);
                preview.Evaluate(1.5f);
            }
            Assert.That(child.Transform.LocalPosition, Is.EqualTo(new Vector2(7, 11)));
            Assert.That(child.Transform.LocalRotation, Is.EqualTo(.3f));
            Assert.That(child.Transform.LocalScale, Is.EqualTo(new Vector2(.5f, .75f)));
            Assert.That(Vector2.Distance(child.Position, originalPosition), Is.LessThan(.001f));
            Assert.That(child.Rotation, Is.EqualTo(originalRotation).Within(.001f));
            Assert.That(child.Scale, Is.EqualTo(originalScale));
            Assert.That(parent.Position, Is.EqualTo(new Vector2(100, 50)));
            Assert.That(parent.Rotation, Is.EqualTo(.2f));
            Assert.That(sprite.Color, Is.EqualTo(Color.White));
        }
    }
}
