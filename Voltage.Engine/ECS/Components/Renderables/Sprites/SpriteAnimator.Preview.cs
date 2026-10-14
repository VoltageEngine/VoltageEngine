using System;
using System.Collections.Generic;
using Voltage.Cinematics;

namespace Voltage.Sprites;

public partial class SpriteAnimator
{
    /// <summary>Captures exact playback state without invoking animation callbacks on restore.</summary>
    public void CapturePreviewState(StateSnapshot snapshot)
    {
        var animation = CurrentAnimation;
        var name = CurrentAnimationName;
        var frame = CurrentFrame;
        var sprite = Sprite;
        var origin = Origin;
        var count = FrameCount;
        var elapsed = CurrentElapsedTime;
        var left = FrameTimeLeft;
        var loop = CurrentLoopMode;
        var state = AnimationState;
        var ping = PingPongLoopState;
        var started = _pingPongOnceAnimationStarted;
        snapshot.Add(() =>
        {
            CurrentAnimation = animation;
            CurrentAnimationName = name;
            CurrentFrame = frame;
            Sprite = sprite;
            Origin = origin;
            FrameCount = count;
            CurrentElapsedTime = elapsed;
            FrameTimeLeft = left;
            CurrentLoopMode = loop;
            AnimationState = state;
            PingPongLoopState = ping;
            _pingPongOnceAnimationStarted = started;
        });
    }

    /// <summary>Samples a clip deterministically without animation events or completion callbacks.</summary>
    public bool SamplePreview(string name, LoopMode loop, float elapsed)
    {
        if (name == null || !Animations.TryGetValue(name, out var animation) ||
            animation.Sprites.Length == 0 || animation.FrameRates.Length != animation.Sprites.Length)
            return false;
        var frames = new List<int>();
        for (var i = 0; i < animation.Sprites.Length; i++) frames.Add(i);
        if (loop == LoopMode.PingPong || loop == LoopMode.PingPongOnce)
            for (var i = animation.Sprites.Length - 2; i > 0; i--) frames.Add(i);
        double duration = 0;
        foreach (var frame in frames)
        {
            var rate = animation.FrameRates[frame] * Math.Abs(Speed);
            if (!float.IsFinite(rate) || rate <= 0) return false;
            duration += 1.0 / rate;
        }
        var sample = Math.Max(0, float.IsFinite(elapsed) ? elapsed : 0);
        var local = (double)sample;
        if (loop == LoopMode.Loop || loop == LoopMode.PingPong) local %= duration;
        else if (local >= duration)
            local = loop == LoopMode.Once || loop == LoopMode.PingPongOnce ? 0 : Math.BitDecrement(duration);
        var index = frames[frames.Count - 1];
        foreach (var frame in frames)
        {
            var span = 1.0 / (animation.FrameRates[frame] * Math.Abs(Speed));
            index = frame;
            if (local < span) break;
            local -= span;
        }
        CurrentAnimation = animation;
        CurrentAnimationName = name;
        FrameCount = animation.Sprites.Length;
        CurrentLoopMode = loop;
        CurrentElapsedTime = sample;
        AnimationState = State.Paused;
        SetFrame(index);
        FrameTimeLeft -= (float)local;
        return true;
    }
}
