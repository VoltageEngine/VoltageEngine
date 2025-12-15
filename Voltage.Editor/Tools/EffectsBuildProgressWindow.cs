using System;
using Voltage.Editor.Utils;
using Voltage.Utils;

namespace Voltage.Editor.Tools
{
	/// <summary>
	/// ImGui window that displays progress of effect compilation using the reusable progress window
	/// </summary>
	public class EffectBuildProgressWindow
	{
		private CompilationProgressWindow _progressWindow;
		private CompilationProgress _currentProgress;

		public EffectBuildProgressWindow()
		{
			_progressWindow = new CompilationProgressWindow(isScriptProgress: false);

			EffectBuilder.OnBuildStarted += OnBuildStarted;
			EffectBuilder.OnFileCompiling += OnFileCompiling;
			EffectBuilder.OnFileCompiled += OnFileCompiled;
			EffectBuilder.OnBuildCompleted += OnBuildCompleted;
		}

		public void Show()
		{
			if (_currentProgress != null)
			{
				_progressWindow.Show(_currentProgress);
			}
		}

		public void Draw()
		{
			_progressWindow.Draw();
		}

		private void OnBuildStarted(int totalFiles)
		{
			_currentProgress = new CompilationProgress
			{
				Title = "Building Effects",
				TotalItems = totalFiles,
				CompletedItems = 0,
				SuccessCount = 0,
				FailureCount = 0,
				CurrentItem = "",
				IsComplete = false
			};

			_progressWindow.Show(_currentProgress);
		}

		private void OnFileCompiling(string fileName)
		{
			if (_currentProgress != null)
			{
				_currentProgress.CurrentItem = fileName;
			}
		}

		private void OnFileCompiled(string fileName, bool success)
		{
			if (_currentProgress != null)
			{
				_currentProgress.CompletedItems++;

				if (success)
					_currentProgress.SuccessCount++;
				else
					_currentProgress.FailureCount++;
			}
		}

		private void OnBuildCompleted(int successCount, int failureCount)
		{
			if (_currentProgress != null)
			{
				_currentProgress.IsComplete = true;
				_currentProgress.CurrentItem = "";

				if (failureCount == 0)
				{
					_currentProgress.CompletionMessage = $"Successfully compiled {successCount} effect(s)!";
				}
				else
				{
					_currentProgress.CompletionMessage = $"Completed with {failureCount} error(s). Check console for details.";
				}
			}
		}
	}
}