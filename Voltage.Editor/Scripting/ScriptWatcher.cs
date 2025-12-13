using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Timers;

namespace Voltage.Editor.Scripting
{
	/// <summary>
	/// Watches script files for changes and triggers hot reload
	/// </summary>
	public class ScriptWatcher : IDisposable
	{
		private readonly string _scriptsDirectory;
		private readonly FileSystemWatcher _fileWatcher;
		private readonly Timer _debounceTimer;
		private readonly HashSet<string> _changedFiles = new();
		private readonly object _lockObject = new();
		private bool _isCompiling = false;
		private ScriptCompilationProgressWindow _progressWindow;

		public event Action<CompilationResult> OnCompilationComplete;

		/// <summary>
		/// Debounce delay in milliseconds to avoid multiple compilations
		/// </summary>
		public double DebounceDelay { get; set; } = 500;

		public ScriptWatcher(string scriptsDirectory)
		{
			_scriptsDirectory = scriptsDirectory;
			_progressWindow = new ScriptCompilationProgressWindow();

			if (!Directory.Exists(_scriptsDirectory))
			{
				Directory.CreateDirectory(_scriptsDirectory);
				Debug.Log($"Created scripts directory: {_scriptsDirectory}");
			}

			_fileWatcher = new FileSystemWatcher(_scriptsDirectory)
			{
				Filter = "*.cs",
				NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.CreationTime,
				IncludeSubdirectories = true,
				EnableRaisingEvents = true
			};

			_fileWatcher.Changed += OnFileChanged;
			_fileWatcher.Created += OnFileChanged;
			_fileWatcher.Deleted += OnFileChanged;
			_fileWatcher.Renamed += OnFileRenamed;

			_debounceTimer = new Timer(DebounceDelay);
			_debounceTimer.Elapsed += OnDebounceTimerElapsed;
			_debounceTimer.AutoReset = false;
		}

		private void OnFileChanged(object sender, FileSystemEventArgs e)
		{
			lock (_lockObject)
			{
				if (_isCompiling)
					return;

				_changedFiles.Add(e.FullPath);
				_debounceTimer.Stop();
				_debounceTimer.Start();

				Debug.Log($"Script file changed: {Path.GetFileName(e.FullPath)}");
			}
		}

		private void OnFileRenamed(object sender, RenamedEventArgs e)
		{
			lock (_lockObject)
			{
				if (_isCompiling)
					return;

				_changedFiles.Add(e.FullPath);
				_debounceTimer.Stop();
				_debounceTimer.Start();

				Debug.Log($"Script file renamed: {Path.GetFileName(e.OldFullPath)} -> {Path.GetFileName(e.FullPath)}");
			}
		}

		private void OnDebounceTimerElapsed(object sender, ElapsedEventArgs e)
		{
			lock (_lockObject)
			{
				if (_isCompiling || _changedFiles.Count == 0)
					return;

				_isCompiling = true;
				var changedFilesCopy = new List<string>(_changedFiles);
				_changedFiles.Clear();

				// Trigger compilation on main thread
				Core.Schedule(0.01f, false, this, _ =>
				{
					CompileScripts(changedFilesCopy);
				});
			}
		}

		private void CompileScripts(List<string> changedFiles, bool reloadScene = true)
		{
			try
			{
				Debug.Log($"Hot reloading scripts: {string.Join(", ", changedFiles.Select(Path.GetFileName))}");

				var allScriptFiles = Directory.GetFiles(_scriptsDirectory, "*.cs", SearchOption.AllDirectories)
					.Where(f => !f.Contains("obj") && !f.Contains("bin"))
					.ToList();

				if (allScriptFiles.Count == 0)
				{
					Debug.Warn("No script files found to compile");
					return;
				}

				_progressWindow.Show(allScriptFiles.Count);
				_progressWindow.UpdateProgress($"Compiling {allScriptFiles.Count} file(s)...");

				var result = ScriptCompiler.Compile(allScriptFiles, "DynamicScripts_" + DateTime.Now.Ticks);
				_progressWindow.Complete(result.Success, allScriptFiles.Count);

				if (result.Success)
				{
					Debug.Log($"Successfully compiled {allScriptFiles.Count} script file(s)");
					OnCompilationComplete?.Invoke(result);
				}
				else
				{
					Debug.Error("Script compilation failed:");
					foreach (var error in result.Errors)
					{
						Debug.Error($"  {error}");
					}
					OnCompilationComplete?.Invoke(result);
				}
			}
			catch (Exception ex)
			{
				Debug.Error($"Error during script hot reload: {ex.Message}\n{ex.StackTrace}");
				_progressWindow?.Complete(false, 0);
			}
			finally
			{
				lock (_lockObject)
				{
					_isCompiling = false;
				}
			}
		}

		/// <summary>
		/// Manually trigger compilation of all scripts
		/// </summary>
		public void CompileAllScripts(bool reloadScene)
		{
			var allScriptFiles = Directory.GetFiles(_scriptsDirectory, "*.cs", SearchOption.AllDirectories)
				.Where(f => !f.Contains("obj") && !f.Contains("bin"))
				.ToList();

			if (allScriptFiles.Count > 0)
			{
				CompileScripts(allScriptFiles);
			}
		}

		/// <summary>
		/// Draws the progress window if visible
		/// </summary>
		public void DrawProgress()
		{
			_progressWindow?.Draw();
		}

		public void Dispose()
		{
			_fileWatcher?.Dispose();
			_debounceTimer?.Dispose();
		}
	}
}