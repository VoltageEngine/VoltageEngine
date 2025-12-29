using System.Collections;
using ImGuiNET;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Voltage.Console;
using Voltage.Editor.ImGuiCore;
using Voltage.Editor.Persistence;
using Voltage.Editor.ProjectFile;
using Voltage.Editor.SceneFile;
using Voltage.Utils;
using Voltage.Utils.Coroutines;

#if OS_MAC
using System;
using System.IO;
#endif

namespace Voltage.Editor;

public class Editor : Core
{
	protected override void Initialize()
	{
		base.Initialize();

		Content.RootDirectory = "Content";

		var options = new ImGuiOptions();

		if (Screen.ActualMonitorWidth <= 1920)
		{
			options.FontSizeMultiplier = 1f;
			DebugConsole.RenderScale = 1.5f;
		}
		else if (Screen.ActualMonitorWidth < 3840)
		{
			options.FontSizeMultiplier = 1.1f;
			DebugConsole.RenderScale = 2.5f;
		}
		else
		{
			options.FontSizeMultiplier = 1.2f;
			DebugConsole.RenderScale = 3f;
		}

		options.IncludeDefaultFont(true);
		var imGuiManager = new ImGuiManager(options);

		RegisterGlobalManager(imGuiManager);

		Scene.OnSceneBegin += SetImGuiEditor; //Make sure all values of ImGuiEditor are reset when changing scenes
		Scene.OnSceneBegin += TrackSceneChange;

#if DEBUG
		DebugRenderEnabled = true;
#else
		DebugRenderEnabled = false;
#endif
		Window.AllowUserResizing = true;
		ExitOnEscapeKeypress = false;
		IsFixedTimeStep = true; //Run Update() every 60 frames
		Screen.SynchronizeWithVerticalRetrace = false; //Vsync = off
		// DefaultSamplerState = SamplerState.PointClamp; // pixel perfect rendering

		ScreenUtils.ApplyScreenChange(ScreenUtils.ScreenMode.WindowedMax);
		HandleCommandLineArguments(); // when we open a project file through the file explorer
		SceneManager.Instance.LoadLastUsedScene();
	}

	protected override void EndRun()
	{
		base.EndRun();
		Scene.OnSceneBegin -= SetImGuiEditor;
		Scene.OnSceneBegin -= TrackSceneChange;
	}

	private void SetImGuiEditor()
	{
		StartCoroutine(StartInEditMode());
	}

	// TODO: Refactor ImGuiEditor to NOT rely on a hacky coroutine like this, and instead load the entities correctly
	private IEnumerator StartInEditMode()
	{
		IsEditMode = false;
		yield return Coroutine.WaitForSeconds(0.05f);
		IsEditMode = true;
	}

	/// <summary>
	/// Tracks scene changes and persists the last opened scene.
	/// </summary>
	private void TrackSceneChange()
	{
		var sceneManager = SceneManager.Instance;

		if (sceneManager.HasLoadedScene && !string.IsNullOrWhiteSpace(sceneManager.CurrentScenePath))
		{
			PersistentScene.SetLastScenePath(sceneManager.CurrentScenePath);
		}
		else
		{
			PersistentScene.Clear();
		}
	}

	protected override void Update(GameTime gameTime)
	{
		base.Update(gameTime);

#if OS_WINDOWS || LINUX
		if (Input.IsKeyPressed(Keys.F11))
		{
			if (ScreenUtils.Mode == ScreenUtils.ScreenMode.FullScreen)
			{
				ScreenUtils.ApplyScreenChange(ScreenUtils.ScreenMode.Windowed);
			}
			else
			{
				ScreenUtils.ApplyScreenChange(ScreenUtils.ScreenMode.FullScreen);
			}
		}
#elif OS_MAC
    if (Input.IsKeyDown(Keys.LeftControl) && Input.IsKeyDown(Keys.LeftWindows) && Input.IsKeyPressed(Keys.F))
    {
        if (ScreenUtils.IsFullscreen)
            ScreenUtils.SetEditorWindowedMode(false);
        else
            ScreenUtils.SetFullScreenMode();
    }
#endif
	}

	/// <summary>
	/// Processes command-line arguments to load a project file if specified.
	/// </summary>
	private void HandleCommandLineArguments()
	{
		var args = Program.CommandLineArgs;
		
		if (args != null && args.Length > 0)
		{
			// First argument is expected to be the .voltage file path
			string projectPath = args[0];
			
			if (!string.IsNullOrWhiteSpace(projectPath) && 
				System.IO.File.Exists(projectPath) &&
				System.IO.Path.GetExtension(projectPath).Equals(".voltage", System.StringComparison.OrdinalIgnoreCase))
			{
				var projectManager = ProjectManager.Instance;
				bool success = projectManager.LoadProject(projectPath);
				
				if (!success)
					Debug.Error($"Failed to load project from: {projectPath}");
			}
			else if (!string.IsNullOrWhiteSpace(projectPath))
			{
				Debug.Warn($"Invalid project file specified: {projectPath}");
			}
		}
	}
}