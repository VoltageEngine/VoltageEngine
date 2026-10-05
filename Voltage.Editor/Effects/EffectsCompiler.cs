using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Voltage.Editor.DebugUtils;
using Voltage.Editor.ProjectFile;
using Voltage.Editor.Utils;

namespace Voltage.Editor.Effects;

public class EffectsCompiler
{
	private static readonly SemaphoreSlim BuildLock = new(1, 1);
	public static EffectsCompileProgress CurrentProgress { get; private set; }
	public static bool IsBusy => BuildLock.CurrentCount == 0;
	public static event Action<int> OnBuildStarted;
	public static event Action<string> OnFileCompiling;
	public static event Action<string, bool> OnFileCompiled;
	public static event Action<int, int> OnBuildCompleted;

	public static string EngineOutputDirectory => Path.Combine(
		Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Voltage", "Effects", MonoGameVersionResolver.GetVersion(), EffectResource.BundledRevision, "OpenGL");

	private static string EngineSourceDirectory()
	{
		var dir = new DirectoryInfo(AppContext.BaseDirectory);
		while (dir != null)
		{
			var source = Path.Combine(dir.FullName, "Voltage.Editor", "DefaultContent", "Effects");
			if (Directory.Exists(source)) return source;
			dir = dir.Parent;
		}
		return Path.Combine(AppContext.BaseDirectory, "DefaultContent", "Effects");
	}

	private static IEnumerable<(string Source, string Output, string Stamp)> EngineFiles() => Files(EngineSourceDirectory(), EngineOutputDirectory, EngineOutputDirectory);

	private static IEnumerable<(string Source, string Output, string Stamp)> ProjectFiles(IGameProject project) =>
		Files(project.EffectsFolder, Path.Combine(project.ContentsFolder, "Effects"), Path.Combine(project.ProjectPath, "obj", "Effects", MonoGameVersionResolver.GetVersion()));

	private static IEnumerable<(string Source, string Output, string Stamp)> Files(string source, string output, string cache)
	{
		if (string.IsNullOrEmpty(source) || !Directory.Exists(source)) yield break;
		foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories)
			.Where(f => string.Equals(Path.GetExtension(f), ".fx", StringComparison.OrdinalIgnoreCase)).OrderBy(f => f, StringComparer.Ordinal))
		{
			var relative = Path.ChangeExtension(Path.GetRelativePath(source, file), ".mgfxo");
			yield return (file, Path.Combine(output, relative), Path.Combine(cache, relative + ".sha256"));
		}
	}

	private static bool CompileFiles(IEnumerable<(string Source, string Output, string Stamp)> files, CancellationToken token, bool progress)
	{
		var version = MonoGameVersionResolver.GetVersion();
		string compiler = null;
		Exception toolFailure = null;
		bool success = true;
		foreach (var file in files)
		{
			token.ThrowIfCancellationRequested();
			var name = Path.GetFileName(file.Source);
			if (progress) { CurrentProgress.UpdateProgress(name); OnFileCompiling?.Invoke(name); }
			bool compiled = false;
			try
			{
				var fingerprint = EffectToolchain.Fingerprint(file.Source, version);
				if (EffectToolchain.IsCurrent(file.Output, fingerprint, file.Stamp))
					Log($"Effect up to date: {name}");
				else
				{
					if (toolFailure != null) throw new InvalidOperationException("Shader compiler setup failed earlier in this build.", toolFailure);
					if (compiler == null)
					{
						try
						{
							var environment = EffectToolchain.CreateStartInfo("mgfxc", Array.Empty<string>());
							EffectToolchain.ConfigureEnvironment(environment, OperatingSystem.IsWindows(), OperatingSystem.IsMacOS(), Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
							var problem = EffectToolchain.WineProblem(environment, OperatingSystem.IsWindows());
							if (problem != null) throw new InvalidOperationException(problem);
							compiler = EffectToolchain.EnsureTool(version, Log, token);
						}
						catch (Exception ex) { toolFailure = ex; throw; }
					}
					EffectToolchain.Compile(compiler, file.Source, file.Output, fingerprint, Log, token, file.Stamp);
				}
				compiled = true;
			}
			catch (OperationCanceledException) { throw; }
			catch (Exception ex) { Debug.Error($"Effect '{name}': {ex.Message}"); success = false; }
			if (progress)
			{
				if (compiled) CurrentProgress.IncrementSuccess(name); else CurrentProgress.IncrementFailure(name);
				OnFileCompiled?.Invoke(name, compiled);
			}
		}
		return success;
	}

	private static void Log(string message)
	{
		if (!string.IsNullOrWhiteSpace(message)) EditorDebug.Log(message.TrimEnd(), "Effects");
	}

	/// <summary>Compiles project shaders before publishing, preserving the last valid output on failure.</summary>
	public static bool BuildProjectForPublish(IGameProject project, CancellationToken token)
	{
		if (!BuildLock.Wait(0)) throw new InvalidOperationException("An effects build is already running.");
		try { return CompileFiles(ProjectFiles(project), token, false); }
		finally { BuildLock.Release(); }
	}

	/// <summary>Checks the pinned compiler without downloading anything or requiring Wine.</summary>
	public static bool IsMgfxcAvailable() => File.Exists(EffectToolchain.ToolPath(
		EffectToolchain.ToolDirectory(MonoGameVersionResolver.GetVersion()), OperatingSystem.IsWindows()));

	public static bool IsWineSetupComplete()
	{
		var info = EffectToolchain.CreateStartInfo("mgfxc", Array.Empty<string>());
		EffectToolchain.ConfigureEnvironment(info, OperatingSystem.IsWindows(), OperatingSystem.IsMacOS(), Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
		return EffectToolchain.WineProblem(info, OperatingSystem.IsWindows()) == null;
	}

	public static void ClearProgress() => CurrentProgress = null;

	private static void Start(IEnumerable<(string Source, string Output, string Stamp)> files, EffectsCompileProgressWindow window, ref CancellationTokenSource cancellation, bool engine)
	{
		if (!BuildLock.Wait(0)) { Debug.Warn("An effects build is already running."); return; }
		cancellation?.Dispose();
		cancellation = new CancellationTokenSource();
		var token = cancellation.Token;
		window.SetCancellationToken(cancellation);
		Task.Run(() =>
		{
			CurrentProgress = new EffectsCompileProgress();
			try
			{
				var jobs = files.ToArray();
				CurrentProgress.TotalFiles = jobs.Length;
				OnBuildStarted?.Invoke(jobs.Length);
				if (engine && !jobs.Any()) throw new DirectoryNotFoundException("Built-in shader sources were not packaged with this editor.");
				CompileFiles(jobs, token, true);
				if (engine) EffectResource.BuiltinOverrideDirectory = EngineOutputDirectory;
			}
			catch (OperationCanceledException) { CurrentProgress.FailureCount++; Log("Effects build cancelled."); }
			catch (Exception ex) { CurrentProgress.FailureCount++; Debug.Error($"Effects build: {ex.Message}"); }
			finally
			{
				CurrentProgress.Complete();
				var ok = CurrentProgress.SuccessCount;
				var failed = CurrentProgress.FailureCount;
				BuildLock.Release();
				OnBuildCompleted?.Invoke(ok, failed);
			}
		});
	}

	public static void BuildEditorProjectEffects(ProjectManager projects, EffectsCompileProgressWindow window, ref CancellationTokenSource cancellation)
	{
		if (!projects.HasActiveProject) { Debug.Error("No active project loaded."); return; }
		Start(ProjectFiles(projects.CurrentProject), window, ref cancellation, false);
	}

	public static void BuildEditorEngineEffects(EffectsCompileProgressWindow window, ref CancellationTokenSource cancellation) =>
		Start(EngineFiles(), window, ref cancellation, true);

	public static void BuildEditorAllEffects(ProjectManager projects, EffectsCompileProgressWindow window, ref CancellationTokenSource cancellation)
	{
		if (!projects.HasActiveProject) { Debug.Error("No active project loaded."); return; }
		Start(EngineFiles().Concat(ProjectFiles(projects.CurrentProject)), window, ref cancellation, true);
	}
}
