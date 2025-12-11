using System;

namespace Voltage.Editor.ProjectManagement;

/// <summary>
/// Tracks the progress of effect compilation
/// </summary>
public class EffectBuildProgress
{
    public int TotalFiles { get; set; }
    public int CompletedFiles { get; set; }
    public int SuccessCount { get; set; }
    public int FailureCount { get; set; }
    public string CurrentFile { get; set; }
    public bool IsComplete { get; set; }
    
    public float Progress => TotalFiles > 0 ? (float)CompletedFiles / TotalFiles : 0f;
    
    public event Action<EffectBuildProgress> OnProgressChanged;
    
    public void UpdateProgress(string currentFile)
    {
        CurrentFile = currentFile;
        OnProgressChanged?.Invoke(this);
    }
    
    public void IncrementSuccess(string fileName)
    {
        CompletedFiles++;
        SuccessCount++;
        CurrentFile = fileName;
        OnProgressChanged?.Invoke(this);
    }
    
    public void IncrementFailure(string fileName)
    {
        CompletedFiles++;
        FailureCount++;
        CurrentFile = fileName;
        OnProgressChanged?.Invoke(this);
    }
    
    public void Complete()
    {
        IsComplete = true;
        OnProgressChanged?.Invoke(this);
    }
}