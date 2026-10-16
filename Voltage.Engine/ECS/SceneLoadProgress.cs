using System;

namespace Voltage;

/// <summary>Monotonic progress across eight completed loading phases, with readiness reserved for presentation.</summary>
public sealed class SceneLoadProgress
{
    public const int PhaseCount = 8;
    public int CompletedPhases { get; private set; }
    public int Percent { get; private set; }
    public bool Updated { get; private set; }
    public bool Ready { get; private set; }
    public string Error { get; private set; }
    public void CompletePhase(int phase)
    {
        if (Error != null || Ready) return;
        CompletedPhases = Math.Max(CompletedPhases, Math.Clamp(phase + 1, 0, PhaseCount - 1));
        Percent = Math.Max(Percent, CompletedPhases * 100 / PhaseCount);
    }
    public void Items(int phase, int completed, int total)
    {
        if (Error != null || Ready || total <= 0) return;
        Percent = Math.Max(Percent, Math.Clamp((int)((phase + Math.Clamp(completed / (double)total, 0, 1)) * 100 / PhaseCount), 0, 99));
    }
    public void MarkUpdated() { Updated = true; CompletePhase(6); }
    public void MarkPresented()
    {
        if (!Updated || Error != null) return;
        Percent = 100; CompletedPhases = PhaseCount; Ready = true;
    }
    public void Fail(string error) { Error = error ?? "Scene load failed"; Ready = false; }
}
