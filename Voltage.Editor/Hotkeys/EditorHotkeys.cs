using System;
using System.Collections.Generic;
using ImGuiNET;

namespace Voltage.Editor.Hotkeys
{
	/// <summary>Registry of every rebindable editor shortcut, grouped by the window that owns it.</summary>
	public static class EditorHotkeys
	{
		public const string CategoryGlobal = "Global";
		public const string CategoryPlayback = "Playback";
		public const string CategoryViewport = "Viewport";
		public const string CategorySceneGraph = "Scene Graph";
		public const string CategoryAssetBrowser = "Asset Browser";
		public const string CategoryTilePalette = "Tile Palette";

		// Global
		public const string Undo = "Global.Undo";
		public const string Redo = "Global.Redo";
		public const string SaveScene = "Global.SaveScene";
		public const string Cancel = "Global.Cancel";

		// Playback
		public const string TogglePlay = "Playback.TogglePlay";
		public const string TogglePause = "Playback.TogglePause";
		public const string ResetScene = "Playback.ResetScene";
		public const string ReloadScene = "Playback.ReloadScene";
		public const string BuildAndRun = "Playback.BuildAndRun";

		// Viewport cursor modes
		public const string CursorNormal = "Viewport.CursorNormal";
		public const string CursorResize = "Viewport.CursorResize";
		public const string CursorRotate = "Viewport.CursorRotate";
		public const string CursorColliderResize = "Viewport.CursorColliderResize";
		public const string CursorTilePaint = "Viewport.CursorTilePaint";

		// Scene Graph / entities
		public const string SelectPrevEntity = "SceneGraph.SelectPrevious";
		public const string SelectNextEntity = "SceneGraph.SelectNext";
		public const string NewEntity = "SceneGraph.NewEntity";
		public const string DuplicateEntity = "SceneGraph.DuplicateEntity";
		public const string CopyEntity = "SceneGraph.CopyEntity";
		public const string PasteEntity = "SceneGraph.PasteEntity";
		public const string DeleteEntity = "SceneGraph.DeleteEntity";

		// Asset Browser
		public const string AssetCopy = "AssetBrowser.Copy";
		public const string AssetPaste = "AssetBrowser.Paste";
		public const string AssetDuplicate = "AssetBrowser.Duplicate";
		public const string AssetDelete = "AssetBrowser.Delete";

		// Tile Palette
		public const string TileRotateLeft = "TilePalette.RotateLeft";
		public const string TileRotateRight = "TilePalette.RotateRight";
		public const string TileFlipX = "TilePalette.FlipX";
		public const string TileFlipY = "TilePalette.FlipY";

		private static readonly List<HotkeyAction> _actions = new();
		private static readonly Dictionary<string, HotkeyAction> _byId = new(StringComparer.Ordinal);

		static EditorHotkeys()
		{
			Register(Undo, CategoryGlobal, "Undo", "Step back through the editor undo stack.",
				new HotkeyBinding(ImGuiKey.Z, ctrl: true));
			Register(Redo, CategoryGlobal, "Redo", "Step forward through the editor undo stack.",
				new HotkeyBinding(ImGuiKey.Y, ctrl: true));
			Register(SaveScene, CategoryGlobal, "Save Scene", "Write the current scene (or prefab) to disk.",
				new HotkeyBinding(ImGuiKey.S, ctrl: true));
			Register(Cancel, CategoryGlobal, "Cancel / Deselect", "Drop gizmo drags, box selection and in-flight strokes.",
				new HotkeyBinding(ImGuiKey.Escape));

			Register(TogglePlay, CategoryPlayback, "Play / Stop", "Switch between edit mode and play mode.",
				new HotkeyBinding(ImGuiKey.F1));
			Register(TogglePause, CategoryPlayback, "Pause / Unpause", "Pause the running game.",
				new HotkeyBinding(ImGuiKey.F2));
			Register(ResetScene, CategoryPlayback, "Reset Scene", "Reset the scene to its saved state.",
				new HotkeyBinding(ImGuiKey.F5));
			Register(ReloadScene, CategoryPlayback, "Reload Scene", "Reload the scene from disk.",
				new HotkeyBinding(ImGuiKey.F6));
			Register(BuildAndRun, CategoryPlayback, "Build and Run", "Build with the last options and launch the game.",
				new HotkeyBinding(ImGuiKey.F5, ctrl: true));

			Register(CursorNormal, CategoryViewport, "Move Cursor", "Translate gizmo.",
				new HotkeyBinding(ImGuiKey._1), new HotkeyBinding(ImGuiKey.Q));
			Register(CursorResize, CategoryViewport, "Scale Cursor", "Scale gizmo.",
				new HotkeyBinding(ImGuiKey._2), new HotkeyBinding(ImGuiKey.E));
			Register(CursorRotate, CategoryViewport, "Rotate Cursor", "Rotate gizmo.",
				new HotkeyBinding(ImGuiKey._3), new HotkeyBinding(ImGuiKey.R));
			Register(CursorColliderResize, CategoryViewport, "Collider Cursor", "Collider resize gizmo.",
				new HotkeyBinding(ImGuiKey._4), new HotkeyBinding(ImGuiKey.T));
			Register(CursorTilePaint, CategoryViewport, "Tile Paint Cursor", "Tile painting cursor.",
				new HotkeyBinding(ImGuiKey._5), new HotkeyBinding(ImGuiKey.B));

			Register(SelectPrevEntity, CategorySceneGraph, "Select Previous", "Move the selection up the hierarchy.",
				new HotkeyBinding(ImGuiKey.UpArrow));
			Register(SelectNextEntity, CategorySceneGraph, "Select Next", "Move the selection down the hierarchy.",
				new HotkeyBinding(ImGuiKey.DownArrow));
			Register(NewEntity, CategorySceneGraph, "New Entity", "Create an empty entity.",
				new HotkeyBinding(ImGuiKey.N, shift: true));
			Register(DuplicateEntity, CategorySceneGraph, "Duplicate", "Duplicate the selected entities.",
				new HotkeyBinding(ImGuiKey.D, ctrl: true));
			Register(CopyEntity, CategorySceneGraph, "Copy", "Copy the selected entities.",
				new HotkeyBinding(ImGuiKey.C, ctrl: true));
			Register(PasteEntity, CategorySceneGraph, "Paste", "Paste the copied entities.",
				new HotkeyBinding(ImGuiKey.V, ctrl: true));
			Register(DeleteEntity, CategorySceneGraph, "Delete", "Delete the selected entities.",
				new HotkeyBinding(ImGuiKey.Delete));

			Register(AssetCopy, CategoryAssetBrowser, "Copy", "Copy the selected asset.",
				new HotkeyBinding(ImGuiKey.C, ctrl: true));
			Register(AssetPaste, CategoryAssetBrowser, "Paste", "Paste into the current folder.",
				new HotkeyBinding(ImGuiKey.V, ctrl: true));
			Register(AssetDuplicate, CategoryAssetBrowser, "Duplicate", "Duplicate the selected asset.",
				new HotkeyBinding(ImGuiKey.D, ctrl: true));
			Register(AssetDelete, CategoryAssetBrowser, "Delete", "Delete the selected asset.",
				new HotkeyBinding(ImGuiKey.Delete));

			Register(TileRotateLeft, CategoryTilePalette, "Rotate Brush Left", "Rotate the tile stamp 90° left.",
				new HotkeyBinding(ImGuiKey.Q));
			Register(TileRotateRight, CategoryTilePalette, "Rotate Brush Right", "Rotate the tile stamp 90° right.",
				new HotkeyBinding(ImGuiKey.E));
			Register(TileFlipX, CategoryTilePalette, "Flip Brush X", "Mirror the tile stamp horizontally.",
				new HotkeyBinding(ImGuiKey.Z));
			Register(TileFlipY, CategoryTilePalette, "Flip Brush Y", "Mirror the tile stamp vertically.",
				new HotkeyBinding(ImGuiKey.X));
		}

