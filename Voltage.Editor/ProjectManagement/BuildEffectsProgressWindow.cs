using ImGuiNET;
using System;
using Voltage.Editor.ImGuiCore;
using Voltage.Editor.Utils;
using Voltage.Utils;
using Num = System.Numerics;

namespace Voltage.Editor.ProjectManagement;

/// <summary>
/// ImGui window that displays progress of effect compilation as a modal popup
/// </summary>
public class BuildEffectsProgressWindow
{
    private bool _isOpen = false;
    private bool _wasBuilding = false;
    private bool _modalOpened = false;
	private ImGuiManager _imGuiManager;
	/// <summary>
	/// Gets whether the progress window is currently open
	/// </summary>
	public bool IsOpen => _isOpen;

    /// <summary>
    /// Shows the progress window
    /// </summary>
    public void Show()
    {
        _isOpen = true;
        _modalOpened = false;
    }

    /// <summary>
    /// Hides the progress window
    /// </summary>
    public void Hide()
    {
        _isOpen = false;
        _wasBuilding = false;
        _modalOpened = false;
        EffectBuilder.ClearProgress();
    }

    /// <summary>
    /// Draws the progress window as a modal popup. Call this in your main GUI loop (e.g., LayoutGui)
    /// </summary>
    public void Draw()
    {
	    if (_imGuiManager == null)
	    {
		    _imGuiManager = Core.GetGlobalManager<ImGuiManager>();
	    }

        var progress = EffectBuilder.CurrentProgress;
        
        if (progress != null && !_wasBuilding)
        {
            Show();
            _wasBuilding = true;
        }
        
        if (!_isOpen)
            return;

        if (progress == null)
        {
            Hide();
            return;
        }

        if (!_modalOpened)
        {
            ImGui.OpenPopup("Building Effects");
            _modalOpened = true;
        }

        var center = new Num.Vector2(Screen.Width * 0.5f, Screen.Height * 0.5f);
        ImGui.SetNextWindowPos(center, ImGuiCond.Always, new Num.Vector2(0.5f, 0.5f));
        ImGui.SetNextWindowSize(new Num.Vector2(600, 0), ImGuiCond.Always);

        ImGuiWindowFlags flags = ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove | 
                                 ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.AlwaysAutoResize;

        bool open = true;
        
        if (ImGui.BeginPopupModal("Building Effects", ref open, flags))
        {
            DrawTitle(progress);
            DrawProgressBar(progress);
            DrawCurrentFileStatus(progress);
            DrawStatistics(progress);
            DrawActionButton(progress);
            
            ImGui.EndPopup();
        }

        if (progress.IsComplete && !open)
        {
            Hide();
        }
    }

    /// <summary>
    /// Draws the title with build status
    /// </summary>
    private void DrawTitle(EffectBuildProgress progress)
    {
        if (!progress.IsComplete)
        {
            ImGui.TextColored(new Num.Vector4(0.3f, 0.7f, 1.0f, 1.0f), "Compiling shader effects...");
        }
        else
        {
            if (progress.FailureCount > 0)
            {
                ImGui.TextColored(new Num.Vector4(1.0f, 0.6f, 0.0f, 1.0f), "Compilation completed with errors");
            }
            else
            {
                ImGui.TextColored(new Num.Vector4(0.0f, 1.0f, 0.0f, 1.0f), "Compilation complete!");
            }
        }
        
        ImGui.Spacing();
    }

    /// <summary>
    /// Draws the horizontal progress bar
    /// </summary>
    private void DrawProgressBar(EffectBuildProgress progress)
    {
        float progressValue = progress.Progress;
        string progressText = $"{progress.CompletedFiles} / {progress.TotalFiles} ({(int)(progressValue * 100)}%)";
        
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 4.0f);
        
        if (!progress.IsComplete)
        {
            // Blue progress bar while building
            ImGui.PushStyleColor(ImGuiCol.PlotHistogram, new Num.Vector4(0.2f, 0.5f, 1.0f, 1.0f));
        }
        else if (progress.FailureCount > 0)
        {
            // Orange/Yellow for completed with errors
            ImGui.PushStyleColor(ImGuiCol.PlotHistogram, new Num.Vector4(1.0f, 0.6f, 0.0f, 1.0f));
        }
        else
        {
            // Green for successful completion
            ImGui.PushStyleColor(ImGuiCol.PlotHistogram, new Num.Vector4(0.0f, 0.8f, 0.2f, 1.0f));
        }
        
