using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Voltage.Editor.DebugUtils;
using Voltage.Editor.ProjectFile;
using Voltage.Editor.Utils;

namespace Voltage.Editor.Effects;

public class EffectsCompiler
{
	public static EffectsCompileProgress CurrentProgress { get; private set; }
	private static System.Threading.CancellationToken _currentCancellationToken;
	private static Process _currentProcess;
	private static readonly object _processLock = new object();

	// Events for progress tracking
	public static event Action<int> OnBuildStarted;
	public static event Action<string> OnFileCompiling;
	public static event Action<string, bool> OnFileCompiled;
	public static event Action<int, int> OnBuildCompleted;

	/// <summary>
	/// Compiles effects for the current game project
	/// </summary>
	/// <param name="project">The game project to build effects for</param>
	/// <returns>True if compilation succeeded, false otherwise</returns>
	private static bool BuildProjectEffects(IGameProject project)
	{
		if (project == null)
		{
			Debug.Error("Project cannot be null");
			EditorDebug.Log("No project provided for effect building!");
			return false;
		}

		try
		{
			string effectsFolder = project.EffectsFolder;

			if (string.IsNullOrEmpty(effectsFolder) || !Directory.Exists(effectsFolder))
			{
				Debug.Warn($"Effects folder not found for project '{project.ProjectName}': {effectsFolder}");
				EditorDebug.Log($"No effects folder found for '{project.ProjectName}'");
				return false;
			}

			// Source directory is the EffectsFolder from project
			string shaderSrcDir = effectsFolder;

			// Output directory is Content/Effects within the project
			string contentEffectsDir = Path.Combine(project.ContentsFolder, "Effects");

			Debug.Log($"Building effects for project: {project.ProjectName}");
			Debug.Log($"Source directory: {shaderSrcDir}");
			Debug.Log($"Output directory: {contentEffectsDir}");

			return CompileEffects(shaderSrcDir, contentEffectsDir, $"{project.ProjectName} Project");
		}
		catch (Exception ex)
		{
			Debug.Error($"Error building project effects: {ex.Message}");
			EditorDebug.Log("Failed to build project effects!");
			return false;
		}
	}

	/// <summary>
	/// Compiles effects for the Voltage Engine
	/// </summary>
	/// <returns>True if compilation succeeded, false otherwise</returns>
	private static bool BuildEngineEffects()
	{
		try
		{
			// Engine effects are in Voltage.Engine project
			string editorDir = AppDomain.CurrentDomain.BaseDirectory;
			string solutionDir = Directory.GetParent(editorDir)?.Parent?.Parent?.Parent?.FullName;

			if (string.IsNullOrEmpty(solutionDir))
			{
				Debug.Error("Could not determine solution directory");
				EditorDebug.Log("Failed to find engine directory!");
				return false;
			}

			string engineDir = Path.Combine(solutionDir, "Voltage.Editor");
			string shaderSrcDir = Path.Combine(engineDir, "DefaultContent");
			string shaderOutDir = Path.Combine(engineDir, "Content", "Voltage");

			if (!Directory.Exists(engineDir))
			{
				Debug.Warn($"Engine directory not found: {engineDir}");
				EditorDebug.Log("Engine directory not found!");
				return false;
			}

			return CompileEffects(shaderSrcDir, shaderOutDir, "Voltage Engine");
		}
		catch (Exception ex)
		{
			Debug.Error($"Error building engine effects: {ex.Message}");
			EditorDebug.Log("Failed to build engine effects!");
			return false;
		}
	}

