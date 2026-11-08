using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Xna.Framework;
using Voltage.Editor.Lighting;
using Voltage.Editor.Windows;
using Voltage.Gateway;

namespace Voltage.Editor.Gateway.Commands;

/// <summary>Offscreen sprite lighting for agents, sharing the Light Preview window's renderer.</summary>
internal static class LightingCommands
{
    private static readonly GatewayParam[] Parameters =
    {
        P.Str("path", "An .aseprite file inside the project, absolute or project-relative", required: true),
        P.List("layers", "Diffuse layers; defaults to the visible art layers"),
        P.List("normal_layers", "Normals layers; defaults to all Normals- layers except -gen takes"),
        P.List("glow_layers", "Glow layers; defaults to all of them"),
        P.Str("tag", "Aseprite tag; defaults to the whole file"),
        P.Int("frame", "0-based frame within the tag", 0),
        P.List("lights", "Lights: {type, x, y, fields:{member:value}}; x and y are pixels from the sprite centre", "object"),
        P.Str("ambient", "Ambient colour as #RRGGBB", "#333333"),
        P.Str("background", "Clear colour as #RRGGBB", "#181A20"),
        P.Str("material", "Name of a [LightPreviewMaterial] method; omit for the plain deferred material"),
    };

    public static void Register(GatewayCommandTable table)
    {
        table.Add("lighting.list_light_types", "Every DeferredLight type the Light Preview offers, plus the game's preview materials.", (_, _) => new
        {
            lights = LightPreviewSession.LightTypes().Select(t => new { name = t.Name, type = t.Type.FullName }).ToArray(),
            materials = LightPreviewSession.Materials().Select(m => m.Name).ToArray()
        }).ReadOnly();

        table.Add("lighting.preview_sprite", "Render an .aseprite frame through the real DeferredLightingRenderer and return a PNG.", (args, _) =>
        {
            using var session = new LightPreviewSession();
            Apply(session, args);
            return Result(session);
        }, Parameters).ReadOnly();

        table.Add("lighting.show", "Open the Light Preview window on a sprite with the given setup.", (args, _) =>
        {
            LightPreviewWindow.IsOpen = true;
            var session = LightPreviewWindow.Session;
            foreach (var light in session.Lights.ToArray()) session.RemoveLight(light);
            Apply(session, args);
            return new { path = session.Path, lights = session.Lights.Count };
        }, Parameters);
    }

    private static object Result(LightPreviewSession session)
    {
        session.Render();
        session.Render();
        if (session.Error != null) throw new GatewayException(session.Error);
        return new { image = Convert.ToBase64String(session.Png()), width = session.Target.Width, height = session.Target.Height, lights = session.Lights.Select(l => l.Name).ToArray(), frames = session.FrameCount };
    }

    private static void Apply(LightPreviewSession session, GatewayArgs args)
    {
        var project = GatewayValues.RequireProject();
        var root = Path.GetFullPath(project.ProjectPath);
        if (File.Exists(root)) root = Path.GetDirectoryName(root);
        var path = args.Require("path");
        var full = GatewayPaths.RequireInside(Path.IsPathRooted(path) ? path : Path.Combine(root, path), root);
        if (Path.GetExtension(full).ToLowerInvariant() is not (".aseprite" or ".ase")) throw new GatewayException("path must end in .aseprite or .ase");
        if (!File.Exists(full)) throw new GatewayException("file not found: " + path);

        session.Open(full);
        if (session.Error != null) throw new GatewayException(session.Error);
        Pick(session.Diffuse, session.Layers, args, "layers");
        Pick(session.Normals, session.NormalChoices, args, "normal_layers");
        Pick(session.Glow, session.GlowChoices, args, "glow_layers");
        var tag = args.String("tag");
        if (tag != null)
        {
            if (!session.Tags.Contains(tag)) throw new GatewayException($"unknown tag '{tag}'");
            session.Tag = tag;
        }
        session.Frame = args.Int("frame");
        if (args.Has("ambient")) session.Ambient = Color(args.String("ambient"));
        if (args.Has("background")) session.Background = Color(args.String("background"));
        var material = args.String("material");
        if (material != null)
            session.Material = LightPreviewSession.Materials().FirstOrDefault(m => m.Name == material) ?? throw new GatewayException($"unknown material '{material}'");
        session.Rebuild();
        if (session.Error != null) throw new GatewayException(session.Error);
        if (args.TryGet("lights", out var lights))
            foreach (var entry in lights.EnumerateArray()) AddLight(session, entry);
    }

    private static void Pick(System.Collections.Generic.HashSet<string> picked, System.Collections.Generic.List<string> choices, GatewayArgs args, string name)
    {
        if (!args.Has(name)) return;
        var wanted = args.Strings(name);
        var unknown = wanted.FirstOrDefault(layer => !choices.Contains(layer));
        if (unknown != null) throw new GatewayException($"unknown layer '{unknown}' in {name}");
        picked.Clear();
        picked.UnionWith(wanted);
    }

    private static void AddLight(LightPreviewSession session, JsonElement entry)
    {
        if (entry.ValueKind != JsonValueKind.Object || !entry.TryGetProperty("type", out var type)) throw new GatewayException("each light needs a type");
        var light = session.AddLight(GatewayValues.ResolveType(typeof(Voltage.DeferredLighting.DeferredLight), type.GetString()));
        float Number(string name, float fallback) => entry.TryGetProperty(name, out var value) ? value.GetSingle() : fallback;
        light.Entity.Position = new Vector2(Number("x", light.Entity.Position.X), Number("y", light.Entity.Position.Y));
        if (entry.TryGetProperty("fields", out var fields))
            foreach (var field in fields.EnumerateObject())
                GatewayValues.Set(light.Light, field.Name, field.Value, false, "Light Preview");
    }

    private static Color Color(string text) =>
        GatewayValues.Convert(JsonSerializer.SerializeToElement(text), typeof(Color)) is Color color ? color : throw new GatewayException("bad colour " + text);
}
