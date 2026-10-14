using System;
using System.Collections.Generic;
using Voltage.Sprites;
using Voltage.Serialization;

namespace Voltage.Cinematics;

/// <summary>Reversible sampling of supported cinematic presentation without runtime playback.</summary>
public sealed class TimelinePreviewSession : ITimelineContext, IDisposable
{
    private readonly TimelineDirector _director;
    private readonly TimelineAsset _asset;
    private readonly StateSnapshot _snapshot = new();
    private readonly Dictionary<string, Entity> _roles = new();
    private readonly HashSet<Entity> _capturedEntities = new();
    private readonly HashSet<TimelineParameterTrack> _capturedTracks = new();
    private readonly HashSet<ITimelinePreviewParticipant> _participants = new();
    private readonly Dictionary<TimelineNestedClip, TimelineAsset> _nested = new();
    private readonly HashSet<string> _unsupported = new();
    private bool _disposed;

    public float Time { get; private set; }
    public Scene Scene { get; }
    public IReadOnlyCollection<string> UnsupportedEffects => _unsupported;

    public TimelinePreviewSession(TimelineDirector director, TimelineAsset asset)
    {
        _director = director ?? throw new ArgumentNullException(nameof(director));
        _asset = asset ?? throw new ArgumentNullException(nameof(asset));
        Scene = director.Entity?.Scene;
    }

    public Entity ResolveRole(string role) => role != null && _roles.TryGetValue(role, out var entity) ? entity : null;

    public Component ResolveComponent(string role, string componentId)
    {
        var entity = ResolveRole(role);
        if (entity == null || string.IsNullOrEmpty(componentId)) return null;
        if (ComponentIdRegistry.TryGetType(componentId, out var type) && entity.GetComponent(type) is { } found)
            return found;
        foreach (var component in entity.Components)
            if (ComponentIdRegistry.GetIdForType(component.GetType()) == componentId) return component;
        foreach (var component in entity.ComponentsToAdd)
            if (ComponentIdRegistry.GetIdForType(component.GetType()) == componentId) return component;
        return null;
    }

    public void RaiseSignal(string name, TimelineArg[] args) { }

