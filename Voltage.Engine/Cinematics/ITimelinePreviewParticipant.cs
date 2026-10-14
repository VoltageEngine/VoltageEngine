namespace Voltage.Cinematics;

/// <summary>Opt-in reversible presentation sampling without gameplay events or component updates.</summary>
public interface ITimelinePreviewParticipant
{
    void CaptureTimelinePreview(StateSnapshot snapshot);
    void EvaluateTimelinePreview(float time, TimelineAsset asset, TimelineDirector director);
}
