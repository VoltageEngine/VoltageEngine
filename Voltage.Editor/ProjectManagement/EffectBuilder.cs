using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Voltage.Editor.Utils;

namespace Voltage.Editor.ProjectManagement;

/// <summary>
/// Handles compilation of shader effect files (.fx) to compiled effect files (.mgfxo)
/// </summary>
public static class EffectBuilder
{
    public static EffectBuildProgress CurrentProgress { get; private set; }
    
    /// <summary>
    /// Compiles effects for the current game project
    /// </summary>
    /// <param name="projectName">Name of the current project</param>
    /// <returns>True if compilation succeeded, false otherwise</returns>
    public static bool BuildProjectEffects(string projectName)
    {
        try
        {
            // Determine project directory (go up from Editor to find game project)
            string editorDir = AppDomain.CurrentDomain.BaseDirectory;
            string solutionDir = Directory.GetParent(editorDir)?.Parent?.Parent?.Parent?.FullName;
            
            if (string.IsNullOrEmpty(solutionDir))
            {
                Debug.Error("Could not determine solution directory");
                NotificationSystem.ShowTimedNotification("Failed to find project directory!");
                return false;
            }

            // Look for game project directory (assuming it's named differently than Voltage.*)
            var projectDirs = Directory.GetDirectories(solutionDir)
                .Where(d => !Path.GetFileName(d).StartsWith("Voltage.") && 
                           Directory.Exists(Path.Combine(d, "Content")))
                .ToArray();

            if (projectDirs.Length == 0)
            {
                Debug.Warn($"No game project found in solution directory: {solutionDir}");
                NotificationSystem.ShowTimedNotification($"No game project found for '{projectName}'");
                return false;
            }

            string projectDir = projectDirs[0]; // Take first non-Voltage project
            string shaderSrcDir = Path.Combine(projectDir, "ContentSource");
            string shaderOutDir = Path.Combine(projectDir, "Content", "Effects");

            return CompileEffects(shaderSrcDir, shaderOutDir, $"{projectName} Project");
        }
        catch (Exception ex)
        {
            Debug.Error($"Error building project effects: {ex.Message}");
            NotificationSystem.ShowTimedNotification("Failed to build project effects!");
            return false;
        }
    }

    /// <summary>
    /// Compiles effects for the Voltage Engine
    /// </summary>
    /// <returns>True if compilation succeeded, false otherwise</returns>
    public static bool BuildEngineEffects()
    {
        try
        {
            // Engine effects are in Voltage.Engine project
            string editorDir = AppDomain.CurrentDomain.BaseDirectory;
            string solutionDir = Directory.GetParent(editorDir)?.Parent?.Parent?.Parent?.FullName;
            
            if (string.IsNullOrEmpty(solutionDir))
            {
                Debug.Error("Could not determine solution directory");
                NotificationSystem.ShowTimedNotification("Failed to find engine directory!");
                return false;
            }

            string engineDir = Path.Combine(solutionDir, "Voltage.Editor");
            string shaderSrcDir = Path.Combine(engineDir, "DefaultContent");
            string shaderOutDir = Path.Combine(engineDir, "Content", "EngineEffects");

            if (!Directory.Exists(engineDir))
            {
                Debug.Warn($"Engine directory not found: {engineDir}");
                NotificationSystem.ShowTimedNotification("Engine directory not found!");
                return false;
            }

            return CompileEffects(shaderSrcDir, shaderOutDir, "Voltage Engine");
        }
        catch (Exception ex)
        {
            Debug.Error($"Error building engine effects: {ex.Message}");
            NotificationSystem.ShowTimedNotification("Failed to build engine effects!");
            return false;
        }
    }

    /// <summary>
    /// Compiles all effects (both project and engine)
    /// </summary>
    /// <param name="projectName">Name of the current project</param>
    /// <returns>True if all compilations succeeded, false otherwise</returns>
    public static bool BuildAllEffects(string projectName)
    {
        Debug.Log("Building all effects...");
        NotificationSystem.ShowTimedNotification("Building all effects...");

        bool engineSuccess = BuildEngineEffects();
        bool projectSuccess = BuildProjectEffects(projectName);

        if (engineSuccess && projectSuccess)
        {
            NotificationSystem.ShowTimedNotification("Successfully built all effects!");
            return true;
        }
        else if (engineSuccess)
        {
            NotificationSystem.ShowTimedNotification("Engine effects built. Project effects failed or not found.");
            return false;
        }
        else if (projectSuccess)
        {
            NotificationSystem.ShowTimedNotification("Project effects built. Engine effects failed or not found.");
            return false;
        }
        else
        {
            NotificationSystem.ShowTimedNotification("Failed to build effects!");
            return false;
        }
    }