		private static void Register(string id, string category, string label, string tooltip,
			HotkeyBinding primary, HotkeyBinding alternate = default)
		{
			var action = new HotkeyAction(id, category, label, tooltip, primary, alternate);
			_actions.Add(action);
			_byId[id] = action;
		}

		public static IReadOnlyList<HotkeyAction> Actions => _actions;

		public static HotkeyAction Find(string id) => _byId.TryGetValue(id, out var action) ? action : null;

		/// <summary>Set while the settings UI is reading a new combo, so nothing acts on the keys being typed.</summary>
		public static bool CaptureMode { get; set; }

		public static bool Pressed(string id, bool repeat = false) =>
			!CaptureMode && (Find(id)?.Pressed(repeat) ?? false);

		public static bool Down(string id) => !CaptureMode && (Find(id)?.Down() ?? false);

		/// <summary>Menu shortcut column text for an action, empty when unbound.</summary>
		public static string MenuLabel(string id) => Find(id)?.MenuLabel ?? string.Empty;

		/// <summary>Both bindings for a tooltip, e.g. "1 or Q".</summary>
		public static string Hint(string id)
		{
			var action = Find(id);
			if (action == null)
				return string.Empty;

			if (!action.Alternate.IsBound)
				return action.Primary.IsBound ? action.Primary.ToDisplayString() : string.Empty;

			if (!action.Primary.IsBound)
				return action.Alternate.ToDisplayString();

			return $"{action.Primary.ToDisplayString()} or {action.Alternate.ToDisplayString()}";
		}

		public static IEnumerable<string> Categories()
		{
			var seen = new HashSet<string>(StringComparer.Ordinal);

			foreach (var action in _actions)
			{
				if (seen.Add(action.Category))
					yield return action.Category;
			}
		}

		public static IEnumerable<HotkeyAction> InCategory(string category)
		{
			foreach (var action in _actions)
			{
				if (string.Equals(action.Category, category, StringComparison.Ordinal))
					yield return action;
			}
		}

		/// <summary>
		/// First action already using this combo, or null. Two bindings only clash when they can fire in the same
		/// context: within one category, or when either side is Global.
		/// </summary>
		public static HotkeyAction FindConflict(HotkeyAction target, HotkeyBinding binding)
		{
			if (!binding.IsBound)
				return null;

			foreach (var action in _actions)
			{
				if (ReferenceEquals(action, target))
					continue;

				if (action.Primary != binding && action.Alternate != binding)
					continue;

				if (SharesContext(action.Category, target.Category))
					return action;
			}

			return null;
		}

		private static bool SharesContext(string a, string b) =>
			string.Equals(a, b, StringComparison.Ordinal) ||
			string.Equals(a, CategoryGlobal, StringComparison.Ordinal) ||
			string.Equals(b, CategoryGlobal, StringComparison.Ordinal);

		public static void ResetAllToDefaults()
		{
			foreach (var action in _actions)
				action.ResetToDefault();
		}
	}
}
