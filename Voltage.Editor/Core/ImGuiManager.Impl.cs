using System;
using System.Runtime.InteropServices;
using ImGuiNET;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Voltage.Console;
using Voltage.Utils;
using Voltage.Utils.Extensions;
using Voltage.Editor.UndoActions;
using Voltage.Editor.Utils;
using Voltage.Persistence.Binary;
using Num = System.Numerics;
using Voltage;

namespace Voltage.Editor.ImGuiCore;

/// <summary>
/// Manages the ImGui game window specifically
/// </summary>
public partial class ImGuiManager : GlobalManager, IFinalRenderDelegate, IDisposable
{
	private const string kShowStyleEditor = "ImGui_ShowStyleEditor";
	private const string kShowSceneGraphWindow = "ImGui_ShowSceneGraphWindow";
	private const string kShowCoreWindow = "ImGui_ShowCoreWindow";
	private const string kShowSeperateGameWindow = "ImGui_ShowSeperateGameWindow";
	private const string kPreserveGameWindowAspectRatio = "ImGui_PreserveGameWindowAspectRatio";
	private Num.Vector2 _gameWindowCursorOffset;

	[Flags]
	private enum WindowPosition
	{
		TopLeft,
		Top,
		TopRight,
		Left,
		Center,
		Right,
		BottomLeft,
		Bottom,
		BottomRight
	}

	private void LoadSettings()
	{
		var fileDataStore = Voltage.Core.Services.GetService<FileDataStore>() ?? new FileDataStore(Storage.GetStorageRoot());
		KeyValueDataStore.Default.Load(fileDataStore);

		ShowStyleEditor = KeyValueDataStore.Default.GetBool(kShowStyleEditor, ShowStyleEditor);
		ShowSceneGraphWindow = KeyValueDataStore.Default.GetBool(kShowSceneGraphWindow, ShowSceneGraphWindow);
		ShowCoreWindow = KeyValueDataStore.Default.GetBool(kShowCoreWindow, ShowCoreWindow);
		ShowSeparateGameWindow = KeyValueDataStore.Default.GetBool(kShowSeperateGameWindow, ShowSeparateGameWindow);
		PreserveGameWindowAspectRatio = KeyValueDataStore.Default.GetBool(kPreserveGameWindowAspectRatio, PreserveGameWindowAspectRatio);

		Voltage.Core.Emitter.AddObserver(CoreEvents.Exiting, PersistSettings);
	}

	private void PersistSettings()
	{
		KeyValueDataStore.Default.Set(kShowStyleEditor, ShowStyleEditor);
		KeyValueDataStore.Default.Set(kShowSceneGraphWindow, ShowSceneGraphWindow);
		KeyValueDataStore.Default.Set(kShowCoreWindow, ShowCoreWindow);
		KeyValueDataStore.Default.Set(kShowSeperateGameWindow, ShowSeparateGameWindow);
		KeyValueDataStore.Default.Set(kPreserveGameWindowAspectRatio, PreserveGameWindowAspectRatio);

		KeyValueDataStore.Default.Flush(Voltage.Core.Services.GetOrAddService<FileDataStore>());
	}

	/// <summary>
	/// here we do some cleanup in preparation for a new Scene
	/// </summary>
	private void OnSceneChanged()
	{
		// when the Scene changes we need to rewire ourselves up as the IFinalRenderDelegate in the new Scene
		// if we were previously enabled and do some cleanup
		Unload();
		SceneGraphWindow.OnSceneChanged();

		if (Enabled)
			OnEnabled();
	}

	private void Unload()
	{
		_drawCommands.Clear();
		_entityInspectors.Clear();

		if (_renderTargetId != IntPtr.Zero)
		{
			_renderer.UnbindTexture(_renderTargetId);
			_renderTargetId = IntPtr.Zero;
		}

		_lastRenderTarget = null;
	}

	/// <summary>
	/// Draws the game window and deals with overriding Voltage.Input when appropriate
	/// </summary>
	private void DrawGameWindow()
	{
		if (_lastRenderTarget == null)
			return;

		HandleForcedGameViewParams();

		string gameWindowState = Voltage.Core.IsEditMode ? "Paused" : "Playing";

		ImGuiWindowFlags gameWindowFlags = _gameWindowFlags | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;
		
		ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Num.Vector2(0, 0));
		
