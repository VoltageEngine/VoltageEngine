using Microsoft.Xna.Framework;
using NUnit.Framework;
using Voltage.Data;
using Voltage.Persistence;

namespace Voltage.Effects.Tests;

[TestFixture, NonParallelizable]
public class PrefabSpawningTests
{
    [Test]
    public void Repeated_runtime_spawns_keep_unique_ids_and_their_own_nested_hierarchy()
    {
        if (!OperatingSystem.IsWindows() && Environment.GetEnvironmentVariable("VOLTAGE_TEST_GPU") != "1")
            Assert.Ignore("Requires a native desktop graphics session.");
        using var game = new PrefabGame();
        game.Run();
    }

    private sealed class PrefabGame : Core
    {
        public PrefabGame() : base(32, 32, windowTitle: "Runtime prefab check") { }

        protected override void Initialize()
        {
            base.Initialize();
            var scene = new Scene();
            var rootId = Guid.NewGuid();
            var childId = Guid.NewGuid();
            var grandchildId = Guid.NewGuid();
            var prefab = new PrefabData
            {
                Id = rootId, Name = "Actor",
                ChildEntities = new()
                {
                    new() { Id = childId, ParentId = rootId, InstanceType = Entity.InstanceType.Serialized, Name = "Muzzle", Position = new Vector2(12, -7) },
                    new() { Id = grandchildId, ParentId = childId, InstanceType = Entity.InstanceType.Serialized, Name = "Spark", Position = new Vector2(3, 2) }
                }
            };
            var projectRoot = Path.Combine(Path.GetTempPath(), "VoltageRuntimeAssets-" + Guid.NewGuid());
            var path = Path.Combine(projectRoot, "Actors", "Nested", "Actor.vprefab");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var previousRoot = Scene.ProjectDirectory;
            Scene.ProjectDirectory = projectRoot;
            try
            {
                File.WriteAllText(path, Json.ToJson(prefab));
                var ids = new HashSet<Guid> { rootId, childId, grandchildId };
                var names = new HashSet<string>();
                for (int i = 0; i < 3; i++)
                {
                    var position = new Vector2(100 + i * 200, 80);
                    var actor = scene.LoadPrefab("Actors/Nested/Actor.vprefab", position);
                    Assert.That(actor, Is.Not.Null);
                    Assert.That(actor.OriginalPrefabName, Is.EqualTo(Path.GetFileNameWithoutExtension(path)));
                    Assert.That(actor.Transform.ChildCount, Is.EqualTo(1));
                    var muzzle = actor.Transform.GetChild(0).Entity;
                    var spark = muzzle.Transform.GetChild(0).Entity;
                    Assert.That(actor.Position, Is.EqualTo(position));
                    Assert.That(muzzle.Transform.LocalPosition, Is.EqualTo(new Vector2(12, -7)));
                    Assert.That(spark.Transform.LocalPosition, Is.EqualTo(new Vector2(3, 2)));
                    Assert.That(spark.Position, Is.EqualTo(position + new Vector2(15, -5)));
                    foreach (var entity in new[] { actor, muzzle, spark })
                    {
                        Assert.That(entity.PersistentId, Is.Not.EqualTo(Guid.Empty));
                        Assert.That(ids.Add(entity.PersistentId), Is.True, "Each instance needs fresh IDs");
                        Assert.That(names.Add(entity.Name), Is.True, "Queued instances need unique names");
                        Assert.That(entity.Type, Is.EqualTo(Entity.InstanceType.NonSerialized));
                    }
                }
            }
            finally { Scene.ProjectDirectory = previousRoot; Directory.Delete(projectRoot, true); scene.Content.Dispose(); }
            Exit();
        }
    }
}
