using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using NUnit.Framework;
using static Voltage.Editor.Tests.EditorSession;

namespace Voltage.Editor.Tests;

[TestFixture, NonParallelizable]
public class AssetGuidTests
{
    [Test]
    public void Rename_outside_the_editor_keeps_the_guid_after_a_restart()
    {
        var folder = NewFolder("Rename");
        try
        {
            var original = NewAsset(folder, "Probe.txt");
            var guid = GuidOf(original);

            var renamed = Path.Combine(folder, "Renamed.txt");
            File.Move(original, renamed);
            WaitFor(() => File.Exists(renamed + ".meta"), "the .meta to follow the rename");
            Assert.That(File.Exists(original + ".meta"), Is.False);

            Restart();
            Assert.That(GuidOf(renamed), Is.EqualTo(guid));
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    /// <summary>Windows reports a move to another folder as Created + Deleted, which a copy then delete reproduces on every OS.</summary>
    [TestCase(true), TestCase(false)]
    public void Move_to_another_folder_outside_the_editor_keeps_the_guid_after_a_restart(bool asCopyThenDelete)
    {
        var folder = NewFolder("Move");
        try
        {
            var inner = Directory.CreateDirectory(Path.Combine(folder, "Inner")).FullName;
            var original = NewAsset(folder, "Probe.txt");
            var guid = GuidOf(original);

            var moved = Path.Combine(inner, "Probe.txt");
            if (asCopyThenDelete)
            {
                File.Copy(original, moved);
                File.Delete(original);
            }
            else
            {
                File.Move(original, moved);
            }

            WaitFor(() => File.Exists(moved + ".meta"), "the .meta to follow the move");
            Assert.That(MetaGuid(moved), Is.EqualTo(guid));

            Restart();
            Assert.That(GuidOf(moved), Is.EqualTo(guid));
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Test]
    public void Copying_an_asset_with_its_meta_gives_only_the_copy_a_new_guid()
    {
        var folder = NewFolder("Copy");
        try
        {
            var original = NewAsset(folder, "Probe.txt");
            var guid = GuidOf(original);

            var copy = Path.Combine(folder, "Probe Copy.txt");
            File.Copy(original + ".meta", copy + ".meta");
            File.Copy(original, copy);
            WaitFor(() => MetaGuid(copy) != guid, "the copy to get its own GUID");

            Assert.That(GuidOf(original), Is.EqualTo(guid));
            Assert.That(GuidOf(copy), Is.EqualTo(MetaGuid(copy)));
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Test]
    public void Copies_made_while_the_editor_is_closed_leave_the_guid_with_the_original()
    {
        var folder = NewFolder("ClosedCopy");
        try
        {
            var original = NewAsset(folder, "Probe.txt");
            var guid = GuidOf(original);
            Call("asset.refresh");

            var copies = new[] { "A Probe.txt", "Z Probe.txt" }.Select(name => Path.Combine(folder, name)).ToArray();
            Restart(() =>
            {
                foreach (var copy in copies)
                {
                    File.Copy(original, copy);
                    File.Copy(original + ".meta", copy + ".meta");
                }
            });

            Assert.That(GuidOf(original), Is.EqualTo(guid));
            Assert.That(MetaGuid(original), Is.EqualTo(guid));
            var copyGuids = copies.Select(GuidOf).ToArray();
            Assert.That(copyGuids, Does.Not.Contain(guid));
            Assert.That(copyGuids.Distinct().Count(), Is.EqualTo(copies.Length));
            Assert.That(copies.Select(MetaGuid), Is.EqualTo(copyGuids));
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    private static string NewFolder(string name)
    {
        var folder = Path.Combine(Path.GetDirectoryName(ProjectFile), "Content", $"GuidTest{name}{System.Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        return folder;
    }

    /// <summary>Indexes a new file, then waits out the watcher's debounce so the next edit lands in a batch of its own.</summary>
    private static string NewAsset(string folder, string name)
    {
        var path = Path.Combine(folder, name);
        File.WriteAllText(path, name);
        Call("asset.refresh");
        Thread.Sleep(1500);
        return path;
    }

    private static Guid GuidOf(string path) =>
        System.Guid.Parse(Call("asset.meta", new { path }).GetProperty("guid").GetString());

    private static Guid MetaGuid(string path)
    {
        try
        {
            using var meta = JsonDocument.Parse(File.ReadAllText(path + ".meta"));
            return System.Guid.Parse(meta.RootElement.GetProperty("Guid").GetString());
        }
        catch (IOException)
        {
            return System.Guid.Empty;
        }
        catch (JsonException)
        {
            return System.Guid.Empty;
        }
    }

    private static void WaitFor(Func<bool> condition, string what, int timeoutMs = 10_000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                Assert.Fail($"timed out waiting for {what}");
            Thread.Sleep(100);
        }
    }
}
