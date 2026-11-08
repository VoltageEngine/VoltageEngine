using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading;
using NUnit.Framework;
using static Voltage.Editor.Tests.EditorSession;

namespace Voltage.Editor.Tests;

[TestFixture, NonParallelizable]
public class LightPreviewTests
{
    private const string Sprite = "Content/LightFixture.aseprite";
    private const string CustomLight = """
        using Microsoft.Xna.Framework;
        using Voltage.DeferredLighting;

        [LightPreview("Test Beam")]
        public partial class TestBeamLight : DeferredLight
        {
            public float Spacing = 10;
            public override bool RenderCustom(DeferredLightContext context)
            {
                for (int i = -2; i <= 2; i++)
                    context.DrawPoint(Entity.Position + new Vector2(i * Spacing, 0), 24, 90, 1.2f, Color.Orange);
                return true;
            }
        }
        """;

    private static string Project => Path.GetDirectoryName(ProjectFile);

    [OneTimeSetUp]
    public void CopyFixture()
    {
        Directory.CreateDirectory(Path.Combine(Project, "Content"));
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(root.FullName, "VoltageEngine.sln"))) root = root.Parent;
        File.Copy(Path.Combine(root.FullName, "Voltage.Effects.Tests", "Fixtures", "LightFixture.aseprite"), Path.Combine(Project, Sprite), true);
    }

    private static (int Width, byte[] Rgba) Render(params object[] lights)
    {
        var result = Call("lighting.preview_sprite", new { path = Sprite, tag = "Idle", ambient = "#000000", background = "#000000", lights });
        return Png(Convert.FromBase64String(result.GetProperty("image").GetString()));
    }

    private static int Red((int Width, byte[] Rgba) image, int x, int y) => image.Rgba[(y * image.Width + x) * 4];

    /// <summary>Decodes the 8-bit RGBA PNGs MonoGame writes.</summary>
    private static (int Width, byte[] Rgba) Png(byte[] file)
    {
        int Int(int at) => file[at] << 24 | file[at + 1] << 16 | file[at + 2] << 8 | file[at + 3];
        int width = Int(16), height = Int(20);
        using var packed = new MemoryStream();
        for (int at = 8; at < file.Length;)
        {
            int length = Int(at);
            if (System.Text.Encoding.ASCII.GetString(file, at + 4, 4) == "IDAT") packed.Write(file, at + 8, length);
            at += length + 12;
        }
        packed.Position = 0;
        using var inflate = new ZLibStream(packed, CompressionMode.Decompress);
        var raw = new byte[(width * 4 + 1) * height];
        inflate.ReadExactly(raw);
        var rgba = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        {
            int filter = raw[y * (width * 4 + 1)];
            for (int x = 0; x < width * 4; x++)
            {
                int a = x >= 4 ? rgba[y * width * 4 + x - 4] : 0;
                int b = y > 0 ? rgba[(y - 1) * width * 4 + x] : 0;
                int c = x >= 4 && y > 0 ? rgba[(y - 1) * width * 4 + x - 4] : 0;
                int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
                int predicted = filter switch
                {
                    0 => 0, 1 => a, 2 => b, 3 => (a + b) / 2,
                    _ => pa <= pb && pa <= pc ? a : pb <= pc ? b : c
                };
                rgba[y * width * 4 + x] = (byte)(raw[y * (width * 4 + 1) + 1 + x] + predicted);
            }
        }
        return (width, rgba);
    }

    [Test]
    public void Lists_built_in_and_script_lights_and_renders_a_custom_one()
    {
        Call("script.write", new { path = "Scripts/TestBeamLight.cs", content = CustomLight });
        var deadline = DateTime.UtcNow.AddSeconds(60);
        JsonElement types;
        do
        {
            Thread.Sleep(200);
            types = Call("lighting.list_light_types");
        } while (!types.GetProperty("lights").EnumerateArray().Any(l => l.GetProperty("name").GetString() == "Test Beam") && DateTime.UtcNow < deadline);
        var names = types.GetProperty("lights").EnumerateArray().Select(l => l.GetProperty("name").GetString()).ToArray();
        Assert.That(names, Is.SupersetOf(new[] { "PointLight", "SpotLight", "FluorescentLight", "RoomLight", "Test Beam" }));
        var dark = Render();
        var lit = Render(new { type = "TestBeamLight", x = 0, y = -20 });
        if (Environment.GetEnvironmentVariable("VOLTAGE_LIGHT_REVIEW") is { } review)
            File.WriteAllBytes(Path.Combine(review, "custom-light-test-beam.png"), Convert.FromBase64String(Call("lighting.preview_sprite", new { path = Sprite, tag = "Idle", ambient = "#101014", background = "#000000", lights = new object[] { new { type = "TestBeamLight", x = 0, y = -20 } } }).GetProperty("image").GetString()));
        int center = dark.Width / 2;
        Assert.That(Red(dark, center + 4, center), Is.Zero, "The right half has no glow and no light");
        Assert.That(Red(lit, center + 4, center), Is.GreaterThan(0), "A game-defined light must render without any engine change");
    }

    [Test]
    public void Glow_shows_with_no_lights_and_the_layer_pick_turns_it_off()
    {
        var withGlow = Render();
        int center = withGlow.Width / 2;
        Assert.That(Red(withGlow, center - 4, center), Is.InRange(95, 105), "Half-strength glow on 200 grey");
        var off = Call("lighting.preview_sprite", new { path = Sprite, tag = "Idle", ambient = "#000000", background = "#000000", glow_layers = Array.Empty<string>() });
        Assert.That(Red(Png(Convert.FromBase64String(off.GetProperty("image").GetString())), center - 4, center), Is.Zero);
    }

    [Test]
    public void Vertical_facets_respond_to_light_above_and_below()
    {
        byte[] Shot(string name, float y, string normals)
        {
            var result = Call("lighting.preview_sprite", new
            {
                path = Sprite, tag = "Idle", frame = 1, ambient = "#000000", background = "#000000", normal_layers = new[] { normals },
                lights = new object[] { new { type = "PointLight", y, fields = new { Radius = 200f, ZPosition = 40f, Intensity = 2f } } }
            });
            var bytes = Convert.FromBase64String(result.GetProperty("image").GetString());
            var folder = Environment.GetEnvironmentVariable("VOLTAGE_LIGHT_REVIEW");
            if (folder != null) File.WriteAllBytes(Path.Combine(folder, "vertical-" + name + ".png"), bytes);
            return bytes;
        }
        var above = Png(Shot("top-facet-light-above", -60, "Normals-Art"));
        var below = Png(Shot("top-facet-light-below", 60, "Normals-Art"));
        var flat = Png(Shot("front-facet-light-above", -60, "Normals-Other"));
        int center = above.Width / 2;
        TestContext.Progress.WriteLine($"Top facet G=198, sprite centre: light above {Red(above, center, center)}, light below {Red(below, center, center)}; front facet, light above {Red(flat, center, center)}");
        Assert.That(Red(above, center, center), Is.GreaterThan(Red(flat, center, center)), "The top facet must brighten under a light above it");
        Assert.That(Red(below, center, center), Is.LessThan(Red(flat, center, center)), "The top facet must darken under a light below it");
    }

    [Test]
    public void Paths_outside_the_project_are_rejected()
    {
        Assert.That(() => Call("lighting.preview_sprite", new { path = "../outside.aseprite" }), Throws.Exception);
    }

    [Test]
    public void Window_opens_from_the_menu_on_a_sprite_with_lights()
    {
        Call("ui.click", new { path = "View/Window/Light Preview" });
        WaitForWidget("Pick", "Light Preview");
        Call("lighting.show", new { path = Sprite, tag = "Idle", lights = new object[] { new { type = "PointLight", y = -30, fields = new { Radius = 120f, ZPosition = 30f, Intensity = 2f } } } });
        WaitForWidget("Play", "Light Preview");
        Call("ui.click", new { path = "View/Window/Light Preview" });
    }
}
