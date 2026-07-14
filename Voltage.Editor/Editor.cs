using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using System;
using System.Linq;
using System.Reflection;
using Voltage.Console;
using Voltage.Editor.ImGuiCore;
using Voltage.Editor.Persistence;
using Voltage.Editor.ProjectFile;
using Voltage.Editor.SceneFile;
using Voltage.Utils;

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

		var font = Content.LoadBitmapFont("DefaultContent/Fonts/VoltageDefaultBMFont.fnt");
		Graphics.Instance = new Graphics(font);

		// Component-bearing assemblies (e.g. Farseer) are no longer force-loaded here — they are
		// plugins now, loaded per-project by PluginManager when the project's plugins.json asks for them.
		
#if OS_MAC
        Directory.SetCurrentDirectory(AppContext
            .BaseDirectory); //For some reason, on Mac directory needs to be set manually, or it won't find the Content folder
#endif
		Content.RootDirectory = "Content";

		IsEditMode = true;
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

		Scene.OnSceneBegin += TrackSceneChange;
		Scene.OnSceneBegin += SetSceneClearColor; // Set grey background color when scene changes

		Window.AllowUserResizing = true;
		ExitOnEscapeKeypress = false;

		IsFixedTimeStep = false; 
		Screen.SynchronizeWithVerticalRetrace = true;
		// IsFixedTimeStep = true; //Run Update() every 60 frames
		// Screen.SynchronizeWithVerticalRetrace = false; //Vsync = off

		ScreenUtils.ApplyScreenChange(ScreenUtils.ScreenMode.WindowedMax);
		HandleCommandLineArguments(); // when we open a project file through the file explorer
		SceneManager.Instance.LoadLastUsedScene();
		
		if (Scene != null)
			Scene.ClearColor = new Color(45, 45, 48);
	}

	protected override void EndRun()
	{
		base.EndRun();
		Scene.OnSceneBegin -= TrackSceneChange;
		Scene.OnSceneBegin -= SetSceneClearColor;
	}


	private void SetSceneClearColor()
	{
		if (Scene != null)
			Scene.ClearColor = new Color(45, 45, 48);
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
	    if (ScreenUtils.Mode == ScreenUtils.ScreenMode.FullScreen)
	    {
		    ScreenUtils.ApplyScreenChange(ScreenUtils.ScreenMode.Windowed);
	    }
	    else
	    {
		    ScreenUtils.ApplyScreenChange(ScreenUtils.ScreenMode.FullScreen);
	    }
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