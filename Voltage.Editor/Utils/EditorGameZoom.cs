using Voltage.Editor.Undo.Core;

namespace Voltage.Editor.Utils
{
	/// <summary>Keeps the editor view zoom (RawZoom in Edit mode, never saved) apart from the scene's saved <see cref="Camera.GameZoom"/>.</summary>
	public static class EditorGameZoom
	{
		private static float? _viewZoom;
		private static float? _playChange;
		private static Scene _stoppedScene;

		/// <summary>Sets the game zoom. In Play it also applies to the running game immediately.</summary>
		public static void SetGame(float zoom)
		{
			var camera = Core.Scene?.Camera;
			if (camera == null || zoom <= 0 || camera.GameZoom == zoom)
				return;

			camera.GameZoom = zoom;
			if (Core.IsEditMode)
			{
				EditorChangeTracker.MarkChanged(camera, "Game Zoom");
				return;
			}

			camera.RawZoom = zoom;
			_playChange = zoom;
		}

		/// <summary>Called on Play/Stop, before the stop reload is requested.</summary>
		public static void OnEditModeSwitched(bool isEditMode)
		{
			var camera = Core.Scene?.Camera;
			if (camera == null)
				return;

			if (!isEditMode)
			{
				_stoppedScene = null;
				_playChange = null;
				_viewZoom = camera.RawZoom;
				if (camera.GameZoom > 0)
					camera.RawZoom = camera.GameZoom;
				return;
			}

			_stoppedScene = Core.Scene;
			if (!Core.ResetSceneAutomatically)
				Restore(Core.Scene);
		}

		/// <summary>Runs every editor frame; finishes a Stop once the reloaded scene has loaded its entities.</summary>
		public static void Update()
		{
			var scene = Core.Scene;
			if (_stoppedScene == null || !Core.IsEditMode || scene == null || ReferenceEquals(scene, _stoppedScene) ||
			    !scene.DidAllEntitiesLoad)
				return;

			Restore(scene);
		}

		private static void Restore(Scene scene)
		{
			_stoppedScene = null;
			var camera = scene.Camera;
			if (camera == null)
				return;

			if (_playChange is float zoom)
			{
				camera.GameZoom = zoom;
				EditorChangeTracker.MarkChanged(camera, "Game Zoom");
			}

			if (_viewZoom is float view)
				camera.RawZoom = view;

			_playChange = null;
			_viewZoom = null;
		}
	}
}
