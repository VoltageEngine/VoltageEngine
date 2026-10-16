using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using NUnit.Framework;
using Voltage.DeferredLighting;
using Voltage.Textures;
using Voltage.Utils;
using Voltage.Assets;
using Voltage.Serialization;

namespace Voltage.Effects.Tests;

[TestFixture]
public class ScenePreparationTests
{
    [Test]
    public void Reference_paths_refresh_next_frame_and_manifest_queries_stay_fresh()
    {
        var directory = Path.Combine(Path.GetTempPath(), "voltage-reference-cache", Guid.NewGuid().ToString("N"));
        var previousFrame = Time.FrameCount;
        var previousResolver = Scene.AssetPathResolver;
        var guid = Guid.NewGuid();
        Directory.CreateDirectory(directory);
        var manifest = Path.Combine(directory, "assets.manifest");
        var file = Path.Combine(directory, "source.txt");
        var reference = new AssetReference { AssetGuid = guid };
        try
        {
            Scene.AssetPathResolver = null;
            File.WriteAllText(manifest, $"{{\"{guid}\":\"source.txt\"}}");
            File.WriteAllText(file, "asset");
            AssetManifest.LoadFrom(manifest, directory);
            Assert.That(reference.ResolvePath(), Is.EqualTo(file));
            File.Delete(file);
            Assert.That(AssetManifest.TryGetAbsolutePath(guid, out _), Is.False);
            Time.FrameCount++;
            Assert.That(reference.ResolvePath(), Is.Null);
            File.WriteAllText(file, "replacement");
            AssetManifest.LoadFrom(manifest, directory);
            Assert.That(reference.ResolvePath(), Is.EqualTo(file), "Manifest reload must refresh references in the same frame");
            Scene.AssetPathResolver = _ => "editor asset";
            Assert.That(reference.ResolvePath(), Is.EqualTo("editor asset"));
        }
        finally
        {
            Scene.AssetPathResolver = previousResolver;
            Time.FrameCount = previousFrame;
            AssetManifest.LoadFrom(Path.Combine(AppContext.BaseDirectory, AssetManifest.DefaultRelativePath));
            Directory.Delete(directory, true);
        }
    }

    [Test]
    public void Builtin_bytes_reuse_reads_in_a_frame_and_refresh_files_and_overrides()
    {
        var directory = Path.Combine(Path.GetTempPath(), "voltage-effect-cache", Guid.NewGuid().ToString("N"));
        var previousOverride = EffectResource.BuiltinOverrideDirectory;
        var previousFrame = Time.FrameCount;
        var getter = typeof(EffectResource).GetProperty("InvertBytes", BindingFlags.NonPublic | BindingFlags.Static)!;
        byte[] Load() => (byte[])getter.GetValue(null)!;
        Directory.CreateDirectory(directory);
        try
        {
            var invert = EffectResource.GetFileResourceBytes("Content/Voltage/Effects/Invert.mgfxo");
            var grayscale = EffectResource.GetFileResourceBytes("Content/Voltage/Effects/Grayscale.mgfxo");
            var file = Path.Combine(directory, "Invert.mgfxo");
            File.WriteAllBytes(file, invert);
            EffectResource.BuiltinOverrideDirectory = directory;
            var first = Load();
            Assert.That(Load(), Is.SameAs(first));
            File.WriteAllBytes(file, grayscale);
            Assert.That(EffectResource.GetFileResourceBytes(file), Is.EqualTo(grayscale), "Custom file loads must remain fresh within a frame");
            Time.FrameCount++;
            Assert.That(Load(), Is.EqualTo(grayscale));
            File.WriteAllText(file, "legacy invalid shader");
            Time.FrameCount++;
            Assert.That(Load(), Is.EqualTo(invert), "Invalid overrides must retain the bundled fallback");
            Assert.That(Load(), Is.SameAs(Load()), "Fallback reads must also be reused");
            File.WriteAllBytes(file, grayscale);
            EffectResource.BuiltinOverrideDirectory = null;
            Assert.That(Load(), Is.EqualTo(invert), "Changing the override must take effect in the same frame");
        }
        finally
        {
            EffectResource.BuiltinOverrideDirectory = previousOverride;
            Time.FrameCount = previousFrame;
            Directory.Delete(directory, true);
        }
    }

    [Test]
    public void Sprite_light_builds_only_used_masks_and_preserves_pixels_and_cache_invalidation()
    {
        using var game = new MaskGame();
        game.Run();
    }

    private sealed class MaskGame : Core
    {
        public MaskGame() : base(32, 32, windowTitle: "Sprite mask preparation check") { }

        protected override void Initialize()
        {
            base.Initialize();
            using var texture = new Texture2D(GraphicsDevice, 2, 1);
            texture.SetData(new[] { Color.White, Color.Transparent });
            var firstMask = new Sprite(texture, new Rectangle(0, 0, 1, 1));
            var secondMask = new Sprite(texture, new Rectangle(1, 0, 1, 1));
            var light = new SpriteLight { Radius = 4 };
            var fields = (Dictionary<Sprite, Sprite>)typeof(SpriteLight).GetField("_fields", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(light)!;
            var getter = typeof(SpriteLight).GetMethod("GetField", BindingFlags.NonPublic | BindingFlags.Instance)!;
            Sprite Load(Sprite mask) => (Sprite)getter.Invoke(light, new object[] { mask })!;
            light.SetFrameMask(firstMask, firstMask);
            light.SetFrameMask(secondMask, secondMask);
            Assert.That(fields, Is.Empty, "Scene initialization must not prepare every animation frame");
            var field = Load(firstMask);
            Assert.That(fields.Count, Is.EqualTo(1));
            Assert.That(Load(firstMask), Is.SameAs(field));
            var pixels = new Color[field.Texture2D.Width * field.Texture2D.Height];
            field.Texture2D.GetData(pixels);
            Assert.That(pixels, Is.EqualTo(SpriteLight.BuildField(new[] { Color.White }, 1, 1, 4)));
            Load(secondMask);
            light.Radius = 5;
            var resized = Load(firstMask);
            Assert.That(field.Texture2D.IsDisposed, Is.True);
            Assert.That(fields.Count, Is.EqualTo(1));
            Assert.That(resized.Texture2D.Width, Is.EqualTo(11));
            light.OnRemovedFromEntity();
            Assert.That(resized.Texture2D.IsDisposed, Is.True);
            ((Game)this).Exit();
        }
    }
}