    /// <summary>
    /// Core compilation logic using mgfxc
    /// </summary>
    /// <param name="shaderSrcDir">Source directory containing .fx files</param>
    /// <param name="shaderOutDir">Output directory for .mgfxo files</param>
    /// <param name="contextName">Name for logging purposes</param>
    /// <returns>True if compilation succeeded, false otherwise</returns>
    private static bool CompileEffects(string shaderSrcDir, string shaderOutDir, string contextName)
    {
        if (!Directory.Exists(shaderSrcDir))
        {
            Debug.Warn($"Shader source directory not found: {shaderSrcDir}");
            NotificationSystem.ShowTimedNotification($"No shader source directory found for {contextName}");
            return false;
        }

        Directory.CreateDirectory(shaderOutDir);

        // Find all .fx files recursively
        var shaderFiles = Directory.GetFiles(shaderSrcDir, "*.fx", SearchOption.AllDirectories);

        if (shaderFiles.Length == 0)
        {
            Debug.Log($"No shader files (.fx) found in: {shaderSrcDir}");
            NotificationSystem.ShowTimedNotification($"No shader files found for {contextName}");
            return true; // Not an error, just nothing to compile
        }

        Debug.Log($"Found {shaderFiles.Length} shader file(s) to compile for {contextName}");

        // Initialize progress tracking
        CurrentProgress = new EffectBuildProgress
        {
            TotalFiles = shaderFiles.Length,
            CompletedFiles = 0,
            SuccessCount = 0,
            FailureCount = 0,
            IsComplete = false
        };

        foreach (var shaderFile in shaderFiles)
        {
            string fileName = Path.GetFileNameWithoutExtension(shaderFile);
            string outputFile = Path.Combine(shaderOutDir, fileName + ".mgfxo");

            Debug.Log($"Compiling: {Path.GetFileName(shaderFile)} -> {Path.GetFileName(outputFile)}");
            
            // Update progress - currently compiling this file
            CurrentProgress.UpdateProgress(fileName);

            try
            {
                var processInfo = new ProcessStartInfo
                {
                    FileName = "mgfxc",
                    Arguments = $"\"{shaderFile}\" \"{outputFile}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using (var process = Process.Start(processInfo))
                {
                    if (process == null)
                    {
                        Debug.Error($"Failed to start mgfxc for {fileName}");
                        CurrentProgress.IncrementFailure(fileName);
                        continue;
                    }

                    string output = process.StandardOutput.ReadToEnd();
                    string error = process.StandardError.ReadToEnd();
                    process.WaitForExit();

                    if (process.ExitCode == 0)
                    {
                        Debug.Log($"Successfully compiled: {fileName}");
                        CurrentProgress.IncrementSuccess(fileName);
                    }
                    else
                    {
                        Debug.Error($"Failed to compile {fileName}:");
                        if (!string.IsNullOrEmpty(output))
                            Debug.Error($"Output: {output}");
                        if (!string.IsNullOrEmpty(error))
                            Debug.Error($"Error: {error}");
                        CurrentProgress.IncrementFailure(fileName);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.Error($"Exception while compiling {fileName}: {ex.Message}");
                CurrentProgress.IncrementFailure(fileName);
            }
        }

        // Mark as complete
        CurrentProgress.Complete();

        string resultMessage = $"{contextName}: Compiled {CurrentProgress.SuccessCount}/{shaderFiles.Length} effects";
        if (CurrentProgress.FailureCount > 0)
        {
            resultMessage += $" ({CurrentProgress.FailureCount} failed)";
            Debug.Warn(resultMessage);
        }
        else
        {
            Debug.Log(resultMessage);
        }

        NotificationSystem.ShowTimedNotification(resultMessage);
        return CurrentProgress.FailureCount == 0;
    }

    /// <summary>
    /// Checks if mgfxc is available in the system PATH
    /// </summary>
    /// <returns>True if mgfxc is available, false otherwise</returns>
    public static bool IsMgfxcAvailable()
    {
	    try
	    {
		    var processInfo = new ProcessStartInfo
		    {
			    FileName = "mgfxc",
			    Arguments = "--help",
			    UseShellExecute = false,
			    RedirectStandardOutput = true,
			    RedirectStandardError = true,
			    CreateNoWindow = true
		    };

		    using (var process = Process.Start(processInfo))
		    {
			    if (process == null)
				    return false;

			    string output = process.StandardOutput.ReadToEnd();
			    string error = process.StandardError.ReadToEnd();

			    process.WaitForExit(5000);

			    // mgfxc outputs to stderr, check combined output for signature text
			    string combined = output + error;
			    bool available = combined.Contains("MonoGame Effect compiler");

			    if (available)
				    Debug.Log("mgfxc is available");

			    return available;
		    }
	    }
	    catch (System.ComponentModel.Win32Exception)
	    {
		    Debug.Error("mgfxc not found in PATH");
		    return false;
	    }
	    catch (Exception ex)
	    {
		    Debug.Error($"Error checking mgfxc: {ex.Message}");
		    return false;
	    }
    }

    /// <summary>
    /// Clears the current progress tracking
    /// </summary>
    public static void ClearProgress()
    {
	    CurrentProgress = null;
    }
}