		ImGui.Begin($"Game: {gameWindowState}###GameWindow", gameWindowFlags);

		GameWindowWidth = ImGui.GetWindowSize().X;
		GameWindowHeight = ImGui.GetWindowSize().Y;

		Num.Vector2 imageSize;
		Num.Vector2 cursorOffset = Num.Vector2.Zero;

		if (PreserveGameWindowAspectRatio)
		{
			var availableRegion = ImGui.GetContentRegionAvail();
			var targetAspect = (float)_lastRenderTarget.Width / _lastRenderTarget.Height;
			var availableAspect = availableRegion.X / availableRegion.Y;

			if (availableAspect > targetAspect)
			{
				imageSize = new Num.Vector2(availableRegion.Y * targetAspect, availableRegion.Y);
				cursorOffset.X = (availableRegion.X - imageSize.X) * 0.5f;
			}
			else
			{
				imageSize = new Num.Vector2(availableRegion.X, availableRegion.X / targetAspect);
				cursorOffset.Y = (availableRegion.Y - imageSize.Y) * 0.5f;
			}

			if (cursorOffset.X > 0 || cursorOffset.Y > 0)
			{
				var currentCursor = ImGui.GetCursorPos();
				ImGui.SetCursorPos(currentCursor + cursorOffset);
			}
		}
		else
		{
			imageSize = ImGui.GetContentRegionAvail();
		}

		_gameWindowCursorOffset = cursorOffset;

		// Only draw the image if we have a valid texture ID
		if (_renderTargetId != IntPtr.Zero)
		{
			ImGui.Image(_renderTargetId, imageSize);
		}
		else
		{
			// Reserve the space even if we don't have a texture yet
			ImGui.Dummy(imageSize);
		}

		// NOW draw buttons and text on top using screen coordinates
		var camera = Voltage.Core.Scene?.Camera;
		bool showZoomButton = camera != null && Math.Abs(camera.Zoom - Camera.DefaultZoom) > 0.01f;
		bool showSpeedButton = Math.Abs(GetDynamicCameraSpeed() - EditModeCameraSpeed) > 0.1f;

		if (showZoomButton || showSpeedButton)
		{
			var windowPos = ImGui.GetWindowPos();
			var contentMin = ImGui.GetWindowContentRegionMin();
			var buttonStartPos = windowPos + contentMin + cursorOffset + new Num.Vector2(8, 8) * ImGui.GetIO().FontGlobalScale;

			ImGui.SetCursorScreenPos(buttonStartPos);

			if (showZoomButton)
			{
				var zoomButtonText = "Reset Camera Zoom";
				var zoomTextSize = ImGui.CalcTextSize(zoomButtonText);
				var zoomPadding = new Num.Vector2(16, 8) * ImGui.GetIO().FontGlobalScale;
				var zoomButtonSize = zoomTextSize + zoomPadding;

				if (ImGui.Button(zoomButtonText, zoomButtonSize))
				{
					camera.Zoom = Camera.DefaultZoom;
				}

				if (showSpeedButton)
				{
					ImGui.SameLine(0, 8f * ImGui.GetIO().FontGlobalScale); 
				}
			}

			if (showSpeedButton)
			{
				var speedButtonText = "Reset Camera Speed";
				var speedTextSize = ImGui.CalcTextSize(speedButtonText);
				var speedPadding = new Num.Vector2(16, 8) * ImGui.GetIO().FontGlobalScale;
				var speedButtonSize = speedTextSize + speedPadding;

				if (ImGui.Button(speedButtonText, speedButtonSize))
				{
					ResetDynamicCameraSpeed();
				}
			}
		}

