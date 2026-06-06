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
			return false;
		}

		try
		{
			string effectsFolder = project.EffectsFolder;

			if (string.IsNullOrEmpty(effectsFolder) || !Directory.Exists(effectsFolder))
			{
				Debug.Warn($"Effects folder not found for project '{project.ProjectName}': {effectsFolder}");
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
			// Walk up from the running assembly until we find the solution root.
			// Using a fixed Parent-chain count is fragile: Windows builds include
			// a RID sub-folder (.../bin/Editor-Debug/net8.0/win-x64/) while macOS
			// does not (.../bin/Editor-Debug/net8.0/), so the depth differs by one
			// and the old code landed inside Voltage.Editor/ instead of above it.
			string solutionDir = FindSolutionDirectory();

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
				FileName = FindMgfxcExecutable(),
				Arguments = BuildMgfxcArguments(shaderFile, outputFile),
				UseShellExecute = false,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				CreateNoWindow = true
			};

			// On macOS/Linux mgfxc uses Wine internally to call the DirectX shader
			// compiler (WineHelper.cs in MonoGame). WineHelper.SetupWine() reads the
			// MGFXC_WINE_PATH environment variable to locate the Wine prefix. This
			// variable is written to ~/.zprofile by the wine setup script, but
			// GUI-launched apps do NOT source shell profiles, so the variable is
			// missing when the editor is not started from a terminal that sourced it.
			// We therefore inject it explicitly so mgfxc always finds the prefix.
			InjectMacOsWineEnvironment(processInfo);

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

							// Provide specific guidance for common macOS/Wine failures
							string combined = output + error;
							if (!System.Runtime.InteropServices.RuntimeInformation
									.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
							{
								if (combined.Contains("MGFXC_WINE_PATH") ||
									combined.Contains("MGFXC0001") ||
									combined.Contains("Wine installation"))
								{
									Debug.Error(
										"Wine setup is missing or incomplete. " +
										"Run the MonoGame Wine setup script:\n" +
										"  curl -fsSL https://raw.githubusercontent.com/MonoGame/MonoGame/develop/Tools/MonoGame.Effect.Compiler/mgfxc_wine_setup.sh | bash\n" +
										"Requires: wine64, 7z (p7zip). See https://docs.monogame.net/errors/mgfx0001?tab=macos");
								}
								else if (combined.Contains("Failed to resolve full path") ||
										 combined.Contains("dotnet.exe"))
								{
									Debug.Error(
										"The Wine prefix appears to have an incorrect .NET installation (likely installed with the 'master' branch script). " +
										"Fix: delete ~/.winemonogame and re-run the setup script from the 'develop' branch:\n" +
										"  rm -rf ~/.winemonogame\n" +
										"  curl -fsSL https://raw.githubusercontent.com/MonoGame/MonoGame/develop/Tools/MonoGame.Effect.Compiler/mgfxc_wine_setup.sh | bash");
								}
							}

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
				FileName = FindMgfxcExecutable(),
				Arguments = "--help",
				UseShellExecute = false,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				CreateNoWindow = true
			};

			// Inject Wine env vars so the --help call also validates the Wine setup
			InjectMacOsWineEnvironment(processInfo);

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

	/// <summary>
	/// Builds the argument string for mgfxc.
	/// On macOS/Linux the /Profile:OpenGL flag is appended so shader output targets
	/// the OpenGL/DesktopGL backend used by FNA/MonoGame on those platforms.
	/// </summary>
	private static string BuildMgfxcArguments(string shaderFile, string outputFile)
	{
		bool isWindows = System.Runtime.InteropServices.RuntimeInformation
			.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows);

		if (isWindows)
			return $"\"{shaderFile}\" \"{outputFile}\"";

		// macOS / Linux: compile for the OpenGL profile
		return $"\"{shaderFile}\" \"{outputFile}\" /Profile:OpenGL";
	}

	/// <summary>
	/// Injects the Wine-related environment variables required by mgfxc on macOS/Linux.
	///
	/// Why this is needed (from the MonoGame community thread
	/// https://community.monogame.net/t/using-fx-files-on-mac-m1/18428):
	///
	/// mgfxc uses a WineHelper class that reads MGFXC_WINE_PATH to locate the Wine
	/// prefix (~/.winemonogame).  The setup script appends this variable to
	/// ~/.zprofile, but macOS GUI apps (launched from the Dock or Finder) never
	/// source shell profiles, so the variable is absent from the editor's process
	/// environment.  We therefore set it explicitly so the child mgfxc process
	/// always finds the prefix, regardless of how the editor was launched.
	///
	/// Prerequisites (one-time setup):
	///   1. Install Wine (brew install --cask wine-stable) and p7zip.
	///   2. Run the MonoGame setup script (use the develop branch, NOT master —
	///      the master script installed x86 .NET which fails on Apple Silicon M1/M2/M3):
	///        curl -fsSL https://raw.githubusercontent.com/MonoGame/MonoGame/develop/Tools/MonoGame.Effect.Compiler/mgfxc_wine_setup.sh | bash
	/// </summary>
	private static void InjectMacOsWineEnvironment(ProcessStartInfo processInfo)
	{
		bool isWindows = System.Runtime.InteropServices.RuntimeInformation
			.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows);

		if (isWindows)
			return;

		var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
		var winePrefix = Path.Combine(homeDir, ".winemonogame");

		// Only inject when the prefix directory actually exists; if it's missing the
		// user will get the standard MGFXC0001 error from mgfxc with a docs URL.
		if (!Directory.Exists(winePrefix))
		{
			Debug.Warn(
				$"Wine prefix not found at {winePrefix}. " +
				"Run the MonoGame Wine setup script to enable shader compilation on macOS:\n" +
				"  brew install --cask wine-stable p7zip\n" +
				"  curl -fsSL https://raw.githubusercontent.com/MonoGame/MonoGame/develop/Tools/MonoGame.Effect.Compiler/mgfxc_wine_setup.sh | bash");
			return;
		}

		// Set MGFXC_WINE_PATH only if it isn't already present (respect user overrides)
		if (!processInfo.EnvironmentVariables.ContainsKey("MGFXC_WINE_PATH"))
			processInfo.EnvironmentVariables["MGFXC_WINE_PATH"] = winePrefix;

		// Also ensure WINEPREFIX is set so Wine itself uses the right prefix
		if (!processInfo.EnvironmentVariables.ContainsKey("WINEPREFIX"))
			processInfo.EnvironmentVariables["WINEPREFIX"] = winePrefix;

		// Ensure the wine binaries in /usr/local/bin are in PATH so mgfxc's
		// WineHelper can locate wine/wine64 via `which`.
		string existingPath;
		if (processInfo.EnvironmentVariables.ContainsKey("PATH"))
			existingPath = processInfo.EnvironmentVariables["PATH"] ?? string.Empty;
		else
			existingPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;

		if (!existingPath.Contains("/usr/local/bin"))
		{
			processInfo.EnvironmentVariables["PATH"] =
				"/usr/local/bin:" + existingPath;
		}
	}

	/// <summary>
	/// Returns true when the Wine prefix required by mgfxc looks complete on macOS.
	/// Checks for the presence of dotnet.exe and d3dcompiler_47.dll that the
	/// mgfxc_wine_setup.sh script installs.
	/// </summary>
	public static bool IsWineSetupComplete()
	{
		bool isWindows = System.Runtime.InteropServices.RuntimeInformation
			.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows);

		if (isWindows)
			return true; // Wine not needed on Windows

		var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
		var system32 = Path.Combine(homeDir, ".winemonogame", "drive_c", "windows", "system32");

		bool hasDotnet = File.Exists(Path.Combine(system32, "dotnet.exe"));
		bool hasD3d = File.Exists(Path.Combine(system32, "d3dcompiler_47.dll"));

		if (!hasDotnet || !hasD3d)
		{
			Debug.Warn(
				"Wine prefix is incomplete (missing dotnet.exe or d3dcompiler_47.dll). " +
				"Delete ~/.winemonogame and re-run the setup script using the 'develop' branch " +
				"(the 'master' branch script installed x86 .NET which fails on Apple Silicon M1/M2/M3):\n" +
				"  rm -rf ~/.winemonogame\n" +
				"  curl -fsSL https://raw.githubusercontent.com/MonoGame/MonoGame/develop/Tools/MonoGame.Effect.Compiler/mgfxc_wine_setup.sh | bash");
		}

		return hasDotnet && hasD3d;
	}

	/// <summary>
	/// Walks up the directory tree from AppDomain.CurrentDomain.BaseDirectory until it
	/// finds a directory that contains a "Voltage.Editor" sub-folder (the solution root).
	/// Cross-platform safe: does not assume a fixed number of parent hops.
	/// Windows RID builds add an extra sub-folder vs macOS/Linux, so a fixed count fails.
	/// </summary>
	private static string FindSolutionDirectory()
	{
		var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
		while (dir != null)
		{
			if (Directory.Exists(Path.Combine(dir.FullName, "Voltage.Editor")))
				return dir.FullName;
			dir = dir.Parent;
		}
		return null;
	}

	/// <summary>
	/// Resolves the absolute path to the mgfxc executable.
	///
	/// Why this is needed:
	/// When UseShellExecute = false, Windows processes do not inherit the user-level
	/// PATH (which contains %USERPROFILE%\.dotnet\tools). The mgfxc wrapper exe then
	/// finds C:\windows\system32\dotnet.exe (the system stub) instead of the real
	/// .NET SDK dotnet, and immediately fails with:
	///   "Failed to resolve full path of the current executable"
	/// Passing the full path lets the OS launch the exe directly, bypassing PATH.
	/// </summary>
	private static string FindMgfxcExecutable()
	{
		var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
		var toolsDir = Path.Combine(userProfile, ".dotnet", "tools");

		// Try the standard .NET global-tools location (works on Windows, macOS, Linux)
		var exeName = System.Runtime.InteropServices.RuntimeInformation
			.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows)
			? "mgfxc.exe"
			: "mgfxc";

		var candidate = Path.Combine(toolsDir, exeName);
		if (File.Exists(candidate))
			return candidate;

		// Fall back to PATH resolution (will still work when the shell inherits it)
		return "mgfxc";
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

		if (!IsWineSetupComplete())
		{
			Debug.Error(
				"Wine setup is incomplete on macOS. " +
				"Run the MonoGame Wine setup script from the 'develop' branch (NOT 'master' — that one installs x86 .NET which fails on Apple Silicon):\n" +
				"  rm -rf ~/.winemonogame\n" +
				"  curl -fsSL https://raw.githubusercontent.com/MonoGame/MonoGame/develop/Tools/MonoGame.Effect.Compiler/mgfxc_wine_setup.sh | bash");
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

		if (!IsWineSetupComplete())
		{
			Debug.Error(
				"Wine setup is incomplete on macOS. " +
				"Run the MonoGame Wine setup script from the 'develop' branch (NOT 'master' — that one installs x86 .NET which fails on Apple Silicon):\n" +
				"  rm -rf ~/.winemonogame\n" +
				"  curl -fsSL https://raw.githubusercontent.com/MonoGame/MonoGame/develop/Tools/MonoGame.Effect.Compiler/mgfxc_wine_setup.sh | bash");
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

		if (!IsWineSetupComplete())
		{
			Debug.Error(
				"Wine setup is incomplete on macOS. " +
				"Run the MonoGame Wine setup script from the 'develop' branch (NOT 'master' — that one installs x86 .NET which fails on Apple Silicon):\n" +
				"  rm -rf ~/.winemonogame\n" +
				"  curl -fsSL https://raw.githubusercontent.com/MonoGame/MonoGame/develop/Tools/MonoGame.Effect.Compiler/mgfxc_wine_setup.sh | bash");
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
