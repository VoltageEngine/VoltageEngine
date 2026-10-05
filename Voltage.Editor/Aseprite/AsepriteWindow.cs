using System;
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
	private static Task<object> _request;
	private static string _message = "Install the Voltage extension, then restart Aseprite and allow its connection request.";
	private static JsonElement _sprites;

	public static void Draw()
	{
		if (!IsOpen) return;
		if (_request?.IsCompleted == true)
		{
			try { _sprites = (JsonElement)_request.GetAwaiter().GetResult(); _message = "Documents refreshed."; }
			catch (Exception ex) { _message = ex.Message; }
			_request = null;
		}
		if (Gui.Begin("Aseprite", ref IsOpen))
		{
			var dispatcher = EditorGatewayDispatcher.Current;
			var bridge = dispatcher?.Aseprite;
			ImGui.TextUnformatted(bridge?.IsConnected == true ? "Connected to Aseprite" : bridge?.IsListening == true ? "Waiting for Aseprite" : "Bridge stopped");
			ImGui.BeginDisabled(bridge == null || dispatcher.Options.Safe);
			if (Gui.Button("Install or update extension")) Run(() => { bridge.Install(); _message = "Extension installed. Restart Aseprite to load it."; });
			ImGui.SameLine();
			if (Gui.Button("Connect")) Run(bridge.Start);
			ImGui.SameLine();
			if (Gui.Button("Disconnect")) bridge.Stop();
			ImGui.EndDisabled();
			ImGui.TextWrapped(_message);
			ImGui.Separator();
			ImGui.BeginDisabled(bridge?.IsConnected != true || _request != null);
			if (Gui.Button("Refresh open sprites")) _request = bridge.Call("list_open_sprites", JsonSerializer.SerializeToElement(new { }));
			ImGui.EndDisabled();
			if (_sprites.ValueKind != JsonValueKind.Undefined) ImGui.TextWrapped(_sprites.GetRawText());
		}
		Gui.End();
	}

	private static void Run(Action action)
	{
		try { action(); }
		catch (Exception ex) { _message = ex.Message; }
	}
}