    /// <summary>Restores the baseline before each sample so backward scrubbing has no accumulated state.</summary>
    public void Evaluate(float time)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(TimelinePreviewSession));
        _snapshot.Restore();
        _snapshot.Clear();
        _capturedEntities.Clear();
        _capturedTracks.Clear();
        _participants.Clear();
        _roles.Clear();
        _unsupported.Clear();
        foreach (var binding in _director.Bindings)
        {
            if (binding == null || string.IsNullOrEmpty(binding.Role)) continue;
            var id = binding.Entity.GetPersistentId();
            var entity = id != Guid.Empty ? Scene?.FindEntityByPersistentId(id) : null;
            if (id == Guid.Empty && !string.IsNullOrEmpty(binding.Entity.EntityName))
                entity = Scene?.FindEntity(binding.Entity.EntityName);
            if (entity == null) continue;
            _roles[binding.Role] = entity;
            CaptureEntity(entity);
        }
        foreach (var entity in _roles.Values) RefreshWorldTransform(entity);
        Time = Math.Clamp(float.IsFinite(time) ? time : 0, 0, Math.Max(0, _asset.Duration));
        CaptureTracks(_asset, 0);
        try
        {
            SampleTracks(_asset, Time, 0);
            foreach (var participant in _participants)
                if (participant is not Component component || component.Entity?.Scene == Scene)
                    participant.EvaluateTimelinePreview(Time, _asset, _director);
        }
        catch
        {
            _snapshot.Restore();
            throw;
        }
    }

    private void CaptureEntity(Entity entity)
    {
        if (!_capturedEntities.Add(entity)) return;
        var position = entity.Transform.LocalPosition;
        var rotation = entity.Transform.LocalRotation;
        var scale = entity.Transform.LocalScale;
        var enabled = entity.Enabled;
        _snapshot.Add(() => { entity.Transform.LocalPosition = position; entity.Transform.LocalRotation = rotation; entity.Transform.LocalScale = scale; entity.SetTimelinePreviewEnabled(enabled); });
        foreach (var component in entity.Components) CaptureComponent(component);
        foreach (var component in entity.ComponentsToAdd) CaptureComponent(component);
    }

    private void CaptureComponent(Component component)
    {
        if (component is SpriteAnimator animator) animator.CapturePreviewState(_snapshot);
        if (component is ITimelinePreviewParticipant participant && _participants.Add(participant))
            participant.CaptureTimelinePreview(_snapshot);
    }

    private static bool Supported(TimelineParameterTrack track) => track?.GetType() == typeof(TimelineTransformTrack) ||
        track?.GetType() == typeof(TimelineCameraTrack) || track?.GetType() == typeof(TimelineTintTrack) ||
        track?.GetType() == typeof(TimelineActivationTrack) || track?.GetType() == typeof(TimelinePropertyTrack);

    private static void RefreshWorldTransform(Entity entity)
    {
        if (entity == null) return;
        _ = entity.Position;
        _ = entity.Rotation;
        _ = entity.Scale;
    }

    private void CaptureTracks(TimelineAsset asset, int depth)
    {
        if (depth >= 8) return;
        foreach (var track in asset.ParameterTracks)
        {
            if (Supported(track) && track is not TimelineActivationTrack and not TimelineTransformTrack && _capturedTracks.Add(track)) track.CaptureRestoreState(_snapshot, this);
            if (track is TimelineNestedTrack nested)
                foreach (var clip in nested.Clips)
                    if (LoadNested(clip) is { } child) CaptureTracks(child, depth + 1);
        }
    }

    private void SampleTracks(TimelineAsset asset, float time, int depth)
    {
        if (depth >= 8) { _unsupported.Add("Nested timelines beyond eight levels"); return; }
        if (asset.Events.Count > 0) _unsupported.Add("Gameplay events and broadcast signals");
        if (asset.SpawnClips.Count > 0) _unsupported.Add("Prefab spawning and component lifecycle callbacks");
        foreach (var track in asset.ParameterTracks)
        {
            if (track == null) continue;
            if (track.GetType() == typeof(TimelineActivationTrack))
                ResolveRole(track.TargetRole)?.SetTimelinePreviewEnabled(((TimelineActivationTrack)track).IsActiveAt(time));
            else if (Supported(track))
            {
                if (track is TimelineTransformTrack) RefreshWorldTransform(ResolveRole(track.TargetRole));
                track.Evaluate(time, this);
            }
            else if (track.GetType() == typeof(TimelineSpriteTrack))
            {
                var sprite = (TimelineSpriteTrack)track;
                var clip = sprite.ClipAt(time);
                if (clip != null) ResolveRole(sprite.TargetRole)?.GetComponent<SpriteAnimator>()?.SamplePreview(clip.Animation, clip.Loop, time - clip.Time);
            }
            else if (track.GetType() == typeof(TimelineNestedTrack))
            {
                foreach (var clip in ((TimelineNestedTrack)track).Clips)
                {
                    var child = LoadNested(clip);
                    if (child == null) continue;
                    var local = (time - clip.Time) * (clip.Speed > 0 ? clip.Speed : 1);
                    if (local >= 0 && local <= child.Duration) SampleTracks(child, local, depth + 1);
                }
            }
            else _unsupported.Add(track is TimelineAudioTrack ? "Audio playback" : $"Custom track {track.GetType().Name}");
        }
    }

    private TimelineAsset LoadNested(TimelineNestedClip clip)
    {
        if (clip == null) return null;
        if (_nested.TryGetValue(clip, out var asset)) return asset;
        try
        {
            var path = clip.Timeline.ResolvePath();
            asset = string.IsNullOrEmpty(path) ? null : TimelineAssetIO.Load(path);
        }
        catch { asset = null; }
        _nested[clip] = asset;
        return asset;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _snapshot.Restore();
        _snapshot.Clear();
        _participants.Clear();
    }
}