        ImGui.ProgressBar(progressValue, new Num.Vector2(-1, 30), progressText);
        
        ImGui.PopStyleColor();
        ImGui.PopStyleVar();
        
        ImGui.Spacing();
    }

    /// <summary>
    /// Draws the current file being compiled or completion status
    /// </summary>
    private void DrawCurrentFileStatus(EffectBuildProgress progress)
    {
        if (!progress.IsComplete)
        {
            ImGui.TextDisabled("Currently compiling:");
            ImGui.Indent(20);
            ImGui.TextWrapped(progress.CurrentFile);
            ImGui.Unindent(20);
            
            DrawSpinner();
        }
        else
        {
            if (progress.FailureCount > 0)
            {
                ImGui.TextColored(new Num.Vector4(1.0f, 0.4f, 0.0f, 1.0f), 
                    $"{progress.FailureCount} effect(s) failed to compile");
                ImGui.TextDisabled("Check the Debug window for error details");
            }
            else
            {
                ImGui.TextColored(new Num.Vector4(0.0f, 1.0f, 0.0f, 1.0f), 
                    "All effects compiled successfully!");
            }
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
    }

    /// <summary>
    /// Draws success and failure statistics
    /// </summary>
    private void DrawStatistics(EffectBuildProgress progress)
    {
        // Create a columns layout for statistics
        ImGui.Columns(2, "stats", false);

        ImGui.Text($"Success: {progress.SuccessCount}");
        
        ImGui.NextColumn();
        
        if (progress.FailureCount > 0)
        {
            ImGui.TextColored(new Num.Vector4(1.0f, 0.4f, 0.0f, 1.0f), $" Failed: {progress.FailureCount}");
        }
        else
        {
            ImGui.TextDisabled($"Failed: {progress.FailureCount}");
        }
        
        ImGui.Columns(1);
        ImGui.Spacing();
    }

    /// <summary>
    /// Draws the action button (only close button when complete)
    /// </summary>
    private void DrawActionButton(EffectBuildProgress progress)
    {
        if (progress.IsComplete)
        {
            float buttonWidth = 120 * _imGuiManager.FontSizeMultiplier;
            float windowWidth = ImGui.GetContentRegionAvail().X;
            float buttonPosX = (windowWidth + buttonWidth) * 0.5f;
            
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + buttonPosX);
            
            if (progress.FailureCount > 0)
            {
                ImGui.PushStyleColor(ImGuiCol.Button, new Num.Vector4(1.0f, 0.6f, 0.0f, 0.8f));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Num.Vector4(1.0f, 0.7f, 0.2f, 1.0f));
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Num.Vector4(1.0f, 0.5f, 0.0f, 1.0f));
            }
            else
            {
                ImGui.PushStyleColor(ImGuiCol.Button, new Num.Vector4(0.0f, 0.7f, 0.2f, 0.8f));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Num.Vector4(0.0f, 0.8f, 0.3f, 1.0f));
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Num.Vector4(0.0f, 0.6f, 0.1f, 1.0f));
            }
            
            if (ImGui.Button("Close", new Num.Vector2(buttonWidth, 30)))
            {
                Hide();
            }
            
            ImGui.PopStyleColor(3);
        }
        else
        {
            ImGui.PushStyleColor(ImGuiCol.Text, new Num.Vector4(0.6f, 0.6f, 0.6f, 1.0f));
			string text = "Please wait, compiling effects...";
			float textWidth = ImGui.CalcTextSize(text).X;
            float windowWidth = ImGui.GetContentRegionAvail().X;
            float textPosX = (windowWidth - textWidth) * 0.5f;
            
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + textPosX);
            ImGui.Text(text);
            ImGui.PopStyleColor();
        }
    }

    /// <summary>
    /// Draws an animated spinner
    /// </summary>
    private void DrawSpinner()
    {
        float time = (float)ImGui.GetTime();
        string spinnerText = GetSpinnerChar(time);
        
        ImGui.SameLine();
        ImGui.TextColored(new Num.Vector4(0.3f, 0.7f, 1.0f, 1.0f), spinnerText);
    }

    /// <summary>
    /// Gets a spinning character for animation
    /// </summary>
    private string GetSpinnerChar(float time)
    {
        string[] spinner = { "⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏" };
        int index = (int)(time * 8) % spinner.Length;
        return spinner[index];
    }
}