		// Camera Speed Indicator at top-right
		if (Voltage.Core.IsEditMode && Math.Abs(GetDynamicCameraSpeed() - EditModeCameraSpeed) > 0.1f)
		{
			var speedText = $"Camera Speed: {(int)GetDynamicCameraSpeed()}";
			var speedTextSize = ImGui.CalcTextSize(speedText);
			
			var windowPos = ImGui.GetWindowPos();
			var contentMin = ImGui.GetWindowContentRegionMin();
			var margin = new Num.Vector2(8, 8) * ImGui.GetIO().FontGlobalScale;
			
			var speedTextPos = new Num.Vector2(
				windowPos.X + contentMin.X + cursorOffset.X + imageSize.X - speedTextSize.X - margin.X,
				windowPos.Y + contentMin.Y + cursorOffset.Y + margin.Y
			);

			var drawList = ImGui.GetWindowDrawList();
			var bgPadding = new Num.Vector2(8, 4) * ImGui.GetIO().FontGlobalScale;
			var bgMin = speedTextPos - bgPadding;
			var bgMax = speedTextPos + speedTextSize + bgPadding;
			
			drawList.AddRectFilled(
				bgMin, 
				bgMax, 
				ImGui.ColorConvertFloat4ToU32(new Num.Vector4(0.0f, 0.0f, 0.0f, 0.6f)),
				4.0f * ImGui.GetIO().FontGlobalScale
			);

			ImGui.SetCursorScreenPos(speedTextPos);
			ImGui.TextColored(new Num.Vector4(1.0f, 1.0f, 0.0f, 1.0f), speedText);
		}

		// convert mouse input to the game windows coordinates
		OverrideMouseInput();

		if (!ImGui.IsWindowFocused())
		{
			var focusedWindow = false;
			Mouse.SetCursor(MouseCursor.Arrow);

			if (ImGui.IsWindowHovered())
				if (ImGui.IsMouseClicked(ImGuiMouseButton.Left)
				    || (ImGui.IsMouseClicked(ImGuiMouseButton.Right) && FocusGameWindowOnRightClick)
				    || (ImGui.IsMouseClicked(ImGuiMouseButton.Middle) && FocusGameWindowOnMiddleClick))
				{
					ImGui.SetWindowFocus();
					focusedWindow = true;
				}

			if (!focusedWindow)
			{
				var mouseState = new MouseState(
					Input.CurrentMouseState.X,
					Input.CurrentMouseState.Y,
					DisableMouseWheelWhenGameWindowUnfocused ? 0 : Input.MouseWheel,
					ButtonState.Released,
					ButtonState.Released,
					ButtonState.Released,
					ButtonState.Released,
					ButtonState.Released
				);
				Input.SetCurrentMouseState(mouseState);

				if (DisableKeyboardInputWhenGameWindowUnfocused) Input.SetCurrentKeyboardState(new KeyboardState());
			}
		}

		ImGui.End();
		ImGui.PopStyleVar();
	}

	/// <summary>
	/// handles any SetNextWindow* options chosen from a menu
	/// </summary>
	private void HandleForcedGameViewParams()
	{
		if (_gameViewForcedSize.HasValue)
		{
			ImGui.SetNextWindowSize(_gameViewForcedSize.Value);
			_gameViewForcedSize = null;
		}

		if (_gameViewForcedPos.HasValue)
		{
			string gameWindowState = Voltage.Core.IsEditMode ? "Paused" : "Playing";
			string windowTitle = $"Game: {gameWindowState}###GameWindow";
			
			ImGui.Begin(windowTitle, _gameWindowFlags);
			var windowSize = ImGui.GetWindowSize();
			ImGui.End();

			var pos = new Num.Vector2();
			switch (_gameViewForcedPos.Value)
			{
				case WindowPosition.TopLeft:
					pos.Y = _mainMenuBarHeight;
					pos.X = 0;
					break;
				case WindowPosition.Top:
					pos.Y = _mainMenuBarHeight;
					pos.X = Screen.Width / 2f - windowSize.X / 2f;
					break;
				case WindowPosition.TopRight:
					pos.Y = _mainMenuBarHeight;
					pos.X = Screen.Width - windowSize.X;
					break;
				case WindowPosition.Left:
					pos.Y = Screen.Height / 2f - windowSize.Y / 2f;
					pos.X = 0;
					break;
				case WindowPosition.Center:
					pos.Y = Screen.Height / 2f - windowSize.Y / 2f;
					pos.X = Screen.Width / 2f - windowSize.X / 2f;
					break;
				case WindowPosition.Right:
					pos.Y = Screen.Height / 2f - windowSize.Y / 2f;
					pos.X = Screen.Width - windowSize.X;
					break;
				case WindowPosition.BottomLeft:
					pos.Y = Screen.Height - windowSize.Y;
					pos.X = 0;
					break;
				case WindowPosition.Bottom:
					pos.Y = Screen.Height - windowSize.Y;
					pos.X = Screen.Width / 2f - windowSize.X / 2f;
					break;
				case WindowPosition.BottomRight:
					pos.Y = Screen.Height - windowSize.Y;
					pos.X = Screen.Width - windowSize.X;
					break;
			}

			ImGui.SetNextWindowPos(pos);
			_gameViewForcedPos = null;
		}
	}

	/// <summary>
	/// converts the mouse position from global window position to the game window's coordinates and overrides Voltage.Input with
	/// the new value. This keeps input working properly in the game window.
	/// </summary>