	/// <summary>
	/// Compiles all effects (both project and engine)
	/// </summary>
	/// <param name="project">The game project to build effects for</param>
	/// <returns>True if all compilations succeeded, false otherwise</returns>
	private static bool BuildAllEffects(IGameProject project)
	{
		if (project == null)
		{
			Debug.Error("Project cannot be null");
			EditorDebug.Log("No project provided for effect building!");
			return false;
		}

		Debug.Log("Building all effects...");
		EditorDebug.Log("Building all effects...");

		bool engineSuccess = BuildEngineEffects();
		bool projectSuccess = BuildProjectEffects(project);

		if (engineSuccess && projectSuccess)
		{
			EditorDebug.Log("Successfully built all effects!");
			return true;
		}
		else if (engineSuccess)
		{
			EditorDebug.Log("Engine effects built. Project effects failed or not found.");
			return false;
		}
		else if (projectSuccess)
		{
			EditorDebug.Log("Project effects built. Engine effects failed or not found.");
			return false;
		}
		else
		{
			EditorDebug.Log("Failed to build effects!");
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
			EditorDebug.Log($"No shader source directory found for {contextName}");
			return false;
		}

		Directory.CreateDirectory(shaderOutDir);

		// Find all .fx files recursively
		var shaderFiles = Directory.GetFiles(shaderSrcDir, "*.fx", SearchOption.AllDirectories);

		if (shaderFiles.Length == 0)
		{
			Debug.Log($"No shader files (.fx) found in: {shaderSrcDir}");
			EditorDebug.Log($"No shader files found for {contextName}");
			return true; // Not an error, just nothing to compile
		}

		Debug.Log($"Found {shaderFiles.Length} shader file(s) to compile for {contextName}");

		// Initialize progress tracking
		CurrentProgress = new EffectsCompileProgress
		{
			TotalFiles = shaderFiles.Length,
			CompletedFiles = 0,
			SuccessCount = 0,
			FailureCount = 0,
			IsComplete = false
		};

	OnBuildStarted?.Invoke(shaderFiles.Length);

	foreach (var shaderFile in shaderFiles)
	{
		// Check for cancellation
		if (_currentCancellationToken.IsCancellationRequested)
		{
			// Kill any running mgfxc process
			lock (_processLock)
			{
				if (_currentProcess != null && !_currentProcess.HasExited)
				{
					try
					{
						_currentProcess.Kill();
						Debug.Log("Killed running mgfxc process");
					}
					catch (Exception ex)
					{
						Debug.Warn($"Failed to kill mgfxc process: {ex.Message}");
					}
				}
			}

			Debug.Log($"Effect build cancelled for {contextName}");
			EditorDebug.Log($"Effect build cancelled for {contextName}");
			OnBuildCompleted?.Invoke(CurrentProgress.SuccessCount, CurrentProgress.FailureCount);
			return false;
		}

		// Get relative path from source directory to preserve subdirectory structure
		string relativePath = Path.GetRelativePath(shaderSrcDir, shaderFile);
		string relativeDir = Path.GetDirectoryName(relativePath) ?? string.Empty;
		string fileName = Path.GetFileNameWithoutExtension(shaderFile);

			// Create subdirectory in output if needed
			string outputSubDir = Path.Combine(shaderOutDir, relativeDir);
			Directory.CreateDirectory(outputSubDir);

			string outputFile = Path.Combine(outputSubDir, fileName + ".mgfxo");

			Debug.Log($"Compiling: {relativePath} -> {Path.GetRelativePath(shaderOutDir, outputFile)}");

			// Update progress - currently compiling this file
			CurrentProgress.UpdateProgress(relativePath);

			OnFileCompiling?.Invoke(Path.GetFileName(relativePath));

		bool success = false;
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
					Debug.Error($"Failed to start mgfxc for {relativePath}");
					CurrentProgress.IncrementFailure(relativePath);
				}
				else
				{
					// Track the current process so we can kill it on cancellation
					lock (_processLock)
					{
						_currentProcess = process;
					}

					try
					{
						string output = process.StandardOutput.ReadToEnd();
						string error = process.StandardError.ReadToEnd();
						process.WaitForExit();

						// Check if we were cancelled while waiting
						if (_currentCancellationToken.IsCancellationRequested)
						{
							Debug.Log($"Compilation cancelled during {relativePath}");
							return false;
						}

						if (process.ExitCode == 0)
						{
							Debug.Log($"Successfully compiled: {relativePath}");
							CurrentProgress.IncrementSuccess(relativePath);
							success = true;
						}
						else
						{
							Debug.Error($"Failed to compile {relativePath} (Exit code: {process.ExitCode}):");
							if (!string.IsNullOrEmpty(output))
								Debug.Error($"Standard Output:\n{output}");
							if (!string.IsNullOrEmpty(error))
								Debug.Error($"Standard Error:\n{error}");
							CurrentProgress.IncrementFailure(relativePath);
						}
					}
					finally
					{
						// Clear the current process reference
						lock (_processLock)
						{
							if (_currentProcess == process)
							{
								_currentProcess = null;
							}
						}
					}
				}
			}
		}
		catch (System.ComponentModel.Win32Exception win32Ex)
		{
			Debug.Error($"Failed to execute mgfxc for {relativePath}. Make sure mgfxc is installed and in PATH.");
			Debug.Error($"Win32 Error: {win32Ex.Message}");
			CurrentProgress.IncrementFailure(relativePath);
		}
		catch (Exception ex)
		{
			Debug.Error($"Exception while compiling {relativePath}: {ex.Message}");
			Debug.Error($"Stack trace: {ex.StackTrace}");
			CurrentProgress.IncrementFailure(relativePath);
		}

			OnFileCompiled?.Invoke(Path.GetFileName(relativePath), success);
		}

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

		EditorDebug.Log(resultMessage);

		OnBuildCompleted?.Invoke(CurrentProgress.SuccessCount, CurrentProgress.FailureCount);

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
				{
					Debug.Error("Failed to start mgfxc process");
					return false;
				}

				string output = process.StandardOutput.ReadToEnd();
				string error = process.StandardError.ReadToEnd();

				process.WaitForExit(5000);

				// mgfxc outputs to stderr, check combined output for signature text
				string combined = output + error;
				bool available = combined.Contains("MonoGame Effect compiler");

				if (available)
				{
					Debug.Log("mgfxc is available and working");
				}
				else
				{
					Debug.Warn($"mgfxc found but output doesn't match expected pattern. Output: {combined.Substring(0, Math.Min(200, combined.Length))}");
				}

				return available;
			}
		}
		catch (System.ComponentModel.Win32Exception win32Ex)
		{
			Debug.Error($"mgfxc not found in PATH. Win32Exception: {win32Ex.Message}");
			Debug.Error("On macOS, make sure MonoGame is installed via: dotnet tool install -g dotnet-mgfxc");
			return false;
		}
		catch (Exception ex)
		{
			Debug.Error($"Error checking mgfxc: {ex.Message}");
			Debug.Error($"Stack trace: {ex.StackTrace}");
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

	#region Editor Effect Builder Methods

	/// <summary>
	/// Builds effects for the current project.
	/// </summary>
	public static void BuildEditorProjectEffects(ProjectManager projectManager,
		EffectsCompileProgressWindow progressWindow, ref System.Threading.CancellationTokenSource buildCancellationToken)
	{
		if (!projectManager.HasActiveProject)
		{
			Debug.Error("No active project loaded!");
			return;
		}

		if (!IsMgfxcAvailable())
		{
			Debug.Error("mgfxc compiler not found in PATH. Install MonoGame SDK: https://www.monogame.net/downloads/");
			return;
		}

	var project = projectManager.CurrentProject;
	
	// Cancel any existing build
	buildCancellationToken?.Cancel();
	buildCancellationToken?.Dispose();
	buildCancellationToken = new System.Threading.CancellationTokenSource();
	
	// Store the cancellation token for the build process
	_currentCancellationToken = buildCancellationToken.Token;
	
	// Pass the cancellation token to the progress window
	progressWindow.SetCancellationToken(buildCancellationToken);
	progressWindow.Show();

	// Capture the token in a local variable for use in lambda
	var token = buildCancellationToken.Token;
	System.Threading.Tasks.Task.Run(() =>
	{
		try
		{
			token.ThrowIfCancellationRequested();
			bool success = BuildProjectEffects(project);

			if (success)
			{
				EditorDebug.Log($"Successfully built {project.ProjectName} effects");
			}
		}
		catch (OperationCanceledException)
		{
			Debug.Log($"Build cancelled for {project.ProjectName}");
		}
		catch (Exception ex)
		{
			Debug.Error($"Error during build: {ex.Message}");
		}
	}, token);
	}

	/// <summary>
	/// Builds effects for the Voltage Engine.
	/// </summary>
	public static void BuildEditorEngineEffects(EffectsCompileProgressWindow progressWindow,
		ref System.Threading.CancellationTokenSource buildCancellationToken)
	{
		if (!IsMgfxcAvailable())
		{
			Debug.Error("mgfxc compiler not found in PATH. Install MonoGame SDK: https://www.monogame.net/downloads/");
			return;
		}

	// Cancel any existing build
	buildCancellationToken?.Cancel();
	buildCancellationToken?.Dispose();
	buildCancellationToken = new System.Threading.CancellationTokenSource();
	
	// Store the cancellation token for the build process
	_currentCancellationToken = buildCancellationToken.Token;
	
	// Pass the cancellation token to the progress window
	progressWindow.SetCancellationToken(buildCancellationToken);
	progressWindow.Show();

	// Capture the token in a local variable for use in lambda
	var token = buildCancellationToken.Token;
	System.Threading.Tasks.Task.Run(() =>
	{
		try
		{
			token.ThrowIfCancellationRequested();
			bool success = BuildEngineEffects();
			if (success)
			{
				EditorDebug.Log("Successfully built Engine effects");
			}
		}
		catch (OperationCanceledException)
		{
			Debug.Log("Engine effects build cancelled");
		}
		catch (Exception ex)
		{
			Debug.Error($"Error during build: {ex.Message}");
		}
	}, token);
	}

	/// <summary>
	/// Builds all effects (both project and engine).
	/// </summary>
	public static void BuildEditorAllEffects(ProjectManager projectManager, EffectsCompileProgressWindow progressWindow,
		ref System.Threading.CancellationTokenSource buildCancellationToken)
	{
		if (!projectManager.HasActiveProject)
		{
			EditorDebug.Log("No active project loaded!");
			return;
		}

		if (!IsMgfxcAvailable())
		{
			Debug.Error("mgfxc compiler not found in PATH. Install MonoGame SDK: https://www.monogame.net/downloads/");
			return;
		}

	var project = projectManager.CurrentProject;
	
	// Cancel any existing build
	buildCancellationToken?.Cancel();
	buildCancellationToken?.Dispose();
	buildCancellationToken = new System.Threading.CancellationTokenSource();
	
	// Store the cancellation token for the build process
	_currentCancellationToken = buildCancellationToken.Token;
	
	// Pass the cancellation token to the progress window
	progressWindow.SetCancellationToken(buildCancellationToken);
	progressWindow.Show();

	// Capture the token in a local variable for use in lambda
	var token = buildCancellationToken.Token;
	System.Threading.Tasks.Task.Run(() =>
	{
		try
		{
			token.ThrowIfCancellationRequested();
			bool success = BuildAllEffects(project);

			if (success)
			{
				EditorDebug.Log("Successfully built all effects");
			}
		}
		catch (OperationCanceledException)
		{
			Debug.Log("Build all effects cancelled");
		}
		catch (Exception ex)
		{
			Debug.Error($"Error during build: {ex.Message}");
		}
	}, token);
	}

	#endregion
}
