using System.Collections.Generic;
using Voltage.Materials;
using Voltage.Serialization;
using Voltage.Sprites;

namespace Voltage.DeferredLighting;

[RequireComponent(typeof(SpriteAnimator))]
public partial class AnimatedNormalMap : Component
{
    [AssetType(".aseprite", ".ase")]
    [Tooltip("Normal-map source. Leave empty to use the animator's Aseprite source.")]
    public AssetReference Source;
    [Tooltip("Normal-map layer names, including hidden layers. Tags and frames must match the animator.")]
    public List<string> Layers = new();
    [Tooltip("Normal clips from additional assets, keyed by the animator's gameplay animation names.")]
    public List<Clip> Clips = new();

    public class Clip : IComponentGroup
    {
        public string Animation = "";
        [AssetType(".aseprite", ".ase")]
        public AssetReference Source;
        [Tooltip("Matching Aseprite tag. Leave empty only for an untagged file.")]
        public string Tag = "";
        public List<string> Layers = new();
    }
    private DeferredAnimatedMaterial _material;
    private SpriteAnimator _animator;
    private Material _previousMaterial;
    public override void OnStart()
    {
        if (_material == null) Reload();
    }

    public override void OnEnabled()
    {
        if (_material == null) Reload();
    }

    public override void OnDisabled() => ReleaseMaterial();

    [InspectorCallable]
    public void Reload()
    {
        ReleaseMaterial();
        _animator = GetComponent<SpriteAnimator>();
        if (!Enabled || _animator == null || Entity?.Scene?.GetRenderer<DeferredLightingRenderer>() == null)
            return;
        var animations = new Dictionary<string, SpriteAnimation>();
        var path = Source.IsValid ? Source.ResolvePath() : _animator.AsepriteAsset.IsValid
            ? _animator.AsepriteAsset.ResolvePath() : _animator.TextureFilePath;
        if (Layers is { Count: > 0 } && !string.IsNullOrEmpty(path))
        {
            var atlas = Entity.Scene.Content.LoadAsepriteAtlas(path, Layers, false);
            foreach (var name in atlas.AnimationNames)
                animations[name] = atlas.GetAnimation(name);
            if (atlas.AnimationNames.Length == 0 && atlas.Sprites.Length > 0)
                animations["Default"] = new SpriteAnimation(atlas.Sprites, 10);
        }
        if (Clips != null)
            foreach (var clip in Clips)
            {
                if (clip == null || string.IsNullOrWhiteSpace(clip.Animation) || !clip.Source.IsValid || clip.Layers is not { Count: > 0 })
                    continue;
                var atlas = Entity.Scene.Content.LoadAsepriteAtlas(clip.Source.ResolvePath(), clip.Layers, false);
                if (string.IsNullOrWhiteSpace(clip.Tag) && atlas.AnimationNames.Length == 0 && atlas.Sprites.Length > 0)
                    animations[clip.Animation] = new SpriteAnimation(atlas.Sprites, 10);
                else
                    animations[clip.Animation] = atlas.GetAnimation(clip.Tag);
            }
        if (animations.Count == 0) return;
        _previousMaterial = _animator.Material;
        _animator.SetMaterial(_material = new DeferredAnimatedMaterial(_animator, animations));
    }

    private void ReleaseMaterial()
    {
        if (_material == null) return;
        if (_animator?.Material == _material) _animator.SetMaterial(_previousMaterial);
        _material.Dispose();
        _material = null;
        _previousMaterial = null;
    }

    public override void OnRemovedFromEntity()
    {
        ReleaseMaterial();
    }
}