private void OverrideMouseInput()
	{
		// ImGui.GetCursorScreenPos() is the position of top-left pixel in windows drawable area
		// Account for the cursor offset when aspect ratio preservation is enabled
		var offset = new Vector2(
			ImGui.GetCursorScreenPos().X - _gameWindowCursorOffset.X, 
			ImGui.GetCursorScreenPos().Y - _gameWindowCursorOffset.Y
		);

		// remove window position offset from our raw input. this gets us normalized back to the top-left origin.
		// We are essentilly removing any input delta that is not in the game window.
		var normalizedPos = Input.RawMousePosition.ToVector2() - offset;

		var scaleX = ImGui.GetContentRegionAvail().X / _lastRenderTarget.Width;
		var scaleY = ImGui.GetContentRegionAvail().Y / _lastRenderTarget.Height;
		
		// When preserving aspect ratio, use the uniform scale
		float scale;
		if (PreserveGameWindowAspectRatio)
		{
			scale = Math.Min(scaleX, scaleY);
		}
		else
		{
			// Non-uniform scaling when not preserving aspect ratio
			normalizedPos.X /= scaleX;
			normalizedPos.Y /= scaleY;
			
			var unNormalizedPos = normalizedPos / Input.ResolutionScale;
			unNormalizedPos += Input.ResolutionOffset;

			var mouseState = Input.CurrentMouseState;
			var newMouseState = new MouseState((int)unNormalizedPos.X, (int)unNormalizedPos.Y,
				mouseState.ScrollWheelValue,
				mouseState.LeftButton, mouseState.MiddleButton, mouseState.RightButton, mouseState.XButton1,
				mouseState.XButton2);
			Input.SetCurrentMouseState(newMouseState);
			return;
		}

		// Uniform scaling for aspect ratio preservation
		normalizedPos /= scale;

		// trick the input system. Take our normalizedPos and undo the scale and offsets (do the
		// reverse of what Input.scaledPosition does) so that any consumers of mouse input can get
		// the correct coordinates.
		var unNormalizedPos2 = normalizedPos / Input.ResolutionScale;
		unNormalizedPos2 += Input.ResolutionOffset;

		var mouseState2 = Input.CurrentMouseState;
		var newMouseState2 = new MouseState((int)unNormalizedPos2.X, (int)unNormalizedPos2.Y,
			mouseState2.ScrollWheelValue,
			mouseState2.LeftButton, mouseState2.MiddleButton, mouseState2.RightButton, mouseState2.XButton1,
			mouseState2.XButton2);
		Input.SetCurrentMouseState(newMouseState2);
	}


	#region GlobalManager Lifecycle

	public override void OnEnabled()
	{
		if (Voltage.Core.Scene != null)
		{
			Voltage.Core.Scene.FinalRenderDelegate = this;

			// why call beforeLayout here? If added from the DebugConsole we missed the GlobalManger.update call and ImGui needs NextFrame
			// called or it fails. Calling NextFrame twice in a frame causes no harm, just missed input.
			_renderer.BeforeLayout(Time.DeltaTime);
		}
	}

	public override void OnDisabled()
	{
		Unload();
		if (Voltage.Core.Scene != null)
			Voltage.Core.Scene.FinalRenderDelegate = null;
	}

	public override void Update()
	{
		// we have to do our layout in update so that if the game window is not focused or being displayed we can wipe
		// the Input, essentially letting ImGui consume it
		_renderer.BeforeLayout(Time.DeltaTime);

		// Exit prompt drawing and management
		DrawApplicationExitPrompt(ref _pendingExit, ExitPromptType.Exit);
		DrawApplicationExitPrompt(ref _pendingSceneChange, ExitPromptType.SceneChange);
		DrawApplicationExitPrompt(ref _pendingResetScene, ExitPromptType.ResetScene);
		ManageApplicationExitPrompt();
		ManageUndoAndRedo();

		LayoutGui();
	}

	private enum ExitPromptType
	{
		Exit,
		SceneChange,
		ResetScene
	}

	private void DrawApplicationExitPrompt(ref bool pendingValue, ExitPromptType exitPromptType)
	{
		if (!pendingValue)
			return;

		// Only open the popup if there are unsaved changes
		if (EditorChangeTracker.IsDirty)
		{
			ImGui.OpenPopup("Unsaved Changes");
		}
		else
		{
			// No unsaved changes, reset the flag so prompt doesn't get stuck
			pendingValue = false;
			return;
		}

		if (ImGui.BeginPopupModal("Unsaved Changes", ref pendingValue, ImGuiWindowFlags.AlwaysAutoResize))
		{
			ImGui.TextWrapped("You have unsaved changes for:");

			ImGui.Spacing();
			VoltageEditorUtils.MediumVerticalSpace();
			ImGui.Separator();

			int i = 1;
			foreach (var (_, description) in EditorChangeTracker.ChangedObjects)
			{
				ImGui.BulletText($"{i++}. {description}");
			}

			ImGui.Separator();
			VoltageEditorUtils.MediumVerticalSpace();

			if (ImGui.Button("Save", new Num.Vector2(120, 0)))
			{
				_pendingActionAfterSave = exitPromptType;
				_pendingSaveTask = SaveSceneAsyncAndThenAct();
				ImGui.CloseCurrentPopup();
			}

			ImGui.SameLine();

			if (ImGui.Button("Discard", new Num.Vector2(120, 0))
)
			{
				EditorChangeTracker.Revert();
				ImGui.CloseCurrentPopup();
				if (exitPromptType == ExitPromptType.SceneChange && _requestedSceneType != null)
				{
					ChangeScene(_requestedSceneType);
					pendingValue = false;
					_requestedSceneType = null;
				}
				else if (exitPromptType == ExitPromptType.ResetScene)
				{
					ResetScene();
					pendingValue = false;
				}
				else
				{
					pendingValue = false;
					Voltage.Core.ConfirmAndExit();
				}
			}

			ImGui.SameLine();

			if (ImGui.Button("Cancel", new Num.Vector2(120, 0)))
			{
				pendingValue = false;
				_requestedSceneType = null;
				ImGui.CloseCurrentPopup();
			}

			ImGui.EndPopup();
		}
			
	}

	private void ManageApplicationExitPrompt()
	{
		if (_pendingSaveTask != null && _pendingSaveTask.IsCompleted)
		{
			switch (_pendingActionAfterSave)
			{
				case ExitPromptType.SceneChange:
					ChangeScene(_requestedSceneType);
					_pendingSceneChange = false;
					_requestedSceneType = null;
					break;
				case ExitPromptType.ResetScene:
					ResetScene();
					_pendingResetScene = false;
					_requestedResetSceneType = null;
					break;
				case ExitPromptType.Exit:
					Voltage.Core.ConfirmAndExit();
					_pendingExit = false;
					break;
			}
			_pendingSaveTask = null;
		}
	}

	#endregion


	#region IFinalRenderDelegate

	void IFinalRenderDelegate.HandleFinalRender(RenderTarget2D finalRenderTarget, Color letterboxColor,
		RenderTarget2D source, Rectangle finalRenderDestinationRect,
		SamplerState samplerState)
	{
		if (ShowSeparateGameWindow)
		{
			if (_lastRenderTarget != source)
			{
				// unbind the old texture if we had one
				if (_lastRenderTarget != null)
					_renderer.UnbindTexture(_renderTargetId);

				// bind the new texture
				_lastRenderTarget = source;
				_renderTargetId = _renderer.BindTexture(source);
			}

			// Use the SAME ID as DrawGameWindow - just "GameWindow" without spaces
			string gameWindowState = Voltage.Core.IsEditMode ? "Paused" : "Playing";
			string windowTitle = $"Game: {gameWindowState}###GameWindow";

			ImGui.Begin(windowTitle, _gameWindowFlags);

			IsGameWindowFocused = ImGui.IsWindowFocused(ImGuiFocusedFlags.None);
			GameWindowSize = ImGui.GetWindowSize();
			GameWindowPosition = ImGui.GetWindowPos();

			// Calculate aspect-ratio-preserving image size if enabled
			Num.Vector2 imageSize;
			Num.Vector2 cursorOffset = Num.Vector2.Zero;

			var availableRegion = ImGui.GetContentRegionAvail();

			if (PreserveGameWindowAspectRatio && _lastRenderTarget != null)
			{
				var targetAspect = (float)_lastRenderTarget.Width / _lastRenderTarget.Height;
				var availableAspect = availableRegion.X / availableRegion.Y;

				if (availableAspect > targetAspect)
				{
					imageSize = new Num.Vector2(availableRegion.Y * targetAspect, availableRegion.Y);
					cursorOffset.X = (availableRegion.X - imageSize.X) * 0.5f;
				}
				else
				{
					imageSize = new Num.Vector2(availableRegion.X, availableRegion.X / targetAspect);
					cursorOffset.Y = (availableRegion.Y - imageSize.Y) * 0.5f;
				}

				if (cursorOffset.X > 0 || cursorOffset.Y > 0)
				{
					var currentCursor = ImGui.GetCursorPos();
					ImGui.SetCursorPos(currentCursor + cursorOffset);
				}
			}
			else
			{
				imageSize = availableRegion;
			}

			ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, Num.Vector2.Zero);
			ImGui.ImageButton("SeparateGameWindowImageButton", _renderTargetId, imageSize);
			ImGui.PopStyleVar();
			ImGui.End();

			Voltage.Core.GraphicsDevice.SamplerStates[0] = samplerState;
			Voltage.Core.GraphicsDevice.SetRenderTarget(finalRenderTarget);
			Voltage.Core.GraphicsDevice.Clear(letterboxColor);
		}
		else
		{
			Voltage.Core.GraphicsDevice.SetRenderTarget(finalRenderTarget);
			Voltage.Core.GraphicsDevice.Clear(letterboxColor);
			Graphics.Instance.Batcher.Begin(BlendState.Opaque, samplerState, null, null);
			Graphics.Instance.Batcher.Draw(source, finalRenderDestinationRect, Color.White);
			Graphics.Instance.Batcher.End();
		}

		_renderer.AfterLayout();
	}

	void IFinalRenderDelegate.OnAddedToScene(Scene scene)
	{
	}

	void IFinalRenderDelegate.OnSceneBackBufferSizeChanged(int newWidth, int newHeight)
	{
	}

	void IFinalRenderDelegate.Unload()
	{
	}

	#endregion


	#region IDisposable Support

	private bool _isDisposed = false; // To detect redundant calls

	private void Dispose(bool disposing)
	{
		if (!_isDisposed)
		{
			if (disposing)
			{
				Voltage.Core.Emitter.RemoveObserver(CoreEvents.SceneChanged, OnSceneChanged);
				_layoutManager.AutoSaveCurrentLayout();

				// Always save the default .ini for next startup
				if (!string.IsNullOrEmpty(_layoutFilePath))
				{
					ImGui.SaveIniSettingsToDisk(_layoutFilePath);
				}
			}

			_isDisposed = true;
		}
	}

	void IDisposable.Dispose()
	{
		Dispose(true);
	}

	#endregion

	[Command("toggle-imgui", "Toggles the Dear ImGui renderer")]
	public static void ToggleImGui()
	{
		// install the service if it isnt already there
		var service = Voltage.Core.GetGlobalManager<ImGuiManager>();
		if (service == null)
		{
			service = new ImGuiManager();
			Voltage.Core.RegisterGlobalManager(service);
		}
		else
		{
			service.SetEnabled(!service.Enabled);
		}
	}
}