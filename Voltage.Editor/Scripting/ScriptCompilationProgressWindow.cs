using System;
using Voltage.Editor.Utils;
using Voltage.Utils;

namespace Voltage.Editor.Scripting
{
	/// <summary>
	/// ImGui window that displays progress of script compilation
	/// </summary>
	public class ScriptCompilationProgressWindow
	{
		private CompilationProgressWindow _progressWindow;
		private CompilationProgress _currentProgress;

		public ScriptCompilationProgressWindow()
		{
			_progressWindow = new CompilationProgressWindow(isScriptProgress: true);
		}

		public void Show(int totalScripts)
		{
			_currentProgress = new CompilationProgress
			{
				Title = "Compiling Scripts",
				TotalItems = totalScripts,
				CompletedItems = 0,
				SuccessCount = 0,
				FailureCount = 0,
				CurrentItem = totalScripts > 0 ? $"Compiling {totalScripts} script file(s)..." : "No scripts to compile",
				IsComplete = false
			};
			
			_progressWindow.Show(_currentProgress);
		}

		public void UpdateProgress(string currentFile)
		{
			if (_currentProgress != null)
			{
				_currentProgress.CurrentItem = currentFile;
			}
		}

		public void Complete(bool success, int fileCount)
		{
			if (_currentProgress != null)
			{
				_currentProgress.IsComplete = true;
				_currentProgress.CompletedItems = _currentProgress.TotalItems;
				
				if (success)
				{
					_currentProgress.SuccessCount = fileCount;
					_currentProgress.FailureCount = 0;
					_currentProgress.CompletionMessage = $"Successfully compiled {fileCount} script file(s)!";
				}
				else
				{
					_currentProgress.SuccessCount = 0;
					_currentProgress.FailureCount = fileCount;
					_currentProgress.CompletionMessage = "Compilation failed. Check console for errors.";
				}
			}
		}

		public void Draw()
		{
			_progressWindow.Draw();
		}
	}
}