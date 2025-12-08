using System.Collections;
using ImGuiNET;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Voltage.Console;
using Voltage.Editor.ImGuiCore;
using Voltage.Editor.Persistence;
using Voltage.Editor.Scenes;
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
			options.AddFont("DefaultContent/Fonts/Lexend-Medium.ttf", 12); // Normal
			options.AddFont("DefaultContent/Fonts/Lexend-Medium.ttf", 14); // Info
			options.AddFont("DefaultContent/Fonts/Lexend-Medium.ttf", 16); // Warn
			options.AddFont("DefaultContent/Fonts/Lexend-Medium.ttf", 18); // Error
			options.FontSizeMultiplier = 1f;
			DebugConsole.RenderScale = 2f;
		}
		else if (Screen.ActualMonitorWidth < 3840)
		{
			options.AddFont("DefaultContent/Fonts/Lexend-Medium.ttf", 16); // Normal
			options.AddFont("DefaultContent/Fonts/Lexend-Medium.ttf", 18); // Info
			options.AddFont("DefaultContent/Fonts/Lexend-Medium.ttf", 20); // Warn
			options.AddFont("DefaultContent/Fonts/Lexend-Medium.ttf", 22); // Error
			options.FontSizeMultiplier = 1.2f;
			DebugConsole.RenderScale = 3f;
		}
		else
		{
			options.AddFont("DefaultContent/Fonts/Lexend-Medium.ttf", 20); // Normal
			options.AddFont("DefaultContent/Fonts/Lexend-Medium.ttf", 22); // Info
			options.AddFont("DefaultContent/Fonts/Lexend-Medium.ttf", 24); // Warn
			options.AddFont("DefaultContent/Fonts/Lexend-Medium.ttf", 26); // Error
			options.FontSizeMultiplier = 1.5f;
			DebugConsole.RenderScale = 4f;
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

		Scene = LoadLastOrDefaultScene();
		ScreenUtils.SetEditorWindowedMode(true);
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
		if (Scene != null)
		{
			PersistentScene.SetLastScene(Scene);
		}
	}

	/// <summary>
	/// Loads the last opened scene if available, otherwise returns the default scene.
	/// </summary>
	private Scene LoadLastOrDefaultScene()
	{
		var lastScene = PersistentScene.CreateLastScene();

		if (lastScene != null)
		{
			return lastScene;
		}

		Debug.Warn("No last scene found, loading default EMPTY SCENE");
		return new EmptyScene();
	}

	protected override void Update(GameTime gameTime)
	{
		base.Update(gameTime);

#if OS_WINDOWS || LINUX
		if (Input.IsKeyPressed(Keys.F11))
		{
			if (ScreenUtils.IsFullscreen)
			{
				ScreenUtils.SetEditorWindowedMode(false);
			}
			else
			{
				ScreenUtils.SetFullScreenMode();
			}
		}
#elif OS_MAC
    if (Input.IsKeyDown(Keys.LeftControl) && Input.IsKeyDown(Keys.LeftWindows) && Input.IsKeyPressed(Keys.F))
    {
        if (ScreenUtils.IsFullscreen)
            ScreenUtils.SetWindowedMode(false);
        else
            ScreenUtils.SetFullScreenMode();
    }
#endif
	}
}