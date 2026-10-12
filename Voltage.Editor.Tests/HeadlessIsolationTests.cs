using System;
using System.IO;
using NUnit.Framework;
using Voltage.Cli;

namespace Voltage.Editor.Tests;

[TestFixture]
public class HeadlessIsolationTests
{
    [Test]
    public void Running_headless_editor_uses_private_binaries_and_retains_the_original_build_path()
    {
        var info = GatewayInfo.Read(EditorSession.InfoPath);
        Assert.That(info.SourceExe, Is.EqualTo(EditorSession.EditorExe));
        Assert.That(info.Exe, Is.Not.EqualTo(EditorSession.EditorExe));
        Assert.That(File.Exists(Path.Combine(Path.GetDirectoryName(info.Exe), "Voltage.dll")), Is.True);
        Assert.That(EditorSession.Call("status").GetProperty("headless").GetBoolean(), Is.True);
    }

    [Test]
    public void Snapshot_copies_runtime_content_and_releases_only_its_own_directory()
    {
        var root = Path.Combine(Path.GetTempPath(), "voltage-snapshot-test-" + Guid.NewGuid().ToString("N"));
        var source = Path.Combine(root, "source");
        Directory.CreateDirectory(Path.Combine(source, "Content"));
        var exe = Path.Combine(source, "Voltage.Editor.exe");
        File.WriteAllText(exe, "executable");
        File.WriteAllText(Path.Combine(source, "Content", "font.fnt"), "font");
        try
        {
            string copy;
            using (var snapshot = EditorBinarySnapshot.Create(exe, Path.Combine(root, "copy")))
            {
                copy = snapshot.Executable;
                Assert.That(File.ReadAllText(copy), Is.EqualTo("executable"));
                Assert.That(File.ReadAllText(Path.Combine(Path.GetDirectoryName(copy), "Content", "font.fnt")), Is.EqualTo("font"));
                File.SetAttributes(Path.Combine(Path.GetDirectoryName(copy), "Content", "font.fnt"), FileAttributes.ReadOnly);
                File.WriteAllText(exe, "rebuilt");
                Assert.That(File.ReadAllText(copy), Is.EqualTo("executable"));
                Assert.Throws<ArgumentException>(() => EditorBinarySnapshot.Create(exe, source));
                Assert.Throws<ArgumentException>(() => EditorBinarySnapshot.Create(exe, Path.Combine(source, "nested")));
                EditorBinarySnapshot.Remove(exe, exe);
                Assert.That(File.Exists(exe), Is.True);
            }
            Assert.That(File.Exists(copy), Is.False);
            Assert.That(File.ReadAllText(exe), Is.EqualTo("rebuilt"));
        }
        finally { Directory.Delete(root, true); }
    }

    [Test]
    public void Cli_owned_snapshot_can_be_removed_after_exit_without_deleting_the_build()
    {
        using var snapshot = EditorBinarySnapshot.Create(EditorSession.EditorExe);
        EditorBinarySnapshot.Remove(snapshot.Executable, EditorSession.EditorExe);
        Assert.That(File.Exists(snapshot.Executable), Is.False);
        Assert.That(File.Exists(EditorSession.EditorExe), Is.True);
    }
}
