using System.Text;
using System.Text.Json;
using Microsoft.Xna.Framework;
using NUnit.Framework;
using Voltage.Editor.Assets;
using Voltage.Editor.Windows;

namespace Voltage.Effects.Tests;

[TestFixture]
public class AssetPreviewTests
{
    private string _root;
    [SetUp] public void SetUp() { _root = Path.Combine(Path.GetTempPath(), "voltage-asset-preview", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(_root); }
    [TearDown] public void TearDown() { Directory.Delete(_root, true); }

    [Test]
    public void Asset_zoom_accumulates_fine_trackpad_deltas_and_reverses_immediately_at_limits()
    {
        float remainder = 0;
        int zoom = 0;
        for (var i = 0; i < 50; i++) zoom = AssetBrowserWindow.ApplyWheelZoom(zoom, 0.01f, ref remainder);
        Assert.That(zoom, Is.EqualTo(5));
        zoom = AssetBrowserWindow.ApplyWheelZoom(zoom, 100, ref remainder);
        Assert.That(zoom, Is.EqualTo(200));
        zoom = AssetBrowserWindow.ApplyWheelZoom(zoom, -1, ref remainder);
        Assert.That(zoom, Is.EqualTo(190));
        zoom = AssetBrowserWindow.ApplyWheelZoom(zoom, -100, ref remainder);
        Assert.That(zoom, Is.Zero);
        Assert.That(AssetBrowserWindow.ApplyWheelZoom(zoom, 1, ref remainder), Is.EqualTo(10));
    }

    [Test]
    public void Text_preview_reads_unicode_literal_format_characters_and_both_utf_encodings()
    {
        var path = Path.Combine(_root, "Script.cs");
        foreach (var encoding in new Encoding[] { new UTF8Encoding(false), Encoding.Unicode })
        {
            File.WriteAllText(path, "\tΕλληνικά 100% {value}\nsecond line", encoding);
            Assert.That(AssetTextPreview.Read(path), Is.EqualTo("    Ελληνικά 100% {value}\nsecond line"));
        }
        File.WriteAllText(path, "a\0b");
        Assert.That(AssetTextPreview.Read(path), Does.Contain("binary data"));
        File.WriteAllText(path, new string('a', 300_000));
        Assert.That(AssetTextPreview.Read(path).Length, Is.LessThan(270_000));
        Assert.That(AssetTextPreview.Read(path), Does.Contain("truncated"));
        Assert.That(AssetTextPreview.CanPreview("image.png"), Is.False);
        Assert.That(AssetTextPreview.CanPreview("code.CS"), Is.True);
        foreach (var extension in new[] { "json", "vasset", "vprefab", "vscene", "vtimeline", "vtileset" })
            Assert.That(AssetTextPreview.CanPreview("asset." + extension), Is.False);
    }

    [Test]
    public void Prefab_preview_reads_saved_child_transforms_layers_and_asset_references_without_instantiation()
    {
        var path = Path.Combine(_root, "Test.vprefab");
        var sprite = new { TextureFilePath = "Content/Sprite.png", Origin = new { X = 4, Y = 5 }, Enabled = true, SpriteEffects = "FlipHorizontally", RenderLayer = 2 };
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            Id = "root", Name = "Root", Position = new { X = 10, Y = 20 }, Scale = new { X = 2, Y = 2 },
            EntityData = new { ComponentDataList = Array.Empty<object>() },
            ChildEntities = new[] { new { Id = "child", Name = "Child", ParentId = "root", Position = new { X = 3, Y = 4 },
                EntityData = new { ComponentDataList = new[] { new { ComponentTypeName = "Voltage.Sprites.SpriteRenderer", Json = JsonSerializer.Serialize(sprite) } } } } }
        }));
        var previews = AssetThumbnailCache.ReadPrefab(path, _root, new());
        Assert.That(previews, Has.Count.EqualTo(1));
        Assert.That(Vector2.Transform(Vector2.Zero, previews[0].Transform), Is.EqualTo(new Vector2(16, 28)));
        Assert.That(previews[0].Origin, Is.EqualTo(new Vector2(4, 5)));
        Assert.That(previews[0].Path, Is.EqualTo(Path.Combine(_root, "Content", "Sprite.png")));
        using var escape = JsonDocument.Parse("\"../Outside.png\"");
        Assert.That(AssetThumbnailCache.ResolveSource(escape.RootElement, _root), Is.Null);
        using var differentCase = JsonDocument.Parse(JsonSerializer.Serialize(Path.Combine(_root.ToUpperInvariant(), "Sprite.png")));
        var resolvedCase = AssetThumbnailCache.ResolveSource(differentCase.RootElement, _root);
        if (OperatingSystem.IsWindows()) Assert.That(resolvedCase, Is.Not.Null);
        else Assert.That(resolvedCase, Is.Null, "A differently cased sibling directory is outside the project on Unix");
    }

    [Test]
    public void Prefab_animator_preview_uses_idle_clip_from_authored_data_asset()
    {
        var data = Path.Combine(_root, "Animation.vasset");
        File.WriteAllText(data, "{\"data\":{\"Clips\":[{\"Key\":\"Attack\",\"Source\":{\"AssetPath\":\"Attack.aseprite\"}},{\"Key\":\"Idle\",\"Source\":{\"AssetPath\":\"Idle.aseprite\"},\"Tag\":\"Idle-Tag\",\"Layers\":[\"Body\"]}]}}");
        var path = Path.Combine(_root, "Test.vprefab");
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            Id = "root", Name = "Root", EntityData = new { ComponentDataList = new[]
            {
                new { ComponentTypeName = "Voltage.Sprites.SpriteAnimator", Json = "{\"TextureFilePath\":\"\"}" },
                new { ComponentTypeName = "Voltage.Mover", Json = (string)null },
                new { ComponentTypeName = "Game.Controller", Json = "{\"TuningAsset\":{\"AssetPath\":\"Animation.vasset\"}}" }
            } }
        }));
        var dependencies = new Dictionary<string, long>();
        var previews = AssetThumbnailCache.ReadPrefab(path, _root, dependencies);
        Assert.That(previews[0].Layers, Is.EqualTo(new[] { "Body" }));
        Assert.That(previews.Single().Path, Is.EqualTo(Path.Combine(_root, "Idle.aseprite")));
        Assert.That(previews.Single().Tag, Is.EqualTo("Idle-Tag"));
        Assert.That(dependencies, Does.ContainKey(data));
    }
}
