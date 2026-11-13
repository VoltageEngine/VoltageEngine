using System;
using System.Numerics;
using System.Text.Json;
using System.Threading.Tasks;
using ImGuiNET;
using Voltage.Editor.Gateway;
using Voltage.Editor.ImGuiCore;

namespace Voltage.Editor.Aseprite;

/// <summary>Connection management and live document inspection.</summary>
public static class AsepriteWindow
{
	public static bool IsOpen;
	private static readonly Vector4 Green = new(0.15f, 0.5f, 0.2f, 1f), Red = new(1f, 0.4f, 0.4f, 1f);
	private static Task<object> _request, _openPanel;
	private static string _message = "Install the Voltage extension, then restart Aseprite and allow its connection request.";
	private static bool _error, _awaitingConnect;
	private static double _installFlash, _connectFlash;
	private static string _reportedVersion;
	private static JsonElement _sprites;

	public static void Draw()
	{
		if (!IsOpen) return;
		Voltage.Editor.Utils.EditorLayout.WindowSize(new Vector2(760, 560));
		if (_request?.IsCompleted == true)
		{
			try { _sprites = (JsonElement)_request.GetAwaiter().GetResult(); Say("Documents refreshed."); }
			catch (Exception ex) { Fail(ex.Message); }
			_request = null;
		}
		if (_openPanel?.IsCompleted == true)
		{
			try { _openPanel.GetAwaiter().GetResult(); Say("Voltage Normals is open in Aseprite."); }
			catch (Exception ex) { Fail(ex.Message); }
			_openPanel = null;
		}
		if (Gui.Begin("Aseprite", ref IsOpen))
		{
			var dispatcher = EditorGatewayDispatcher.Current;
			var bridge = dispatcher?.Aseprite;
			var version = bridge?.IsConnected == true ? bridge.AsepriteVersion : null;
			if (version != _reportedVersion)
			{
				_reportedVersion = version;
				if (version != null)
				{
					Check(version, $"Connected to Aseprite {version}.", ref _connectFlash, _awaitingConnect);
					_awaitingConnect = false;
				}
			}
			ImGui.TextUnformatted(bridge?.IsConnected == true ? "Connected to Aseprite" : bridge?.IsListening == true ? "Waiting for Aseprite" : "Bridge stopped");
			ImGui.BeginDisabled(bridge == null || dispatcher.Options.Safe);
			if (FlashButton("Install or update extension", _installFlash)) Run(() =>
			{
				bridge.Install();
				var found = bridge.AsepriteVersion ?? AsepriteBridge.InstalledVersion();
				if (found == null) { Say("Extension installed. Restart Aseprite to load it."); _installFlash = ImGui.GetTime() + 2; }
				else Check(found, $"Extension installed for Aseprite {found}. Restart Aseprite to load it.", ref _installFlash, true);
			});
			ImGui.SameLine();
			if (FlashButton("Connect", _connectFlash)) Run(() =>
			{
				bridge.Start();
				if (bridge.AsepriteVersion is { } live) Check(live, $"Connected to Aseprite {live}.", ref _connectFlash, true);
				else { _awaitingConnect = true; Say("Listening. Open or restart Aseprite to connect."); }
			});
			ImGui.SameLine();
			if (Gui.Button("Disconnect")) { bridge.Stop(); _awaitingConnect = false; }
			ImGui.EndDisabled();
			if (_error) ImGui.PushStyleColor(ImGuiCol.Text, Red);
			ImGui.TextWrapped(_message);
			if (_error) ImGui.PopStyleColor();
			ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]);
			ImGui.TextWrapped("Tip: open the normal map panel in Aseprite with Edit > Voltage Normals, just under Stroke. It needs Aseprite 1.3 or newer.");
			ImGui.PopStyleColor();
			ImGui.Separator();
			ImGui.BeginDisabled(bridge?.IsConnected != true);
			ImGui.BeginDisabled(_request != null);
			if (Gui.Button("Refresh open sprites")) _request = bridge.Call("list_open_sprites", JsonSerializer.SerializeToElement(new { }));
			ImGui.EndDisabled();
			ImGui.SameLine();
			ImGui.BeginDisabled(_openPanel != null);
			if (Gui.Button("Open Voltage Normals in Aseprite")) _openPanel = bridge.Call("normals_open_panel", JsonSerializer.SerializeToElement(new { }));
			ImGui.EndDisabled();
			ImGui.EndDisabled();
			if (_sprites.ValueKind != JsonValueKind.Undefined) ImGui.TextWrapped(_sprites.GetRawText());
		}
		Gui.End();
	}

	private static bool FlashButton(string label, double until)
	{
		var flash = ImGui.GetTime() < until;
		if (flash) ImGui.PushStyleColor(ImGuiCol.Button, Green);
		var pressed = Gui.Button(label);
		if (flash) ImGui.PopStyleColor();
		return pressed;
	}

	private static void Check(string version, string success, ref double flash, bool flashOnSuccess)
	{
		if (AsepriteBridge.IsTooOld(version)) { Fail($"Aseprite {version} is too old. Voltage needs Aseprite 1.3 or newer: update Aseprite, then restart it."); return; }
		Say(success);
		if (flashOnSuccess) flash = ImGui.GetTime() + 2;
	}

	private static void Say(string message) { _message = message; _error = false; }
	private static void Fail(string message) { _message = message; _error = true; }

	private static void Run(Action action)
	{
		try { action(); }
		catch (Exception ex) { Fail(ex.Message); }
	}
}
