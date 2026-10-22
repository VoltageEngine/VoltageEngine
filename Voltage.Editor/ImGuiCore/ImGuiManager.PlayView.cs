using System;
using ImGuiNET;
using Voltage.Editor.Persistence;
using Voltage.Editor.Utils;
using Num = System.Numerics;

namespace Voltage.Editor.ImGuiCore;

/// <summary>Where the game view is shown while playing.</summary>
public enum PlayViewMode { Custom, Maximized, Floating }

public partial class ImGuiManager
{
	private readonly PersistentInt _playView = new("Editor_PlayView", (int)PlayViewMode.Custom);
	private PlayViewMode _shownPlayView;

	public PlayViewMode PlayView
	{
		get => (PlayViewMode)Math.Clamp(_playView.Value, 0, (int)PlayViewMode.Floating);
		set => _playView.Value = (int)value;
	}

	/// <summary>Edit mode always uses the docked panel.</summary>
	private PlayViewMode ActivePlayView => Core.IsEditMode ? PlayViewMode.Custom : PlayView;

	private static string PlayViewGlyph(PlayViewMode mode) => mode switch
	{
		PlayViewMode.Maximized => "\uf31e",
		PlayViewMode.Floating => "\uf2d2",
		_ => "\uf0db"
	};

	private void DrawPlayViewButton(Num.Vector2 size)
	{
		if (Gui.IconButton("PlayView", PlayViewGlyph(PlayView), _renderer.IconFontPtr, size, PlayView != PlayViewMode.Custom))
			ImGui.OpenPopup("play-view-popup");
		if (ImGui.IsItemHovered()) ImGuiSafe.SetTooltipSafe($"Play View: {PlayView}. Where the game shows while playing.");

		if (!Gui.BeginPopup("play-view-popup"))
			return;

		ImGui.TextUnformatted("Play View");
		ImGui.Separator();
		if (!ShowSeparateGameWindow)
			ImGui.TextDisabled("Turn on View > Separate Game Window to use these.");
		ImGui.BeginDisabled(!ShowSeparateGameWindow);
		Option(PlayViewMode.Custom, "Play in the Game panel, as you laid it out.");
		Option(PlayViewMode.Maximized, "Fill the editor window. Stop restores the layout.");
		Option(PlayViewMode.Floating, "Pop out into a window you can move and resize.");
		ImGui.EndDisabled();
		Gui.EndPopup();

		void Option(PlayViewMode mode, string hint)
		{
			if (Gui.RadioButton(mode.ToString(), PlayView == mode))
			{
				PlayView = mode;
				ImGui.CloseCurrentPopup();
			}
			ImGui.Indent();
			ImGui.TextDisabled(hint);
			ImGui.Unindent();
		}
	}

	/// <summary>Draws the game view above the dock layout, which stays untouched underneath.</summary>
	private void DrawPoppedOutGameView(PlayViewMode view, string title)
	{
		var viewport = ImGui.GetMainViewport();
		var flags = _gameWindowFlags | ImGuiWindowFlags.NoDocking | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;
		var maximized = view == PlayViewMode.Maximized;
		if (maximized)
		{
			ImGui.SetNextWindowPos(viewport.WorkPos);
			ImGui.SetNextWindowSize(viewport.WorkSize);
			flags |= ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoSavedSettings;
			ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0f);
			ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
		}
		else
		{
			ImGui.SetNextWindowPos(viewport.WorkPos + viewport.WorkSize * 0.5f, ImGuiCond.FirstUseEver, new Num.Vector2(0.5f));
			ImGui.SetNextWindowSize(viewport.WorkSize * 0.75f, ImGuiCond.FirstUseEver);
		}

		if (view != _shownPlayView)
			ImGui.SetNextWindowFocus();

		Gui.Begin($"{title}###GameWindow{view}", flags);
		if (maximized)
			ImGui.PopStyleVar(2);
		DrawGameView();
		Gui.End();
	}
}
