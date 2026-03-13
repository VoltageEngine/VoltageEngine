using System;
using Voltage.Editor.Utils;
using Voltage.Utils;

namespace Voltage.Editor.Tools
{
	/// <summary>
	/// ImGui window that displays progress of effect compilation using the reusable progress window
	/// </summary>
	public class EffectsCompileProgressWindow
	{
		private CompilationProgressWindow _progressWindow;
		private CompilationProgress _currentProgress;
		private System.Threading.CancellationTokenSource _cancellationTokenSource;
		
		public EffectsCompileProgressWindow()
		{
			_progressWindow = new CompilationProgressWindow(isScriptProgress: false, onCancel: OnCancelRequested);

			EffectsCompiler.OnBuildStarted += OnBuildStarted;
			EffectsCompiler.OnFileCompiling += OnFileCompiling;
			EffectsCompiler.OnFileCompiled += OnFileCompiled;
			EffectsCompiler.OnBuildCompleted += OnBuildCompleted;
		}

		public void SetCancellationToken(System.Threading.CancellationTokenSource cancellationTokenSource)
		{
			_cancellationTokenSource = cancellationTokenSource;
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
		if (_currentProgress != null && !_currentProgress.IsComplete)
		{
			_currentProgress.CurrentItem = fileName;
		}
	}

	private void OnFileCompiled(string fileName, bool success)
	{
		if (_currentProgress != null && !_currentProgress.IsComplete)
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

	private void OnCancelRequested()
	{
		// Mark progress as complete immediately to stop event handlers from updating UI
		if (_currentProgress != null)
		{
			_currentProgress.IsComplete = true;
		}

		if (_cancellationTokenSource != null && !_cancellationTokenSource.IsCancellationRequested)
		{
			_cancellationTokenSource.Cancel();
		}

		// Reset progress state after a short delay to allow the task to clean up
		_currentProgress = null;
		_progressWindow.Hide();
	}
	}
}