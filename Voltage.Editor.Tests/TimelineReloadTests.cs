using System;
using System.IO;
using System.Text.Json;
using NUnit.Framework;
using static Voltage.Editor.Tests.EditorSession;

namespace Voltage.Editor.Tests;

[TestFixture, NonParallelizable]
public class TimelineReloadTests
{
    [Test]
    public void Generated_timeline_bindings_survive_compilation_in_play_and_a_fresh_scene_load()
    {
        Call("scene.load", new { name = "TestScene", force = true });
        var path = Path.Combine(Path.GetDirectoryName(ProjectFile), "Scripts", "TimelineReloadProbe.cs");
        var source = """
            using System.Collections.Generic;
            using Microsoft.Xna.Framework;
            using Voltage;
            using Voltage.Cinematics;
            using Voltage.Serialization;
            public partial class TimelineReloadProbe : Component, IUpdatable
            {
                [TimelineProperty] public float Fade = 1;
                [TimelineProperty] public Vector2 Center;
                [TimelineProperty] public Color Tint;
                public bool Run;
                public string Result = "Waiting";
                public int Calls;
                private TimelineDirector _director;
                [TimelineEvent] public void Cue() { Calls++; }
                public override void OnStart()
                {
                    var id = ComponentIdRegistry.GetIdForType(GetType());
                    _director = Entity.AddComponent(new TimelineDirector());
                    _director.Bindings.Add(new RoleBinding { Role = "actor", Entity = EntityReference.From(Entity) });
                    var asset = new TimelineAsset();
                    asset.ParameterTracks.Add(new TimelinePropertyTrack { TargetRole = "actor", TargetComponentId = id, Property = "Fade",
                        FloatKeys = new() { new() { Time = 0, Value = 0 } } });
                    asset.ParameterTracks.Add(new TimelinePropertyTrack { TargetRole = "actor", TargetComponentId = id, Property = "Center",
                        Vector2Keys = new() { new() { Time = 0, Value = new(17, 23) } } });
                    asset.ParameterTracks.Add(new TimelinePropertyTrack { TargetRole = "actor", TargetComponentId = id, Property = "Tint",
                        ColorKeys = new() { new() { Time = 0, Value = Color.Red } } });
                    asset.Events.Add(new TimelineEventClip { Time = 0, TargetRole = "actor", TargetComponentId = id, BeginMethod = "Cue" });
                    _director.SetAsset(asset);
                }
                public void Update()
                {
                    if (!Run || Core.IsEditMode) return;
                    Run = false;
                    _director.Play();
                    Result = Fade == 0 && Center == new Vector2(17, 23) && Tint == Color.Red && Calls > 0 ? "Passed" : "Failed";
                    _director.Stop();
                }
            }
            """;
        try
        {
            File.WriteAllText(path, source);
            Compile();
            Call("entity.create", new { name = "Timeline reload probe" });
            Call("component.add", new { entity = "Timeline reload probe", type = "TimelineReloadProbe" });
            Call("scene.save");
            Call("play.start");
            RunProbe();
            Compile();
            RunProbe();
            Call("play.stop");
            Call("scene.load", new { name = "TestScene", force = true });
            Call("play.start");
            RunProbe();
        }
        finally
        {
            Call("play.stop");
            Call("scene.load", new { name = "TestScene", force = true });
            Call("entity.delete", new { entity = "Timeline reload probe" });
            Call("scene.save");
            File.Delete(path);
        }
    }

    private static void Compile()
    {
        var result = Call("scripts.compile", new { reloadScene = false });
        Assert.That(result.GetProperty("success").GetBoolean(), Is.True, result.ToString());
    }

    private static void RunProbe()
    {
        Call("component.set", new { entity = "Timeline reload probe", type = "TimelineReloadProbe", member = "Result", value = "Waiting" });
        Call("component.set", new { entity = "Timeline reload probe", type = "TimelineReloadProbe", member = "Run", value = true });
        var deadline = DateTime.UtcNow.AddSeconds(5);
        JsonElement values;
        do
        {
            values = Call("component.get", new { entity = "Timeline reload probe", type = "TimelineReloadProbe" }).GetProperty("values");
            if (DateTime.UtcNow > deadline) Assert.Fail("Probe did not update: " + values);
        } while (values.GetProperty("Result").GetString() == "Waiting");
        Assert.That(values.GetProperty("Result").GetString(), Is.EqualTo("Passed"), values.ToString());
    }
}
