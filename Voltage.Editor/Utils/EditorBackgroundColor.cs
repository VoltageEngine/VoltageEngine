using Microsoft.Xna.Framework;
using Voltage.Editor.Persistence;
using Voltage.Project;

namespace Voltage.Editor.Utils
{
	/// <summary>
	/// Decides what the game view is cleared to while the editor is running: either the project's
	/// BackgroundClearColor (what a build uses) or a temporary colour applied through
	/// <see cref="Scene.ClearColorOverride"/>, which is never serialized.
	///
	/// The temporary colour is a per-user preference - it is stored in the editor's own Settings.json, outside
	/// any game project, and applies to every scene since <see cref="Apply"/> runs on each Scene.OnSceneBegin.
	/// </summary>
	public static class EditorBackgroundColor
	{
		private static readonly PersistentBool _useTemporary = new("Editor_UseTemporaryBackgroundColor", false);

		// Stored as a packed RGBA int - the editor settings file has no colour type of its own.
		private static readonly PersistentInt _temporaryPacked =
			new("Editor_TemporaryBackgroundColor", unchecked((int)new Color(45, 45, 48, 255).PackedValue));

		/// <summary>When true the game view is cleared to <see cref="Temporary"/> instead of the project colour.</summary>
		public static bool UseTemporary
		{
			get => _useTemporary.Value;
			set
			{
				if (_useTemporary.Value == value)
					return;

				_useTemporary.Value = value;
				Apply();
			}
		}

		private static Color? _temporary;
		private static bool _temporaryUnsaved;

		/// <summary>
		/// Editor-only working colour. Assigning only touches memory; <see cref="FlushTemporary"/> writes it.
		/// </summary>
		public static Color Temporary
		{
			get
			{
				if (_temporary == null)
				{
					var loaded = new Color();
					loaded.PackedValue = unchecked((uint)_temporaryPacked.Value);
					_temporary = loaded;
				}

				return _temporary.Value;
			}
			set
			{
				if (_temporary == value)
					return;

				_temporary = value;
				_temporaryUnsaved = true;

				if (UseTemporary)
					Apply();
			}
		}

		/// <summary>
		/// Writes a changed <see cref="Temporary"/> to the editor's settings file. Cheap to call every frame;
		/// callers hold off while the mouse is down so a drag costs one write instead of one per frame.
		/// </summary>
		public static void FlushTemporary()
		{
			if (!_temporaryUnsaved)
				return;

			_temporaryPacked.Value = unchecked((int)Temporary.PackedValue);
			_temporaryUnsaved = false;
		}

		/// <summary>The colour saved in Project Settings - what the built game clears to.</summary>
		public static Color Project =>
			ProjectSettings.Instance?.Rendering?.BackgroundClearColor ?? new Color(100, 149, 237, 255);

		public static Color Active => UseTemporary ? Temporary : Project;

		/// <summary>Pushes the current choice onto the active scene.</summary>
		public static void Apply()
		{
			var scene = Core.Scene;
			if (scene == null)
				return;

			scene.ClearColor = Project;
			scene.ClearColorOverride = UseTemporary ? Temporary : null;
		}
	}
}
