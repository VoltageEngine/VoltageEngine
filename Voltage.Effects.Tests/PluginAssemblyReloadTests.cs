using System.Reflection;
using NUnit.Framework;
using Voltage.Editor.Plugins;
using Voltage.Editor.ProjectFile;
using Voltage.Persistence;

namespace Voltage.Effects.Tests;

[TestFixture, NonParallelizable]
public class PluginAssemblyReloadTests
{
    [Test]
    public void Same_plugin_can_reopen_in_another_project_without_stale_compiler_paths()
    {
        var root = Path.Combine(Path.GetTempPath(), "voltage-plugin-reload-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var manager = new PluginManager();
        var plugins = (List<PluginInstance>)typeof(PluginManager).GetField("_plugins", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(manager);
        var load = typeof(PluginManager).GetMethod("LoadGameplayAssemblies", BindingFlags.Instance | BindingFlags.NonPublic);
        PluginInstance Payload(string project, string id)
        {
            var path = Path.Combine(root, project, "PluginLibs", id);
            Directory.CreateDirectory(path);
            var assemblyName = Path.GetFileName(typeof(PluginAssemblyReloadTests).Assembly.Location);
            File.Copy(typeof(PluginAssemblyReloadTests).Assembly.Location, Path.Combine(path, assemblyName));
            return new PluginInstance { Entry = new() { Id = id }, PayloadPath = path, State = PluginState.Restored,
                Manifest = new() { Id = id, Kinds = new() { PluginManifest.KindGameplay }, Gameplay = new() {
                    ManagedAssemblies = new() { assemblyName }, RootTypes = new() { typeof(PluginAssemblyReloadTests).FullName } } } };
        }
        try
        {
            var first = Payload("first", "sample.plugin");
            plugins.Add(first);
            load.Invoke(manager, null);
            Assert.That(first.State, Is.EqualTo(PluginState.Loaded), first.Error);
            manager.OnProjectUnloaded();
            Assert.That(manager.GetEditorReferenceAssemblyPaths(), Is.Empty);
            var second = Payload("second", "sample.plugin");
            plugins.Add(second);
            load.Invoke(manager, null);
            Assert.That(second.State, Is.EqualTo(PluginState.Loaded), second.Error);
            Assert.That(second.StaleAssemblyWarning, Is.Null, "Identical copied binaries are reusable");
            var currentDll = Path.Combine(second.PayloadPath, second.Manifest.Gameplay.ManagedAssemblies[0]);
            Assert.That(manager.GetEditorReferenceAssemblyPaths(), Is.EquivalentTo(new[] { currentDll }));
            PluginSync.GenerateBuildFiles(Path.Combine(root, "second"), manager.Plugins);
            Assert.That(File.ReadAllText(Path.Combine(root, "second", "PluginLibs", "Plugins.g.props")), Does.Contain(second.Manifest.Gameplay.ManagedAssemblies[0]));
            using (var stream = File.Open(currentDll, FileMode.Append, FileAccess.Write)) stream.WriteByte(0);
            second.State = PluginState.Restored;
            load.Invoke(manager, null);
            Assert.That(second.State, Is.EqualTo(PluginState.Loaded));
            Assert.That(second.StaleAssemblyWarning, Does.Contain("Restart"));
            manager.OnProjectUnloaded();
            var collision = Payload("third", "different.plugin");
            plugins.Add(collision);
            load.Invoke(manager, null);
            Assert.That(collision.State, Is.EqualTo(PluginState.Failed));
            Assert.That(collision.Error, Does.Contain("another plugin"));
            Assert.That(manager.GetEditorReferenceAssemblyPaths(), Is.Empty);
            plugins.Clear();
            var engine = Payload("engine", "renamed.engine");
            var renamed = Path.Combine(engine.PayloadPath, "Renamed.dll");
            File.Copy(typeof(Entity).Assembly.Location, renamed);
            engine.Manifest.Gameplay.ManagedAssemblies = new() { "Renamed.dll" };
            plugins.Add(engine);
            load.Invoke(manager, null);
            Assert.That(engine.State, Is.EqualTo(PluginState.Failed));
            Assert.That(engine.Error, Does.Contain("engine assembly"));
            RuntimeGameProject Project(string name)
            {
                var projectRoot = Path.Combine(root, name);
                var package = Path.Combine(projectRoot, "Plugins", "sample");
                Directory.CreateDirectory(package);
                var assemblyName = Path.GetFileName(typeof(PluginAssemblyReloadTests).Assembly.Location);
                File.Copy(typeof(PluginAssemblyReloadTests).Assembly.Location, Path.Combine(package, assemblyName));
                var manifest = new PluginManifest { Id = "sample.plugin", Name = "Sample", Version = "1.0.0",
                    Kinds = new() { PluginManifest.KindGameplay }, Gameplay = new() {
                        ManagedAssemblies = new() { assemblyName }, RootTypes = new() { typeof(PluginAssemblyReloadTests).FullName } } };
                File.WriteAllText(Path.Combine(package, PluginManifest.FileName), Json.ToJson(manifest));
                new ProjectPluginsConfig { Plugins = new() { new() { Id = manifest.Id, Dev = true,
                    Source = new() { Path = "Plugins/sample" } } } }.SaveTo(projectRoot);
                return new RuntimeGameProject(new ProjectCreatorWindow.ProjectMetadata { ProjectName = name, ProjectPath = projectRoot });
            }
            var firstProject = Project("restored-first");
            var secondProject = Project("restored-second");
            var restored = new PluginManager();
            restored.RestoreForProject(firstProject);
            Assert.That(restored.FindById("sample.plugin").State, Is.EqualTo(PluginState.Loaded), restored.FindById("sample.plugin").Error);
            restored.OnProjectUnloaded();
            restored.RestoreForProject(secondProject);
            Assert.That(restored.FindById("sample.plugin").State, Is.EqualTo(PluginState.Loaded), restored.FindById("sample.plugin").Error);
            var props = File.ReadAllText(Path.Combine(secondProject.ProjectPath, "PluginLibs", "Plugins.g.props"));
            Assert.That(props, Does.Contain("Voltage.Effects.Tests.dll"));
            Assert.That(restored.GetEditorReferenceAssemblyPaths(), Is.Not.Empty);
            Assert.That(restored.GetEditorReferenceAssemblyPaths().All(path => path.StartsWith(secondProject.ProjectPath + Path.DirectorySeparatorChar)), Is.True);
            restored.OnProjectUnloaded();
        }
        finally
        {
            manager.OnProjectUnloaded();
            Directory.Delete(root, true);
        }
    }
}
