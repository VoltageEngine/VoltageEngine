using NUnit.Framework;
using Voltage.Cinematics;
using Voltage.Editor.Windows;

namespace Voltage.Effects.Tests;

[TestFixture]
public class TimelineDocumentTests
{
    private string _root, _first, _second;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "voltage-timeline-document", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _first = Path.Combine(_root, "First.vtimeline");
        _second = Path.Combine(_root, "Second.vtimeline");
        TimelineAssetIO.CreateAndSave(_first);
        var second = TimelineAssetIO.CreateDefault();
        second.Duration = 12;
        TimelineAssetIO.Save(second, _second);
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_root, true);

    [Test]
    public void Clean_open_uses_a_detached_copy_and_switches_immediately()
    {
        var source = TimelineAssetIO.CreateDefault();
        var document = new TimelineDocument();
        document.Request(source, _first);
        document.Asset.Duration = 9;
        Assert.That(source.Duration, Is.EqualTo(5));
        document.Resolve(TimelineSwitchChoice.Cancel);
        var clean = new TimelineDocument();
        Assert.That(clean.RequestOpen(_second, _root), Is.True);
        Assert.That(clean.Asset.Duration, Is.EqualTo(12));
        Assert.That(clean.IsDirty, Is.False);
    }

    [TestCase(TimelineSwitchChoice.Cancel)]
    [TestCase(TimelineSwitchChoice.Discard)]
    public void Dirty_switch_waits_and_cancel_or_discard_preserves_disk(TimelineSwitchChoice choice)
    {
        var document = OpenFirst();
        document.Asset.Duration = 9;
        var activated = 0;
        Assert.That(document.RequestOpen(_second, _root, () => activated++), Is.False);
        Assert.That(document.Path, Is.EqualTo(_first));
        Assert.That(activated, Is.Zero);
        document.Resolve(choice);
        Assert.That(TimelineAssetIO.Load(_first).Duration, Is.EqualTo(5));
        Assert.That(document.Asset.Duration, Is.EqualTo(choice == TimelineSwitchChoice.Cancel ? 9 : 12));
        Assert.That(activated, Is.EqualTo(choice == TimelineSwitchChoice.Cancel ? 0 : 1));
        Assert.That(document.HasPendingSwitch, Is.False);
    }

    [Test]
    public void Save_switch_saves_old_document_before_activating_new_one()
    {
        var document = OpenFirst();
        document.Asset.Duration = 9;
        document.RequestOpen(_second, _root);
        Assert.That(document.Resolve(TimelineSwitchChoice.Save), Is.True);
        Assert.That(TimelineAssetIO.Load(_first).Duration, Is.EqualTo(9));
        Assert.That(document.Path, Is.EqualTo(_second));
        Assert.That(document.IsDirty, Is.False);
    }

    [TestCase(TimelineSwitchChoice.Save, 9)]
    [TestCase(TimelineSwitchChoice.Discard, 5)]
    public void Opening_the_current_file_never_activates_a_stale_pre_save_copy(TimelineSwitchChoice choice, int expected)
    {
        var document = OpenFirst();
        document.Asset.Duration = 9;
        document.RequestOpen(_first, _root);
        Assert.That(document.Resolve(choice), Is.True);
        Assert.That(document.Asset.Duration, Is.EqualTo(expected));
        Assert.That(TimelineAssetIO.Load(_first).Duration, Is.EqualTo(expected));
        Assert.That(document.IsDirty, Is.False);
    }

    [TestCase("Roles")]
    [TestCase("ParameterTracks")]
    [TestCase("Events")]
    public void Malformed_list_structure_never_replaces_the_working_document(string field)
    {
        var document = OpenFirst();
        document.Asset.Duration = 9;
        File.WriteAllText(_second, "{\"Duration\":5,\"" + field + "\":null}");
        Assert.That(document.RequestOpen(_second, _root), Is.False);
        Assert.That(document.Asset.Duration, Is.EqualTo(9));
        Assert.That(document.HasPendingSwitch, Is.False);
    }

    [Test]
    public void Save_failure_keeps_edit_selection_callback_and_pending_target()
    {
        var document = new TimelineDocument();
        document.Request(TimelineAssetIO.CreateDefault(), Path.Combine(_root, "missing", "First.vtimeline"));
        document.Asset.Duration = 9;
        var activated = 0;
        document.RequestOpen(_second, _root, () => activated++);
        Assert.That(document.Resolve(TimelineSwitchChoice.Save), Is.False);
        Assert.That(document.Asset.Duration, Is.EqualTo(9));
        Assert.That(document.IsDirty, Is.True);
        Assert.That(document.HasPendingSwitch, Is.True);
        Assert.That(activated, Is.Zero);
        Assert.That(document.Error, Does.StartWith("Save failed:"));
        document.Resolve(TimelineSwitchChoice.Cancel);
        Assert.That(document.Asset.Duration, Is.EqualTo(9));
    }

    [TestCase("wrong")]
    [TestCase("missing")]
    [TestCase("invalid")]
    [TestCase("outside")]
    public void Invalid_drop_never_replaces_dirty_asset_or_selection(string kind)
    {
        var document = OpenFirst();
        document.Asset.Duration = 9;
        var path = kind switch {
            "wrong" => Path.Combine(_root, "Image.png"),
            "missing" => Path.Combine(_root, "Absent.vtimeline"),
            "invalid" => Path.Combine(_root, "Broken.vtimeline"),
            _ => Path.Combine(_root, "..", "Outside.vtimeline")
        };
        if (kind is "wrong" or "invalid") File.WriteAllText(path, "{broken");
        var activated = 0;
        Assert.That(document.RequestOpen(path, _root, () => activated++), Is.False);
        Assert.That(document.Path, Is.EqualTo(_first));
        Assert.That(document.Asset.Duration, Is.EqualTo(9));
        Assert.That(document.IsDirty, Is.True);
        Assert.That(document.HasPendingSwitch, Is.False);
        Assert.That(activated, Is.Zero);
    }

    private TimelineDocument OpenFirst()
    {
        var document = new TimelineDocument();
        Assert.That(document.RequestOpen(_first, _root), Is.True);
        return document;
    }
}
