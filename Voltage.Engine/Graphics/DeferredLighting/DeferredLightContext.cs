using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Voltage.DeferredLighting;

/// <summary>What a custom light draws with: additive blending is set and the light buffer is the render target.</summary>
public readonly struct DeferredLightContext
{
    private readonly DeferredLightingRenderer _renderer;

    public Batcher Batcher => Graphics.Instance.Batcher;
    public Camera Camera { get; }
    public DeferredLightEffect Effect { get; }

    /// <summary>The scene's normals: RGB is the normal, alpha the self-illumination.</summary>
    public Texture2D NormalMap => _renderer.NormalRT.RenderTarget;

    internal DeferredLightContext(DeferredLightingRenderer renderer, Camera camera, DeferredLightEffect effect)
    {
        _renderer = renderer;
        Camera = camera;
        Effect = effect;
    }

    /// <summary>Draws one lobe with the point-light shader; several calls build a light of any shape.</summary>
    public void DrawPoint(Vector2 position, float z, float radius, float intensity, Color color)
    {
        Effect.PreparePointLight(new Vector3(position, z), radius, intensity, color);
        _renderer.RenderPointVolume();
    }

    /// <summary>Draws the circular light volume with the pass already applied to Effect.</summary>
    public void DrawCircleVolume() => _renderer.RenderPointVolume();

    /// <summary>Draws the rectangular light volume with the pass already applied to Effect.</summary>
    public void DrawQuadVolume() => _renderer.RenderQuadVolume();

    /// <summary>Draws a full-screen quad with the pass already applied to Effect.</summary>
    public void DrawFullscreen() => _renderer.RenderFullscreen();
}

/// <summary>Names a light in the editor's Light Preview list, or hides it from the list.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class LightPreviewAttribute : Attribute
{
    public string DisplayName { get; }
    public bool Hidden { get; set; }

    public LightPreviewAttribute(string displayName = null) => DisplayName = displayName;
}

/// <summary>Offers a static method to the Light Preview as a material: <c>DeferredAnimatedMaterial Build(SpriteAnimator animator, IReadOnlyDictionary&lt;string, SpriteAnimation&gt; normals)</c>.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class LightPreviewMaterialAttribute : Attribute
{
    public string DisplayName { get; }

    public LightPreviewMaterialAttribute(string displayName) => DisplayName = displayName;
}
